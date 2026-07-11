using Kalandra.Infrastructure.Tasks;

namespace Kalandra.Infrastructure.Tests;

public class ObservedTaskExtensionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CompletedWithinTimeout_ReturnsTheResult()
    {
        var result = await Task.FromResult(42).WaitObservedAsync(TimeSpan.FromSeconds(5), Ct);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task HungTask_ThrowsTimeoutException()
    {
        var hung = new TaskCompletionSource();

        await Assert.ThrowsAsync<TimeoutException>(
            () => hung.Task.WaitObservedAsync(TimeSpan.FromMilliseconds(20), Ct));
    }

    [Fact]
    public async Task FaultBeforeTimeout_PropagatesTheOriginalException()
    {
        var failed = Task.FromException(new InvalidOperationException("boom"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => failed.WaitObservedAsync(TimeSpan.FromSeconds(5), Ct));

        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public void FaultAfterTimeout_IsObservedInsteadOfEscalatingToUnobservedTaskException()
    {
        var marker = new InvalidOperationException("late-fault-after-timeout");
        var markerWentUnobserved = false;
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
        {
            if (args.Exception.Flatten().InnerExceptions.Any(inner => ReferenceEquals(inner, marker)))
                markerWentUnobserved = true;
        };

        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            AbandonTaskThenFaultIt(marker);

            // Unobserved-exception escalation happens on the finalizer thread once the task is collected.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.False(markerWentUnobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }

    // Synchronous on purpose: an await here would pin the abandoned task in this frame's awaiter
    // slot (Debug builds extend local lifetimes), keeping it reachable past the GC assertion.
    private static void AbandonTaskThenFaultIt(Exception marker)
    {
        var hung = new TaskCompletionSource();
        var abandoned = hung.Task.WaitObservedAsync(TimeSpan.FromMilliseconds(1), CancellationToken.None);
        Assert.Throws<TimeoutException>(() => abandoned.GetAwaiter().GetResult());
        hung.SetException(marker);
    }
}
