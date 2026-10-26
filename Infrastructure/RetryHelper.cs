namespace _20strike_ui.Infrastructure;

/// <summary>
/// Retries an operation with a short linear back-off (1s, 2s, ...) between attempts. Cancellation
/// is honored between attempts; the caller's token aborts immediately.
/// </summary>
public static class RetryHelper
{
    /// <summary>
    /// Runs <paramref name="operation"/> up to <paramref name="maxAttempts"/> times, waiting
    /// <paramref name="attempt"/> seconds between attempts. Returns the last error message when all
    /// attempts fail, or <c>null</c> on success. Throws <see cref="OperationCanceledException"/>
    /// when the token is cancelled.
    /// </summary>
    public static async Task<string?> RunWithBackoffAsync(
        Func<Task> operation,
        int maxAttempts,
        CancellationToken ct)
    {
        string? lastError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await operation().ConfigureAwait(false);
                return null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                lastError = ex.Message;
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        return lastError;
    }
}