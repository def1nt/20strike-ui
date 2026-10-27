using _20strike_ui.Data.ComputersV2;
using _20strike_ui.Infrastructure;

namespace _20strike_ui.Tests;

public class ComputerPingMonitorTests
{
    private sealed class FakeClient : IComputersV2Client
    {
        public Func<CancellationToken, Task<IReadOnlyList<PingInfo>>>? PingHandler { get; set; }
        public bool IsPingThrottled { get; set; }

        public Task<IReadOnlyList<ComputerSummary>> GetComputersAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<ComputerLocationSummary>> GetComputersWithLocationsAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<MapPoint?> GetLocationAsync(string computerName, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task SaveLocationAsync(string computerName, MapPoint location, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task DeleteLocationAsync(string computerName, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<ComputerSummary>> SearchComputersAsync(ComputerSearchType type, string query, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<ComputerDetail?> GetComputerByNameAsync(string computerName, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<PingInfo>> GetPingAsync(CancellationToken ct = default) => PingHandler!(ct);

        public Task<IReadOnlyList<ProblemInfo>> GetProblemsAsync(string computerName, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyDictionary<string, string>> GetUsersAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task Start_RaisesPingsUpdatedWithFreshPayload()
    {
        var client = new FakeClient
        {
            PingHandler = _ => Task.FromResult<IReadOnlyList<PingInfo>>(new[] { new PingInfo("PC-1", "10.0.0.1", true) }),
        };
        using var monitor = new ComputerPingMonitor(client);
        var tcs = new TaskCompletionSource<IReadOnlyList<PingInfo>>();
        monitor.PingsUpdated += pings => tcs.TrySetResult(pings);

        monitor.Start();

        var pings = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("PC-1", Assert.Single(pings).Name);
    }

    [Fact]
    public async Task Start_RaisesPingFailedWhenRefreshThrows()
    {
        var client = new FakeClient
        {
            PingHandler = _ => throw new ComputersV2RateLimitedException("busy"),
        };
        using var monitor = new ComputerPingMonitor(client);
        var tcs = new TaskCompletionSource();
        monitor.PingFailed += () => tcs.TrySetResult();

        monitor.Start();

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Stop_ThenStart_ResumesPolling()
    {
        var client = new FakeClient
        {
            PingHandler = _ => Task.FromResult<IReadOnlyList<PingInfo>>(new[] { new PingInfo("PC-1", "10.0.0.1", true) }),
        };
        using var monitor = new ComputerPingMonitor(client);
        var first = new TaskCompletionSource<IReadOnlyList<PingInfo>>();
        var second = new TaskCompletionSource<IReadOnlyList<PingInfo>>();
        var calls = 0;
        monitor.PingsUpdated += pings =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                first.TrySetResult(pings);
            }
            else
            {
                second.TrySetResult(pings);
            }
        };

        monitor.Start();
        await first.Task.WaitAsync(TimeSpan.FromSeconds(5));

        monitor.Stop();
        monitor.Start(); // a remounted page on the same circuit must get a fresh polling loop

        var pings = await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("PC-1", Assert.Single(pings).Name);
    }

    [Fact]
    public void PageNavigationCycles_DoNotThrow()
    {
        // Regression test for the v2/computers navigation bug: the scoped monitor was disposed by
        // the page on unmount, so revisiting the page on the same circuit reused a disposed monitor
        // and the next unmount threw ObjectDisposedException from CancellationTokenSource.Cancel().
        var client = new FakeClient
        {
            PingHandler = _ => Task.FromResult<IReadOnlyList<PingInfo>>(Array.Empty<PingInfo>()),
        };
        var monitor = new ComputerPingMonitor(client);

        monitor.Start();  // visit /v2/computers
        monitor.Stop();   // navigate away
        monitor.Start();  // navigate back (same circuit, same scoped instance)
        monitor.Stop();   // navigate away again — previously threw
        monitor.Stop();   // extra stop is a no-op

        monitor.Dispose(); // circuit teardown by the DI container
    }

    [Fact]
    public void Dispose_IsIdempotent_AndPreventsRestart()
    {
        var client = new FakeClient
        {
            PingHandler = _ => Task.FromResult<IReadOnlyList<PingInfo>>(Array.Empty<PingInfo>()),
        };
        var monitor = new ComputerPingMonitor(client);

        monitor.Start();
        monitor.Dispose();
        monitor.Dispose(); // container teardown after the page already stopped it — must not throw
        monitor.Start();   // ignored after dispose
        monitor.Stop();
    }
}