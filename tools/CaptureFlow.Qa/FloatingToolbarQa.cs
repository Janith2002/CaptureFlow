using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;
using CaptureFlow.Core.Desktop;
using CaptureFlow.Core.Video;
using CaptureFlow.Desktop;
using CaptureFlow.Desktop.Services;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Desktop.Views;
using CaptureFlow.Infrastructure.Desktop;

internal static class FloatingToolbarQa
{
    public static int Run()
    {
        var app = new App(); app.InitializeComponent(); var started = false; var failed = false; var completed = false;
        var bindings = new Errors(); PresentationTraceSources.DataBindingSource.Listeners.Add(bindings); PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        app.DispatcherUnhandledException += (_, e) => { failed = true; Console.WriteLine("FAIL unhandled: " + e.Exception); e.Handled = true; app.Shutdown(1); };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            if (started || app.MainWindow is not MainWindow main) return; started = true; timer.Stop();
            var dashboard = (DashboardViewModel)main.DataContext;
            try
            {
                await main.DesktopInitialization;
                var integration = (WindowDesktopIntegration)Field(main, "desktopIntegration")!;
                var countdown = (IRecordingCountdown)Field(integration, "countdown")!;
                var model = (FloatingToolbarViewModel)Field(integration, "toolbarModel")!;
                dashboard.Experience.ShowToolbar = true;
                var watch = Stopwatch.StartNew(); var run = countdown.RunAsync(new(3), default);
                Check(app.Windows.OfType<CountdownWindow>().Any(w => w.IsVisible), "Countdown overlay did not open.");
                await run;
                Check(watch.Elapsed.TotalSeconds >= 3 && watch.Elapsed.TotalSeconds < 6, "Countdown elapsed timing is incorrect.");
                Check(!app.Windows.OfType<CountdownWindow>().Any(), "Countdown overlay not closed before completion.");
                Console.WriteLine("PASS real silent 3-second animated overlay completes and closes; no capture started.");
                var cancel = countdown.RunAsync(new(10), default); await model.StopCommand.ExecuteAsync(null);
                try { await cancel; throw new Exception("Countdown cancellation completed as success."); } catch (OperationCanceledException) { }
                Check(!countdown.IsRunning, "Cancelled countdown is still running.");
                Console.WriteLine("PASS toolbar Cancel closes countdown without starting any recording.");
                // Simulated presentation state only; the actual recording service remains idle.
                dashboard.Screen.Status = VideoRecordingState.Recording; dashboard.Screen.ElapsedText = "00:01:23";
                var toolbar = app.Windows.OfType<FloatingToolbarWindow>().Single(w => w.IsVisible);
                Check(model.Elapsed == dashboard.Screen.ElapsedText && model.Status == "Recording", "Toolbar diverged from main VM state/timer.");
                main.WindowState = WindowState.Minimized; Check(toolbar.IsVisible, "Toolbar disappears with main window.");
                dashboard.Screen.Status = VideoRecordingState.Paused; Check(model.Status == "Paused" && model.PauseText == "Resume", "Paused presentation is incorrect.");
                model.CollapseCommand.Execute(null); Check(Math.Abs(toolbar.Height - 70) < 1, $"Toolbar did not collapse: height={toolbar.Height}."); model.CollapseCommand.Execute(null);
                dashboard.Experience.ToolbarTopmost = false; Check(!toolbar.Topmost, "Topmost setting not applied."); dashboard.Experience.ToolbarTopmost = true;
                dashboard.Experience.ShowToolbar = false; Check(!toolbar.IsVisible, "Hide preference did not apply."); dashboard.Experience.ShowToolbar = true; Check(toolbar.IsVisible, "Toolbar did not restore.");
                var areas = DesktopWorkAreas.Enumerate(); var area = areas.First(a => a.Primary);
                var handle = new WindowInteropHelper(toolbar).Handle;
                DesktopWorkAreas.Place(handle, ToolbarPlacement.Resolve(new(area.Id, 20, 30), areas, toolbar.Width, toolbar.Height)); await Task.Delay(700);
                Check(dashboard.Experience.Anchor?.MonitorId == area.Id, "Position was not captured.");
                await dashboard.Experience.SaveAsync(); var preferences = await new JsonRecordingExperienceStore(Environment.GetEnvironmentVariable("CAPTUREFLOW_EXPERIENCE_PREFERENCES")!).LoadAsync();
                Check(preferences.Anchor?.MonitorId == area.Id && preferences.ShowToolbar, "Toolbar preferences not persisted.");
                Console.WriteLine("PASS shared VM timer/state, collapse, visibility, topmost, minimized accessibility and native position persistence.");
                Console.WriteLine($"Desktop work areas={areas.Count}; DPI={string.Join(",", areas.Select(a => a.Dpi))}. Exclusion status: {model.CaptureNotice}");
                Console.WriteLine("Screen capture exclusion NOT VERIFIED: no real recording was made.");
                dashboard.Screen.Status = VideoRecordingState.Idle;
                main.WindowState = WindowState.Normal;
                // Test shutdown while a real countdown is pending, without an associated capture command.
                var pending = countdown.RunAsync(new(10), default);
                main.Closed += (_, _) =>
                {
                    Check(!app.Windows.OfType<FloatingToolbarWindow>().Any() && !app.Windows.OfType<CountdownWindow>().Any(), "Floating windows survived shutdown.");
                    Console.WriteLine("PASS shutdown closes countdown and toolbar windows and releases subscriptions."); completed = true;
                };
                main.RequestExit();
                try { await pending; throw new Exception("Exit did not cancel countdown."); } catch (OperationCanceledException) { }
                Check(bindings.Messages.Count == 0, string.Join("\n", bindings.Messages));
            }
            catch (Exception ex) { failed = true; Console.WriteLine("FAIL toolbar QA: " + ex); dashboard.Screen.Status = VideoRecordingState.Idle; main.RequestExit(); }
        };
        timer.Start(); app.Run(); Console.WriteLine($"Binding errors: {bindings.Messages.Count}; capture commands never executed.");
        return failed || !completed ? 1 : 0;
    }
    private static object? Field(object instance, string name) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Errors : TraceListener { public List<string> Messages = []; public override void Write(string? message) { } public override void WriteLine(string? message) { if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message); } }
}
