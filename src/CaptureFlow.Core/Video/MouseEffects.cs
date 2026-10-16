namespace CaptureFlow.Core.Video;

public sealed record MouseEffectsPreferences(bool ShowCursor = true, bool Highlight = false, string HighlightColor = "#60A5FA",
    double Radius = 24, double Opacity = .3, bool ClickEffects = false, bool LeftClicks = true, bool RightClicks = true,
    string LeftColor = "#60A5FA", string RightColor = "#C084FC", double ClickDurationMilliseconds = 500)
{
    public void Validate()
    {
        _ = MouseEffectColor.Parse(HighlightColor); _ = MouseEffectColor.Parse(LeftColor); _ = MouseEffectColor.Parse(RightColor);
        if (!double.IsFinite(Radius) || Radius is < 8 or > 100) throw new ArgumentOutOfRangeException(nameof(Radius));
        if (!double.IsFinite(Opacity) || Opacity is < .05 or > 1) throw new ArgumentOutOfRangeException(nameof(Opacity));
        if (!double.IsFinite(ClickDurationMilliseconds) || ClickDurationMilliseconds is < 100 or > 2000) throw new ArgumentOutOfRangeException(nameof(ClickDurationMilliseconds));
    }
}
public readonly record struct MouseEffectColor(byte R, byte G, byte B)
{
    public static MouseEffectColor Parse(string value)
    {
        if (value is null || value.Length != 7 || value[0] != '#' || !uint.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var rgb))
            throw new ArgumentException("Mouse effect colors must use #RRGGBB.");
        return new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
public readonly record struct MousePoint(double X, double Y);
public enum MouseClickButton { Left, Right }
public readonly record struct MouseClick(MousePoint Position, MouseClickButton Button, TimeSpan At);
public static class MouseCoordinates
{
    public static MousePoint ToFrame(MousePoint desktop, double sourceLeft, double sourceTop, ScreenRegion? region = null) =>
        new(desktop.X - sourceLeft - (region?.X ?? 0), desktop.Y - sourceTop - (region?.Y ?? 0));
    public static MousePoint DipToPhysical(MousePoint dip, double dpi)
    {
        if (!double.IsFinite(dpi) || dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));
        return new(dip.X * dpi / 96, dip.Y * dpi / 96);
    }
    public static bool Contains(MousePoint point, VideoSize size) => point.X >= 0 && point.Y >= 0 && point.X < size.Width && point.Y < size.Height;
}
/// <summary>Only transient down events; bounded and cleared at every recording/pause boundary.</summary>
public sealed class MouseClickBuffer
{
    public const int Capacity = 32;
    private readonly object sync = new();
    private readonly List<MouseClick> clicks = [];
    private bool active;
    public void SetActive(bool value) { lock (sync) { active = value; clicks.Clear(); } }
    public void Add(MouseClick click) { lock (sync) { if (!active) return; if (clicks.Count == Capacity) clicks.RemoveAt(0); clicks.Add(click); } }
    public MouseClick[] Snapshot(TimeSpan now, TimeSpan duration)
    {
        lock (sync) { clicks.RemoveAll(c => now - c.At >= duration || now < c.At); return clicks.ToArray(); }
    }
}
public interface IMouseEffectsPreferencesStore
{
    Task<MouseEffectsPreferences> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(MouseEffectsPreferences preferences, CancellationToken cancellationToken = default);
}
