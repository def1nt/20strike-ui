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