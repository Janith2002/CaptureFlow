using System.Diagnostics;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Infrastructure.Video;

/// <summary>Serializes native controls and provides one shared, awaited disposal result.</summary>
internal sealed class CameraSessionLifetime : ICameraCaptureSession
{
    private readonly ICameraCaptureSession native;
    private readonly CameraOwnerContext owner;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object sync = new();
    private readonly string id = Guid.NewGuid().ToString("N");
    private Task? disposal;
    private volatile bool closing;
    private int state = (int)CameraLifecycleState.Ready;
    public CameraLifecycleState LifecycleState => (CameraLifecycleState)Volatile.Read(ref state);
    public VideoSize Size => native.Size;
    public IReadOnlyList<CameraFormat> SupportedFormats { get; }
    public event EventHandler<Exception>? Failed;
    public CameraSessionLifetime(ICameraCaptureSession native, CameraOwnerContext owner)
    {
        this.native = native; this.owner = owner; SupportedFormats = native.SupportedFormats.ToArray(); native.Failed += OnFailure;
        Transition(CameraLifecycleState.Ready);
    }
    private void OnFailure(object? sender, Exception error)
    {
        lock (sync)
        {
            if (closing || !ReferenceEquals(sender, native)) return;
            Transition(CameraLifecycleState.Faulted); Trace.TraceError($"Camera {id}: native failure HRESULT=0x{error.HResult:X8}; {error}");
        }
        if (!closing) Failed?.Invoke(this, error);
    }
    public async Task ConfigureAsync(CameraFormat format, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            CheckUsable(); Transition(CameraLifecycleState.Stopping);
            await owner.InvokeAsync(() => native.ConfigureAsync(format, token)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); lock (sync) { CheckUsable(); Transition(CameraLifecycleState.Ready); }
        }
        catch { if (!closing) Transition(CameraLifecycleState.Faulted); throw; }
        finally { gate.Release(); }
    }
    public async Task StartAsync(CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            CheckUsable();
            if (LifecycleState == CameraLifecycleState.Capturing) return;
            if (LifecycleState != CameraLifecycleState.Ready) throw new InvalidOperationException("Configure the camera before starting capture.");
            await owner.InvokeAsync(() => native.StartAsync(token)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); lock (sync) { CheckUsable(); Transition(CameraLifecycleState.Capturing); }
        }
        catch { if (!closing) Transition(CameraLifecycleState.Faulted); throw; }
        finally { gate.Release(); }
    }
    public async ValueTask<VideoFrame> ReadFrameAsync(CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { CheckUsable(); if (LifecycleState != CameraLifecycleState.Capturing) throw new InvalidOperationException("Camera capture is stopped."); return await native.ReadFrameAsync(token).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    private void CheckUsable()
    { lock (sync) { ObjectDisposedException.ThrowIf(closing, this); if (LifecycleState == CameraLifecycleState.Faulted) throw new IOException("Reload the camera after a native failure."); } }
    public ValueTask DisposeAsync()
    {
        lock (sync) { closing = true; disposal ??= CloseAsync(); return new(disposal); }
    }
    private async Task CloseAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Transition(CameraLifecycleState.Disposing);
            await owner.InvokeAsync(async () => { native.Failed -= OnFailure; await native.DisposeAsync(); }).ConfigureAwait(false);
            Transition(CameraLifecycleState.Disposed);
        }
        catch (Exception ex) { Transition(CameraLifecycleState.Faulted); Trace.TraceError($"Camera {id}: cleanup failed; {ex}"); throw; }
        finally { gate.Release(); }
    }
    private void Transition(CameraLifecycleState value)
    {
        Volatile.Write(ref state, (int)value);
        Trace.TraceInformation($"Camera {id}: {value}; managed thread={Environment.CurrentManagedThreadId}; apartment={Thread.CurrentThread.GetApartmentState()}");
    }
}
