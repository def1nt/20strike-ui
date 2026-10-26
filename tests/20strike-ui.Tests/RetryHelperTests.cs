using _20strike_ui.Infrastructure;

namespace _20strike_ui.Tests;

public class RetryHelperTests
{
    [Fact]
    public async Task RunWithBackoffAsync_ReturnsNullOnFirstSuccess()
    {
        var calls = 0;
        var error = await RetryHelper.RunWithBackoffAsync(() =>
        {
            calls++;
            return Task.CompletedTask;
        }, maxAttempts: 3, CancellationToken.None);

        Assert.Null(error);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RunWithBackoffAsync_RetriesThenSucceeds()
    {
        var calls = 0;
        var error = await RetryHelper.RunWithBackoffAsync(() =>
        {
            calls++;
            if (calls < 2)
            {
                throw new InvalidOperationException("transient");
            }
            return Task.CompletedTask;
        }, maxAttempts: 2, CancellationToken.None);

        Assert.Null(error);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RunWithBackoffAsync_ReturnsLastErrorWhenAllAttemptsFail()
    {
        var error = await RetryHelper.RunWithBackoffAsync(
            () => throw new InvalidOperationException("boom"),
            maxAttempts: 2,
            CancellationToken.None);

        Assert.Equal("boom", error);
    }

    [Fact]
    public async Task RunWithBackoffAsync_ThrowsOnCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The helper surfaces cancellation when the operation observes the token (as the real
        // client does); a cancelled token alone does not abort a completed operation.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RetryHelper.RunWithBackoffAsync(() => Task.FromCanceled(cts.Token), maxAttempts: 3, cts.Token));
    }
}