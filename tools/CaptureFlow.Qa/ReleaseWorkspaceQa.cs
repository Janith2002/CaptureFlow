using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureFlow.Desktop;
namespace CaptureFlow.Qa;
internal static class ReleaseWorkspaceQa
{
    private static readonly string Folder = Path.GetFullPath("artifacts/qa/m69-workspace-layouts/" + Guid.NewGuid().ToString("N"));
    public static async Task VerifyAsync(MainWindow window, string pageName, UserControl page)
    {
        var width = window.Width; var height = window.Height; var state = window.WindowState;
        try
        {
            window.WindowState = WindowState.Normal; window.Width = 760; window.Height = 480; await Task.Delay(100); window.UpdateLayout();
            var buttons = Find(page).OfType<Button>().Where(b => b.IsVisible && b.ActualWidth > 0).ToArray();
            foreach (var button in buttons)
            {
                button.BringIntoView(); window.UpdateLayout(); await Task.Delay(50);
                var bounds = button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
                if (bounds.Left < 0 || bounds.Right > window.ActualWidth || bounds.Top < 0 || bounds.Bottom > window.ActualHeight)
                    throw new InvalidOperationException($"Compact {pageName}: button '{button.Content}' is unreachable: {bounds}; window={window.ActualWidth}x{window.ActualHeight}.");
            }
            foreach (var scroll in Find(window).OfType<ScrollViewer>()) { scroll.ScrollToTop(); scroll.ScrollToLeftEnd(); }
            window.UpdateLayout(); Save(window, pageName + "-compact");
            window.WindowState = WindowState.Maximized; await Task.Delay(100); window.UpdateLayout(); Save(window, pageName + "-maximized");
            Console.WriteLine($"PASS compact/maximized layout renders and {buttons.Length} button scroll-reachability checks: {pageName}; DPI={VisualTreeHelper.GetDpi(window).DpiScaleX}; evidence={Folder}.");
        }
        finally { window.WindowState = state; window.Width = width; window.Height = height; window.UpdateLayout(); }
    }
    private static void Save(Window window, string label)
    {
        Directory.CreateDirectory(Folder); var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(Path.Combine(Folder, label.Replace(" ", "-") + ".png")); encoder.Save(file);
    }
    private static IEnumerable<DependencyObject> Find(DependencyObject parent)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var descendant in Find(child)) yield return descendant; } }
}
