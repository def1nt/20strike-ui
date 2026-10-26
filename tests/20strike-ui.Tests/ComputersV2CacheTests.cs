using _20strike_ui.Data.ComputersV2;

namespace _20strike_ui.Tests;

public class ComputersV2CacheTests
{
    [Fact]
    public async Task GetOrLoadDetailAsync_DeduplicatesConcurrentLoads()
    {
        var cache = new ComputersV2Cache();
        var calls = 0;
        Task<ComputerDetail?> Loader(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<ComputerDetail?>(new ComputerDetail());
        }

        var t1 = cache.GetOrLoadDetailAsync("PC-1", Loader);
        var t2 = cache.GetOrLoadDetailAsync("PC-1", Loader);
        await Task.WhenAll(t1, t2);

        Assert.Equal(1, calls);
        Assert.NotNull((await t1).Detail);
    }

    [Fact]
    public async Task GetOrLoadDetailAsync_InvalidateReloads()
    {
        var cache = new ComputersV2Cache();
        var calls = 0;
        Task<ComputerDetail?> Loader(CancellationToken ct)
        {
            calls++;
            return Task.FromResult<ComputerDetail?>(new ComputerDetail());
        }

        await cache.GetOrLoadDetailAsync("PC-1", Loader);
        cache.InvalidateDetail("PC-1");
        await cache.GetOrLoadDetailAsync("PC-1", Loader);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetOrLoadDetailAsync_WrapsFailureIntoResult()
    {
        var cache = new ComputersV2Cache();
        Task<ComputerDetail?> Loader(CancellationToken ct) => throw new ComputersV2ApiException(null, "boom");

        var result = await cache.GetOrLoadDetailAsync("PC-1", Loader);

        Assert.Null(result.Detail);
        Assert.Equal("boom", result.ErrorMessage);
    }

    [Fact]
    public async Task GetOrLoadDetailAsync_NotFoundReturnsErrorMessage()
    {
        var cache = new ComputersV2Cache();
        Task<ComputerDetail?> Loader(CancellationToken ct) => Task.FromResult<ComputerDetail?>(null);

        var result = await cache.GetOrLoadDetailAsync("PC-1", Loader);

        Assert.Null(result.Detail);
        Assert.Equal("Компьютер не найден.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetOrLoadProblemsAsync_DeduplicatesAndInvalidates()
    {
        var cache = new ComputersV2Cache();
        var calls = 0;
        Task<IReadOnlyList<ProblemInfo>> Loader(CancellationToken ct)
        {
            calls++;
            return Task.FromResult<IReadOnlyList<ProblemInfo>>(Array.Empty<ProblemInfo>());
        }

        await cache.GetOrLoadProblemsAsync("PC-1", Loader);
        await cache.GetOrLoadProblemsAsync("PC-1", Loader);
        Assert.Equal(1, calls);

        cache.InvalidateProblems("PC-1");
        await cache.GetOrLoadProblemsAsync("PC-1", Loader);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetOrLoadUsersAsync_LoadsOnceAndInvalidates()
    {
        var cache = new ComputersV2Cache();
        var calls = 0;
        Task<IReadOnlyDictionary<string, string>> Loader(CancellationToken ct)
        {
            calls++;
            return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
        }

        await cache.GetOrLoadUsersAsync(Loader);
        await cache.GetOrLoadUsersAsync(Loader);
        Assert.Equal(1, calls);

        cache.InvalidateUsers();
        await cache.GetOrLoadUsersAsync(Loader);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetOrLoadUsersAsync_WrapsFailureIntoResult()
    {
        var cache = new ComputersV2Cache();
        Task<IReadOnlyDictionary<string, string>> Loader(CancellationToken ct) => throw new InvalidOperationException("boom");

        var result = await cache.GetOrLoadUsersAsync(Loader);

        Assert.Empty(result.UsersByLogin);
        Assert.Equal("boom", result.ErrorMessage);
    }
}