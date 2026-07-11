namespace Kalandra.Infrastructure.Tasks;

/// <summary>
/// <see cref="Task.WaitAsync(TimeSpan, CancellationToken)"/> for clients that ignore
/// <see cref="CancellationToken"/>: the task a timeout abandons keeps running, and its
/// eventual failure must be observed or it resurfaces on the finalizer thread as an
/// UnobservedTaskException.
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
