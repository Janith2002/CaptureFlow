using System.Buffers.Binary;
using CaptureFlow.Infrastructure.Audio;
using NAudio.Wave;
namespace CaptureFlow.Tests;
public class MixedWavPipelineTests
{
    [Fact]
    public void DifferentDeviceFormatsProduceAReadableStereoWavContainingBothSources()
    {
        var mic = new AudioPacketConverter(new WaveFormat(44100, 16, 1), 48000);
        var system = new AudioPacketConverter(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2), 48000);
        var mixer = new AudioTimelineMixer(48000, true, 0);
        using var stream = new MemoryStream();
        using (var writer = new WaveFileWriter(stream, new WaveFormat(48000, 16, 2)))
        {
        for (var packet = 0; packet < 100; packet++)
        {
            var micBytes = new byte[441 * 2];
            var systemBytes = new byte[480 * 8];
            for (var i = 0; i < 441; i++)
                BinaryPrimitives.WriteInt16LittleEndian(micBytes.AsSpan(i * 2), (short)(16384 * Math.Sin(2 * Math.PI * 440 * (packet * 441 + i) / 44100)));
            for (var i = 0; i < 480; i++)
            {
                var sample = .5f * MathF.Sin(2 * MathF.PI * 1000 * (packet * 480 + i) / 48000);
                BinaryPrimitives.WriteInt32LittleEndian(systemBytes.AsSpan(i * 8), BitConverter.SingleToInt32Bits(sample));
                BinaryPrimitives.WriteInt32LittleEndian(systemBytes.AsSpan(i * 8 + 4), BitConverter.SingleToInt32Bits(-sample));
            }
            var timestamp = packet * AudioTimelineMixer.QpcUnitsPerSecond / 100;
            mixer.Add(true, timestamp, mic.Convert(micBytes), .8f, false);
            mixer.Add(false, timestamp, system.Convert(systemBytes), .4f, false);
            // Mirror the production writer's delayed timeline consumption.
            if (packet >= 30) writer.Write(mixer.Render(480));
        }
        mixer.Stop(AudioTimelineMixer.QpcUnitsPerSecond);
        writer.Write(mixer.Render((int)(48000 - mixer.WrittenFrames)));
        }
        using var reader = new WaveFileReader(new MemoryStream(stream.ToArray()));
        Assert.Equal(WaveFormatEncoding.Pcm, reader.WaveFormat.Encoding); Assert.Equal(2, reader.WaveFormat.Channels);
        Assert.Equal(48000 * 4, reader.Length); Assert.Equal(TimeSpan.FromSeconds(1), reader.TotalTime);
        var pcm = new byte[reader.Length]; Assert.Equal(pcm.Length, reader.Read(pcm));
        var left = Enumerable.Range(0, 48000).Select(i => BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i * 4)) / 32768d).ToArray();
        Assert.InRange(AmplitudeAt(left, 440), .19, .21); Assert.InRange(AmplitudeAt(left, 1000), .09, .11);
        Assert.All(left, sample => Assert.InRange(sample, -1, 1));
    }
    private static double AmplitudeAt(double[] samples, int hz)
    {
        var sine = samples.Select((s, i) => s * Math.Sin(2 * Math.PI * hz * i / 48000)).Sum();
        var cosine = samples.Select((s, i) => s * Math.Cos(2 * Math.PI * hz * i / 48000)).Sum();
        return 2 * Math.Sqrt(sine * sine + cosine * cosine) / samples.Length;
    }
}

