using CaptureFlow.Core.Video;
namespace CaptureFlow.Infrastructure.Video;

/// <summary>Blends antialiased effects into the existing packed BGRA buffer; no full-frame copy or desktop overlay.</summary>
public static class MouseEffectsRenderer
{
    public static void Render(Span<byte> pixels, VideoSize size, MouseEffectsPreferences settings, MousePoint? cursor,
        IReadOnlyList<MouseClick> clicks, TimeSpan now)
    {
        if (pixels.Length < checked(size.Width * size.Height * 4)) throw new ArgumentException("Invalid BGRA buffer.");
        if (settings.Highlight && cursor is { } position && MouseCoordinates.Contains(position, size))
            Circle(pixels, size, position, settings.Radius, settings.Opacity, MouseEffectColor.Parse(settings.HighlightColor), false);
        if (!settings.ClickEffects) return;
        foreach (var click in clicks.Take(MouseClickBuffer.Capacity))
        {
            if (!MouseCoordinates.Contains(click.Position, size) || (click.Button == MouseClickButton.Left ? !settings.LeftClicks : !settings.RightClicks)) continue;
            var progress = (now - click.At).TotalMilliseconds / settings.ClickDurationMilliseconds;
            if (progress < 0 || progress >= 1) continue;
            Circle(pixels, size, click.Position, 5 + settings.Radius * 1.5 * progress, .85 * (1 - progress),
                MouseEffectColor.Parse(click.Button == MouseClickButton.Left ? settings.LeftColor : settings.RightColor), true);
        }
    }
    private static void Circle(Span<byte> pixels, VideoSize size, MousePoint center, double radius, double opacity, MouseEffectColor color, bool ring)
    {
        var outside = radius + (ring ? 2 : .5); var inside = Math.Max(0, radius - 2);
        var left = Math.Max(0, (int)Math.Floor(center.X - outside)); var right = Math.Min(size.Width - 1, (int)Math.Ceiling(center.X + outside));
        var top = Math.Max(0, (int)Math.Floor(center.Y - outside)); var bottom = Math.Min(size.Height - 1, (int)Math.Ceiling(center.Y + outside));
        for (var y = top; y <= bottom; y++) for (var x = left; x <= right; x++)
        {
            var dx = x + .5 - center.X; var dy = y + .5 - center.Y; var squared = dx * dx + dy * dy;
            if (squared >= outside * outside || (ring && squared <= inside * inside)) continue;
            var distance = Math.Sqrt(squared);
            var alpha = opacity * Math.Clamp(ring ? 2 - Math.Abs(distance - radius) : radius + .5 - distance, 0, 1);
            if (alpha <= 0) continue;
            var index = (y * size.Width + x) * 4;
            pixels[index] = (byte)Math.Round(pixels[index] * (1 - alpha) + color.B * alpha);
            pixels[index + 1] = (byte)Math.Round(pixels[index + 1] * (1 - alpha) + color.G * alpha);
            pixels[index + 2] = (byte)Math.Round(pixels[index + 2] * (1 - alpha) + color.R * alpha);
        }
    }
}
