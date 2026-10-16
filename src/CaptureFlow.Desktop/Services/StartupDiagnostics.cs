using System.Diagnostics;
using System.IO;
using System.Text.Json;
namespace CaptureFlow.Desktop.Services;

/// <summary>Best-effort, per-process lifecycle evidence. Never records media or input.</summary>
public static class StartupDiagnostics
{
    private static readonly object Gate = new();
    private static readonly string CorrelationId = Guid.NewGuid().ToString("N");
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static string? logPath;
    public static string LogPath => logPath ?? "startup log unavailable (see stderr/Trace)";
    public static void Record(string phase, object? detail = null, Exception? exception = null)
    {
        try
        {
            var json = JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, correlationId = CorrelationId,
                pid = Environment.ProcessId, elapsedMs = Clock.Elapsed.TotalMilliseconds,
                managedThread = Environment.CurrentManagedThreadId, phase, detail, exception = exception?.ToString() });
            lock (Gate)
            {
                logPath ??= Environment.GetEnvironmentVariable("CAPTUREFLOW_STARTUP_DIAGNOSTICS") ?? Path.Combine(
                    CaptureFlow.Infrastructure.Desktop.ApplicationDataPaths.Root, "Logs", $"startup-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{CorrelationId}.jsonl");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
                File.AppendAllText(logPath, json + Environment.NewLine);
            }
        }
        catch (Exception ex) { Trace.TraceError("Startup diagnostics unavailable: {0}; phase={1}; exception={2}", ex, phase, exception); Console.Error.WriteLine($"Startup diagnostics unavailable: {ex.Message}; phase={phase}; {exception}"); }
    }
}
