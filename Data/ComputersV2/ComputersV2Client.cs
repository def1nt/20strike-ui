using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace _20strike_ui.Data.ComputersV2;

/// <summary>
/// Dedicated API client for the v2 inventory endpoints. Talks ONLY to the v2 backend and
/// normalizes every response into internal camelCase models via <see cref="ComputersV2Mapper"/>.
/// All requests are cancellable; timeouts are classified separately from user aborts.
/// </summary>
public sealed class ComputersV2Client
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Detail responses are large (Software/Processes can be huge), so they get a generous timeout.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DetailTimeout = TimeSpan.FromSeconds(60);

    public ComputersV2Client(HttpClient http, string baseUrl)
    {
        _http = http;
        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        }

        // The problems endpoint rejects requests without an explicit JSON Accept header.
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Per-call timeouts are applied via linked CancellationTokenSource below.
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<IReadOnlyList<ComputerSummary>> GetComputersAsync(CancellationToken ct = default)
    {
        var dtos = await SendAsync<List<ComputerDto>>("computers", ct, DefaultTimeout).ConfigureAwait(false);
        return ComputersV2Mapper.MapComputers(dtos);
    }

    /// <summary>Returns <c>null</c> when the computer does not exist (404) or the name is invalid.</summary>
    public async Task<ComputerDetail?> GetComputerByNameAsync(string computerName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(computerName))
        {
            return null;
        }

        var url = $"computers/{Uri.EscapeDataString(computerName.Trim())}";
        var dto = await SendAsync<DetailDto?>(url, ct, DetailTimeout, allowNotFound: true).ConfigureAwait(false);
        return dto is null ? null : ComputersV2Mapper.MapDetail(dto);
    }

    /// <summary>
    /// Fetches ping statuses. The /ping endpoint is rate-limited by the backend (HTTP 429 with a
    /// text/plain body). Calls are therefore serialized process-wide (all circuits share the same
    /// backend), rejected responses trigger a short back-off, and the last known good payload is
    /// served while the endpoint is throttled. A burst of initial loads (prerender + circuit,
    /// several users) never surfaces as an error: either fresh data, stale data or a retryable
    /// <see cref="ComputersV2RateLimitedException"/> comes back.
    /// </summary>
    public async Task<IReadOnlyList<PingInfo>> GetPingAsync(CancellationToken ct = default)
    {
        Task<IReadOnlyList<PingInfo>> shared;
        lock (PingSync)
        {
            if (DateTimeOffset.UtcNow < _pingBlockedUntil)
            {
                if (_lastKnownPings is { Count: > 0 } cached)
                {
                    return cached; // endpoint known-throttled; serve the last known payload
                }

                throw new ComputersV2RateLimitedException(
                    "Сервер перегружен (лимит запросов к /ping). Повторите попытку через несколько секунд.");
            }

            shared = _inFlightPing ??= LoadPingCoreAsync(this);
        }

        try
        {
            return await shared.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            // Keep the shared task published until it completes so late joiners coalesce onto the
            // same request; drop it afterwards so the next poll issues a fresh call.
            if (shared.IsCompleted)
            {
                _ = Interlocked.CompareExchange(ref _inFlightPing, null, shared);
            }
        }
    }

    /// <summary>True when the /ping endpoint is currently known to be throttled (stale data is being served).</summary>
    public static bool IsPingThrottled
    {
        get
        {
            lock (PingSync)
            {
                return DateTimeOffset.UtcNow < _pingBlockedUntil;
            }
        }
    }

    // ---------- Ping throttling state (process-wide: every circuit talks to the same backend) ----------

    private static readonly object PingSync = new();
    private static Task<IReadOnlyList<PingInfo>>? _inFlightPing;
    private static DateTimeOffset _pingBlockedUntil;             // do not hit /ping before this instant
    private static TimeSpan _pingBackoff = PingInitialBackoff;   // grows exponentially while throttled
    private static IReadOnlyList<PingInfo>? _lastKnownPings;     // last successful payload

    private static readonly TimeSpan PingInitialBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PingMaxBackoff = TimeSpan.FromSeconds(15);

    private static async Task<IReadOnlyList<PingInfo>> LoadPingCoreAsync(ComputersV2Client client)
    {
        try
        {
            // No caller token here: the request is shared by all waiting circuits. The per-call
            // timeout inside SendAsync still applies.
            var dtos = await client.SendAsync<List<PingDto>>("ping", CancellationToken.None, DefaultTimeout).ConfigureAwait(false);
            var mapped = ComputersV2Mapper.MapPing(dtos);

            lock (PingSync)
            {
                _pingBackoff = PingInitialBackoff;
                _pingBlockedUntil = default;
                _lastKnownPings = mapped;
            }

            return mapped;
        }
        catch (ComputersV2RateLimitedException)
        {
            lock (PingSync)
            {
                _pingBlockedUntil = DateTimeOffset.UtcNow.Add(_pingBackoff);
                _pingBackoff = TimeSpan.FromTicks(Math.Min(_pingBackoff.Ticks * 2, PingMaxBackoff.Ticks));
                if (_lastKnownPings is { Count: > 0 } cached)
                {
                    return cached; // stay silent; the polling loop keeps retrying in the background
                }
            }
            throw;
        }
    }

    /// <summary>Returns an empty list when the computer does not exist (404) or the name is invalid.</summary>
    public async Task<IReadOnlyList<ProblemInfo>> GetProblemsAsync(string computerName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(computerName))
        {
            return Array.Empty<ProblemInfo>();
        }

        var url = $"problems/{Uri.EscapeDataString(computerName.Trim())}";
        var dtos = await SendAsync<List<ProblemDto>>(url, ct, DefaultTimeout, allowNotFound: true).ConfigureAwait(false);
        return ComputersV2Mapper.MapProblems(dtos);
    }

    /// <summary>
    /// Fetches the login-to-full-name map from GET /v2/users. The response is a flat JSON object
    /// mapping lowercased logins to display names (e.g. {"ivanov-av": "Иван Иванов"}). Loaded once
    /// per circuit through <see cref="ComputersV2Cache"/>; failures surface as typed exceptions so
    /// callers can fall back to showing the raw username.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetUsersAsync(CancellationToken ct = default)
    {
        var raw = await SendAsync<Dictionary<string, string>>("users", ct, DefaultTimeout).ConfigureAwait(false);
        return ComputersV2Mapper.MapUsers(raw);
    }

    /// <summary>
    /// GET with typed timeout and error classification. Success responses are parsed as JSON
    /// straight from the stream (the backend sends 200 + application/json). Failures carry a
    /// text/plain body with a proper status code: 429 (and 5xx whose body looks throttled) maps
    /// to the retryable <see cref="ComputersV2RateLimitedException"/>, everything else to a
    /// descriptive <see cref="ComputersV2ApiException"/>. No response body is ever buffered.
    /// </summary>
    private async Task<T?> SendAsync<T>(string url, CancellationToken ct, TimeSpan timeout, bool allowNotFound = false)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ComputersV2TimeoutException($"Превышено время ожидания ответа GET /{url}");
        }
        catch (HttpRequestException ex)
        {
            throw new ComputersV2ApiException(null, $"Сетевая ошибка при обращении к GET /{url}: {ex.Message}");
        }

        var status = response.StatusCode;
        if (allowNotFound && status == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return default;
        }

        // Throttled/server-side failures: read the (small) error body so rate-limit responses get
        // a dedicated, retryable exception instead of a generic "server error".
        if (status == HttpStatusCode.TooManyRequests || (int)status >= 500)
        {
            var raw = await TryReadBodyAsync(response, timeoutCts.Token).ConfigureAwait(false);
            response.Dispose();
            if (status == HttpStatusCode.TooManyRequests || IsThrottledBody(raw))
            {
                throw new ComputersV2RateLimitedException($"Сервер перегружен (GET /{url}): {DescribeBody(raw)}");
            }
            throw new ComputersV2ApiException(status, $"Ошибка сервера {(int)status} для GET /{url}: {DescribeBody(raw)}");
        }

        if (!response.IsSuccessStatusCode)
        {
            var raw = await TryReadBodyAsync(response, timeoutCts.Token).ConfigureAwait(false);
            response.Dispose();
            throw new ComputersV2ApiException(status, $"Ошибка сервера {(int)status} для GET /{url}: {DescribeBody(raw)}");
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ComputersV2TimeoutException($"Превышено время ожидания данных GET /{url}");
        }
        catch (JsonException)
        {
            throw new ComputersV2ApiException(status, $"Некорректный ответ сервера для GET /{url}: тело ответа не является JSON.");
        }
    }

    private static bool IsThrottledBody(string? body)
    {
        if (body is null)
        {
            return false;
        }
        return body.Contains("server is busy", StringComparison.OrdinalIgnoreCase)
            || body.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
            || body.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeBody(string? body)
    {
        var b = body?.Trim();
        if (string.IsNullOrEmpty(b))
        {
            return "пустой ответ";
        }
        return b.Length <= 160 ? b : b[..160] + "…";
    }

    private static async Task<string?> TryReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}

