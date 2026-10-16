using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Desktop.Views;
public partial class RegionSelectionWindow : Window
{
    private readonly MonitorDescriptor monitor;
    private Point start;
    private Rect rectangle;
    private Rect beforeMove;
    private bool dragging, moving;
    public ScreenRegion? Result { get; private set; }
    public RegionSelectionWindow(MonitorDescriptor monitor) { InitializeComponent(); this.monitor = monitor; KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); }; }
    public void Restore(ScreenRegion? previous)
    { if (previous?.Matches(monitor) == true) { var scale = Scale; rectangle = new(previous.X / scale, previous.Y / scale, previous.Width / scale, previous.Height / scale); RenderSelection(); } }
    private double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;
    private Point Clamped(Point p) => new(Math.Clamp(p.X, 0, monitor.Width / Scale), Math.Clamp(p.Y, 0, monitor.Height / Scale));
    private void BeginSelection(object sender, MouseButtonEventArgs e)
    {
        start = Clamped(e.GetPosition(Surface)); moving = rectangle.Width > 0 && rectangle.Contains(start); beforeMove = rectangle;
        if (!moving) rectangle = new(start, start); dragging = true; Surface.CaptureMouse(); e.Handled = true;
    }
    private void MoveSelection(object sender, MouseEventArgs e)
    {
        if (!dragging) return; var p = Clamped(e.GetPosition(Surface));
        if (moving) rectangle = new(Math.Clamp(beforeMove.X + p.X - start.X, 0, monitor.Width / Scale - beforeMove.Width), Math.Clamp(beforeMove.Y + p.Y - start.Y, 0, monitor.Height / Scale - beforeMove.Height), beforeMove.Width, beforeMove.Height);
        else rectangle = new(start, p);
        RenderSelection();
    }
    private void EndSelection(object sender, MouseButtonEventArgs e) { dragging = false; Surface.ReleaseMouseCapture(); RenderSelection(); }
    private void ResizeSelection(object sender, DragDeltaEventArgs e)
    { rectangle = new(rectangle.X, rectangle.Y, Math.Clamp(rectangle.Width + e.HorizontalChange, 1, monitor.Width / Scale - rectangle.X), Math.Clamp(rectangle.Height + e.VerticalChange, 1, monitor.Height / Scale - rectangle.Y)); RenderSelection(); }
    private ScreenRegion Pixels => RegionSelectionGeometry.FromDrag(monitor.Id, monitor.Width, monitor.Height, rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom, Scale * 96);
    private void RenderSelection()
    {
        Canvas.SetLeft(Selection, rectangle.Left); Canvas.SetTop(Selection, rectangle.Top); Selection.Width = rectangle.Width; Selection.Height = rectangle.Height;
        Selection.Visibility = ResizeHandle.Visibility = rectangle.Width > 0 ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(ResizeHandle, rectangle.Right - 9); Canvas.SetTop(ResizeHandle, rectangle.Bottom - 9);
        var pixels = Pixels; Dimensions.Text = $"{pixels.Width} × {pixels.Height} px · monitor-relative ({pixels.X}, {pixels.Y})";
        try { pixels.Validate(); ConfirmButton.IsEnabled = true; } catch (ArgumentException) { ConfirmButton.IsEnabled = false; }
    }
    private void Confirm(object sender, RoutedEventArgs e) { Result = Pixels; Result.Validate(); Close(); }
    private void Reset(object sender, RoutedEventArgs e) { rectangle = new(); RenderSelection(); }
    private void Cancel(object sender, RoutedEventArgs e) => Close();
}
