using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaptureFlow.Desktop;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Desktop.Views;
using CaptureFlow.Infrastructure.Editing;
using CaptureFlow.Core.Editing;
using CaptureFlow.Application.Library;
using CaptureFlow.Qa;

internal static class EditorUiQa
{
    public static int Run(bool releaseQa = false, bool resourceIsolation = false, int resourceRounds = 1, bool managedLifetime = false)
    {
        var app = new App(); app.InitializeComponent(); var started = false; var failed = false; var completed = false;
        Stopwatch? shutdownWatch = null; TimeSpan shutdownCpu = default; WindowsPerformanceObservation.Snapshot? shutdownWindows = null;
        var bindings = new Errors(); PresentationTraceSources.DataBindingSource.Listeners.Add(bindings); PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        app.DispatcherUnhandledException += (_, e) => { failed = true; Console.WriteLine("FAIL unhandled: " + e.Exception); e.Handled = true; app.Shutdown(1); };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            if (started || app.MainWindow is not MainWindow main) return;
            if (CaptureFlow.Qa.PublishedSmokeQa.AcknowledgeOwnedTrayWarning(Environment.ProcessId)) return;
            started = true; timer.Stop();
            var dashboard = (DashboardViewModel)main.DataContext;
            try
            {
                Console.WriteLine("Editor QA phase: desktop initialization");
                await main.DesktopInitialization;
                Console.WriteLine("Editor QA phase: generated fixture");
                var folder = Path.GetFullPath("artifacts/qa/editor-ui/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); dashboard.OutputFolder = folder;
                var source = Path.Combine(folder, "synthetic-original.mp4"); await GenerateAsync(source);
                if (Environment.GetCommandLineArgs().Contains("--transport-qa"))
                    await CaptureFlow.Qa.EditorTransportQa.RunAsync(main, dashboard, source, Environment.GetCommandLineArgs().Contains("--transport-baseline"));
                var measuredBackend = new FfmpegEditorBackend(); var metadata = await measuredBackend.ProbeAsync(source, "ffmpeg.exe", default); var analysis = await measuredBackend.AnalyzeAsync(metadata, "ffmpeg.exe", default);
                Console.WriteLine($"Measured generated 320x240/10 FPS/3s artwork: thumbnail={analysis.ThumbnailMilliseconds:0.0} ms, filmstrip={analysis.FilmstripMilliseconds:0.0} ms, waveform={analysis.WaveformMilliseconds:0.0} ms; frames={analysis.FilmstripFrames}, buckets={analysis.Waveform.Length}.");
                foreach (var artifact in new[] { analysis.Thumbnail, analysis.Filmstrip }.OfType<string>()) File.Delete(artifact);
                await dashboard.RefreshRecordingsCommand.ExecuteAsync(null);
                dashboard.SelectedRecording = dashboard.Recordings.Single(r => r.FullPath == source);
                await dashboard.EditRecordingCommand.ExecuteAsync(null);
                Console.WriteLine("Editor QA phase: edit loaded");
                var vm = dashboard.Editor ?? throw new Exception("Editor DI failed.");
                Check(dashboard.IsEditor && vm.Project.Clips.Length == 1 && vm.SelectedClip is not null, "Library Edit did not load/select the recording.");
                Check(vm.Media.Single().Filmstrip.Length == 8 && vm.Media.Single().Waveform.Length == 512, "Bounded artwork caches were not loaded.");
                vm.TrimIn = .2; vm.TrimOut = 2.2; vm.ClipSpeed = 2; vm.ClipVolume = .4; vm.Text = "CaptureFlow synthetic QA"; vm.TextSize = 36; vm.ApplyClipCommand.Execute(null);
                vm.SetCrop(new(16, 16, 160, 120)); vm.Playhead = .5; vm.SplitCommand.Execute(null);
                Check(vm.Project.Clips.Length == 2 && Math.Abs(vm.Project.Duration - 1) < .001, "Trim/speed/split UI commands are incorrect.");
                vm.UndoCommand.Execute(null); vm.RedoCommand.Execute(null);
                vm.SelectedClip = vm.Project.Clips.OrderBy(c => c.Start).First();
                foreach (var property in Enum.GetValues<AnimationProperty>())
                {
                    vm.AnimationProperty = property; vm.Playhead = 0;
                    vm.KeyframeValue = property switch { AnimationProperty.Scale => .8, AnimationProperty.Opacity => .6, AnimationProperty.Volume => .5, _ => 0 };
                    vm.AddKeyframeCommand.Execute(null); vm.Playhead = .5;
                    vm.KeyframeValue = property switch { AnimationProperty.Scale or AnimationProperty.Opacity or AnimationProperty.Volume => 1, AnimationProperty.Rotation => 10, _ => .05 };
                    vm.AddKeyframeCommand.Execute(null);
                }
                Check(vm.ClipKeyframes.Length == 12 && vm.Project.Version == 2, "Native keyframe controls did not create persisted advanced edits.");
                vm.SelectedKeyframe = vm.ClipKeyframes.Last(); vm.KeyframeTime = .4;
                Check(vm.UpdateKeyframeCommand.CanExecute(null), "Selected keyframe did not enable its edit command.");
                vm.UpdateKeyframeCommand.Execute(null); Check(Math.Abs(vm.SelectedKeyframe!.Time - .4) < .001, "Keyframe time edit failed.");
                vm.DeleteKeyframeCommand.Execute(null); Check(vm.ClipKeyframes.Length == 11, "Keyframe deletion failed."); vm.UndoCommand.Execute(null); vm.RedoCommand.Execute(null); vm.UndoCommand.Execute(null);
                vm.TransitionPlacement = TransitionPlacement.Start; vm.TransitionType = TransitionType.FadeIn; vm.TransitionDuration = .08; vm.ApplyTransitionCommand.Execute(null);
                vm.SelectedClip = vm.Project.Clips.OrderBy(c => c.Start).Last(); vm.TransitionType = TransitionType.CrossDissolve; vm.TransitionDuration = .1; vm.ApplyTransitionCommand.Execute(null);
                Check(vm.SelectedClip!.Transitions?.Single().Type == TransitionType.CrossDissolve, "Native transition command failed: " + vm.Message);
                Console.WriteLine("PASS native Inspector: six animated properties, keyframe select/move/delete/undo/redo, fade-in and cross dissolve.");
                vm.PreviewQuality = "360p";
                var preview = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); vm.PreviewRequested += path => preview.TrySetResult(path);
                var previewWatch = Stopwatch.StartNew(); await vm.PreviewCommand.ExecuteAsync(null); Console.WriteLine($"Measured edited 360p preview render+validation: {previewWatch.Elapsed.TotalMilliseconds:0.0} ms."); var previewPath = await preview.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Console.WriteLine("Editor QA phase: preview rendered");
                var page = Find<EditorPage>(main).Single(); var player = (LibraryMediaPlayer)page.FindName("Player");
                for (var i = 0; i < 30 && player.Session.State is PlaybackState.Loading or PlaybackState.Empty; i++) await Task.Delay(100);
                Check(player.Session.State is PlaybackState.Playing or PlaybackState.Ended, "Real edited media did not open/play: " + player.Session.Error);
                var seekWatch = Stopwatch.StartNew(); player.SeekTo(TimeSpan.FromSeconds(.3)); player.Play(); await Task.Delay(150); player.Pause(); Console.WriteLine($"Measured WPF seek/play/pause observation: {seekWatch.Elapsed.TotalMilliseconds:0.0} ms (includes intentional 150-ms playback; not exact decoded-frame latency).");
                Check(player.Session.State == PlaybackState.Paused && player.Session.Position.TotalSeconds >= .25, "Pause/seek did not apply to real media."); player.Stop();
                dashboard.NavigateCommand.Execute("Recordings"); Check(player.Session.State == PlaybackState.Empty, "Navigation did not release media.");
                dashboard.NavigateCommand.Execute("Video & Audio Editor");
                for (var i = 0; i < 30 && player.Session.State is PlaybackState.Loading or PlaybackState.Empty; i++) await Task.Delay(100);
                Check(player.Session.State == PlaybackState.Stopped, "Cached preview was not restored without autoplay.");
                Console.WriteLine("PASS real editor navigation/Library Edit, filmstrip/waveform, trim/speed/split/undo/redo/crop and rendered WPF preview open/play/pause/seek/stop.");
                var projectPath = Path.Combine(folder, "qa.cfedit"); var store = new JsonEditorProjectStore(); await store.SaveAsync(vm.Project, projectPath, default); var reopened = await store.LoadAsync(projectPath, default); Check(reopened.Clips.Length == 2, "Project persistence failed.");
                var memoryBefore = Process.GetCurrentProcess().PrivateMemorySize64; var reloadWatch = Stopwatch.StartNew();
                for (var reload = 0; reload < 4; reload++) { vm.ResetProject(reopened); await vm.ImportFileAsync(source); }
                vm.SelectedClip = vm.Project.Clips.First(); Console.WriteLine($"Measured four generated project reset/reimports with artwork reuse: {reloadWatch.Elapsed.TotalMilliseconds:0.0} ms; private bytes {memoryBefore} -> {Process.GetCurrentProcess().PrivateMemorySize64}; sources={vm.Media.Count}.");
                var dragWatch = Stopwatch.StartNew(); var gestureProject = vm.Project with { Clips = vm.Project.Clips.Select(c => c with { Transitions = [] }).ToArray() }; var anchorClip = gestureProject.Clips.First(); for (var proposal = 0; proposal < 2000; proposal++) CaptureFlow.Application.Editing.TimelineGeometry.Propose(gestureProject, anchorClip, CaptureFlow.Application.Editing.TimelineGesture.Move, 10 + proposal / 30d, 0, 0, 40, false);
                Console.WriteLine($"Measured 2000 validated drag proposals on generated multi-clip project: {dragWatch.Elapsed.TotalMilliseconds:0.0} ms.");
                vm.ExportFormat = "MP4"; vm.ExportResolution = "720p"; var exportWatch = Stopwatch.StartNew(); await vm.ExportCommand.ExecuteAsync(null); Check(vm.Message.StartsWith("Completed"), vm.Message);
                Console.WriteLine($"Measured advanced 720p export/probe/full-decode: {exportWatch.Elapsed.TotalMilliseconds:0.0} ms; private bytes={Process.GetCurrentProcess().PrivateMemorySize64}; owned output/manifest bytes={Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)}.");
                await dashboard.RefreshRecordingsCommand.ExecuteAsync(null); Check(dashboard.Recordings.Count == 2, "Export not discovered in SQLite library.");
                main.WindowState = WindowState.Normal; main.Width = 900; main.Height = 480; await Task.Delay(200); main.UpdateLayout();
                Check(player.ActualWidth >= 200, "Compact editor preview below usable minimum.");
                Check(page.FindName("WorkspaceViewport") is System.Windows.Controls.Grid, "Editor must have a bounded workspace, not a full-page scroll viewer.");
                Find<System.Windows.Controls.Button>(page).Single(b => (string?)b.Content == "Export").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                main.UpdateLayout();
                var export = Find<System.Windows.Controls.Button>(page).Single(b => ReferenceEquals(b.Command, vm.ExportCommand));
                Check(export.IsVisible, "Export settings must open from the compact header.");
                Console.WriteLine("PASS compact editor: bounded workspace, usable preview and on-demand export.");
                Check(Find<System.Windows.Controls.TextBox>(page).Where(t => t.IsVisible).All(t => !string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(t))), "A visible editor text input has no accessible name.");
                Console.WriteLine("PASS visible editor text inputs have accessible names.");
                Console.WriteLine($"PASS editor minimum-width constraint: window={main.ActualWidth:F0} DIP; preview={player.ActualWidth:F0} DIP.");
                main.WindowState = WindowState.Maximized; await Task.Delay(200); main.UpdateLayout();
                var crfInput = Find<System.Windows.Controls.TextBox>(page).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Export Crf");
                var firstCharacter = crfInput.GetRectFromCharacterIndex(0);
                var lastCharacter = crfInput.GetRectFromCharacterIndex(crfInput.Text.Length - 1, true);
                Check(!firstCharacter.IsEmpty && firstCharacter.Left >= 0 && lastCharacter.Right <= crfInput.ActualWidth,
                    "Export CRF input clips characters; check duplicate TextBox padding.");
                Console.WriteLine("PASS narrow export input displays all characters within its bounds.");
                Find<System.Windows.Controls.Button>(page).Single(b => (string?)b.Content == "Close export").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var splitters = Find<System.Windows.Controls.GridSplitter>(page).ToArray();
                Check(splitters.Length == 3, "Expected media/preview/inspector and vertical timeline splitters.");
                main.WindowState = WindowState.Maximized; await Task.Delay(200); main.UpdateLayout();
                foreach (var splitter in splitters)
                {
                    var parent = (System.Windows.Controls.Grid)VisualTreeHelper.GetParent(splitter);
                    var rows = splitter.ResizeDirection == System.Windows.Controls.GridResizeDirection.Rows;
                    var index = rows ? System.Windows.Controls.Grid.GetRow(splitter) : System.Windows.Controls.Grid.GetColumn(splitter);
                    var before = rows ? parent.RowDefinitions[index - 1].ActualHeight : parent.ColumnDefinitions[index - 1].ActualWidth;
                    splitter.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
                    splitter.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(rows ? 0 : 12, rows ? 12 : 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
                    splitter.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
                    main.UpdateLayout();
                    var after = rows ? parent.RowDefinitions[index - 1].ActualHeight : parent.ColumnDefinitions[index - 1].ActualWidth;
                    Console.WriteLine($"Splitter rows={rows} index={index}: {before} -> {after}");
                    Check(Math.Abs(after - before) > 1, "Workspace splitter did not resize its adjacent panel.");
                }
                Console.WriteLine("PASS all three real WPF splitter drag events resize adjacent editor panels.");
                if (resourceIsolation) await CaptureFlow.Qa.ResourceIsolationQa.RunAsync(vm, source, player);
                if (managedLifetime) await CaptureFlow.Qa.ManagedPlaybackLifetimeQa.RunAsync(main, vm, source);
                if (releaseQa) for (var round = 1; round <= resourceRounds; round++)
                { Console.WriteLine($"Resource workload round {round}/{resourceRounds}"); await CaptureFlow.Qa.ReleaseEditorQa.RunAsync(vm, source, folder); }
                // Temporarily remove the adjacency dependency to exercise the baseline clip movement independently.
                vm.SelectedClip = vm.Project.Clips.OrderBy(c => c.Start).Last(); vm.TransitionPlacement = TransitionPlacement.Start; vm.DeleteTransitionCommand.Execute(null);
                await CaptureFlow.Qa.EditorPointerQa.RunAsync(main, (EditorTimeline)page.FindName("Timeline"), vm);
                vm.SelectedClip = vm.Project.Clips.OrderBy(c => c.Start).Last(); vm.TransitionType = TransitionType.CrossDissolve; vm.TransitionDuration = .1; vm.ApplyTransitionCommand.Execute(null);
                await vm.PreviewCommand.ExecuteAsync(null);
                for (var open = 0; open < 30 && player.Session.State is PlaybackState.Loading or PlaybackState.Empty; open++) await Task.Delay(100);
                player.SeekTo(TimeSpan.FromSeconds(.3)); player.Play(); await Task.Delay(200); player.Pause();
                Find<System.Windows.Controls.Button>(page).Single(b => (string?)b.Content == "Fit").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await CaptureFlow.Qa.PremiumVisualQa.CaptureAsync(main, page, "editor");
                main.UpdateLayout();
                Check(page.ActualHeight > 400 && ((FrameworkElement)page.FindName("Timeline")).ActualHeight >= 130, "Editor workspace/timeline layout is collapsed.");
                var image = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(main);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var file = File.Create(Path.Combine(folder, "editor-ui.png"))) encoder.Save(file);
                vm.SelectedClip = vm.Project.Clips.OrderBy(c => c.Start).First(); vm.SelectedKeyframe = vm.ClipKeyframes.First();
                Find<System.Windows.Controls.Expander>(page).Single(e => ((string?)e.Header)?.StartsWith("Animation") == true).BringIntoView();
                await Task.Delay(100); main.UpdateLayout();
                var animationImage = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32); animationImage.Render(main);
                var animationEncoder = new PngBitmapEncoder(); animationEncoder.Frames.Add(BitmapFrame.Create(animationImage)); using (var file = File.Create(Path.Combine(folder, "editor-animation.png"))) animationEncoder.Save(file);
                Check(bindings.Messages.Count == 0, string.Join("\n", bindings.Messages));
                Console.WriteLine("PASS edited export/FFprobe/decode validation, SQLite discovery, project save/load; original fixture preserved.");
                Console.WriteLine("QA folder: " + folder + "; preview: " + previewPath);
                Console.WriteLine("Perceptual audio/video synchronization and pointer/keyboard gestures require manual verification. No physical capture started.");
                vm.Session.MarkSaved(); main.Closed += (_, _) => { completed = true; Console.WriteLine("PASS graceful editor shutdown."); };
                ResourceSnapshot.Log("app-before-shutdown");
                using (var process = Process.GetCurrentProcess()) shutdownCpu = process.TotalProcessorTime;
                shutdownWindows = WindowsPerformanceObservation.Take(); shutdownWatch = Stopwatch.StartNew(); main.RequestExit();
            }
            catch (Exception ex) { failed = true; Console.WriteLine("FAIL editor UI QA: " + ex); dashboard.Editor?.Session.MarkSaved(); main.RequestExit(); }
        };
        timer.Start(); app.Run();
        if (shutdownWatch is not null && shutdownWindows is not null)
        {
            using var process = Process.GetCurrentProcess();
            Console.WriteLine("SHUTDOWN_PERF " + System.Text.Json.JsonSerializer.Serialize(new { elapsedMs = shutdownWatch.Elapsed.TotalMilliseconds,
                cpuPercentNormalized = (process.TotalProcessorTime - shutdownCpu).TotalMilliseconds / shutdownWatch.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100,
                windows = WindowsPerformanceObservation.Compare(shutdownWindows, WindowsPerformanceObservation.Take()), completed }));
            ResourceSnapshot.Log("app-after-shutdown");
        }
        Console.WriteLine($"Binding errors: {bindings.Messages.Count}"); return failed || !completed ? 1 : 0;
    }
    private static async Task GenerateAsync(string file)
    {
        var info = new ProcessStartInfo("ffmpeg.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=30:duration=3", "-f", "lavfi", "-i", "sine=frequency=440:duration=3", "-t", "3", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", file }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!; var errors = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); if (process.ExitCode != 0) throw new Exception(await errors);
    }
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) yield return match; foreach (var nested in Find<T>(child)) yield return nested; } }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Errors : TraceListener { public List<string> Messages = []; public override void Write(string? message) { } public override void WriteLine(string? message) { if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message); } }
}

