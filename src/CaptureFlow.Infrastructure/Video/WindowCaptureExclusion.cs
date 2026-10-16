using System.Runtime.InteropServices;
namespace CaptureFlow.Infrastructure.Video;
public static class WindowCaptureExclusion
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    public static bool TryExclude(nint window) => SetWindowDisplayAffinity(window, 0x11); // WDA_EXCLUDEFROMCAPTURE
}
