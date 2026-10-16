namespace CaptureFlow.Core.Video;
public interface IScreenPreviewService : IAsyncDisposable
{
    VideoSourceOptions? ActiveOptions { get; }
    bool IsRecording { get; }
    Task StartAsync(VideoSourceOptions options, CancellationToken token);
    Task StopAsync();
    ValueTask<VideoFrame?> ReadPreviewAsync(CancellationToken token);
    Task<IVideoFrameSource> AcquireAsync(VideoSourceOptions options, CancellationToken token);
}
