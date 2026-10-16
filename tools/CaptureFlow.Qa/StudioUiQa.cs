using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CaptureFlow.Desktop;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Desktop.Views;
namespace CaptureFlow.Qa;
internal static class StudioUiQa
{
    public static async Task VerifyAsync(MainWindow window, DashboardViewModel dashboard)
    {
        var page = Find(window).OfType<ScreenRecorderPage>().Single(); var vm = dashboard.Screen;
        var oldWidth = window.Width; var oldHeight = window.Height;
        var source = (ColumnDefinition)page.FindName("SourcesColumn"); var properties = (ColumnDefinition)page.FindName("PropertiesColumn");
        window.Width = 760; window.Height = 480; window.UpdateLayout();
        var scrollers = Find(page).OfType<ScrollViewer>().ToArray();
        if (scrollers.Any(s => s.IsVisible && s.ScrollableWidth > 1)) throw new InvalidOperationException("Compact Studio must not require horizontal workspace scrolling.");
        if (source.ActualWidth > 1 || properties.ActualWidth > 1) throw new InvalidOperationException("Compact Studio did not collapse secondary docks.");
        if (Find(page).OfType<GridSplitter>().Count() < 3) throw new InvalidOperationException("Workspace splitters missing.");
        window.Width = 1536; window.Height = 864; window.UpdateLayout();
        var splitter = Find(page).OfType<GridSplitter>().First(s => s.GetValue(System.Windows.Automation.AutomationProperties.NameProperty).ToString() == "Resize sources panel");
        var before = source.ActualWidth;
        splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        splitter.RaiseEvent(new DragDeltaEventArgs(20, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        splitter.RaiseEvent(new DragCompletedEventArgs(20, 0, false) { RoutedEvent = Thumb.DragCompletedEvent }); window.UpdateLayout();
        if (Math.Abs(source.ActualWidth - before) < 1) throw new InvalidOperationException("Source splitter did not resize.");
        vm.ShowStudioProperties = false; window.UpdateLayout(); if (properties.ActualWidth > 1) throw new InvalidOperationException("Properties panel did not collapse.");
        await vm.ResetWorkspaceCommand.ExecuteAsync(null); window.UpdateLayout(); if (!vm.ShowStudioMixer || !vm.ShowStudioProperties || Math.Abs(source.ActualWidth - 220) > 1) throw new InvalidOperationException("Workspace reset failed.");
        // Persist only isolated QA preferences. No preview, camera load or recording commands are issued.
        await vm.SaveStudioAsync(); await vm.LoadStudioAsync();
        Console.WriteLine($"PASS studio native 760x480 DIP: secondary docks collapse without horizontal scrolling; expanded workspace source resize, properties collapse, reset and isolated persistence. DPI={VisualTreeHelper.GetDpi(window).DpiScaleX}.");
        window.Width = oldWidth; window.Height = oldHeight; window.UpdateLayout();
    }
    private static IEnumerable<DependencyObject> Find(DependencyObject parent)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var descendant in Find(child)) yield return descendant; } }
}
