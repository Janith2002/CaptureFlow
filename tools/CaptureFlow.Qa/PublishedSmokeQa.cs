using System.Diagnostics;
using System.IO;
namespace CaptureFlow.Qa;

internal static class PublishedSmokeQa
{
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    internal static bool AcknowledgeOwnedTrayWarning(int processId)
    {
        var owned = System.Windows.Automation.AutomationElement.RootElement.FindAll(System.Windows.Automation.TreeScope.Children,
            new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ProcessIdProperty, processId));
        foreach (System.Windows.Automation.AutomationElement candidate in owned)
        {
            var message = candidate.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, "Tray unavailable: Windows notification area is unavailable."));
            if (message is null) continue;
            var button = candidate.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, "OK"));
            if (button is null || button.Current.NativeWindowHandle == 0) continue;
            Console.WriteLine("BLOCKED tray registration in owned child process: Windows notification area is unavailable. Acknowledging this application warning only; no security dialog or policy changed.");
            SendMessage(new IntPtr(button.Current.NativeWindowHandle), 0xF5, IntPtr.Zero, IntPtr.Zero); return true;
        }
        return false;
    }
    public static async Task<int> RunAsync(string folder, bool verifyTools = false, bool requireBundledRuntime = true)
    {
        folder = Path.GetFullPath(folder);
        string? expectedTool = null;
        if (verifyTools)
        {
            expectedTool = CaptureFlow.Infrastructure.Ffmpeg.FfmpegToolPaths.ResolveExecutable("ffmpeg.exe");
            await new CaptureFlow.Infrastructure.Ffmpeg.JsonFfmpegPreferencesStore(Environment.GetEnvironmentVariable("CAPTUREFLOW_FFMPEG_PREFERENCES")!).SaveAsync(expectedTool);
        }
        var cwd = Path.Combine(Path.GetTempPath(), "CaptureFlow.Qa", "published-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(cwd);
        // The user requested visible application/icon verification. This is the application under test,
        // not a background helper; hiding it prevents Process.MainWindowHandle/UI Automation discovery.
        var info = new ProcessStartInfo(Path.Combine(folder, "CaptureFlow.Desktop.exe")) { UseShellExecute = false, WorkingDirectory = requireBundledRuntime ? cwd : folder, WindowStyle = ProcessWindowStyle.Normal };
        using var process = Process.Start(info) ?? throw new IOException("Published application did not start.");
        Console.WriteLine($"Owned application launch: PID={process.Id}; EXE={info.FileName}");
        try
        {
            var until = DateTime.UtcNow.AddSeconds(45); var acknowledged = false;
            while (DateTime.UtcNow < until)
            { process.Refresh(); if (process.HasExited) throw new IOException("Published app exited during startup: " + process.ExitCode); if (process.MainWindowHandle != IntPtr.Zero && process.MainWindowTitle == "CaptureFlow") break; if (!acknowledged) acknowledged = AcknowledgeOwnedTrayWarning(process.Id); await Task.Delay(100); }
            process.Refresh(); if (process.MainWindowHandle == IntPtr.Zero || process.MainWindowTitle != "CaptureFlow") throw new TimeoutException($"Application main window not found; PID={process.Id}, HWND={process.MainWindowHandle}, title={process.MainWindowTitle}, CPU={process.TotalProcessorTime}.");
            await Task.Delay(1500);
            var window = System.Windows.Automation.AutomationElement.FromHandle(process.MainWindowHandle);
            foreach (var page in new[] { "Recordings", "Video and Audio Editor", "Settings", "Dashboard" })
            {
                var navigation = window.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                    new System.Windows.Automation.AndCondition(
                        new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, page),
                        new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.RadioButton)))
                    ?? throw new IOException("Published navigation missing: " + page);
                ((System.Windows.Automation.SelectionItemPattern)navigation.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern)).Select();
                await Task.Delay(250);
                if (!((System.Windows.Automation.SelectionItemPattern)navigation.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern)).Current.IsSelected)
                    throw new IOException("Published page did not activate: " + page);
                Console.WriteLine("PASS published native navigation: " + page);
                if (page == "Settings" && expectedTool is not null)
                {
                    var tab = window.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                        new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, "External tools"))
                        ?? throw new IOException("External tools tab missing.");
                    ((System.Windows.Automation.SelectionItemPattern)tab.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern)).Select();
                    await Task.Delay(250);
                    var input = window.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                        new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, "External FFmpeg executable path"))
                        ?? throw new IOException("External tools input missing.");
                    if (((System.Windows.Automation.ValuePattern)input.GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern)).Current.Value != expectedTool)
                        throw new IOException("Installed application did not load the saved external-tool path.");
                    Console.WriteLine("PASS installed app loaded persisted external FFmpeg path into Settings.");
                }
            }
            var database = Environment.GetEnvironmentVariable("CAPTUREFLOW_LIBRARY_DATABASE")!;
            if (!File.Exists(database) || new FileInfo(database).Length < 16) throw new IOException("Published SQLite database was not initialized.");
            if (!File.Exists(Environment.GetEnvironmentVariable("CAPTUREFLOW_CAMERA_DIAGNOSTICS"))) throw new IOException("Published diagnostic log was not created.");
            Console.WriteLine("PASS published SQLite first-run database and writable diagnostic log.");
            var runtime = process.Modules.Cast<ProcessModule>().Single(m => string.Equals(m.ModuleName, "coreclr.dll", StringComparison.OrdinalIgnoreCase)).FileName;
            if (requireBundledRuntime && !string.Equals(Path.GetDirectoryName(runtime), folder, StringComparison.OrdinalIgnoreCase)) throw new IOException("Self-contained smoke test unexpectedly loaded an external runtime: " + runtime);
            Console.WriteLine(requireBundledRuntime ? "PASS published startup from independent working directory; bundled coreclr=" + runtime : "PASS fresh built EXE startup directly from output directory; coreclr=" + runtime);
            if (!process.CloseMainWindow()) throw new IOException("Could not request published window shutdown.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            if (process.ExitCode != 0) throw new IOException("Published shutdown failed: " + process.ExitCode);
            Console.WriteLine("PASS published graceful close, exit 0. QA-only preferences/database/logs. No capture or preview started. Not a clean-machine test."); return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL published smoke: " + ex); return 1; }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
}
