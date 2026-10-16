using System.Buffers.Binary;
using CaptureFlow.Infrastructure.Audio;
namespace CaptureFlow.Tests;
public class AudioTimelineMixerTests
{
    private const double Second = AudioTimelineMixer.QpcUnitsPerSecond;
    [Fact]
    public void IdlePlaybackProducesSilenceWithoutLosingRecordingTime()
    {
        var mixer = new AudioTimelineMixer(100, false, 0);
        mixer.Add(false, Second * .5, Stereo(25, .5f), 1, false);
        mixer.Stop(Second);
        var samples = Decode(mixer.Render((int)mixer.FramesAt(Second)));
        Assert.Equal(200, samples.Length);
        Assert.All(samples.Take(100), sample => Assert.Equal(0, sample));
        Assert.All(samples.Skip(100).Take(50), sample => Assert.InRange(sample, .499, .501));
        Assert.All(samples.Skip(150), sample => Assert.Equal(0, sample));
    }
    [Fact]
    public void CombinedSourcesAreTimestampAlignedAndHaveHeadroom()
    {
        var mixer = new AudioTimelineMixer(100, true, 0);
        mixer.Add(true, 0, Stereo(100, 1), 1, false);
        mixer.Add(false, Second / 2, Stereo(50, 1), 1, false);
        var samples = Decode(mixer.Render(100));
        Assert.All(samples.Take(100), sample => Assert.InRange(sample, .499, .501));
        Assert.All(samples.Skip(100), sample => Assert.InRange(sample, .999, 1));
    }
    [Fact]
    public void IndependentGainsAndMuteAffectActualPcmSamples()
    {
        var mixer = new AudioTimelineMixer(100, true, 0);
        mixer.Add(true, 0, Stereo(10, 1), .8f, false);
        mixer.Add(false, 0, Stereo(10, 1), .2f, true);
        Assert.All(Decode(mixer.Render(10)), sample => Assert.InRange(sample, .399, .401));
        mixer.Add(true, Second / 10, Stereo(10, 1), .8f, true);
        mixer.Add(false, Second / 10, Stereo(10, 1), .2f, false);
        Assert.All(Decode(mixer.Render(10)), sample => Assert.InRange(sample, .099, .101));
    }
    [Fact]
    public void PausedAudioIsOmittedWithoutAnOutputTimingGap()
    {
        var mixer = new AudioTimelineMixer(100, false, 0);
        mixer.Pause(Second); mixer.Resume(Second * 3); mixer.Stop(Second * 4);
        var packet = Stereo(100, .25f).Concat(Stereo(200, .9f)).Concat(Stereo(100, .5f)).ToArray();
        mixer.Add(false, 0, packet, 1, false);
        Assert.Equal(100, mixer.FramesAt(Second * 2));
        Assert.Equal(200, mixer.FramesAt(Second * 4));
        var output = Decode(mixer.Render(200));
        Assert.All(output.Take(200), sample => Assert.InRange(sample, .249, .251));
        Assert.All(output.Skip(200), sample => Assert.InRange(sample, .499, .501));
    }
    [Fact]
    public void DelayedPacketsBeforePauseAreKeptButPostStopAudioIsExcluded()
    {
        var mixer = new AudioTimelineMixer(100, false, 0);
        mixer.Pause(Second); mixer.Stop(Second * 2);
        mixer.Add(false, Second * .9, Stereo(30, .5f), 1, false);
        Assert.Equal(100, mixer.FramesAt(Second * 3));
        var output = Decode(mixer.Render(100));
        Assert.All(output.Take(180), sample => Assert.Equal(0, sample));
        Assert.All(output.Skip(180), sample => Assert.InRange(sample, .499, .501));
    }
    [Fact]
    public void ResumingTwiceDoesNotAddExtraSegmentsOrTime()
    {
        var mixer = new AudioTimelineMixer(100, false, 0);
        mixer.Pause(Second); mixer.Pause(Second * 2); mixer.Resume(Second * 3); mixer.Resume(Second * 4);
        Assert.Equal(200, mixer.FramesAt(Second * 4));
    }
    [Fact]
    public void RingWrapDoesNotRepeatOldAudioIntoNewSilence()
    {
        var mixer = new AudioTimelineMixer(100, false, 0);
        mixer.Add(false, 0, Stereo(10, .8f), 1, false);
        mixer.Render(400);
        Assert.All(Decode(mixer.Render(10)), sample => Assert.Equal(0, sample));
    }
    [Fact]
    public void ExcessiveBufferedAudioRaisesAnActionableError()
    {
        var mixer = new AudioTimelineMixer(100, false, 0);
        Assert.Throws<IOException>(() => mixer.Add(false, 0, Stereo(501, .5f), 1, false));
    }
    [Fact]
    public void PeakLimiterPreservesSignalRatiosInsteadOfFlatteningOverloads()
    {
        var mixer = new AudioTimelineMixer(100, true, 0);
        float[] samples = [1.5f, 1.5f, .5f, .5f];
        mixer.Add(true, 0, samples, 1, false); mixer.Add(false, 0, samples, 1, false);
        var output = Decode(mixer.Render(2));
        Assert.InRange(output[0], .999, 1);
        Assert.InRange(output[2], .332, .334);
    }
    [Fact]
    public void SeverelyLateAudioIsReportedInsteadOfSilentlyDiscarded()
    {
        var mixer = new AudioTimelineMixer(100, false, 0);
        mixer.Render(100);
        Assert.Throws<IOException>(() => mixer.Add(false, 0, Stereo(100, .5f), 1, false));
    }    private static float[] Stereo(int frames, float sample) => Enumerable.Repeat(sample, frames * 2).ToArray();
    private static double[] Decode(byte[] bytes) => Enumerable.Range(0, bytes.Length / 2)
        .Select(i => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2, 2)) / (double)short.MaxValue).ToArray();
}

