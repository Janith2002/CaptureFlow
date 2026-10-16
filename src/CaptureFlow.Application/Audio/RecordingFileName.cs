using System.Globalization;
using CaptureFlow.Core.Audio;
namespace CaptureFlow.Application.Audio;
public static class RecordingFileName
{
    public static string Create(DateTimeOffset timestamp, Guid uniqueId, AudioRecordingMode mode = AudioRecordingMode.MicrophoneOnly, AudioOutputFormat format = AudioOutputFormat.Wav)
    {
        if (!Enum.IsDefined(format)) throw new ArgumentException("Unsupported audio format.", nameof(format));
        return $"CaptureFlow_{mode switch { AudioRecordingMode.SystemAudioOnly => "SystemAudio", AudioRecordingMode.MicrophoneAndSystemAudio => "CombinedAudio", _ => "Microphone" }}_{timestamp.UtcDateTime.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)}_{uniqueId:N}.{(format == AudioOutputFormat.Mp3 ? "mp3" : "wav")}";
    }
}


