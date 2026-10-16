using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CaptureFlow.Core.Video;
using CaptureFlow.Desktop.Views;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Desktop.Services;
public sealed class WpfRegionSelection : IRegionSelection
{
    public async Task<ScreenRegion?> SelectAsync(MonitorDescriptor monitor, ScreenRegion? previous, CancellationToken cancellationToken)
    {
        var areas = DesktopWorkAreas.Enumerate(true); var area = areas.FirstOrDefault(a => a.Id == monitor.Id) ?? throw new InvalidOperationException("Selected monitor disconnected. Refresh sources.");
        if (area.Width != monitor.Width || area.Height != monitor.Height) throw new InvalidOperationException("Display resolution changed. Refresh monitors before selecting a region.");
        var window = new RegionSelectionWindow(monitor); var finished = new TaskCompletionSource<ScreenRegion?>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.SourceInitialized += (_, _) => WindowCaptureExclusion.TryExclude(new WindowInteropHelper(window).Handle);
        window.Closed += (_, _) => finished.TrySetResult(window.Result);
        using var cancelled = cancellationToken.Register(() => window.Dispatcher.BeginInvoke(window.Close));
        try
        {
            cancellationToken.ThrowIfCancellationRequested(); window.Show();
            DesktopWorkAreas.Place(new WindowInteropHelper(window).Handle, new(area.Left, area.Top, area.Width, area.Height)); window.Restore(previous);
            var region = await finished.Task; cancellationToken.ThrowIfCancellationRequested();
            if (region is not null)
            {
                var current = DesktopWorkAreas.Enumerate(true).FirstOrDefault(a => a.Id == monitor.Id);
                if (current is null || current.Width != monitor.Width || current.Height != monitor.Height || current.Left != area.Left || current.Top != area.Top || current.Dpi != area.Dpi)
                    throw new InvalidOperationException("The selected display disconnected or its resolution, position or DPI changed. Refresh monitors and select the region again.");
            }
            var result = await Task.Run(DwmFlush); if (result < 0) Marshal.ThrowExceptionForHR(result); await Task.Delay(150, cancellationToken);
            return region;
        }
        finally { window.Close(); }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
