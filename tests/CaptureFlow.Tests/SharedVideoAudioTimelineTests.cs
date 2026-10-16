using System.Buffers.Binary;
using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Audio;
namespace CaptureFlow.Tests;
public class SharedVideoAudioTimelineTests
{
    [Theory]
    [InlineData(30)] [InlineData(60)]
    public void SharedTimelineRemovesSamePauseFromVideoMicrophoneAndSystemAudio(int videoFps)
    {
        double clock = 0;
        var timeline = new RecordingTimeline(() => clock);
        timeline.Start(); clock = 10_000_000; timeline.Pause();
        clock = 110_000_000; timeline.Resume(); clock = 120_000_000; timeline.Stop();
        Assert.Equal(TimeSpan.FromSeconds(2), timeline.Duration);
        Assert.Equal(videoFps, (int)Math.Ceiling(timeline.DurationAt(60_000_000).TotalSeconds * videoFps));
        Assert.Equal(videoFps * 2, (int)Math.Ceiling(timeline.Duration.TotalSeconds * videoFps));
        const int rate = 48000;
        var mixer = new AudioTimelineMixer(rate, true, 0, timeline);
        var microphone = Enumerable.Repeat(.4f, rate * 2).ToArray();
        var system = Enumerable.Repeat(.2f, rate * 2).ToArray();
        mixer.Add(true, 0, microphone, 1, false); mixer.Add(false, 0, system, 1, false);
        mixer.Add(true, 50_000_000, Enumerable.Repeat(1f, rate * 2).ToArray(), 1, false); // packet entirely inside pause: excluded.
        mixer.Add(true, 110_000_000, microphone, 1, false); mixer.Add(false, 110_000_000, system, 1, false);
        Assert.Equal(rate, mixer.FramesAt(60_000_000)); Assert.Equal(rate * 2, mixer.FramesAt(clock));
        var pcm = mixer.Render(rate * 2);
        for (var i = 0; i < pcm.Length; i += 2) Assert.Equal((short)9830, BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i, 2)));
    }
}
