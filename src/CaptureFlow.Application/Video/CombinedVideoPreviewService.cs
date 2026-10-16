using System.Diagnostics;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Application.Video;

/// <summary>Owns coordinated screen/camera capture and lends a single composited source to the recorder.</summary>
public sealed class CombinedVideoPreviewService(IMonitorCaptureBackend screens, ICameraPreviewService cameras,
    IVideoCompositor compositor, IOverlayLayoutStore layouts) : ICombinedVideoPreviewService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private CompositeSource? source;
    private bool disposed;
    public event EventHandler<Exception>? Failed;
    public bool IsPreviewing { get; private set; }
    public bool IsRecording { get; private set; }
    public VideoSize? Size => source?.Size;
    public VideoSourceOptions? ActiveOptions { get; private set; }
    public async Task StartPreviewAsync(VideoSourceOptions options, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (IsRecording) throw new InvalidOperationException("Stop recording before changing the combined preview.");
            new VideoRecordingConfiguration(options, Path.GetTempPath()).Validate();
            if (options.Kind != VideoSourceKind.ScreenAndWebcam) throw new ArgumentException("Select Screen + Webcam.");
            await CloseAsync().ConfigureAwait(false);
            IVideoFrameSource? screen = null, camera = null;
            try
            {
                camera = await cameras.AcquireRecordingSourceAsync(new(VideoSourceKind.Webcam, options.CameraId!, CameraFormat: options.CameraFormat), cancellationToken).ConfigureAwait(false);
                screen = await screens.CreateAsync(options.ScreenSource, cancellationToken).ConfigureAwait(false);
                source = new CompositeSource(screen, camera, compositor, layouts, options.CameraFormat!.FramesPerSecond);
                screen = null; camera = null;
                source.Failed += CaptureFailed;
                await source.StartAsync(cancellationToken).ConfigureAwait(false);
                ActiveOptions = options; IsPreviewing = true;
            }
            catch
            {
                try { if (screen is not null) await screen.DisposeAsync().ConfigureAwait(false); }
                finally { try { if (camera is not null) await camera.DisposeAsync().ConfigureAwait(false); } finally { await CloseAsync().ConfigureAwait(false); } }
                throw;
            }
        }
        finally { gate.Release(); }
    }
    public async Task StopPreviewAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { if (IsRecording) throw new InvalidOperationException("Stop recording before releasing preview capture."); await CloseAsync().ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    public async ValueTask<VideoFrame> ReadPreviewFrameAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var current = source;
            if (!IsPreviewing || current is null) throw new InvalidOperationException("Combined preview is stopped.");
            try
            {
                // During healthy recording, preview reuses the actual exported composition without another GPU readback.
                if (IsRecording && current.TryGetRecordedFrame() is { } recorded) return recorded;
                return await current.ReadPreviewAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { CaptureFailed(current, ex); throw; }
        }
        finally { gate.Release(); }
    }
    public async Task<IVideoFrameSource> AcquireRecordingSourceAsync(VideoSourceOptions options, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (IsRecording) throw new InvalidOperationException("A combined recording is already active.");
            if (!IsPreviewing || source is null || ActiveOptions != options) throw new InvalidOperationException("Start the matching screen/camera preview before recording.");
            IsRecording = true;
            return new RecordingLease(this, source);
        }
        finally { gate.Release(); }
    }
    private void CaptureFailed(object? sender, Exception error)
    {
        if (!ReferenceEquals(source, sender)) return;
        IsPreviewing = false; Failed?.Invoke(this, error);
        if (sender is CompositeSource current) _ = CloseFailureAsync(current);
    }
    private async Task CloseFailureAsync(CompositeSource current)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsRecording && ReferenceEquals(source, current))
            {
                await CloseAndReleaseCameraAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex) { Failed?.Invoke(this, ex); }
        finally { gate.Release(); }
    }
    private async Task CloseAsync()
    {
        var current = source; source = null; ActiveOptions = null; IsPreviewing = false;
        if (current is not null) { current.Failed -= CaptureFailed; await current.DisposeAsync().ConfigureAwait(false); }
    }
    private async Task CloseAndReleaseCameraAsync()
    {
        try { await CloseAsync().ConfigureAwait(false); }
        finally { await cameras.StopPreviewAsync().ConfigureAwait(false); }
    }
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { if (disposed) return; disposed = true; await CloseAsync().ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    private sealed class RecordingLease : IVideoFrameSource
    {
        private readonly CombinedVideoPreviewService owner;
        private readonly CompositeSource source;
        private int disposed;
        public RecordingLease(CombinedVideoPreviewService owner, CompositeSource source)
        { this.owner = owner; this.source = source; source.Failed += ForwardFailure; }
        public event EventHandler<Exception>? Failed;
        private void ForwardFailure(object? sender, Exception error) => Failed?.Invoke(this, error);
        public VideoSize Size => source.Size;
        public void SetRecordingActivity(bool active) => source.SetRecordingActivity(active);
        public Task StartAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public ValueTask<VideoFrame> ReadFrameAsync(CancellationToken cancellationToken)
        { ObjectDisposedException.ThrowIf(disposed != 0, this); return source.ReadFrameAsync(cancellationToken); }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            source.Failed -= ForwardFailure;
            await owner.gate.WaitAsync().ConfigureAwait(false);
            try
            {
                owner.IsRecording = false;
                source.SetRecordingActivity(true);
                if (!owner.IsPreviewing)
                {
                    await owner.CloseAndReleaseCameraAsync().ConfigureAwait(false);
                }
            }
            finally { owner.gate.Release(); }
        }
    }
    private sealed class CompositeSource : IVideoFrameSource
    {
        private readonly IVideoFrameSource screen, camera;
        private readonly IVideoCompositor compositor;
        private readonly IOverlayLayoutStore layouts;
        private readonly double cameraFps;
        private readonly object sync = new();
        private readonly CancellationTokenSource cancellation = new();
        private VideoFrame? latestCamera, recorded;
        private OverlayLayout? recordedLayout;
        private long recordedAt;
        private Task pump = Task.CompletedTask;
        private Exception? failure;
        private int disposed;
        public event EventHandler<Exception>? Failed;
        public VideoSize Size => screen.Size;
        public void SetRecordingActivity(bool active) => screen.SetRecordingActivity(active);
        public CompositeSource(IVideoFrameSource screen, IVideoFrameSource camera, IVideoCompositor compositor, IOverlayLayoutStore layouts, double cameraFps)
        {
            this.screen = screen; this.camera = camera; this.compositor = compositor; this.layouts = layouts; this.cameraFps = cameraFps;
            screen.Failed += Report; camera.Failed += Report;
        }
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await screen.StartAsync(cancellationToken).ConfigureAwait(false);
            await camera.StartAsync(cancellationToken).ConfigureAwait(false);
            latestCamera = await camera.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            pump = Task.Run(PumpCameraAsync);
            if (failure is not null) throw failure;
        }
        private async Task PumpCameraAsync()
        {
            var token = cancellation.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1 / Math.Min(60, cameraFps)), token).ConfigureAwait(false);
                    var next = await camera.ReadFrameAsync(token).ConfigureAwait(false);
                    lock (sync) { latestCamera?.Dispose(); latestCamera = next; }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { Report(this, ex); }
        }
        private void Report(object? sender, Exception error)
        {
            if (Interlocked.CompareExchange(ref failure, error, null) is not null) return;
            Failed?.Invoke(this, error);
        }
        public VideoFrame? TryGetRecordedFrame()
        {
            lock (sync)
            {
                if (recorded is null || recordedLayout != layouts.Current || Stopwatch.GetElapsedTime(recordedAt) > TimeSpan.FromMilliseconds(250)) return null;
                return recorded.Lease();
            }
        }
        public ValueTask<VideoFrame> ReadFrameAsync(CancellationToken cancellationToken) => ComposeAsync(true, cancellationToken);
        public ValueTask<VideoFrame> ReadPreviewAsync(CancellationToken cancellationToken) => ComposeAsync(false, cancellationToken);
        private async ValueTask<VideoFrame> ComposeAsync(bool recording, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            if (failure is not null) throw failure;
            using var primary = await screen.ReadFrameAsync(token).ConfigureAwait(false);
            VideoFrame cameraFrame;
            lock (sync) cameraFrame = (latestCamera ?? throw new IOException("No real camera frame is available.")).Lease();
            using (cameraFrame)
            {
                var layout = layouts.Current;
                var bounds = OverlayGeometry.Calculate(primary.Size, cameraFrame.Size, layout);
                VideoOverlay[] overlays = layout.Visible ? [new(cameraFrame, bounds.X, bounds.Y, bounds.Width, bounds.Height, layout.Shape, layout.BorderEnabled ? layout.BorderThickness : 0)] : [];
                var result = await compositor.ComposeAsync(primary, overlays, token).ConfigureAwait(false);
                if (recording)
                {
                    lock (sync) { recorded?.Dispose(); recorded = result.Lease(); recordedLayout = layout; recordedAt = Stopwatch.GetTimestamp(); }
                }
                return result;
            }
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            screen.Failed -= Report; camera.Failed -= Report;
            await cancellation.CancelAsync().ConfigureAwait(false);
            try { await pump.ConfigureAwait(false); }
            finally
            {
                lock (sync) { latestCamera?.Dispose(); latestCamera = null; recorded?.Dispose(); recorded = null; }
                cancellation.Dispose();
                try { await screen.DisposeAsync().ConfigureAwait(false); }
                finally { await camera.DisposeAsync().ConfigureAwait(false); }
            }
        }
    }
}
