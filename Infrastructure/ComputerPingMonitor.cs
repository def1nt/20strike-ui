using _20strike_ui.Data.ComputersV2;

namespace _20strike_ui.Infrastructure;

/// <summary>
/// Scoped (per-circuit) ping poller for the computer list. Refreshes immediately on start, then
/// every 30 seconds (5 seconds while the last refresh failed or the endpoint is throttled) so the
/// UI self-heals when the backend rate limiter recovers. The page subscribes to
/// <see cref="PingsUpdated"/> / <see cref="PingFailed"/> and calls <see cref="Start"/> on mount
/// and <see cref="Stop"/> on unmount.
/// </summary>
/// <remarks>
/// The monitor is a scoped service owned by the DI container: within a circuit it outlives
/// individual page instances and is disposed by the container at circuit teardown. A page must
/// therefore call <see cref="Stop"/> (not <see cref="Dispose"/>) on unmount so that a remounted
/// page on the same circuit can restart polling. <see cref="Start"/> / <see cref="Stop"/> cycles
/// are safe; <see cref="Dispose"/> is idempotent.
/// </remarks>
public sealed class ComputerPingMonitor : IDisposable
{
    private readonly IComputersV2Client _client;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts; // one per polling run; replaced on every Start
    private Task? _loop;
    private bool _lastRefreshFailed;
    private bool _disposed;

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

    /// <summary>
    /// Starts the polling loop. Ignored while a loop is already running or after
    /// <see cref="Dispose"/>; resumes polling with a fresh loop after <see cref="Stop"/>.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _loop is not null)
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _cts = cts;
            _loop = PollLoopAsync(cts);
        }
    }

    /// <summary>
    /// Stops the polling loop, keeping the monitor usable: a subsequent <see cref="Start"/>
    /// begins a fresh loop. Safe to call when no loop is running.
    /// </summary>
    public void Stop() => StopCore();

    /// <summary>
    /// Permanently shuts the monitor down; later <see cref="Start"/> calls are ignored.
    /// Idempotent — also invoked by the DI container when the circuit scope is disposed.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        StopCore();
    }

    private void StopCore()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _cts;
            _cts = null;
            _loop = null;
        }

        if (cts is null)
        {
            return;
        }

        // Each run owns its CTS and only the stopper that took it from the field disposes it,
        // so Cancel/Dispose can never race a second Dispose of the same source.
        cts.Cancel();
        cts.Dispose();
    }

    private async Task PollLoopAsync(CancellationTokenSource cts)
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                await RefreshAsync(cts).ConfigureAwait(false);
                await Task.Delay(IsThrottled || _lastRefreshFailed ? PingRetryInterval : PingPollInterval, cts.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped on unmount.
        }
    }

    private async Task RefreshAsync(CancellationTokenSource cts)
    {
        try
        {
            var pings = await _client.GetPingAsync(cts.Token).ConfigureAwait(false);
            _lastRefreshFailed = false;
            PingsUpdated?.Invoke(pings);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
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
