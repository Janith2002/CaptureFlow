using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CaptureFlow.Core.Video;
using CaptureFlow.Desktop;
using CaptureFlow.Desktop.Services;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Desktop.Views;
using CaptureFlow.Infrastructure.Video;

internal static class ScreenSourcesQa
{
    public static int Run(bool mouseEffects = false)
    {
        var app = new App(); app.InitializeComponent(); var started = false; var failed = false; var completed = false;
        var bindings = new Errors(); PresentationTraceSources.DataBindingSource.Listeners.Add(bindings);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        app.DispatcherUnhandledException += (_, e) => { failed = true; Console.WriteLine("FAIL unhandled: " + e.Exception); e.Handled = true; app.Shutdown(1); };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            if (started || app.MainWindow is not MainWindow main) return; started = true; timer.Stop();
            var dashboard = (DashboardViewModel)main.DataContext;
            try
            {
                await main.DesktopInitialization; dashboard.NavigateCommand.Execute("Screen Recorder");
                await WindowSourceQa.VerifyAsync();
                var vm = dashboard.Screen;
                if (mouseEffects)
                {
                    await Task.Delay(100);
                    var colors = FindElements(main).OfType<ComboBox>().Where(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Highlight color").ToArray();
                    Check(colors.Length == 1, "Mouse Effects controls were not loaded on Screen Recorder.");
                    colors[0].Text = "#112233"; vm.HighlightCursor = true; vm.HighlightMouseClicks = true; vm.ShowMouseCursor = false;
                    Check(vm.HighlightColor == "#112233" && vm.MousePreferences.Highlight && !vm.SelectedScreenOptions!.CaptureCursor, "Mouse settings bindings did not update capture options.");
                    var selectedCamera = vm.SelectedCamera; vm.SelectedCamera = null; // Visibility-only QA must not open a physical camera.
                    Check(vm.CanEditPreferences, "Cannot change mode during a busy capture operation.");
                    vm.SelectedMode = "Webcam Only"; await vm.CameraTransition; await main.Dispatcher.InvokeAsync(() => { }); Check(!colors[0].IsVisible, "Webcam Only did not hide Mouse Effects.");
                    vm.SelectedMode = "Screen Only"; await vm.CameraTransition; await main.Dispatcher.InvokeAsync(() => { }); Check(colors[0].IsVisible, "Screen Only did not restore Mouse Effects.");
                    vm.SelectedCamera = selectedCamera;
                    Console.WriteLine("PASS real WPF mouse controls, custom color binding, source configuration and Webcam Only visibility. No capture started.");
                }
                var windows = await new WindowsApplicationWindowDiscovery().GetWindowsAsync();
                Check(windows.All(w => w.ProcessId != Environment.ProcessId), "CaptureFlow windows were not excluded.");
                vm.SelectedScreenSource = "Application Window"; await vm.RefreshWindowsCommand.ExecuteAsync(null);
                Check(vm.IsWindowSource && !vm.UsesMonitor, "Window source controls are incorrect.");
                Check(vm.SelectedApplicationWindow is null && !vm.StartCommand.CanExecute(null), "An unselected window could start capture.");
                Console.WriteLine($"PASS native window enumeration: {windows.Count} eligible windows; own windows excluded; no titles logged or capture started.");
                vm.SelectedScreenSource = "Custom Region";
                var monitor = vm.SelectedMonitor ?? throw new InvalidOperationException("BLOCKED: no monitor accessible.");
                var area = DesktopWorkAreas.Enumerate(true).Single(a => a.Id == monitor.Id);
                var previous = new ScreenRegion(monitor.Id, 40, 60, 320, 240, monitor.Width, monitor.Height);
                var selector = new WpfRegionSelection(); var select = selector.SelectAsync(monitor, previous, default);
                var overlay = app.Windows.OfType<RegionSelectionWindow>().Single();
                await Task.Delay(200);
                var bounds = DesktopWorkAreas.Bounds(new WindowInteropHelper(overlay).Handle);
                Check(bounds.Left == area.Left && bounds.Top == area.Top && bounds.Width == area.Width && bounds.Height == area.Height, "Overlay does not cover the physical monitor.");
                var confirm = (Button)overlay.FindName("ConfirmButton"); Check(confirm.IsEnabled, "Valid restored region cannot be confirmed.");
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var result = await select;
                Check(result == previous, $"DPI restoration changed region geometry: {result}.");
                Check(!app.Windows.OfType<RegionSelectionWindow>().Any(), "Region overlay survived confirmation.");
                vm.SelectedRegion = result; Check(vm.SelectedScreenOptions?.Kind == VideoSourceKind.Region, "Region source was not selected.");
                Console.WriteLine($"PASS native overlay bounds, DPI ({area.Dpi}), restored physical coordinates and confirmation; overlay closed before completion.");
                select = selector.SelectAsync(monitor, previous, default); overlay = app.Windows.OfType<RegionSelectionWindow>().Single();
                FindButtons(overlay).Single(b => Equals(b.Content, "Reset")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!((Button)overlay.FindName("ConfirmButton")).IsEnabled, "Reset left invalid region confirmable.");
                FindButtons(overlay).Single(b => Equals(b.Content, "Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(await select is null, "Cancel produced a region.");
                using (var cancellation = new CancellationTokenSource())
                {
                    select = selector.SelectAsync(monitor, previous, cancellation.Token); cancellation.Cancel();
                    try { await select; throw new Exception("Cancellation reported success."); } catch (OperationCanceledException) { }
                }
                Check(!app.Windows.OfType<RegionSelectionWindow>().Any(), "Cancelled overlay leaked.");
                Console.WriteLine("PASS Reset, Cancel and cancellation cleanup; existing region and recordings untouched.");
                Check(bindings.Messages.Count == 0, string.Join("\n", bindings.Messages));
                if (mouseEffects)
                {
                    await vm.StopScreenSelectionOperationsAsync();
                    var saved = await new JsonMouseEffectsPreferencesStore(Environment.GetEnvironmentVariable("CAPTUREFLOW_MOUSE_PREFERENCES")!).LoadAsync();
                    Check(saved == vm.MousePreferences, "Mouse preferences were not persisted before shutdown.");
                    Console.WriteLine("PASS asynchronous mouse preference persistence and shutdown drain in isolated QA folder.");
                }
                main.Closed += (_, _) => { completed = true; Console.WriteLine("PASS graceful shutdown; no screen, webcam or audio capture commands executed."); };
                main.RequestExit();
            }
            catch (Exception ex) { failed = true; Console.WriteLine("FAIL screen-source UI QA: " + ex); main.RequestExit(); }
        };
        timer.Start(); app.Run(); Console.WriteLine($"Binding errors: {bindings.Messages.Count}");
        return failed || !completed ? 1 : 0;
    }
    private static IEnumerable<Button> FindButtons(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is Button b) yield return b;
            foreach (var nested in FindButtons(child)) yield return nested;
        }
    }
    private static IEnumerable<DependencyObject> FindElements(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in FindElements(child)) yield return nested; }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Errors : TraceListener { public List<string> Messages = []; public override void Write(string? message) { } public override void WriteLine(string? message) { if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message); } }
}
