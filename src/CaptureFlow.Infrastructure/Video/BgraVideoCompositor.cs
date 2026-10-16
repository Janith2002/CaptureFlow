using System.Buffers;
using CaptureFlow.Core.Video;
namespace CaptureFlow.Infrastructure.Video;

/// <summary>Pure BGRA composition shared by recording and preview; never captures WPF controls.</summary>
public sealed class BgraVideoCompositor : IVideoCompositor
{
    public ValueTask<VideoFrame> ComposeAsync(VideoFrame primary, IReadOnlyList<VideoOverlay> overlays, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (overlays.Count == 0) return ValueTask.FromResult(primary.Lease());
        var length = checked(primary.Size.Width * primary.Size.Height * 4);
        var owner = MemoryPool<byte>.Shared.Rent(length);
        try
        {
            var output = owner.Memory.Span[..length]; primary.Pixels.Span.CopyTo(output);
            foreach (var overlay in overlays) Draw(output, primary.Size, overlay, cancellationToken);
            return ValueTask.FromResult(new VideoFrame(owner, primary.Size, primary.Timestamp));
        }
        catch { owner.Dispose(); throw; }
    }
    private static void Draw(Span<byte> output, VideoSize screen, VideoOverlay overlay, CancellationToken token)
    {
        if (!double.IsFinite(overlay.X) || !double.IsFinite(overlay.Y) || !double.IsFinite(overlay.Width) || !double.IsFinite(overlay.Height) ||
            overlay.X < 0 || overlay.Y < 0 || overlay.Width < 1 || overlay.Height < 1 || overlay.X + overlay.Width > screen.Width ||
            overlay.Y + overlay.Height > screen.Height || !Enum.IsDefined(overlay.Shape) || !double.IsFinite(overlay.BorderThickness) || overlay.BorderThickness < 0)
            throw new ArgumentException("Overlay bounds or shape are invalid.");
        var left = (int)Math.Round(overlay.X); var top = (int)Math.Round(overlay.Y);
        var width = Math.Min((int)Math.Round(overlay.Width), screen.Width - left);
        var height = Math.Min((int)Math.Round(overlay.Height), screen.Height - top);
        var camera = overlay.Frame.Size; var pixels = overlay.Frame.Pixels.Span;
        var scale = Math.Max(width / (double)camera.Width, height / (double)camera.Height);
        var cropX = (camera.Width - width / scale) / 2; var cropY = (camera.Height - height / scale) / 2;
        var border = Math.Min(overlay.BorderThickness, Math.Min(width, height) / 2d);
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            var sy = Math.Clamp(cropY + (y + .5) / scale - .5, 0, camera.Height - 1);
            var y0 = (int)sy; var y1 = Math.Min(y0 + 1, camera.Height - 1); var fy = sy - y0;
            for (var x = 0; x < width; x++)
            {
                var outer = Coverage(x + .5, y + .5, width, height, overlay.Shape, 0);
                if (outer <= 0) continue;
                var inner = border == 0 ? outer : Coverage(x + .5, y + .5, width, height, overlay.Shape, border);
                inner = Math.Min(inner, outer);
                var sx = Math.Clamp(cropX + (x + .5) / scale - .5, 0, camera.Width - 1);
                var x0 = (int)sx; var x1 = Math.Min(x0 + 1, camera.Width - 1); var fx = sx - x0;
                var a = (y0 * camera.Width + x0) * 4; var b = (y0 * camera.Width + x1) * 4;
                var c = (y1 * camera.Width + x0) * 4; var d = (y1 * camera.Width + x1) * 4;
                var target = ((top + y) * screen.Width + left + x) * 4;
                for (var channel = 0; channel < 3; channel++)
                {
                    var value = (pixels[a + channel] * (1 - fx) + pixels[b + channel] * fx) * (1 - fy) +
                        (pixels[c + channel] * (1 - fx) + pixels[d + channel] * fx) * fy;
                    var borderColor = channel switch { 0 => 255, 1 => 117, _ => 119 };
                    output[target + channel] = (byte)Math.Clamp((int)Math.Round(value * inner + borderColor * (outer - inner) + output[target + channel] * (1 - outer)), 0, 255);
                }
                output[target + 3] = 255;
            }
        }
    }
    private static double Coverage(double x, double y, double width, double height, OverlayShape shape, double inset)
    {
        if (width <= inset * 2 || height <= inset * 2) return 0;
        var halfWidth = width / 2 - inset; var halfHeight = height / 2 - inset;
        var dx = Math.Abs(x - width / 2); var dy = Math.Abs(y - height / 2);
        if (shape == OverlayShape.Circle)
        {
            var radius = Math.Min(halfWidth, halfHeight);
            return Math.Clamp(radius - Math.Sqrt(dx * dx + dy * dy) + .5, 0, 1);
        }
        var radiusRect = shape == OverlayShape.RoundedRectangle ? Math.Max(0, Math.Min(width, height) * .12 - inset) : 0;
        var qx = dx - halfWidth + radiusRect; var qy = dy - halfHeight + radiusRect;
        var distance = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) + Math.Min(Math.Max(qx, qy), 0) - radiusRect;
        return Math.Clamp(.5 - distance, 0, 1);
    }
}
