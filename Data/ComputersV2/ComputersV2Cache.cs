using System.Collections.Concurrent;

namespace _20strike_ui.Data.ComputersV2;

/// <summary>
/// Scoped (per-circuit) cache for computer detail records and per-computer problem lists.
/// Deduplicates concurrent loads, keeps loaded data across re-expands and page navigation within
/// the circuit, and lets a failed load be retried by invalidating its entry.
///
/// The shared load deliberately runs without the caller's cancellation token: one subscriber
/// leaving (e.g. component unmount) must not abort the fetch for everyone else. Per-call timeouts
/// in <see cref="ComputersV2Client"/> still apply, and the awaiting side observes caller
/// cancellation via <see cref="Task.WaitAsync(CancellationToken)"/>.
/// </summary>
public sealed class ComputersV2Cache
{
    private readonly ConcurrentDictionary<string, Lazy<Task<DetailCacheResult>>> _details = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<ProblemInfo>>>> _problems = new(StringComparer.OrdinalIgnoreCase);

    public Task<DetailCacheResult> GetOrLoadDetailAsync(
        string computerName,
        Func<CancellationToken, Task<ComputerDetail?>> loader,
        CancellationToken ct = default)
    {
        var lazy = _details.GetOrAdd(computerName,
            _ => new Lazy<Task<DetailCacheResult>>(() => LoadDetailCore(loader), isThreadSafe: true));
        return AwaitWithCancellationAsync(lazy.Value, ct);
    }

    public void InvalidateDetail(string computerName) => _details.TryRemove(computerName, out _);

    public Task<IReadOnlyList<ProblemInfo>> GetOrLoadProblemsAsync(
        string computerName,
        Func<CancellationToken, Task<IReadOnlyList<ProblemInfo>>> loader,
        CancellationToken ct = default)
    {
        var lazy = _problems.GetOrAdd(computerName,
            _ => new Lazy<Task<IReadOnlyList<ProblemInfo>>>(() => LoadProblemsCore(loader), isThreadSafe: true));
        return AwaitWithCancellationAsync(lazy.Value, ct);
    }

    public void InvalidateProblems(string computerName) => _problems.TryRemove(computerName, out _);

    private Lazy<Task<UsersResult>>? _users;

    /// <summary>
    /// Returns the once-per-circuit login-to-full-name map (GET /v2/users). The first caller
    /// creates the single shared load; concurrent card opens and later re-expands reuse the same
    /// result, so the endpoint is hit at most once per circuit. Failures are wrapped into
    /// <see cref="UsersResult"/> (empty map + message) so the UI can fall back to plain usernames
    /// instead of surfacing an exception.
    /// </summary>
    public Task<UsersResult> GetOrLoadUsersAsync(
        Func<CancellationToken, Task<IReadOnlyDictionary<string, string>>> loader,
        CancellationToken ct = default)
    {
        var current = _users;
        if (current is null)
        {
            var created = new Lazy<Task<UsersResult>>(() => LoadUsersCore(loader), isThreadSafe: true);
            current = Interlocked.CompareExchange(ref _users, created, null) ?? created;
        }

        return AwaitWithCancellationAsync(current.Value, ct);
    }

    /// <summary>Forces the next <see cref="GetOrLoadUsersAsync"/> call to re-fetch (used by the retry link).</summary>
    public void InvalidateUsers() => Interlocked.Exchange(ref _users, null);

    private static async Task<UsersResult> LoadUsersCore(Func<CancellationToken, Task<IReadOnlyDictionary<string, string>>> loader)
    {
        try
        {
            var users = await loader(CancellationToken.None).ConfigureAwait(false);
            return new UsersResult(users, null);
        }
        catch (Exception ex)
        {
            return new UsersResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), ex.Message);
        }
    }

    private static async Task<DetailCacheResult> LoadDetailCore(Func<CancellationToken, Task<ComputerDetail?>> loader)
    {
        try
        {
            var detail = await loader(CancellationToken.None).ConfigureAwait(false);
            if (detail is null)
            {
                return new DetailCacheResult(null, "Компьютер не найден.");
            }

            return new DetailCacheResult(detail, null);
        }
        catch (ComputersV2TimeoutException ex)
        {
            return new DetailCacheResult(null, ex.Message);
        }
        catch (ComputersV2ApiException ex)
        {
            return new DetailCacheResult(null, ex.Message);
        }
        catch (Exception ex)
        {
            return new DetailCacheResult(null, $"Не удалось загрузить данные: {ex.Message}");
        }
    }

    private static async Task<IReadOnlyList<ProblemInfo>> LoadProblemsCore(Func<CancellationToken, Task<IReadOnlyList<ProblemInfo>>> loader)
    {
        // Failures propagate to callers; Retry invalidates the entry and reloads.
        return await loader(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task<T> AwaitWithCancellationAsync<T>(Task<T> task, CancellationToken ct)
    {
        if (!ct.CanBeCanceled)
        {
            return await task.ConfigureAwait(false);
        }

        try
        {
            return await task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
    }
}