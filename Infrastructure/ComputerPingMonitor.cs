using _20strike_ui.Data.ComputersV2;

namespace _20strike_ui.Infrastructure;

/// <summary>
/// Scoped (per-circuit) ping poller for the computer list. Refreshes immediately on start, then
/// every 30 seconds (5 seconds while the last refresh failed or the endpoint is throttled) so the
/// UI self-heals when the backend rate limiter recovers. The page subscribes to
/// <see cref="PingsUpdated"/> / <see cref="PingFailed"/> and calls <see cref="Start"/> /
/// <see cref="Dispose"/>.
/// </summary>
public sealed class ComputerPingMonitor : IDisposable
{
    private readonly IComputersV2Client _client;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private bool _lastRefreshFailed;

    public ComputerPingMonitor(IComputersV2Client client)
    {
        _client = client;
    }

    /// <summary>Raised after each successful refresh with the fresh ping payload.</summary>
    public event Action<IReadOnlyList<PingInfo>>? PingsUpdated;

    /// <summary>Raised when a refresh failed (e.g. backend rate limiting).</summary>
    public event Action? PingFailed;

    /// <summary>True while the /ping endpoint is known to be throttled (stale data is being served).</summary>
    public bool IsThrottled => _client.IsPingThrottled;

    /// <summary>Starts the polling loop. Safe to call once; subsequent calls are ignored.</summary>
    public void Start()
    {
        _loop ??= PollLoopAsync();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private async Task PollLoopAsync()
    {
        try
        {
            while (true)
            {
                await RefreshAsync().ConfigureAwait(false);
                await Task.Delay(IsThrottled || _lastRefreshFailed ? PingRetryInterval : PingPollInterval, _cts.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped on unmount.
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var pings = await _client.GetPingAsync(_cts.Token).ConfigureAwait(false);
            _lastRefreshFailed = false;
            PingsUpdated?.Invoke(pings);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            // Stopped on unmount.
        }
        catch
        {
            _lastRefreshFailed = true;
            PingFailed?.Invoke();
        }
    }

    private static readonly TimeSpan PingPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PingRetryInterval = TimeSpan.FromSeconds(5);
}