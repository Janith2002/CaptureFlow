using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CaptureFlow.Core.Desktop;
using CaptureFlow.Infrastructure.Desktop;
namespace CaptureFlow.Qa;

// Normal CreateProcess launch of the unchanged apphost. This probe never loads the
// app DLL itself, interprets scripts, alters trust, or suppresses security prompts.
internal static class ProductionStartupQa
{
    public static async Task<int> RunAsync(bool inspectEditor = false)
    {
        var root = Path.GetFullPath("artifacts/qa/m696/startup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var exe = Path.GetFullPath("src/CaptureFlow.Desktop/bin/Debug/net10.0-windows10.0.19041.0/CaptureFlow.Desktop.exe");
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)!, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment["CAPTUREFLOW_STARTUP_DIAGNOSTICS"] = Path.Combine(root, "startup.jsonl");
        foreach (var name in new[] { "RECOVERY_ROOT", "FFMPEG_PREFERENCES", "CAMERA_DIAGNOSTICS", "MOUSE_PREFERENCES", "REGION_PREFERENCES", "EXPERIENCE_PREFERENCES", "QUALITY_PREFERENCES", "SHORTCUT_PREFERENCES", "LIBRARY_DATABASE", "LIBRARY_INITIAL_FOLDER", "STUDIO_PREFERENCES", "EDITOR_WORKSPACE" })
            info.Environment["CAPTUREFLOW_" + name] = Path.Combine(root, name.ToLowerInvariant());
        await new JsonShortcutPreferencesStore(info.Environment["CAPTUREFLOW_SHORTCUT_PREFERENCES"]!).SaveAsync(ShortcutPreferences.Defaults with { Enabled = false });
        var started = DateTimeOffset.UtcNow;
        using var child = Process.Start(info) ?? throw new IOException("Apphost process creation failed.");
        using var errors = child.StandardError; using var output = child.StandardOutput;
        var readErrors = errors.ReadToEndAsync(); var readOutput = output.ReadToEndAsync();
        using (var wait = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            try { await child.WaitForExitAsync(wait.Token); } catch (OperationCanceledException) when (wait.IsCancellationRequested) { }
        var exited = child.HasExited; var inspectionPassed = true;
        if (!exited)
        {
            child.Refresh(); Console.WriteLine($"Owned startup still running: PID={child.Id}; title={child.MainWindowTitle}. No capture commands were executed.");
            if (inspectEditor && child.MainWindowTitle == "CaptureFlow")
            {
                try
                {
                var rootElement = System.Windows.Automation.AutomationElement.FromHandle(child.MainWindowHandle);
                var editor = rootElement.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, "Video Editor"));
                if (editor is null) throw new InvalidOperationException("Production Editor navigation element missing.");
                ((System.Windows.Automation.SelectionItemPattern)editor.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern)).Select();
                await Task.Delay(3000);
                var controls = rootElement.FindAll(System.Windows.Automation.TreeScope.Descendants,
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.Button));
                foreach (System.Windows.Automation.AutomationElement control in controls)
                    if (!control.Current.IsOffscreen && new[] { "Play", "Pause", "Stop" }.Contains(control.Current.Name))
                        Console.WriteLine(JsonSerializer.Serialize(new { productionExeEmptyEditor = true, control = control.Current.Name, control.Current.IsEnabled, note = "UI Automation navigation/read, not mouse interaction" }));
                child.Refresh(); var cpu = child.TotalProcessorTime; var sampleWatch = Stopwatch.StartNew();
                await Task.Delay(5000); child.Refresh();
                Console.WriteLine("PRODUCTION_EDITOR_IDLE " + JsonSerializer.Serialize(new { pid = child.Id, path = exe, startedUtc = child.StartTime.ToUniversalTime(),
                    cpuPercentNormalized = (child.TotalProcessorTime - cpu).TotalMilliseconds / sampleWatch.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100,
                    handles = child.HandleCount, threads = child.Threads.Count, privateBytes = child.PrivateMemorySize64, elapsedMs = sampleWatch.Elapsed.TotalMilliseconds }));
                }
                catch (Exception ex) { inspectionPassed = false; Console.WriteLine("FAIL production Editor inspection: " + ex); }
            }
            if (child.MainWindowTitle == "CaptureFlow")
            {
                child.CloseMainWindow();
                using var closeWait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await child.WaitForExitAsync(closeWait.Token); } catch (OperationCanceledException) when (closeWait.IsCancellationRequested) { }
            }
            exited = child.HasExited;
        }
        var stderr = exited ? await readErrors : "Not read to EOF: owned process still running; no termination or prompt dismissal attempted.";
        if (exited) await File.WriteAllTextAsync(Path.Combine(root, "stdout.log"), await readOutput);
        await File.WriteAllTextAsync(Path.Combine(root, "stderr.log"), stderr);
        var blocked = stderr.Contains("0x800711C7", StringComparison.OrdinalIgnoreCase) || stderr.Contains("Application Control", StringComparison.OrdinalIgnoreCase);
        var log = info.Environment["CAPTUREFLOW_STARTUP_DIAGNOSTICS"]!;
        var records = File.Exists(log) ? File.ReadAllLines(log).Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray() : [];
        var phases = records.Select(r => r.GetProperty("phase").GetString()).ToArray();
        var visible = records.Any(r => r.GetProperty("phase").GetString() == "window-content-rendered" &&
            r.GetProperty("detail").GetProperty("IsVisible").GetBoolean() && r.GetProperty("detail").GetProperty("ShowInTaskbar").GetBoolean());
        var verified = visible && phases.Count(p => p == "main-window-construction-complete") == 1 &&
            phases.Count(p => p == "window-loaded") == 1 && phases.Contains("dispatcher-idle") && phases.Contains("shutdown-complete");
        Console.WriteLine(JsonSerializer.Serialize(new { exe, pid = child.Id, processStartedUtc = child.StartTime.ToUniversalTime(), started, exited, exitCode = exited ? (int?)child.ExitCode : null, applicationControlInStderr = blocked, evidence = root, captureCommandsExecuted = false }));
        if (blocked) Console.WriteLine("BLOCKED direct production EXE: Application Control rejected its dependency. No security control changed.");
        else Console.WriteLine($"Structured startup verification={verified}; visible rendered taskbar window={visible}; constructions={phases.Count(p => p == "main-window-construction-complete")}; Loaded={phases.Count(p => p == "window-loaded")}; idle and shutdown required. Human observation remains separate.");
        return blocked || !inspectionPassed || !verified || !exited || child.ExitCode != 0 ? 1 : 0;
    }
}
