using System.Globalization;
using CaptureFlow.Core.Audio;
using CaptureFlow.Application.Audio;
namespace CaptureFlow.Tests;
public class RecordingFileNameTests
{
    [Fact]
    public void ContainsUtcTimestampAndSafeWavExtension()
    {
        var time = new DateTimeOffset(2026, 10, 13, 10, 30, 0, TimeSpan.FromHours(5.5));
        var name = RecordingFileName.Create(time, Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"));
        Assert.Equal("CaptureFlow_Microphone_20261013_050000_000_0123456789abcdef0123456789abcdef.wav", name);
        Assert.DoesNotContain(name, c => Path.GetInvalidFileNameChars().Contains(c));
    }
    [Fact]
    public void RecordingsStartingInSameMillisecondHaveDistinctNames()
    {
        var time = DateTimeOffset.UtcNow;
        var names = Enumerable.Range(0, 10000).Select(_ => RecordingFileName.Create(time, Guid.NewGuid())).ToArray();
        Assert.Equal(names.Length, names.Distinct().Count());
    }
    [Theory]
    [InlineData(AudioRecordingMode.MicrophoneOnly, "Microphone")]
    [InlineData(AudioRecordingMode.SystemAudioOnly, "SystemAudio")]
    [InlineData(AudioRecordingMode.MicrophoneAndSystemAudio, "CombinedAudio")]
    public void FilenameIdentifiesTheRecordingMode(AudioRecordingMode mode, string label)
    {
        Assert.StartsWith("CaptureFlow_" + label + "_", RecordingFileName.Create(DateTimeOffset.UtcNow, Guid.NewGuid(), mode));
    }    [Fact]
    public void FilenameDoesNotDependOnCurrentCulture()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            var time = DateTimeOffset.UnixEpoch;
            var expected = RecordingFileName.Create(time, Guid.Empty);
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            Assert.Equal(expected, RecordingFileName.Create(time, Guid.Empty));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
}


