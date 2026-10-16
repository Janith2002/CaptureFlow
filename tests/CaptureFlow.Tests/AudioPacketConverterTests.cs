using System.Buffers.Binary;
using CaptureFlow.Infrastructure.Audio;
using NAudio.Wave;
namespace CaptureFlow.Tests;
public class AudioPacketConverterTests
{
    [Theory]
    [InlineData(8)] [InlineData(16)] [InlineData(24)] [InlineData(32)]
    public void IntegerPcmIsDecodedAndMonoIsDuplicatedToStereo(int bits)
    {
        var converter = new AudioPacketConverter(new WaveFormat(48000, bits, 1), 48000);
        byte[] bytes = bits switch { 8 => [192], 16 => [0, 64], 24 => [0, 0, 64], _ => [0, 0, 0, 64] };
        Assert.Equal(new[] { .5f, .5f }, converter.Convert(bytes));
    }
    [Fact]
    public void Negative24BitPcmIsSignExtended()
    {
        var converter = new AudioPacketConverter(new WaveFormat(48000, 24, 1), 48000);
        Assert.Equal(new[] { -.5f, -.5f }, converter.Convert([0, 0, 192]));
    }
    [Fact]
    public void StereoFloatPreservesLeftAndRight()
    {
        var converter = new AudioPacketConverter(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2), 48000);
        var values = new[] { .25f, -.75f, .8f, -.2f };
        Assert.Equal(values, converter.Convert(FloatBytes(values)));
    }
    [Fact]
    public void ExtensibleFloatAndSurroundChannelsDownmixWithoutClipping()
    {
        var format = new WaveFormatExtensible(48000, 32, 6, true, 32, 63);
        var converter = new AudioPacketConverter(format, 48000);
        var samples = converter.Convert(FloatBytes([1, 1, 1, 1, 1, 1]));
        Assert.All(samples, value => Assert.InRange(value, .999f, 1.001f));
    }
    [Fact]
    public void SilenceFlagDoesNotLeakStaleBufferContents()
    {
        var converter = new AudioPacketConverter(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2), 48000);
        Assert.Equal(new float[4], converter.Convert(FloatBytes([1, -1, .9f, -.9f]), silent: true));
    }
    [Fact]
    public void NonFiniteDeviceSamplesDoNotPoisonTheMix()
    {
        var converter = new AudioPacketConverter(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2), 48000);
        Assert.Equal(new float[2], converter.Convert(FloatBytes([float.NaN, float.PositiveInfinity])));
    }
    [Theory]
    [InlineData(44100, 48000)] [InlineData(48000, 44100)] [InlineData(48000, 16000)]
    public void ResamplingPreservesDurationPitchAndChannelSeparation(int sourceRate, int targetRate)
    {
        var converter = new AudioPacketConverter(WaveFormat.CreateIeeeFloatWaveFormat(sourceRate, 2), targetRate);
        List<float> samples = [];
        for (var offset = 0; offset < sourceRate; offset += sourceRate / 100)
        {
            var frames = Math.Min(sourceRate / 100, sourceRate - offset);
            var packet = new float[frames * 2];
            for (var i = 0; i < frames; i++) packet[i * 2] = .5f * MathF.Sin(2 * MathF.PI * 440 * (offset + i) / sourceRate);
            samples.AddRange(converter.Convert(FloatBytes(packet)));
        }
        Assert.InRange(samples.Count / 2, targetRate - 64, targetRate + 64);
        Assert.All(samples.Where((_, i) => i % 2 == 1), sample => Assert.Equal(0f, sample));
        var left = samples.Where((_, i) => i % 2 == 0).Skip(targetRate / 10).ToArray();
        var crossings = left.Zip(left.Skip(1)).Count(pair => pair.First <= 0 && pair.Second > 0);
        var measuredHz = crossings / (left.Length / (double)targetRate);
        Assert.InRange(measuredHz, 438, 442);
        var rms = Math.Sqrt(left.Select(v => v * v).Average());
        Assert.InRange(rms, .30, .38);
    }
    [Fact]
    public void ContinuousResamplingDoesNotRestartAtEveryPacket()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        var input = Enumerable.Range(0, 4410).Select(i => .5f * MathF.Sin(2 * MathF.PI * 440 * i / 44100)).ToArray();
        var all = new AudioPacketConverter(format, 48000).Convert(FloatBytes(input));
        var split = new AudioPacketConverter(format, 48000);
        var pieces = Enumerable.Range(0, 10).SelectMany(i => split.Convert(FloatBytes(input.Skip(i * 441).Take(441).ToArray()))).ToArray();
        Assert.Equal(all.Length, pieces.Length);
        for (var i = 0; i < all.Length; i++) Assert.InRange(Math.Abs(all[i] - pieces[i]), 0, .0001f);
    }
    [Fact]
    public void DownsamplingAttenuatesAudioAboveTheNewNyquistFrequency()
    {
        var converter = new AudioPacketConverter(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1), 16000);
        var input = Enumerable.Range(0, 48000).Select(i => .5f * MathF.Sin(2 * MathF.PI * 12000 * i / 48000)).ToArray();
        var samples = converter.Convert(FloatBytes(input)).Skip(3200).ToArray();
        var rms = Math.Sqrt(samples.Select(v => v * v).Average());
        Assert.InRange(rms, 0, .15);
    }
    [Fact]
    public void PartialFramesAreRejected()
    {
        var converter = new AudioPacketConverter(new WaveFormat(48000, 16, 2), 48000);
        Assert.Throws<ArgumentException>(() => converter.Convert([0, 0, 0]));
    }
    [Fact]
    public void SerializedExtensibleFormatRetainsFloatEncodingAndSurroundMask()
    {
        var original = new WaveFormatExtensible(48000, 32, 6, true, 32, 63);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) original.Serialize(writer);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        var length = reader.ReadInt32();
        var restored = WaveFormat.FromFormatChunk(reader, length);
        var converter = new AudioPacketConverter(restored, 48000);
        var samples = converter.Convert(FloatBytes([1, 1, 1, 1, 1, 1]));
        Assert.All(samples, value => Assert.InRange(value, .999f, 1.001f));
    }
    [Fact]
    public void Float64EndpointDataIsDecoded()
    {
        var format = new WaveFormatExtensible(48000, 64, 1, true, 64, 4);
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, BitConverter.DoubleToInt64Bits(.25));
        Assert.Equal(new[] { .25f, .25f }, new AudioPacketConverter(format, 48000).Convert(bytes));
    }    [Fact]
    public void SerializedUnusualSpeakerMaskIsUsedForDownmixing()
    {
        var original = new WaveFormatExtensible(48000, 32, 3, true, 32, 11); // FL, FR, LFE
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) original.Serialize(writer);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        var restored = WaveFormat.FromFormatChunk(reader, reader.ReadInt32());
        var result = new AudioPacketConverter(restored, 48000).Convert(FloatBytes([0, 0, 1]));
        Assert.All(result, value => Assert.InRange(value, .332f, .334f));
    }    [Fact]
    public void PhysicalClockCorrectionPreservesOutputDuration()
    {
        var converter = new AudioPacketConverter(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1), 48000);
        converter.SetInputRate(48005);
        var samples = Enumerable.Range(0, 48005).Select(i => .5f * MathF.Sin(2 * MathF.PI * 440 * i / 48005)).ToArray();
        var output = converter.Convert(FloatBytes(samples));
        Assert.InRange(output.Length / 2, 47996, 48004);
    }
    [Fact]
    public void InvalidClockCorrectionsAreRejected()
    {
        var converter = new AudioPacketConverter(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1), 48000);
        Assert.Throws<ArgumentOutOfRangeException>(() => converter.SetInputRate(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => converter.SetInputRate(96000));
    }    private static byte[] FloatBytes(float[] samples)
    {
        var bytes = new byte[samples.Length * 4];
        for (var i = 0; i < samples.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i * 4), BitConverter.SingleToInt32Bits(samples[i]));
        return bytes;
    }
}



