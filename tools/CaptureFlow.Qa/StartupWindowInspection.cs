using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Automation;
namespace CaptureFlow.Qa;

internal static class StartupWindowInspection
{
    public static int Run(int pid, string expectedStartedUtc)
    {
        using var process = Process.GetProcessById(pid);
        var expectedPath = Path.GetFullPath("src/CaptureFlow.Desktop/bin/Debug/net10.0-windows10.0.19041.0/CaptureFlow.Desktop.exe");
        if (!string.Equals(process.MainModule?.FileName, expectedPath, StringComparison.OrdinalIgnoreCase) ||
            process.StartTime.ToUniversalTime() != DateTime.Parse(expectedStartedUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal).ToUniversalTime())
            throw new InvalidOperationException("Process identity mismatch; no inspection performed.");
        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, pid));
        foreach (AutomationElement window in windows)
        {
            var texts = window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
            Console.WriteLine(JsonSerializer.Serialize(new { pid, path = expectedPath, startedUtc = process.StartTime.ToUniversalTime(),
                name = window.Current.Name, className = window.Current.ClassName, window.Current.IsOffscreen,
                window.Current.IsEnabled, text = texts.Cast<AutomationElement>().Select(t => t.Current.Name).ToArray() }));
        }
        return 0; // Read-only UI Automation; no Invoke, focus, input or message sending.
    }
}
