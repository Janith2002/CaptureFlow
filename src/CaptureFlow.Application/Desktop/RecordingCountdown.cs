using CaptureFlow.Core.Desktop;
namespace CaptureFlow.Application.Desktop;

/// <summary>Owns a cancellable pre-start delay, not a recording session. Uses monotonic deadlines.</summary>
public sealed class RecordingCountdown(ICountdownPresentation? presentation = null, TimeProvider? timeProvider = null) : IRecordingCountdown
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private CancellationTokenSource? cancellation;
    private int running;
    private TaskCompletionSource? completion;
    public event EventHandler? Changed;
    public bool IsRunning => Volatile.Read(ref running) != 0;
    public int Remaining { get; private set; }
    public void Cancel() { try { cancellation?.Cancel(); } catch (ObjectDisposedException) { } }
    public Task CancelAndWaitAsync() { var pending = completion?.Task ?? Task.CompletedTask; Cancel(); return pending; }
    public async Task RunAsync(CountdownRequest request, CancellationToken cancellationToken)
    {
        new RecordingExperiencePreferences(request.Seconds).Validate(); cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0) throw new InvalidOperationException("A recording countdown is already running.");
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); completion = finished;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); cancellation = linked;
        try
        {
            var started = clock.GetTimestamp();
            for (var number = request.Seconds; number > 0; number--)
            {
                Remaining = number; Changed?.Invoke(this, EventArgs.Empty); presentation?.Show(request, number, Cancel);
                var deadline = TimeSpan.FromSeconds(request.Seconds - number + 1);
                var delay = deadline - clock.GetElapsedTime(started);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, clock, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            try { if (request.Seconds > 0 && presentation is not null) await presentation.CloseAsync(); }
            finally { cancellation = null; Remaining = 0; Volatile.Write(ref running, 0); try { Changed?.Invoke(this, EventArgs.Empty); } finally { finished.TrySetResult(); } }
        }
        linked.Token.ThrowIfCancellationRequested();
    }
}
