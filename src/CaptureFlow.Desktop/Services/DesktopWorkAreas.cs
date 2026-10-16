using System.ComponentModel;
using System.Runtime.InteropServices;
using CaptureFlow.Core.Desktop;
namespace CaptureFlow.Desktop.Services;
public static class DesktopWorkAreas
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Info { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    private delegate bool Callback(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);
    public static IReadOnlyList<DesktopWorkArea> Enumerate(bool fullMonitorBounds = false)
    {
        var result = new List<DesktopWorkArea>();
        Callback callback = (monitor, _, _, _) =>
        {
            var info = new Info { Size = Marshal.SizeOf<Info>(), Device = "" };
            if (GetMonitorInfo(monitor, ref info))
            {
                var dpi = GetDpiForMonitor(monitor, 0, out var x, out _) == 0 ? x : 96;
                var bounds = fullMonitorBounds ? info.Monitor : info.Work;
                result.Add(new(info.Device, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, dpi, (info.Flags & 1) != 0));
            }
            return true;
        };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return result;
    }
    public static void Place(IntPtr window, ToolbarRectangle rectangle) => SetWindowPos(window, IntPtr.Zero, (int)Math.Round(rectangle.Left), (int)Math.Round(rectangle.Top), (int)Math.Round(rectangle.Width), (int)Math.Round(rectangle.Height), 0x14);
    public static ToolbarRectangle Bounds(IntPtr window)
    { if (!GetWindowRect(window, out var r)) throw new Win32Exception(Marshal.GetLastWin32Error()); return new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top); }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, Callback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref Info info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
}
