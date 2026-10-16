using CaptureFlow.Core.Video;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace CaptureFlow.Desktop.ViewModels;
public partial class ScreenRecorderViewModel
{
    private readonly ICombinedVideoPreviewService combined;
    private readonly IOverlayLayoutStore layouts;
    private bool synchronizingLayout;
    [ObservableProperty] private string selectedOverlayShape = "Rounded rectangle";
    [ObservableProperty] private double overlayWidthPercent = 25;
    [ObservableProperty] private bool overlayVisible = true;
    [ObservableProperty] private bool overlayBorderEnabled = true;
    [ObservableProperty] private double overlayBorderThickness = 3;
    public string[] OverlayShapeOptions { get; } = ["Rectangle", "Rounded rectangle", "Circle"];
    public bool IsCombined => SelectedMode == "Screen + Webcam";
    public bool IsCameraMode => IsWebcam || IsCombined;
    public VideoSourceOptions? CombinedSourceOptions => SelectedScreenOptions is not { } screen || SelectedCamera is null || SelectedCameraFormat is null ? null :
        new(VideoSourceKind.ScreenAndWebcam, screen.Id, ShowMouseCursor, SelectedCameraFormat, SelectedCamera.Id, screen.Kind, screen.Region, screen.Window, screen.MouseEffects);
    public int PreviewPixelWidth => previewBitmap?.PixelWidth ?? combined.Size?.Width ?? SelectedScreenSize?.Width ?? 1920;
    public int PreviewPixelHeight => previewBitmap?.PixelHeight ?? combined.Size?.Height ?? SelectedScreenSize?.Height ?? 1080;
    private VideoSize OverlayScreenSize => new(PreviewPixelWidth, PreviewPixelHeight);
    private VideoSize OverlayCameraSize => new(cameras.ActiveFormat?.Width ?? SelectedCameraFormat?.Width ?? 640, cameras.ActiveFormat?.Height ?? SelectedCameraFormat?.Height ?? 480);
    public OverlayLayout CurrentOverlayLayout => OverlayGeometry.Clamp(OverlayScreenSize, OverlayCameraSize, layouts.Current);
    private OverlayBounds Bounds => OverlayGeometry.Calculate(OverlayScreenSize, OverlayCameraSize, layouts.Current);
    public double OverlayLeft => Bounds.X;
    public double OverlayTop => Bounds.Y;
    public double OverlayPixelWidth => Bounds.Width;
    public double OverlayPixelHeight => Bounds.Height;
    public double OverlayHandleSize => Math.Max(32, PreviewPixelWidth / 32d);
    private void NotifyOverlayBounds()
    {
        OnPropertyChanged(nameof(OverlayLeft)); OnPropertyChanged(nameof(OverlayTop)); OnPropertyChanged(nameof(OverlayPixelWidth)); OnPropertyChanged(nameof(OverlayPixelHeight));
        OnPropertyChanged(nameof(OverlayHandleSize));
    }
    partial void OnSelectedOverlayShapeChanged(string value) => CommitOverlay(layouts.Current with { Shape = value switch { "Circle" => OverlayShape.Circle, "Rectangle" => OverlayShape.Rectangle, _ => OverlayShape.RoundedRectangle } });
    partial void OnOverlayWidthPercentChanged(double value) => CommitOverlay(layouts.Current with { Width = Math.Max(.01, value / 100) });
    partial void OnOverlayVisibleChanged(bool value) => CommitOverlay(layouts.Current with { Visible = value });
    partial void OnOverlayBorderEnabledChanged(bool value) => CommitOverlay(layouts.Current with { BorderEnabled = value });
    partial void OnOverlayBorderThicknessChanged(double value) => CommitOverlay(layouts.Current with { BorderThickness = value });
    private void CommitOverlay(OverlayLayout layout)
    {
        if (synchronizingLayout) return;
        var clamped = OverlayGeometry.Clamp(OverlayScreenSize, OverlayCameraSize, layout);
        layouts.Current = clamped;
        synchronizingLayout = true;
        try { OverlayWidthPercent = clamped.Width * 100; }
        finally { synchronizingLayout = false; }
        NotifyOverlayBounds();
    }
    public void MoveOverlay(double x, double y) => CommitOverlay(layouts.Current with { X = x, Y = y });
    public void ResizeOverlay(double width) => CommitOverlay(layouts.Current with { Width = Math.Max(.05, width) });
    [RelayCommand]
    private void PositionOverlay(string position)
    {
        if (Enum.TryParse<OverlayPosition>(position, out var preset)) CommitOverlay(OverlayGeometry.Place(OverlayScreenSize, OverlayCameraSize, layouts.Current, preset));
    }
}
