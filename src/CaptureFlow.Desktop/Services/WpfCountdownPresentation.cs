using System.Runtime.InteropServices;
using System.Windows.Interop;
using CaptureFlow.Core.Desktop;
using CaptureFlow.Desktop.Views;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Desktop.Services;
public sealed class WpfCountdownPresentation : ICountdownPresentation
{
    private CountdownWindow? window;
    public void Show(CountdownRequest request, int remaining, Action cancel)
    {
        if (window is null)
        {
            window = new(); window.CancelRequested += cancel;
            var areas = DesktopWorkAreas.Enumerate(); var area = areas.FirstOrDefault(a => a.Id == request.MonitorId) ?? areas.FirstOrDefault(a => a.Primary) ?? areas.First();
            window.SourceInitialized += (_, _) => WindowCaptureExclusion.TryExclude(new WindowInteropHelper(window).Handle);
            window.Show();
            DesktopWorkAreas.Place(new WindowInteropHelper(window).Handle, ToolbarPlacement.Resolve(new(area.Id, (area.Width * 96 / area.Dpi - 320) / 2, (area.Height * 96 / area.Dpi - 340) / 2), areas, 320, 340));
        }
        window.Display(remaining);
    }
    public async Task CloseAsync()
    {
        var current = window; window = null; current?.Close();
        // Destroy the overlay, flush compositor work, then allow previews to refresh before recording starts.
        var result = await Task.Run(DwmFlush);
        if (result < 0) Marshal.ThrowExceptionForHR(result);
        await Task.Delay(150);
    }
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
