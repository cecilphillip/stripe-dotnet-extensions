using Aspire.Hosting;
using Xunit;

namespace Stripe.Hosting.Aspire.Tests;

/// <summary>
/// Pins the timeout guard that keeps instance discovery from blocking resource startup indefinitely.
/// </summary>
/// <remarks>
/// The surrounding <c>OnBeforeResourceStarted</c> path needs DCP to exercise end to end, so the race
/// itself is tested through the extracted seam. The invariant that matters is not just "it gives up"
/// but that giving up does <em>not</em> cancel the in-flight work — the log watcher must still attach
/// if the resource instance appears late.
/// </remarks>
public class InstanceDiscoveryTimeoutTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task CompletedWithinTimeoutAsync_ReturnsTrue_WhenWorkFinishesFirst()
    {
        var completed = await StripeCliBuilderExtensions.CompletedWithinTimeoutAsync(
            Task.CompletedTask,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.True(completed);
    }

    [Fact]
    public async Task CompletedWithinTimeoutAsync_ReturnsFalse_WhenWorkExceedsTimeout()
    {
        var neverCompletes = new TaskCompletionSource();

        var completed = await StripeCliBuilderExtensions.CompletedWithinTimeoutAsync(
            neverCompletes.Task,
            ShortTimeout,
            CancellationToken.None);

        Assert.False(completed);
    }

    [Fact]
    public async Task CompletedWithinTimeoutAsync_LeavesWorkRunning_AfterTimeout()
    {
        var neverCompletes = new TaskCompletionSource();

        var completed = await StripeCliBuilderExtensions.CompletedWithinTimeoutAsync(
            neverCompletes.Task,
            ShortTimeout,
            CancellationToken.None);

        Assert.False(completed);

        // The work must survive the timeout so late-arriving instances are still handled.
        Assert.False(neverCompletes.Task.IsCompleted);
        Assert.False(neverCompletes.Task.IsCanceled);

        // ...and it must still be observable once it eventually finishes.
        neverCompletes.SetResult();
        await neverCompletes.Task;
    }

    [Fact]
    public async Task CompletedWithinTimeoutAsync_ReturnsWhenWorkCompletesJustBeforeTimeout()
    {
        var work = new TaskCompletionSource();
        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            work.SetResult();
        });

        var completed = await StripeCliBuilderExtensions.CompletedWithinTimeoutAsync(
            work.Task,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.True(completed);
    }

    [Fact]
    public async Task CompletedWithinTimeoutAsync_StopsWaiting_WhenCallerTokenIsCancelled()
    {
        var neverCompletes = new TaskCompletionSource();
        using var cancellation = new CancellationTokenSource();

        var pending = StripeCliBuilderExtensions.CompletedWithinTimeoutAsync(
            neverCompletes.Task,
            TimeSpan.FromMinutes(5),
            cancellation.Token);

        cancellation.Cancel();

        // Shutdown must not hang for the full timeout window.
        var completed = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(completed);
    }
}
