using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
namespace CaptureFlow.Desktop.Services;

/// <summary>Stable Windows shell identity and the unchanged, approved multi-resolution icon.</summary>
public static class ApplicationBranding
{
    public const string AppUserModelId = "CaptureFlow.Desktop";
    public const string IconUri = "pack://application:,,,/CaptureFlow.Desktop;component/Assets/Branding/CaptureFlow-New-Icon.ico";
    public static void InitializeIdentity() => Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppUserModelId));
    public static BitmapFrame LoadIconFrame(int size)
    {
        var decoder = BitmapDecoder.Create(new Uri(IconUri), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - size)).First(); frame.Freeze(); return frame;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
