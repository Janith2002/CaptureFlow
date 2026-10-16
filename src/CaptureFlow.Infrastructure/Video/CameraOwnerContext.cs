namespace CaptureFlow.Infrastructure.Video;

/// <summary>Keeps creation, native control, event subscriptions and close on one running STA context.</summary>
internal sealed class CameraOwnerContext(SynchronizationContext context)
{
    public Task<T> InvokeAsync<T>(Func<Task<T>> action)
    {
        if (SynchronizationContext.Current == context) return RunAsync(action);
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(async _ =>
        {
            try { result.TrySetResult(await RunAsync(action)); }
            catch (OperationCanceledException ex) { result.TrySetCanceled(ex.CancellationToken); }
            catch (Exception ex) { result.TrySetException(ex); }
        }, null);
        return result.Task;
    }
    public Task InvokeAsync(Func<Task> action) => InvokeAsync(async () => { await action(); return true; });
    private static Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Camera operations require the running STA owner context.");
        return action();
    }
}
