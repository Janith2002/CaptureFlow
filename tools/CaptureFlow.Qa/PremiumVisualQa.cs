using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureFlow.Desktop;
using CaptureFlow.Desktop.Views;
namespace CaptureFlow.Qa;

internal static class PremiumVisualQa
{
    public static async Task CaptureAsync(MainWindow window, FrameworkElement page, string workspace)
    {
        var folder = Path.GetFullPath("artifacts/qa/m692/visual-" + workspace + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        window.WindowState = WindowState.Normal;
        var nativeDpi = VisualTreeHelper.GetDpi(window).DpiScaleX;
        if (workspace == "editor")
        {
            await ResetLayoutQa.VerifyAsync(window, (EditorPage)page);
            foreach (var menu in Find<Menu>(page))
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                try { item.IsSubmenuOpen = true; await Task.Delay(50); window.UpdateLayout(); }
                finally { item.IsSubmenuOpen = false; }
            }
            var focus = Find<Menu>(page).SelectMany(m => m.Items.OfType<MenuItem>()).SelectMany(m => m.Items.OfType<MenuItem>()).Single(m => m.Header as string == "Focus preview / restore timeline");
            focus.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); window.UpdateLayout();
            if (((RowDefinition)page.FindName("TimelineRow")).ActualHeight > 1) throw new InvalidOperationException("Focus preview failed to reclaim timeline space.");
            await ((CaptureFlow.Desktop.ViewModels.EditorViewModel)page.DataContext).ResetEditorWorkspaceCommand.ExecuteAsync(null);
            window.UpdateLayout();
            if (((RowDefinition)page.FindName("TimelineRow")).ActualHeight < 150 || !((FrameworkElement)page.FindName("TimelineDock")).IsVisible)
                throw new InvalidOperationException("Reset layout must restore the timeline from Focus preview.");
            Console.WriteLine("PASS premium editor menus; focus preview reclaims space; Reset layout restores timeline.");
        }
        foreach (var (width, height) in new[] { (1280, 720), (1366, 768), (1920, 1080) })
        foreach (var scale in new[] { 1d, 1.25, 1.5 })
        {
            // Equivalent DIP bounds exercise layout; this does not change or claim OS DPI.
            window.Width = width / scale; window.Height = height / scale;
            await Task.Delay(120); window.UpdateLayout();
            var preview = workspace == "studio" ? (FrameworkElement)page.FindName("StudioPreview") : (FrameworkElement)page.FindName("Player");
            if (preview.ActualWidth < 200 || preview.ActualHeight < 70) throw new InvalidOperationException("Premium preview collapsed.");
            if (workspace == "editor" && ((RowDefinition)page.FindName("TimelineRow")).ActualHeight < 150) throw new InvalidOperationException("Premium timeline collapsed.");
            var name = $"{width}x{height}-equivalent-{scale * 100:0}pct";
            Save(window, Path.Combine(folder, name + ".png"));
            var constrained = Math.Abs(window.ActualWidth - width / scale) > 3 || Math.Abs(window.ActualHeight - height / scale) > 3;
            Console.WriteLine($"PASS premium {workspace} {name}: window={window.ActualWidth:0}x{window.ActualHeight:0} DIP; preview={preview.ActualWidth:0}x{preview.ActualHeight:0}; actual OS DPI={nativeDpi}; equivalent layout only for other scales; desktop-constrained={constrained}.");
        }
        window.Width = 1536; window.Height = 864; await Task.Delay(120); window.UpdateLayout();
        Save(window, Path.Combine(folder, "review.png"));
        Console.WriteLine("PREMIUM SCREENSHOTS: " + folder);
    }
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) yield return match;
            foreach (var nested in Find<T>(child)) yield return nested;
        }
    }
    private static void Save(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
}
