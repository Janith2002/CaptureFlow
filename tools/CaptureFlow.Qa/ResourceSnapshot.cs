using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
namespace CaptureFlow.Qa;

/// <summary>Read-only inspection of this process only. Never queries object names or other processes.</summary>
internal static class ResourceSnapshot
{
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr process, int information, IntPtr buffer, int length, out int returned);
    [DllImport("ntdll.dll")] private static extern int NtQueryObject(IntPtr handle, int information, IntPtr buffer, int length, out int returned);
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle OpenThread(uint access, bool inherit, uint threadId);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationThread(SafeFileHandle thread, int information, out IntPtr start, int size, IntPtr returned);
    public static void LogThreadOwners()
    {
        using var process = Process.GetCurrentProcess();
        var modules = process.Modules.Cast<ProcessModule>().Select(m => (Name: m.ModuleName, Start: m.BaseAddress.ToInt64(), End: m.BaseAddress.ToInt64() + m.ModuleMemorySize)).ToArray();
        var owners = new Dictionary<string, int>(); var denied = 0;
        foreach (ProcessThread thread in process.Threads)
        using (thread)
        {
            // Query our own thread start addresses only. No suspension, stack walking,
            // token manipulation, writes, or handles to another process.
            using var handle = OpenThread(0x0040, false, (uint)thread.Id);
            if (handle.IsInvalid || NtQueryInformationThread(handle, 9, out var start, IntPtr.Size, IntPtr.Zero) < 0) { denied++; continue; }
            var address = start.ToInt64(); var module = modules.FirstOrDefault(m => address >= m.Start && address < m.End);
            var name = module.Name ?? "unmapped"; owners[name] = owners.GetValueOrDefault(name) + 1;
        }
        Console.WriteLine("OWNED_THREAD_START_MODULES " + JsonSerializer.Serialize(new { owners, denied, note = "Start-address modules, not allocation stacks or proof of semaphore ownership." }));
    }
    [StructLayout(LayoutKind.Sequential)] private struct Entry
    { public IntPtr Handle, HandleCount, PointerCount; public uint Access, Type, Attributes, Reserved; }
    public static Dictionary<string, int> Types()
    {
        var size = 256 * 1024;
        while (size <= 32 * 1024 * 1024)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = NtQueryInformationProcess(new(-1), 51, buffer, size, out var needed);
                if (status is unchecked((int)0xC0000004) or unchecked((int)0xC0000023)) { size = Math.Max(size * 2, needed + 4096); continue; }
                if (status < 0) throw new InvalidOperationException($"Owned-process handle snapshot unavailable: 0x{status:X8}");
                var count = Marshal.ReadIntPtr(buffer).ToInt64(); var stride = Marshal.SizeOf<Entry>();
                if (count < 0 || count > (size - 2 * IntPtr.Size) / stride) throw new InvalidOperationException("Unexpected handle snapshot bounds.");
                var names = new Dictionary<uint, string>(); var types = new Dictionary<string, int>();
                for (var i = 0; i < count; i++)
                {
                    var entry = Marshal.PtrToStructure<Entry>(buffer + 2 * IntPtr.Size + i * stride);
                    if (!names.TryGetValue(entry.Type, out var name))
                    {
                        name = TypeName(entry.Handle);
                        if (name != "Unknown") names[entry.Type] = name;
                    }
                    types[name] = types.GetValueOrDefault(name) + 1;
                }
                return types;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new InvalidOperationException("Owned handle snapshot exceeded its bound.");
    }
    private static string TypeName(IntPtr handle)
    {
        var buffer = Marshal.AllocHGlobal(4096);
        try
        {
            if (NtQueryObject(handle, 2, buffer, 4096, out _) < 0) return "Unknown";
            var length = (ushort)Marshal.ReadInt16(buffer); var pointer = Marshal.ReadIntPtr(buffer, IntPtr.Size);
            return length <= 512 && pointer != IntPtr.Zero ? Marshal.PtrToStringUni(pointer, length / 2) ?? "Unknown" : "Unknown";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public static void Log(string phase)
    {
        using var process = Process.GetCurrentProcess(); var types = Types(); process.Refresh();
        var threads = process.Threads;
        var liveThreads = threads.Count;
        foreach (ProcessThread thread in threads) thread.Dispose();
        Console.WriteLine("RESOURCE " + JsonSerializer.Serialize(new { phase, handles = process.HandleCount, liveThreads, gdi = GetGuiResources(new(-1), 0), user = GetGuiResources(new(-1), 1), privateBytes = process.PrivateMemorySize64, types }));
    }
}
