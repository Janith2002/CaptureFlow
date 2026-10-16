using System.Diagnostics;
namespace CaptureFlow.Core.Video;
public sealed record RecordingInterval(double StartQpc, double? EndQpc, TimeSpan Offset);
/// <summary>One monotonic clock shared by the video sampler and timestamped audio mixer.</summary>
public sealed class RecordingTimeline
{
    private readonly object sync = new();
    private readonly List<RecordingInterval> intervals = [];
    private bool stopped;
    private readonly Func<double> readClock;
    public RecordingTimeline(Func<double>? readClock = null) => this.readClock = readClock ?? (() => NowQpc);
    private double Now => readClock();
    public static double NowQpc => Stopwatch.GetTimestamp() * (10_000_000d / Stopwatch.Frequency);
    public IReadOnlyList<RecordingInterval> Intervals { get { lock (sync) return intervals.ToArray(); } }
    public TimeSpan Duration => DurationAt(Now);
    public bool IsPaused { get { lock (sync) return intervals.Count == 0 || intervals[^1].EndQpc is not null; } }
    public void Start() { lock (sync) { if (intervals.Count != 0) throw new InvalidOperationException("Timeline already started."); intervals.Add(new(Now, null, TimeSpan.Zero)); } }
    public void Pause() { lock (sync) { if (intervals.Count > 0 && intervals[^1].EndQpc is null) intervals[^1] = intervals[^1] with { EndQpc = Now }; } }
    public void Resume() { lock (sync) { if (!stopped && intervals.Count > 0 && intervals[^1].EndQpc is not null) intervals.Add(new(Now, null, DurationAt(Now))); } }
    public void Stop() { lock (sync) { Pause(); stopped = true; } }
    public TimeSpan DurationAt(double qpc)
    {
        lock (sync)
        {
            foreach (var interval in intervals.AsEnumerable().Reverse())
                if (qpc >= interval.StartQpc) return interval.Offset + TimeSpan.FromTicks((long)Math.Max(0, Math.Min(qpc, interval.EndQpc ?? qpc) - interval.StartQpc));
            return TimeSpan.Zero;
        }
    }
}
