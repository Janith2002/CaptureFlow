using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CaptureFlow.Core.Video;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Desktop.Views;
public partial class CompositePreviewEditor : UserControl
{
    private Point startPoint;
    private OverlayLayout? startLayout;
    private double startWidth, startHeight;
    public CompositePreviewEditor() => InitializeComponent();
    private void MoveStarted(object sender, DragStartedEventArgs e)
    {
        startPoint = Mouse.GetPosition(EditorCanvas);
        startLayout = (DataContext as ScreenRecorderViewModel)?.CurrentOverlayLayout;
    }
    private void MoveDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is not ScreenRecorderViewModel model || startLayout is null) return;
        var point = Mouse.GetPosition(EditorCanvas);
        model.MoveOverlay(startLayout.X + (point.X - startPoint.X) / model.PreviewPixelWidth,
            startLayout.Y + (point.Y - startPoint.Y) / model.PreviewPixelHeight);
    }
    private void ResizeStarted(object sender, DragStartedEventArgs e)
    {
        MoveStarted(sender, e);
        if (DataContext is ScreenRecorderViewModel model) { startWidth = model.OverlayPixelWidth; startHeight = model.OverlayPixelHeight; }
    }
    private void ResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is not ScreenRecorderViewModel model || startLayout is null || startHeight <= 0) return;
        var point = Mouse.GetPosition(EditorCanvas);
        var dx = point.X - startPoint.X; var dy = (point.Y - startPoint.Y) * startWidth / startHeight;
        model.ResizeOverlay((startWidth + (Math.Abs(dx) >= Math.Abs(dy) ? dx : dy)) / model.PreviewPixelWidth);
    }
}
