using System.Buffers.Binary;
namespace CaptureFlow.Infrastructure.Audio;
/// <summary>A timestamped stereo timeline. Missing playback packets are genuine silence; paused spans are removed.</summary>
public sealed class AudioTimelineMixer
{
    public const double QpcUnitsPerSecond = 10_000_000;
    private sealed record Segment(double Start, double? End, long BaseFrame);
    private readonly int rate;
    private readonly bool combined;
    private readonly List<Segment> segments = [];
    private readonly SourceRing microphone;
    private readonly SourceRing system;
    private bool ended;
    private readonly CaptureFlow.Core.Video.RecordingTimeline? sharedTimeline;
    public long WrittenFrames { get; private set; }
    public AudioTimelineMixer(int sampleRate, bool combined, double startQpc, CaptureFlow.Core.Video.RecordingTimeline? sharedTimeline = null)
    {
        this.sharedTimeline = sharedTimeline;
        rate = sampleRate; this.combined = combined;
        if (rate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        microphone = new(rate * 4); system = new(rate * 4);
        segments.Add(new(startQpc, null, 0));
    }
    public long FramesAt(double qpc)
    {
        if (sharedTimeline is not null) return (long)Math.Round(sharedTimeline.DurationAt(qpc).TotalSeconds * rate);
        foreach (var segment in segments.AsEnumerable().Reverse())
            if (qpc >= segment.Start) return segment.BaseFrame + Frames(Math.Min(qpc, segment.End ?? qpc) - segment.Start);
        return 0;
    }
    private long Frames(double qpcDuration) => Math.Max(0, (long)Math.Round(qpcDuration * rate / QpcUnitsPerSecond));
    public void Pause(double qpc)
    {
        if (sharedTimeline is not null || ended || segments[^1].End is not null) return;
        segments[^1] = segments[^1] with { End = qpc };
    }
    public void Resume(double qpc)
    {
        if (sharedTimeline is not null || ended || segments[^1].End is null) return;
        segments.Add(new(qpc, null, FramesAt(qpc)));
    }
    public void Stop(double qpc) { Pause(qpc); ended = true; }
    public void Add(bool isMicrophone, double startQpc, ReadOnlySpan<float> stereo, float gain, bool muted)
    {
        if (stereo.Length % 2 != 0) throw new ArgumentException("Stereo data must contain whole frames.");
        if (!float.IsFinite(gain)) throw new ArgumentOutOfRangeException(nameof(gain));
        var source = isMicrophone ? microphone : system;
        var packetFrames = stereo.Length / 2;
        var packetEnd = startQpc + packetFrames * QpcUnitsPerSecond / rate;
        var recordingSegments = sharedTimeline is null ? segments : sharedTimeline.Intervals.Select(i => new Segment(i.StartQpc, i.EndQpc, (long)Math.Round(i.Offset.TotalSeconds * rate))).ToList();
        foreach (var segment in recordingSegments)
        {
            var begin = Math.Max(startQpc, segment.Start);
            var end = Math.Min(packetEnd, segment.End ?? packetEnd);
            if (end <= begin) continue;
            var first = Math.Clamp((int)Math.Round((begin - startQpc) * rate / QpcUnitsPerSecond), 0, packetFrames);
            var count = Math.Clamp((int)Math.Round((end - begin) * rate / QpcUnitsPerSecond), 0, packetFrames - first);
            var frame = segment.BaseFrame + Frames(begin - segment.Start);
            var skip = (int)Math.Min(count, Math.Max(0, WrittenFrames - frame));
            if (skip > rate / 10) throw new IOException("Audio packets arrived too late to preserve synchronization. Check audio drivers and storage performance.");
            first += skip; count -= skip; frame += skip;
            source.Add(frame, stereo.Slice(first * 2, count * 2), muted ? 0 : Math.Clamp(gain, 0, 1), WrittenFrames);
        }
    }
    public byte[] Render(int frames)
    {
        if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
        var pcm = new byte[frames * 4];
        var mixed = new float[frames * 2];
        var headroom = combined ? .5f : 1f;
        var peak = 1f;
        for (var i = 0; i < frames; i++)
        for (var channel = 0; channel < 2; channel++)
        {
            var frame = WrittenFrames + i;
            var sample = (microphone.Read(frame, channel) + system.Read(frame, channel)) * headroom;
            sample = float.IsFinite(sample) ? sample : 0;
            mixed[i * 2 + channel] = sample;
            peak = Math.Max(peak, Math.Abs(sample));
        }
        // Fixed headroom handles normal full-scale sources. Block peak limiting also handles resampler overshoot without hard clipping.
        for (var i = 0; i < mixed.Length; i++)
        {
            var value = (short)Math.Round(Math.Clamp(mixed[i] / peak, -1, 1) * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), value);
        }        WrittenFrames += frames;
        return pcm;
    }
    private sealed class SourceRing(int capacity)
    {
        private readonly long[] tags = Enumerable.Repeat(-1L, capacity).ToArray();
        private readonly float[] samples = new float[capacity * 2];
        public void Add(long frame, ReadOnlySpan<float> stereo, float gain, long written)
        {
            if (frame + stereo.Length / 2 - written > capacity) throw new IOException("Audio capture exceeded the timeline buffer. Check the device driver and storage performance.");
            for (var i = 0; i < stereo.Length / 2; i++)
            {
                var index = (int)((frame + i) % capacity);
                tags[index] = frame + i;
                samples[index * 2] = stereo[i * 2] * gain; samples[index * 2 + 1] = stereo[i * 2 + 1] * gain;
            }
        }
        public float Read(long frame, int channel)
        {
            var index = (int)(frame % capacity);
            return tags[index] == frame ? samples[index * 2 + channel] : 0;
        }
    }
}



