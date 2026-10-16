using CaptureFlow.Core.Video;
namespace CaptureFlow.Infrastructure.Video;
public sealed class VideoSourceFactory(WindowsGraphicsCaptureSourceFactory screens, ICameraPreviewService cameras, ICombinedVideoPreviewService combined, IScreenPreviewService? preview = null) : IVideoSourceFactory
{
    public Task<IReadOnlyList<MonitorDescriptor>> GetMonitorsAsync(CancellationToken cancellationToken = default) => screens.GetMonitorsAsync(cancellationToken);
    public Task<IVideoFrameSource> CreateAsync(VideoSourceOptions options, CancellationToken cancellationToken) => options.Kind switch
    {
        VideoSourceKind.Monitor or VideoSourceKind.Window or VideoSourceKind.Region => preview?.ActiveOptions == options ? preview.AcquireAsync(options, cancellationToken) : screens.CreateAsync(options, cancellationToken),
        VideoSourceKind.Webcam => cameras.AcquireRecordingSourceAsync(options, cancellationToken),
        VideoSourceKind.ScreenAndWebcam => combined.AcquireRecordingSourceAsync(options, cancellationToken),
        _ => throw new NotSupportedException("Unsupported capture source.")
    };
}
