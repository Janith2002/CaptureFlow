namespace CaptureFlow.Core.Video;
public enum OverlayShape { Rectangle, RoundedRectangle, Circle }
public enum OverlayPosition { TopLeft, TopRight, BottomLeft, BottomRight }
/// <summary>Position and width are relative to the captured screen; border thickness is in capture pixels.</summary>
public sealed record OverlayLayout(double X = .72, double Y = .70, double Width = .25,
    OverlayShape Shape = OverlayShape.RoundedRectangle, bool Visible = true, bool BorderEnabled = true, double BorderThickness = 3)
{
    public void Validate()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Width) || Width <= 0 ||
            !double.IsFinite(BorderThickness) || BorderThickness is < 0 or > 32 || !Enum.IsDefined(Shape))
            throw new ArgumentException("Invalid camera overlay layout.");
    }
}
public sealed record OverlayBounds(int X, int Y, int Width, int Height);
public static class OverlayGeometry
{
    public static OverlayBounds Calculate(VideoSize screen, VideoSize camera, OverlayLayout layout)
    {
        layout.Validate();
        if (screen.Width < 1 || screen.Height < 1 || camera.Width < 1 || camera.Height < 1) throw new ArgumentException("Invalid video size.");
        var aspect = layout.Shape == OverlayShape.Circle ? 1 : camera.Width / (double)camera.Height;
        var maxWidth = Math.Min(screen.Width, screen.Height * aspect);
        var width = Math.Clamp((int)Math.Round(Math.Clamp(layout.Width, 0, 1) * screen.Width), 1, Math.Max(1, (int)Math.Floor(maxWidth)));
        var height = Math.Clamp((int)Math.Round(width / aspect), 1, screen.Height);
        return new(Math.Clamp((int)Math.Round(Math.Clamp(layout.X, 0, 1) * screen.Width), 0, screen.Width - width),
            Math.Clamp((int)Math.Round(Math.Clamp(layout.Y, 0, 1) * screen.Height), 0, screen.Height - height), width, height);
    }
    public static OverlayLayout Clamp(VideoSize screen, VideoSize camera, OverlayLayout layout)
    {
        var bounds = Calculate(screen, camera, layout);
        return layout with { X = bounds.X / (double)screen.Width, Y = bounds.Y / (double)screen.Height, Width = bounds.Width / (double)screen.Width };
    }
    public static OverlayLayout Place(VideoSize screen, VideoSize camera, OverlayLayout layout, OverlayPosition position)
    {
        var b = Calculate(screen, camera, layout);
        var margin = Math.Min(16, Math.Min((screen.Width - b.Width) / 2, (screen.Height - b.Height) / 2));
        return Clamp(screen, camera, layout with
        {
            X = (position is OverlayPosition.TopRight or OverlayPosition.BottomRight ? screen.Width - b.Width - margin : margin) / (double)screen.Width,
            Y = (position is OverlayPosition.BottomLeft or OverlayPosition.BottomRight ? screen.Height - b.Height - margin : margin) / (double)screen.Height
        });
    }
}
public interface IOverlayLayoutStore { OverlayLayout Current { get; set; } }
public interface IMonitorCaptureBackend : IVideoSourceFactory { }
public interface ICombinedVideoPreviewService : IAsyncDisposable
{
    event EventHandler<Exception>? Failed;
    bool IsPreviewing { get; }
    bool IsRecording { get; }
    VideoSize? Size { get; }
    VideoSourceOptions? ActiveOptions { get; }
    Task StartPreviewAsync(VideoSourceOptions options, CancellationToken cancellationToken = default);
    Task StopPreviewAsync();
    ValueTask<VideoFrame> ReadPreviewFrameAsync(CancellationToken cancellationToken = default);
    Task<IVideoFrameSource> AcquireRecordingSourceAsync(VideoSourceOptions options, CancellationToken cancellationToken);
}
