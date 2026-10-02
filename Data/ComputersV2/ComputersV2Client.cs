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

    public async Task<IReadOnlyList<PingInfo>> GetPingAsync(CancellationToken ct = default)
    {
        var dtos = await SendAsync<List<PingDto>>("ping", ct, DefaultTimeout).ConfigureAwait(false);
        return ComputersV2Mapper.MapPing(dtos);
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

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new ComputersV2ApiException(status, $"Ошибка сервера {(int)status} для GET /{url}");
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ComputersV2TimeoutException($"Превышено время ожидания данных GET /{url}");
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