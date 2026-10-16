using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Threading;
using CaptureFlow.Application.Library;
using CaptureFlow.Desktop;
using CaptureFlow.Desktop.Views;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Qa;

internal static class EditorTransportQa
{
    public static async Task RunAsync(MainWindow main, DashboardViewModel dashboard, string source, bool baseline)
    {
        dashboard.NavigateCommand.Execute("Video & Audio Editor"); main.UpdateLayout();
        var vm = dashboard.Editor!; var page = Find<EditorPage>(main).Single();
        var player = (LibraryMediaPlayer)page.FindName("Player");
        Button Button(string text) => Find<Button>(page).Single(b => Equals(b.Content, text) && b.IsVisible);
        void Snapshot(string phase)
        {
            Console.WriteLine("TRANSPORT " + JsonSerializer.Serialize(new { phase, vm.PreviewStale, vm.IsBusy, clips = vm.Project.Clips.Length,
                selected = vm.SelectedClip is not null, state = player.Session.State.ToString(), position = player.Session.Position.TotalSeconds,
                controls = new[] { "Play", "Pause", "Stop" }.Select(name => new { name, enabled = Button(name).IsEnabled, command = Button(name).Command?.GetType().Name, binding = System.Windows.Data.BindingOperations.GetBindingExpression(Button(name), System.Windows.Controls.Primitives.ButtonBase.CommandProperty)?.ParentBinding.Path?.Path }).ToArray(), vm.Message }));
        }
        async Task Click(string name)
        {
            var b = Button(name);
            if (!b.IsEnabled) throw new InvalidOperationException(name + " disabled.");
            ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke();
            await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }
        async Task Wait(Func<bool> condition)
        { for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(50); if (!condition()) throw new TimeoutException("Transport state: " + player.Session.State + "; " + vm.Message); }
        async Task Measure(string phase, Func<Task> action)
        {
            using var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
            var systemBefore = WindowsPerformanceObservation.Take();
            var properties = 0; var commandInvalidations = 0; var layoutNotifications = 0; var timelineRefreshRequests = 0;
            System.ComponentModel.PropertyChangedEventHandler changed = (_, _) => properties++;
            EventHandler invalidated = (_, _) => commandInvalidations++;
            EventHandler layout = (_, _) => layoutNotifications++;
            Action timelineChanged = () => timelineRefreshRequests++;
            vm.PropertyChanged += changed; vm.Changed += timelineChanged; page.LayoutUpdated += layout;
            vm.PlayPreviewCommand.CanExecuteChanged += invalidated; vm.PausePreviewCommand.CanExecuteChanged += invalidated; vm.StopPreviewCommand.CanExecuteChanged += invalidated;
            var last = watch.Elapsed.TotalMilliseconds; double maxGap = 0;
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
            EventHandler tick = (_, _) => { var now = watch.Elapsed.TotalMilliseconds; maxGap = Math.Max(maxGap, now - last); last = now; };
            timer.Tick += tick; timer.Start();
            try { await action(); }
            finally
            {
                timer.Stop(); timer.Tick -= tick; process.Refresh();
                vm.PropertyChanged -= changed; vm.Changed -= timelineChanged; page.LayoutUpdated -= layout;
                vm.PlayPreviewCommand.CanExecuteChanged -= invalidated; vm.PausePreviewCommand.CanExecuteChanged -= invalidated; vm.StopPreviewCommand.CanExecuteChanged -= invalidated;
                maxGap = Math.Max(maxGap, watch.Elapsed.TotalMilliseconds - last);
                Console.WriteLine("EDITOR_PERF " + JsonSerializer.Serialize(new { phase, elapsedMs = watch.Elapsed.TotalMilliseconds,
                    cpuPercentNormalized = (process.TotalProcessorTime - cpu).TotalMilliseconds / watch.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100,
                    maxDispatcherGapMs = maxGap, properties, commandInvalidations, layoutUpdatedNotifications = layoutNotifications, timelineRefreshRequests,
                    windows = WindowsPerformanceObservation.Compare(systemBefore, WindowsPerformanceObservation.Take()),
                    note = "20ms background dispatcher sampling; application CPU excludes FFmpeg child CPU; LayoutUpdated notifications are not an exact count of layout passes" }));
                ResourceSnapshot.Log("transport-" + phase);
            }
        }
        vm.ResetProject(CaptureFlow.Core.Editing.EditorProject.Empty); Snapshot("A-empty");
        if (!baseline && new[] { "Play", "Pause", "Stop" }.Any(name => Button(name).IsEnabled)) throw new InvalidOperationException("Empty transport must be disabled.");
        await Task.Delay(3000); // Separate warm-up from the measured idle window.
        await Measure("idle", () => Task.Delay(3000));
        await Measure("studio-navigation", async () => { dashboard.NavigateCommand.Execute("Screen Recorder"); await Task.Delay(700); });
        await Measure("editor-navigation", async () => { dashboard.NavigateCommand.Execute("Video & Audio Editor"); await Task.Delay(700); });
        await Measure("import", () => vm.ImportFileAsync(source)); Snapshot("B-import-only");
        var filmstrip = vm.Media.Single().Filmstrip;
        await Measure("repeat-import", () => vm.ImportFileAsync(source));
        if (!ReferenceEquals(filmstrip, vm.Media.Single().Filmstrip)) throw new InvalidOperationException("Unchanged reimport replaced artwork unexpectedly.");
        Console.WriteLine("PASS unchanged reimport retains the same filmstrip cache instance; no extra media-bin entry.");
        vm.AddClipCommand.Execute(null); Snapshot("C-timeline");
        vm.SelectedClip = vm.Project.Clips.First(); Snapshot("D-selected");
        if (!baseline && (!ReferenceEquals(Button("Play").Command, vm.PlayPreviewCommand) || !ReferenceEquals(Button("Pause").Command, vm.PausePreviewCommand) || !ReferenceEquals(Button("Stop").Command, vm.StopPreviewCommand))) throw new InvalidOperationException("Transport does not bind to the actual production VM commands.");
        await Measure("play-initialize", async () => { await Click("Play"); if (!baseline) await Wait(() => player.Session.State == PlaybackState.Playing); });
        Snapshot("after-play-intent");
        if (baseline) await vm.PreviewCommand.ExecuteAsync(null);
        await Wait(() => player.Session.State == PlaybackState.Playing); Snapshot("E-ready-F-playing");
        await Measure("playback", () => Task.Delay(450));
        if (player.Session.Position <= TimeSpan.Zero) throw new InvalidOperationException("Playback position did not advance.");
        await Measure("pause-seek", async () => { await Click("Pause"); await Wait(() => player.Session.State == PlaybackState.Paused); player.SeekTo(TimeSpan.FromSeconds(.3)); await Task.Delay(300); });
        await Click("Play"); await Wait(() => player.Session.State == PlaybackState.Playing);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            await Click("Pause"); await Wait(() => player.Session.State == PlaybackState.Paused); Snapshot("G-paused-" + cycle);
            var paused = player.Session.Position; await Task.Delay(150);
            if (Math.Abs((player.Session.Position - paused).TotalSeconds) > .12) throw new InvalidOperationException("Pause did not freeze position.");
            player.SeekTo(TimeSpan.FromSeconds(.3)); await Click("Play"); await Wait(() => player.Session.State == PlaybackState.Playing); await Task.Delay(200);
        }
        await Click("Stop"); await Wait(() => player.Session.State == PlaybackState.Stopped); Snapshot("H-stopped");
        if (player.Session.Position != TimeSpan.Zero) throw new InvalidOperationException("Stop did not reset position.");
        await Task.Delay(3000);
        await Measure("loaded-stopped-idle", () => Task.Delay(3000));
        if (!baseline)
        {
            if (!Button("Play").IsEnabled || Button("Pause").IsEnabled || Button("Stop").IsEnabled) throw new InvalidOperationException("Stopped transport availability incorrect.");
            await Click("Play"); await Wait(() => player.Session.State == PlaybackState.Playing);
            player.SeekTo(TimeSpan.FromSeconds(.6)); await Task.Delay(100); Snapshot("seek-during-play");
            await Click("Pause");
            if (!Button("Play").IsEnabled || Button("Pause").IsEnabled || !Button("Stop").IsEnabled) throw new InvalidOperationException("Paused transport availability incorrect.");
            await Click("Stop");
        }
        if (!baseline)
        {
            await Measure("split-undo-redo", async () =>
            {
                vm.Playhead = 1; vm.SplitCommand.Execute(null);
                if (vm.Project.Clips.Length != 2) throw new InvalidOperationException("Split did not commit.");
                vm.UndoCommand.Execute(null); if (vm.Project.Clips.Length != 1) throw new InvalidOperationException("Undo did not restore.");
                vm.RedoCommand.Execute(null); if (vm.Project.Clips.Length != 2) throw new InvalidOperationException("Redo did not restore split.");
                vm.UndoCommand.Execute(null); await Task.Delay(300);
            });
            player.Open(source + ".missing"); Snapshot("failed-media"); await Click("Play");
            await Wait(() => player.Session.State == PlaybackState.Playing); Snapshot("recovered-media");
            await Click("Stop");
        }
        vm.ResetProject(CaptureFlow.Core.Editing.EditorProject.Empty); Snapshot("closed-project");
        Console.WriteLine("PASS transport integration via production visible WPF button automation peers; not foreground pointer verification.");
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T t) yield return t; foreach (var item in Find<T>(child)) yield return item; } }
}
