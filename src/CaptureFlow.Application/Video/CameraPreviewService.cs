using CaptureFlow.Core.Video;
using System.Diagnostics;
namespace CaptureFlow.Application.Video;

/// <summary>Serializes camera ownership and lends the existing preview session to the video recorder.</summary>
public sealed class CameraPreviewService(ICameraCaptureBackend backend, TimeSpan? operationTimeout = null, TimeProvider? timeProvider = null) : ICameraPreviewService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private ICameraCaptureSession? session;
    private bool recording;
    private volatile bool disposed;
    private volatile Exception? failure;
    private readonly object lifetimeSync = new();
    private CancellationTokenSource? activeCancellation;
    private Task? disposal;
    private volatile bool closing;
    private volatile Exception? cleanupFailure;
    private int lifecycleState;
    public CameraLifecycleState LifecycleState => (CameraLifecycleState)Volatile.Read(ref lifecycleState);
    public event EventHandler<Exception>? Failed;
    public bool IsPreviewing { get; private set; }
    public string? DeviceId { get; private set; }
    public CameraFormat? ActiveFormat { get; private set; }
    public async Task<IReadOnlyList<CameraDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        CheckOpen(); cancellationToken.ThrowIfCancellationRequested();
        var devices = await backend.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested(); CheckOpen();
        return devices;
    }
    public async Task<IReadOnlyList<CameraFormat>> SelectCameraAsync(string deviceId, CancellationToken cancellationToken = default)
    { CheckOpen(); return await BoundedAsync(SelectCoreAsync(deviceId, cancellationToken)).ConfigureAwait(false); }
    private async Task<IReadOnlyList<CameraFormat>> SelectCoreAsync(string deviceId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? operation = null;
        try
        {
            CheckOpen();
            if (recording) throw new InvalidOperationException("Stop recording before changing cameras.");
            await CloseAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("Select a camera.");
            operation = BeginOperation(cancellationToken); cancellationToken = operation.Token;
            Transition(CameraLifecycleState.Initializing);
            var opened = await backend.OpenAsync(deviceId, cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (opened.SupportedFormats.Count == 0) throw new NotSupportedException("This camera exposes no supported color video formats.");
                foreach (var format in opened.SupportedFormats) format.Validate();
                if (opened.LifecycleState == CameraLifecycleState.Faulted) throw new IOException("Camera failed during initialization. Reload the device.");
                lock (lifetimeSync) { session = opened; DeviceId = deviceId; failure = null; opened.Failed += CaptureFailed; Transition(CameraLifecycleState.Ready); }
                return opened.SupportedFormats.ToArray();
            }
            catch
            {
                if (ReferenceEquals(session, opened)) await CloseAsync().ConfigureAwait(false);
                else
                {
                    try { await opened.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception ex) { cleanupFailure = ex; Trace.TraceError($"Unpublished camera cleanup failed; reuse blocked: {ex}"); throw; }
                }
                throw;
            }
        }
        catch (Exception ex) { if (operation is not null && !closing) Transition(CameraLifecycleState.Faulted); Trace.TraceError($"Camera selection failed: {ex}"); throw; }
        finally { EndOperation(operation); gate.Release(); }
    }
    public async Task StartPreviewAsync(CameraFormat format, CancellationToken cancellationToken = default)
    { CheckOpen(); await BoundedAsync(StartCoreAsync(format, cancellationToken)).ConfigureAwait(false); }
    private async Task StartCoreAsync(CameraFormat format, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? operation = null;
        try
        {
            CheckOpen();
            if (recording) throw new InvalidOperationException("Stop recording before changing the preview format.");
            var current = session ?? throw new InvalidOperationException("Load a camera first.");
            if (!current.SupportedFormats.Contains(format)) throw new NotSupportedException("The selected format was not reported by this camera.");
            if (failure is not null) throw new IOException("Reload the camera after a capture failure.", failure);
            if (IsPreviewing && ActiveFormat == format) return;
            operation = BeginOperation(cancellationToken); cancellationToken = operation.Token;
            IsPreviewing = false; ActiveFormat = null;
            Transition(CameraLifecycleState.Initializing);
            try
            {
                await current.ConfigureAsync(format, cancellationToken).ConfigureAwait(false);
                await current.StartAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                lock (lifetimeSync)
                {
                    if (failure is not null) throw failure;
                    ActiveFormat = format; IsPreviewing = true; Transition(CameraLifecycleState.Capturing);
                }
            }
            catch { await CloseAsync().ConfigureAwait(false); throw; }
        }
        finally { EndOperation(operation); gate.Release(); }
    }
    public Task StopPreviewAsync()
    { CancelActiveOperation(); return BoundedAsync(StopCoreAsync()); }
    private async Task StopCoreAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (recording) throw new InvalidOperationException("Stop recording before releasing the camera.");
            await CloseAsync().ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public async ValueTask<VideoFrame> ReadPreviewFrameAsync(CancellationToken cancellationToken = default)
    {
        CheckOpen();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CheckOpen();
            if (!IsPreviewing || session is null) throw new InvalidOperationException("Camera preview is stopped.");
            try { return await session.ReadFrameAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { CaptureFailed(session, ex); throw; }
        }
        finally { gate.Release(); }
    }
    public async Task<IVideoFrameSource> AcquireRecordingSourceAsync(VideoSourceOptions options, CancellationToken cancellationToken)
    {
        CheckOpen();
        if (LifecycleState is CameraLifecycleState.Initializing or CameraLifecycleState.Stopping or CameraLifecycleState.Disposing)
            throw new InvalidOperationException("Wait for the camera transition to finish before recording.");
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CheckOpen();
            if (recording) throw new InvalidOperationException("This camera already has an active recording session.");
            if (options.Kind != VideoSourceKind.Webcam || options.Id != DeviceId || options.CameraFormat != ActiveFormat || !IsPreviewing || session is null)
                throw new InvalidOperationException("Start preview for the selected camera and format before recording.");
            if (failure is not null) throw failure;
            recording = true;
            return new RecordingLease(this, session);
        }
        finally { gate.Release(); }
    }
    private void CaptureFailed(object? sender, Exception error)
    {
        lock (lifetimeSync)
        {
            if (closing || disposed || !ReferenceEquals(sender, session)) return;
            failure = error; IsPreviewing = false; Transition(CameraLifecycleState.Faulted);
            Trace.TraceError($"Camera session failure: {error}");
        }
        Failed?.Invoke(this, error);
        if (sender is ICameraCaptureSession current) _ = CloseFailedSessionAsync(current);
    }
    private async Task CloseFailedSessionAsync(ICameraCaptureSession current)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { if (!recording && ReferenceEquals(session, current)) await CloseAsync().ConfigureAwait(false); }
        catch (Exception ex) { Failed?.Invoke(this, ex); }
        finally { gate.Release(); }
    }
    private async Task CloseAsync()
    {
        ICameraCaptureSession? current;
        lock (lifetimeSync) { current = session; session = null; DeviceId = null; ActiveFormat = null; IsPreviewing = false; failure = null; }
        if (current is not null)
        {
            Transition(CameraLifecycleState.Stopping); current.Failed -= CaptureFailed;
            try { await current.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { cleanupFailure = ex; Transition(CameraLifecycleState.Faulted); Trace.TraceError($"Camera cleanup failed; reuse is blocked: {ex}"); throw; }
        }
        if (cleanupFailure is null) Transition(CameraLifecycleState.Uninitialized);
    }
    public ValueTask DisposeAsync()
    {
        lock (lifetimeSync) { closing = true; CancelActiveOperation(); disposal ??= BoundedAsync(DisposeCoreAsync()); return new(disposal); }
    }
    private async Task DisposeCoreAsync()
    {
        await Task.Yield();
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return; disposed = true; Transition(CameraLifecycleState.Disposing); await CloseAsync().ConfigureAwait(false);
            if (cleanupFailure is not null && cleanupFailure is not TimeoutException) { Transition(CameraLifecycleState.Faulted); throw new IOException("Camera native cleanup failed; shutdown cannot confirm device release.", cleanupFailure); }
            Transition(CameraLifecycleState.Disposed);
        }
        finally { gate.Release(); }
    }
    private void CheckOpen()
    {
        ObjectDisposedException.ThrowIf(disposed || closing, this);
        if (cleanupFailure is not null) throw new IOException("Camera cleanup did not complete safely. Restart CaptureFlow before opening another camera.", cleanupFailure);
    }
    private CancellationTokenSource BeginOperation(CancellationToken token)
    {
        lock (lifetimeSync) { CheckOpen(); return activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(token); }
    }
    private void EndOperation(CancellationTokenSource? operation)
    { lock (lifetimeSync) { if (ReferenceEquals(activeCancellation, operation)) activeCancellation = null; operation?.Dispose(); } }
    private void CancelActiveOperation() { lock (lifetimeSync) activeCancellation?.Cancel(); }
    private void Transition(CameraLifecycleState value) => Volatile.Write(ref lifecycleState, (int)value);
    private async Task<T> BoundedAsync<T>(Task<T> task)
    {
        try { return await task.WaitAsync(operationTimeout ?? TimeSpan.FromSeconds(30), timeProvider ?? TimeProvider.System).ConfigureAwait(false); }
        catch (TimeoutException ex)
        {
            cleanupFailure = ex; Transition(CameraLifecycleState.Faulted); CancelActiveOperation();
            Trace.TraceError("Camera operation timed out; native work remains coordinated and camera reuse is blocked."); _ = ObserveAsync(task); throw;
        }
    }
    private async Task BoundedAsync(Task task) { await BoundedAsync(CompleteAsync(task)).ConfigureAwait(false); }
    private static async Task<bool> CompleteAsync(Task task) { await task.ConfigureAwait(false); return true; }
    private static async Task ObserveAsync(Task task)
    { try { await task.ConfigureAwait(false); } catch (Exception ex) { Trace.TraceError($"Deferred camera operation ended: {ex}"); } }
    private sealed class RecordingLease : IVideoFrameSource
    {
        private readonly CameraPreviewService owner;
        private readonly ICameraCaptureSession session;
        private int disposed;
        public RecordingLease(CameraPreviewService owner, ICameraCaptureSession session)
        { this.owner = owner; this.session = session; session.Failed += FailedForward; }
        public event EventHandler<Exception>? Failed;
        private void FailedForward(object? sender, Exception error) { if (Volatile.Read(ref disposed) == 0) Failed?.Invoke(this, error); }
        public VideoSize Size => session.Size;
        public Task StartAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public async ValueTask<VideoFrame> ReadFrameAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            await owner.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { owner.CheckOpen(); if (!ReferenceEquals(owner.session, session)) throw new ObjectDisposedException(nameof(RecordingLease)); return await session.ReadFrameAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { owner.CaptureFailed(session, ex); throw; }
            finally { owner.gate.Release(); }
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            session.Failed -= FailedForward;
            await owner.gate.WaitAsync().ConfigureAwait(false);
            try { if (ReferenceEquals(owner.session, session)) { owner.recording = false; if (owner.failure is not null) await owner.CloseAsync().ConfigureAwait(false); } }
            finally { owner.gate.Release(); }
        }
    }
}

public static class CameraFormatSelection
{
    public static IReadOnlyList<string> Resolutions(IEnumerable<CameraFormat> formats) => formats
        .OrderByDescending(f => f.Width * (long)f.Height).Select(f => f.ResolutionLabel).Distinct().ToArray();
    public static IReadOnlyList<CameraFormat> FrameRates(IEnumerable<CameraFormat> formats, string resolution) => formats
        .Where(f => f.ResolutionLabel == resolution).OrderBy(f => f.FramesPerSecond)
        .DistinctBy(f => Math.Round(f.FramesPerSecond, 6)).ToArray();
}
