using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Infrastructure.Video;
public sealed class WindowsApplicationWindowDiscovery : IApplicationWindowDiscovery
{
    public Task<IReadOnlyList<ApplicationWindowDescriptor>> GetWindowsAsync(CancellationToken cancellationToken = default) => Task.Run<IReadOnlyList<ApplicationWindowDescriptor>>(() =>
    {
        var result = new List<ApplicationWindowDescriptor>();
        Callback callback = (handle, _) => { if (!cancellationToken.IsCancellationRequested && TryRead(handle) is { } window) result.Add(window); return !cancellationToken.IsCancellationRequested; };
        var success = EnumWindows(callback, IntPtr.Zero); cancellationToken.ThrowIfCancellationRequested();
        if (!success) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Application window enumeration failed.");
        return result.OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }, cancellationToken);
    internal static ApplicationWindowDescriptor? TryRead(IntPtr handle)
    {
        if (!IsWindow(handle)) return null;
        GetWindowThreadProcessId(handle, out var processId);
        var title = new StringBuilder(1024); GetWindowText(handle, title, title.Capacity);
        var cloakResult = DwmGetWindowAttribute(handle, 14, out var cloaked, sizeof(int));
        var eligibility = new WindowEligibility(IsWindowVisible(handle), IsIconic(handle), cloakResult != 0 || cloaked != 0,
            (GetWindowLongPtr(handle, -20).ToInt64() & 0x80) != 0, processId == Environment.ProcessId,
            GetWindowDisplayAffinity(handle, out var affinity) && affinity != 0, processId > 0, title.ToString());
        if (!eligibility.Eligible) return null;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            var started = process.StartTime.ToUniversalTime().Ticks;
            if (DwmGetWindowRect(handle, 9, out var rect, Marshal.SizeOf<Rect>()) != 0 && !GetWindowRect(handle, out rect)) return null;
            if (rect.Right - rect.Left < 2 || rect.Bottom - rect.Top < 2) return null;
            var id = $"{handle.ToInt64():X}:{processId}:{started}";
            return new(id, handle.ToInt64(), (int)processId, started, title.ToString(), process.ProcessName, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { return null; }
    }
    internal static void Validate(ApplicationWindowDescriptor window)
    {
        var current = TryRead(new IntPtr(window.Handle));
        if (current is null || current.Id != window.Id) throw new IOException("The selected application window is closed, minimized, protected or inaccessible. Restore it and refresh windows. Capture will not switch to the desktop.");
        ScreenCaptureLifecycle.EnsureDimensionsUnchanged(new(window.Width, window.Height), new(current.Width, current.Height), true);
    }
    internal static void CheckAlive(ApplicationWindowDescriptor window)
    {
        var handle = new IntPtr(window.Handle); GetWindowThreadProcessId(handle, out var processId);
        ScreenCaptureLifecycle.EnsureWindowAvailable(IsWindow(handle), processId == window.ProcessId, IsWindowVisible(handle), IsIconic(handle));
    }
    internal static MousePoint CaptureOrigin(nint handle, VideoSize size)
    {
        if (!GetWindowRect(handle, out var outer)) throw new IOException("Cannot locate the selected window for mouse effects.");
        if (DwmGetWindowRect(handle, 9, out var visible, Marshal.SizeOf<Rect>()) == 0 && visible.Right - visible.Left == size.Width && visible.Bottom - visible.Top == size.Height)
            return new(visible.Left, visible.Top);
        if (outer.Right - outer.Left == size.Width && outer.Bottom - outer.Top == size.Height) return new(outer.Left, outer.Top);
        throw new IOException("Window capture boundaries changed or cannot be mapped accurately for mouse effects. Refresh the window selection.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    private delegate bool Callback(IntPtr window, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumWindows(Callback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetWindowRect(IntPtr window, int attribute, out Rect value, int size);
}