public class ComputersV2Exception : Exception
{
    public ComputersV2Exception(string message) : base(message) { }
}

public sealed class ComputersV2TimeoutException : ComputersV2Exception
{
    public ComputersV2TimeoutException(string message) : base(message) { }
}

public sealed class ComputersV2ApiException : ComputersV2Exception
{
    public HttpStatusCode? StatusCode { get; }
    public ComputersV2ApiException(HttpStatusCode? statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Thrown when the backend signals rate limiting (HTTP 429 or a "Server is busy" style body).
/// Callers should treat it as retryable after a short delay rather than as a hard failure.
/// </summary>
public sealed class ComputersV2RateLimitedException : ComputersV2Exception
{
    public ComputersV2RateLimitedException(string message) : base(message) { }
}

// ---------- Raw DTOs (back-end shape). Location is intentionally ignored for now. ----------

internal sealed class ComputerDto
{
    public string? Name { get; set; }
}

internal sealed class PingDto
{
    public string? Name { get; set; }
    public string? Ip { get; set; }
    public bool Status { get; set; }
}

internal sealed class ProblemDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}

// Section values arrive as strings but may occasionally be numbers; JsonElement keeps the
// deserialization robust for both.
internal sealed class DetailDto
{
    public Dictionary<string, JsonElement>? Bios { get; set; }
    public Dictionary<string, JsonElement>? ComputerSystem { get; set; }
    public Dictionary<string, JsonElement>? OperatingSystem { get; set; }
    public List<Dictionary<string, JsonElement>>? Processor { get; set; }
    public List<Dictionary<string, JsonElement>>? PhysicalMemory { get; set; }
    public List<Dictionary<string, JsonElement>>? PhysicalDisk { get; set; }
    public List<Dictionary<string, JsonElement>>? LogicalDisk { get; set; }
    public List<Dictionary<string, JsonElement>>? VideoController { get; set; }
    public List<Dictionary<string, JsonElement>>? Monitor { get; set; }
    public List<Dictionary<string, JsonElement>>? NetworkAdapter { get; set; }
    public List<Dictionary<string, JsonElement>>? Printer { get; set; }
    public List<Dictionary<string, JsonElement>>? Software { get; set; }
    public List<Dictionary<string, JsonElement>>? Process { get; set; }
}