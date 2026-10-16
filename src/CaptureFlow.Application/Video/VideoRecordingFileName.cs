using System.Globalization;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Application.Video;
public static class VideoRecordingFileName
{
    public static string Create(DateTimeOffset timestamp, Guid id, VideoSourceKind kind = VideoSourceKind.Monitor) => $"CaptureFlow_{(kind == VideoSourceKind.Webcam ? "Webcam" : kind == VideoSourceKind.ScreenAndWebcam ? "ScreenWebcam" : "Screen")}_{timestamp.UtcDateTime.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)}_{id:N}.mp4";
}
public sealed class SingleSourceVideoCompositor : IVideoCompositor
{
    public ValueTask<VideoFrame> ComposeAsync(VideoFrame primary, IReadOnlyList<VideoOverlay> overlays, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (overlays.Count != 0) throw new NotSupportedException("Webcam overlay compositing is a future milestone.");
        return ValueTask.FromResult(primary.Lease());
    }
}
