using System.ComponentModel;
using System.Runtime.InteropServices;
namespace CaptureFlow.Qa;

// Aggregate read-only Windows observations. No process enumeration, sampling stacks,
// privileged tracing, policy changes or device access.
internal static class WindowsPerformanceObservation
{
    public static async Task<int> RunAsync()
    {
        await Task.Delay(3000);
        static int AppCount()
        {
            var apps = System.Diagnostics.Process.GetProcessesByName("CaptureFlow.Desktop");
            try { return apps.Length; } finally { foreach (var app in apps) app.Dispose(); }
        }
        var appsBefore = AppCount(); var before = Take();
        await Task.Delay(5000);
        Console.WriteLine("WINDOWS_BASELINE " + System.Text.Json.JsonSerializer.Serialize(new { captureFlowProcessesBefore = appsBefore,
            captureFlowProcessesAfter = AppCount(), windows = Compare(before, Take()), note = "No CaptureFlow application or capture device constructed by this observer." }));
        return 0;
    }
    internal sealed record Snapshot(DateTimeOffset Utc, long Idle, long Kernel, long User,
        ulong TotalPhysical, ulong AvailablePhysical, uint MemoryLoad, string? Error);
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    public static Snapshot Take()
    {
        try
        {
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (!GetSystemTimes(out var idle, out var kernel, out var user) || !GlobalMemoryStatusEx(ref memory)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return new(DateTimeOffset.UtcNow, idle, kernel, user, memory.TotalPhysical, memory.AvailablePhysical, memory.MemoryLoad, null);
        }
        catch (Exception ex) { return new(DateTimeOffset.UtcNow, 0, 0, 0, 0, 0, 0, ex.Message); }
    }
    public static object Compare(Snapshot before, Snapshot after)
    {
        var total = after.Kernel - before.Kernel + after.User - before.User;
        double? busy = before.Error is null && after.Error is null && total > 0 ? Math.Clamp((total - (after.Idle - before.Idle)) * 100d / total, 0, 100) : null;
        return new { beforeUtc = before.Utc, afterUtc = after.Utc, systemCpuPercent = busy,
            totalPhysicalBytes = after.TotalPhysical, availablePhysicalBefore = before.AvailablePhysical,
            availablePhysicalAfter = after.AvailablePhysical, memoryLoadPercent = after.MemoryLoad,
            error = before.Error ?? after.Error, note = "Windows aggregate CPU includes this process, tools and unrelated activity; not attribution to a particular application." };
    }
}
