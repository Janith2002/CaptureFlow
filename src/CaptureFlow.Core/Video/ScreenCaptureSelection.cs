namespace CaptureFlow.Core.Video;
public sealed record ApplicationWindowDescriptor(string Id, long Handle, int ProcessId, long ProcessStartedUtcTicks,
    string Title, string ApplicationName, int Width, int Height)
{
    public string DisplayName => $"{Title} · {ApplicationName}";
    public void Validate() { if (string.IsNullOrWhiteSpace(Id) || Handle <= 0 || ProcessId <= 0 || ProcessStartedUtcTicks <= 0 || Width < 2 || Height < 2) throw new ArgumentException("Invalid application window identity."); }
}
public interface IApplicationWindowDiscovery
{
    Task<IReadOnlyList<ApplicationWindowDescriptor>> GetWindowsAsync(CancellationToken cancellationToken = default);
}
public sealed record ScreenRegion(string MonitorId, int X, int Y, int Width, int Height, int MonitorWidth, int MonitorHeight)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(MonitorId) || X < 0 || Y < 0 || Width < 16 || Height < 16 || Width % 2 != 0 || Height % 2 != 0 ||
            MonitorWidth < 16 || MonitorHeight < 16 || (long)X + Width > MonitorWidth || (long)Y + Height > MonitorHeight)
            throw new ArgumentException("Select an even-sized region of at least 16 × 16 pixels within one monitor.");
    }
    public bool Matches(MonitorDescriptor monitor) => MonitorId == monitor.Id && MonitorWidth == monitor.Width && MonitorHeight == monitor.Height;
}
public static class RegionSelectionGeometry
{
    public static ScreenRegion FromDrag(string id, int monitorWidth, int monitorHeight, double x1Dip, double y1Dip, double x2Dip, double y2Dip, double dpi)
    {
        if (monitorWidth < 16 || monitorHeight < 16 || !double.IsFinite(dpi) || dpi is < 48 or > 768 || new[] { x1Dip, y1Dip, x2Dip, y2Dip }.Any(v => !double.IsFinite(v))) throw new ArgumentException("Invalid monitor dimensions, region coordinates or DPI.");
        var scale = dpi / 96;
        // Cancel insignificant floating-point error when restoring integer pixels through fractional DPI scales.
        const double epsilon = 1e-7;
        var x = (int)Math.Clamp(Math.Floor(Math.Min(x1Dip, x2Dip) * scale + epsilon), 0, monitorWidth - 1);
        var y = (int)Math.Clamp(Math.Floor(Math.Min(y1Dip, y2Dip) * scale + epsilon), 0, monitorHeight - 1);
        var right = (int)Math.Clamp(Math.Ceiling(Math.Max(x1Dip, x2Dip) * scale - epsilon), x, monitorWidth);
        var bottom = (int)Math.Clamp(Math.Ceiling(Math.Max(y1Dip, y2Dip) * scale - epsilon), y, monitorHeight);
        return new(id, x, y, (right - x) / 2 * 2, (bottom - y) / 2 * 2, monitorWidth, monitorHeight);
    }
    public static ScreenRegion Move(ScreenRegion region, int x, int y) => region with { X = Math.Clamp(x, 0, region.MonitorWidth - region.Width), Y = Math.Clamp(y, 0, region.MonitorHeight - region.Height) };
    public static ScreenRegion FromDesktopPixels(string id, double monitorLeft, double monitorTop, int width, int height,
        double x1, double y1, double x2, double y2) => FromDrag(id, width, height, x1 - monitorLeft, y1 - monitorTop, x2 - monitorLeft, y2 - monitorTop, 96);
}
public interface IRegionSelection
{
    Task<ScreenRegion?> SelectAsync(MonitorDescriptor monitor, ScreenRegion? previous, CancellationToken cancellationToken);
}
public interface IRegionPreferencesStore
{
    Task<ScreenRegion?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ScreenRegion region, CancellationToken cancellationToken = default);
}
public sealed record WindowEligibility(bool Visible, bool Minimized, bool Cloaked, bool ToolWindow, bool OwnProcess, bool CaptureProtected, bool Accessible, string Title)
{
    public bool Eligible => Visible && !Minimized && !Cloaked && !ToolWindow && !OwnProcess && !CaptureProtected && Accessible && !string.IsNullOrWhiteSpace(Title);
}
public static class ScreenCaptureLifecycle
{
    public static void EnsureWindowAvailable(bool exists, bool sameProcess, bool visible, bool minimized)
    {
        if (!exists || !sameProcess) throw new IOException("The selected application window closed. Intermediate files are retained.");
        if (minimized || !visible) throw new IOException("The selected application window was minimized or hidden. Restore it before starting a new recording.");
    }
    public static void EnsureDimensionsUnchanged(VideoSize expected, VideoSize actual, bool window)
    {
        if (expected != actual) throw new IOException(window ? "The application window resized. Recording stops safely; reselect the window to record its new dimensions." : "The monitor resolution changed during recording. Reselect the source before restarting.");
    }
}
