using CaptureFlow.Infrastructure.Audio;
using NAudio.Wave;

namespace CaptureFlow.Tests;

public class WavRecordingFinalizationTests
{
    [Fact]
    public void FailedHeaderFinalizationStillReleasesCallerOwnedStream()
    {
        var stream = new FailingStream();
        var writer = new WaveFileWriter(stream, new WaveFormat(48000, 16, 1));
        writer.Write(new byte[16]);
        stream.FailWrites = true;
        Assert.Throws<IOException>(() => WavRecordingFinalization.Close(writer, stream));
        Assert.True(stream.WasDisposed);
    }
    private sealed class FailingStream : MemoryStream
    {
        public bool FailWrites { get; set; }
        public bool WasDisposed { get; private set; }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (FailWrites) throw new IOException("Simulated header write failure.");
            base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (FailWrites) throw new IOException("Simulated header write failure.");
            base.Write(buffer);
        }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void ExclusiveCaptureFileCanBeValidatedAndPublishedAfterFinalization(int channels)
    {
        var directory = Path.Combine(Path.GetTempPath(), "CaptureFlow WAV QA", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var pending = Path.Combine(directory, "capture.wav.partial");
        var final = Path.Combine(directory, "capture.wav");
        try
        {
            using var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var writer = new WaveFileWriter(stream, new WaveFormat(48000, 16, channels));
            var pcm = new byte[4800 * channels * 2];
            writer.Write(pcm);
            WavRecordingFinalization.Close(writer, stream);
            using (var reader = new WaveFileReader(pending))
            {
                Assert.Equal(pcm.Length, reader.Length);
                Assert.Equal(channels, reader.WaveFormat.Channels);
                Assert.Equal(TimeSpan.FromMilliseconds(100), reader.TotalTime);
            }
            File.Move(pending, final, overwrite: false);
            using (File.Open(final, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }
        finally { Directory.Delete(directory, true); }
    }
}
