using System.Globalization;
namespace CaptureFlow.Core.Editing;
public enum AnimationProperty { PositionX, PositionY, Scale, Rotation, Opacity, Volume }
public enum KeyframeInterpolation { Linear, Hold }
public sealed record EditorKeyframe(Guid Id, AnimationProperty Property, double Time, double Value, KeyframeInterpolation Interpolation = KeyframeInterpolation.Linear);
public enum TransitionType { FadeIn, FadeOut, FadeToBlack, CrossDissolve }
public enum TransitionPlacement { Start, End }
public sealed record EditorTransition(TransitionType Type, double Duration, TransitionPlacement Placement);
/// <summary>Clip-local seconds after speed conversion; edge values extend to clip boundaries.</summary>
public static class EditorAnimation
{
    public static double Default(AnimationProperty p) => p is AnimationProperty.Scale or AnimationProperty.Opacity or AnimationProperty.Volume ? 1 : 0;
    public static double Evaluate(EditorClip c, AnimationProperty p, double time)
    {
        var keys = (c.Keyframes ?? []).Where(k => k.Property == p).OrderBy(k => k.Time).ToArray();
        if (keys.Length == 0) return Default(p);
        if (time <= keys[0].Time) return keys[0].Value;
        for (var i = 1; i < keys.Length; i++) if (time < keys[i].Time)
        { var l = keys[i - 1]; var r = keys[i]; return l.Interpolation == KeyframeInterpolation.Hold ? l.Value : l.Value + (r.Value - l.Value) * (time - l.Time) / (r.Time - l.Time); }
        return keys[^1].Value;
    }
    public static string Expression(EditorClip c, AnimationProperty p, string time)
    {
        static string N(double v) => v.ToString("0.#########", CultureInfo.InvariantCulture);
        var keys = (c.Keyframes ?? []).Where(k => k.Property == p).OrderBy(k => k.Time).ToArray();
        if (keys.Length == 0) return N(Default(p));
        var result = N(keys[^1].Value);
        for (var i = keys.Length - 2; i >= 0; i--)
        { var l = keys[i]; var r = keys[i + 1]; var segment = l.Interpolation == KeyframeInterpolation.Hold ? N(l.Value) : $"({N(l.Value)}+({N(r.Value - l.Value)})*(({time})-{N(l.Time)})/{N(r.Time - l.Time)})"; result = $"if(lt(({time}),{N(r.Time)}),{segment},{result})"; }
        return $"if(lte(({time}),{N(keys[0].Time)}),{N(keys[0].Value)},{result})";
    }
    public static void Validate(EditorClip c, bool video)
    {
        var keys = c.Keyframes ?? [];
        if (keys.Length > 256 || keys.Any(k => k is null) || keys.Select(k => k.Id).Distinct().Count() != keys.Length) throw new ArgumentException("Invalid keyframe count or identities.");
        foreach (var k in keys)
        {
            var (min, max) = k.Property switch { AnimationProperty.PositionX or AnimationProperty.PositionY => (-1d, 1d), AnimationProperty.Scale => (.1, 3d), AnimationProperty.Rotation => (-360d, 360d), AnimationProperty.Opacity => (0d, 1d), _ => (0d, 2d) };
            if (k.Id == Guid.Empty || !Enum.IsDefined(k.Property) || !Enum.IsDefined(k.Interpolation) || !double.IsFinite(k.Time) || k.Time < 0 || k.Time > c.Duration + 1e-9 || !double.IsFinite(k.Value) || k.Value < min || k.Value > max || !video && k.Property != AnimationProperty.Volume) throw new ArgumentException("Invalid keyframe property, clip-local time or value.");
        }
        if (keys.GroupBy(k => (k.Property, k.Time)).Any(g => g.Count() > 1)) throw new ArgumentException("A property can have only one keyframe at each time.");
        var transitions = c.Transitions ?? [];
        if (transitions.Length > 2 || transitions.Any(t => t is null) || transitions.GroupBy(t => t.Placement).Any(g => g.Count() > 1)) throw new ArgumentException("Only one transition per clip edge is supported.");
        foreach (var t in transitions)
            if (!video || !Enum.IsDefined(t.Type) || !Enum.IsDefined(t.Placement) || !double.IsFinite(t.Duration) || t.Duration <= 0 || t.Duration > c.Duration || (t.Type is TransitionType.FadeIn or TransitionType.CrossDissolve) && t.Placement != TransitionPlacement.Start || (t.Type is TransitionType.FadeOut or TransitionType.FadeToBlack) && t.Placement != TransitionPlacement.End) throw new ArgumentException("Invalid transition type, placement or duration.");
        if (transitions.Sum(t => t.Duration) > c.Duration + 1e-9) throw new ArgumentException("Transitions exceed clip boundaries.");
    }
    public static EditorClip Retime(EditorClip original, EditorClip result)
    {
        var ratio = original.Speed / result.Speed;
        var sliced = Slice(original, result with { Speed = original.Speed }, (result.In - original.In) / original.Speed);
        return result with { Keyframes = sliced.Keyframes?.Select(k => k with { Time = k.Time * ratio }).ToArray(), Transitions = sliced.Transitions?.Select(t => t with { Duration = t.Duration * ratio }).ToArray() };
    }
    public static EditorClip Slice(EditorClip original, EditorClip result, double offset)
    {
        if (original.Keyframes is { Length: > 0 })
        {
            var keys = new List<EditorKeyframe>();
            foreach (var group in original.Keyframes.GroupBy(k => k.Property))
            {
                var sorted = group.OrderBy(k => k.Time).ToArray(); var mode = sorted.LastOrDefault(k => k.Time <= offset)?.Interpolation ?? sorted[0].Interpolation;
                keys.Add(new(Guid.NewGuid(), group.Key, 0, Evaluate(original, group.Key, offset), mode));
                keys.AddRange(sorted.Where(k => k.Time > offset && k.Time < offset + result.Duration).Select(k => k with { Time = k.Time - offset }));
                keys.Add(new(Guid.NewGuid(), group.Key, result.Duration, Evaluate(original, group.Key, offset + result.Duration)));
            }
            result = result with { Keyframes = keys.ToArray() };
        }
        if (result.Transitions is null) return result;
        var transitions = result.Transitions;
        var factor = Math.Min(1, result.Duration / Math.Max(result.Duration, transitions.Sum(t => t.Duration)));
        return result with { Transitions = transitions.Select(t => t with { Duration = t.Duration * factor }).ToArray() };
    }
}

