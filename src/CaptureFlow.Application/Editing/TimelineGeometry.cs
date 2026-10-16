using CaptureFlow.Core.Editing;
namespace CaptureFlow.Application.Editing;

public enum TimelineGesture { Move, TrimLeft, TrimRight }
public static class TimelineGeometry
{
    public static double Quantize(double seconds, double fps = 30)
    {
        if (!double.IsFinite(seconds) || !double.IsFinite(fps) || fps <= 0) throw new ArgumentException("Invalid timeline time or frame rate.");
        return Math.Round(seconds * fps, MidpointRounding.AwayFromZero) / fps;
    }
    public static double Snap(double time, IEnumerable<double> boundaries, double tolerance)
    {
        if (!double.IsFinite(time) || !double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentException("Invalid snap coordinates.");
        var nearest = boundaries.Append(0).Where(v => double.IsFinite(v) && v >= 0).OrderBy(v => Math.Abs(v - time)).ThenBy(v => v).First();
        return Math.Abs(nearest - time) <= tolerance ? nearest : Math.Max(0, time);
    }
    public static EditorClip Propose(EditorProject project, EditorClip anchor, TimelineGesture gesture, double deltaSeconds, int track,
        double playhead, double pixelsPerSecond, bool snapping, double fps = 30)
    {
        if (!double.IsFinite(pixelsPerSecond) || pixelsPerSecond <= 0) throw new ArgumentException("Invalid zoom.");
        var media = project.Media.Single(m => m.Id == anchor.MediaId);
        var boundaries = project.Clips.Where(c => c.Id != anchor.Id).SelectMany(c => new[] { c.Start, c.End }).Append(playhead).Append(0).ToArray();
        double Position(double requested, bool bothEdges = false)
        {
            var quantized = Quantize(Math.Max(0, requested), fps);
            return snapping ? Snap(quantized, bothEdges ? boundaries.Concat(boundaries.Select(b => b - anchor.Duration)) : boundaries, 8 / pixelsPerSecond) : quantized;
        }
        EditorClip result;
        if (gesture == TimelineGesture.Move) result = anchor with { Start = Position(anchor.Start + deltaSeconds, true), TrackId = track };
        else if (gesture == TimelineGesture.TrimLeft)
        {
            var start = Position(anchor.Start + deltaSeconds);
            var source = anchor.In + (start - anchor.Start) * anchor.Speed;
            result = anchor with { Start = start, In = source };
        }
        else
        {
            var end = Position(anchor.End + deltaSeconds);
            result = anchor with { Out = anchor.In + (end - anchor.Start) * anchor.Speed };
        }
        if (result.In < 0 || result.Out > media.Duration || result.Out - result.In < .02) throw new ArgumentException("Trim exceeds the available source range.");
        result = result with { FadeIn = Math.Min(result.FadeIn, result.Duration), FadeOut = Math.Min(result.FadeOut, Math.Max(0, result.Duration - Math.Min(result.FadeIn, result.Duration))) };
        if (gesture != TimelineGesture.Move) result = EditorAnimation.Retime(anchor, result);
        var proposed = project with { Clips = project.Clips.Select(c => c.Id == anchor.Id ? result : c).ToArray() };
        proposed.Validate(); return result;
    }
}

