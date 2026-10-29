namespace Kalandra.Infrastructure.Tasks;

/// <summary>
/// For clients that ignore cancellation: the task a timeout abandons keeps running, and its fault must be observed or it surfaces as an UnobservedTaskException.
/// </summary>
public static class ObservedTaskExtensions
{
    public static Task WaitObservedAsync(this Task task, TimeSpan timeout, CancellationToken ct)
    {
        ObserveEventualFault(task);
        return task.WaitAsync(timeout, ct);
    }

    public static Task<TResult> WaitObservedAsync<TResult>(this Task<TResult> task, TimeSpan timeout, CancellationToken ct)
    {
        ObserveEventualFault(task);
        return task.WaitAsync(timeout, ct);
    }

    private static void ObserveEventualFault(Task task) =>
        task.ContinueWith(static faulted => _ = faulted.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
