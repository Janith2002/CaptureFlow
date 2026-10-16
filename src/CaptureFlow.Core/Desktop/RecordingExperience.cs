namespace CaptureFlow.Core.Desktop;

public sealed record ToolbarAnchor(string? MonitorId = null, double? X = null, double? Y = null)
{
    public void Validate()
    { if ((X is null) != (Y is null) || X is { } x && (!double.IsFinite(x) || Math.Abs(x) > 1000000) || Y is { } y && (!double.IsFinite(y) || Math.Abs(y) > 1000000)) throw new ArgumentException("Invalid toolbar position."); }
}
public sealed record RecordingExperiencePreferences(int CountdownSeconds = 3, bool ShowToolbar = true, bool ToolbarTopmost = true, ToolbarAnchor? Anchor = null)
{
    public void Validate() { if (CountdownSeconds is not (0 or 3 or 5 or 10)) throw new ArgumentException("Choose Off, 3, 5 or 10 seconds."); Anchor?.Validate(); }
}
public interface IRecordingExperienceStore
{
    Task<RecordingExperiencePreferences> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(RecordingExperiencePreferences preferences, CancellationToken cancellationToken = default);
}
public sealed record CountdownRequest(int Seconds, string? MonitorId = null);
public interface ICountdownPresentation
{
    void Show(CountdownRequest request, int remaining, Action cancel);
    Task CloseAsync();
}
public interface IRecordingCountdown
{
    event EventHandler? Changed;
    bool IsRunning { get; }
    int Remaining { get; }
    Task RunAsync(CountdownRequest request, CancellationToken cancellationToken);
    void Cancel();
    Task CancelAndWaitAsync();
}
public sealed record DesktopWorkArea(string Id, double Left, double Top, double Width, double Height, double Dpi = 96, bool Primary = false);
public sealed record ToolbarRectangle(double Left, double Top, double Width, double Height);
public static class ToolbarPlacement
{
    public static ToolbarRectangle Resolve(ToolbarAnchor? anchor, IReadOnlyList<DesktopWorkArea> monitors, double widthDip, double heightDip)
    {
        anchor?.Validate();
        if (monitors.Count == 0 || !double.IsFinite(widthDip) || !double.IsFinite(heightDip) || widthDip <= 0 || heightDip <= 0) throw new ArgumentException("No valid desktop work area is available.");
        var monitor = monitors.FirstOrDefault(m => m.Id == anchor?.MonitorId) ?? monitors.FirstOrDefault(m => m.Primary) ?? monitors[0];
        if (monitor.Dpi is < 48 or > 768 || monitor.Width <= 0 || monitor.Height <= 0) throw new ArgumentException("Invalid monitor dimensions or DPI.");
        var scale = monitor.Dpi / 96;
        var width = Math.Min(monitor.Width, widthDip * scale); var height = Math.Min(monitor.Height, heightDip * scale);
        var x = anchor?.X is { } savedX ? savedX * scale : monitor.Width - width - 20 * scale;
        var y = anchor?.Y is { } savedY ? savedY * scale : 20 * scale;
        return new(monitor.Left + Math.Clamp(x, 0, monitor.Width - width), monitor.Top + Math.Clamp(y, 0, monitor.Height - height), width, height);
    }
    public static ToolbarAnchor Capture(ToolbarRectangle window, IReadOnlyList<DesktopWorkArea> monitors)
    {
        var monitor = monitors.OrderByDescending(m => Math.Max(0, Math.Min(window.Left + window.Width, m.Left + m.Width) - Math.Max(window.Left, m.Left)) *
            Math.Max(0, Math.Min(window.Top + window.Height, m.Top + m.Height) - Math.Max(window.Top, m.Top))).FirstOrDefault() ?? throw new ArgumentException("No monitors.");
        return new(monitor.Id, (window.Left - monitor.Left) * 96 / monitor.Dpi, (window.Top - monitor.Top) * 96 / monitor.Dpi);
    }
}
