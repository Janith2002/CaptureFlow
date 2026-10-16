using System.IO;
using System.Windows;
using System.Windows.Controls;
using CaptureFlow.Core.Editing;
using CaptureFlow.Desktop;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Desktop.Views;
using CaptureFlow.Infrastructure.Editing;
namespace CaptureFlow.Qa;

internal static class ResetLayoutQa
{
    public static async Task VerifyAsync(MainWindow window, EditorPage page)
    {
        var vm = (EditorViewModel)page.DataContext;
        var path = Environment.GetEnvironmentVariable("CAPTUREFLOW_EDITOR_WORKSPACE") ?? throw new InvalidOperationException("An isolated workspace is required.");
        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath("artifacts/qa") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Reset QA refuses non-QA workspace settings.");
        var project = vm.Project; var history = vm.Session.Snapshot().Undo.Length;
        var oldWidth = window.Width; var oldHeight = window.Height; var oldState = window.WindowState;
        try
        {
            window.WindowState = WindowState.Normal;
            foreach (var (width, height) in new[] { (1536d, 864d), (900d, 480d), (854d, 480d) })
            {
                window.Width = width; window.Height = height; await Task.Delay(100);
                await vm.SaveWorkspaceAsync(new(340, 380, 1, 3, false, false));
                vm.IsMonitorFocused = true; window.UpdateLayout();
                if (((RowDefinition)page.FindName("TimelineRow")).ActualHeight > 1) throw new InvalidOperationException("Focus preview setup failed.");
                await vm.ResetEditorWorkspaceCommand.ExecuteAsync(null); window.UpdateLayout();
                if (vm.IsMonitorFocused || vm.Workspace != new EditorWorkspace()) throw new InvalidOperationException("Reset did not restore default workspace state.");
                if (!((FrameworkElement)page.FindName("TimelineDock")).IsVisible || ((RowDefinition)page.FindName("TimelineRow")).ActualHeight < 150)
                    throw new InvalidOperationException("Reset did not restore a usable timeline.");
                if (((FrameworkElement)page.FindName("Player")).ActualWidth < 200) throw new InvalidOperationException("Reset collapsed the preview.");
                if (width > 1400 && (!((FrameworkElement)page.FindName("MediaDock")).IsVisible || !((FrameworkElement)page.FindName("InspectorDock")).IsVisible))
                    throw new InvalidOperationException("Wide reset did not restore both panels.");
                if (width > 1400 && (Math.Abs(((ColumnDefinition)page.FindName("MediaColumn")).ActualWidth - 200) > 2 || Math.Abs(((ColumnDefinition)page.FindName("InspectorColumn")).ActualWidth - 260) > 2))
                    throw new InvalidOperationException("Reset did not restore the approved default splitter widths.");
                if (!File.Exists(path) || await new JsonEditorWorkspaceStore(path).LoadAsync() != new EditorWorkspace()) throw new InvalidOperationException("Reset was not saved to isolated settings.");
                if (!ReferenceEquals(project, vm.Project) || vm.Session.Snapshot().Undo.Length != history) throw new InvalidOperationException("Layout reset changed project/history.");
                Console.WriteLine($"PASS native Reset Layout at {window.ActualWidth:0}x{window.ActualHeight:0} DIP: focus, timeline, panel preferences, splitter defaults, disk read-back and unchanged project/history. Fresh-process reopen remains a separate check.");
            }
        }
        finally { window.Width = oldWidth; window.Height = oldHeight; window.WindowState = oldState; window.UpdateLayout(); }
    }
}
