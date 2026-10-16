using CaptureFlow.Core.Video;
namespace CaptureFlow.Application.Video;

/// <summary>A single screen session with a one-frame preview cache; preview never consumes recording frames.</summary>
public sealed class ScreenPreviewService(IMonitorCaptureBackend backend) : IScreenPreviewService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IVideoFrameSource? source;
    private VideoFrame? latest;
    private bool disposed;
    public VideoSourceOptions? ActiveOptions { get; private set; }
    public bool IsRecording { get; private set; }
    public async Task StartAsync(VideoSourceOptions options, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (IsRecording) throw new InvalidOperationException("Stop recording before changing preview.");
            options.ValidateScreen(); await CloseAsync();
            source = await backend.CreateAsync(options, token);
            try { await source.StartAsync(token); ActiveOptions = options; }
            catch (Exception startError)
            {
                try { await CloseAsync(); }
                catch (Exception cleanupError) { throw new AggregateException("Screen preview initialization and cleanup failed.", startError, cleanupError); }
                throw;
            }
        }
        finally { gate.Release(); }
    }
    public async ValueTask<VideoFrame?> ReadPreviewAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (source is null) return null;
            if (!IsRecording) { using var frame = await source.ReadFrameAsync(token); latest?.Dispose(); latest = frame.Lease(); }
            return latest?.Lease();
        }
        catch { if (!IsRecording) await CloseAsync(); throw; }
        finally { gate.Release(); }
    }
    public async Task<IVideoFrameSource> AcquireAsync(VideoSourceOptions options, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (source is null || ActiveOptions != options || IsRecording) throw new InvalidOperationException("Start the matching preview before acquiring it.");
            IsRecording = true; return new Lease(this, source);
        }
        finally { gate.Release(); }
    }
    public async Task StopAsync()
    {
        await gate.WaitAsync();
        try { if (IsRecording) throw new InvalidOperationException("Stop recording before releasing preview."); await CloseAsync(); }
        finally { gate.Release(); }
    }
    private async Task CloseAsync()
    { var current = source; source = null; ActiveOptions = null; latest?.Dispose(); latest = null; if (current is not null) await current.DisposeAsync(); }
    public async ValueTask DisposeAsync()
    { await gate.WaitAsync(); try { if (disposed) return; disposed = true; await CloseAsync(); } finally { gate.Release(); } }
    private sealed class Lease(ScreenPreviewService owner, IVideoFrameSource source) : IVideoFrameSource
    {
        private int disposed;
        public event EventHandler<Exception>? Failed { add => source.Failed += value; remove => source.Failed -= value; }
        public VideoSize Size => source.Size;
        public void SetRecordingActivity(bool active) => source.SetRecordingActivity(active);
        public Task StartAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public async ValueTask<VideoFrame> ReadFrameAsync(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            await owner.gate.WaitAsync(token);
            try { var frame = await source.ReadFrameAsync(token); owner.latest?.Dispose(); owner.latest = frame.Lease(); return frame; }
            finally { owner.gate.Release(); }
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            await owner.gate.WaitAsync(); try { owner.IsRecording = false; if (!owner.disposed) source.SetRecordingActivity(true); } finally { owner.gate.Release(); }
        }
    }
}
