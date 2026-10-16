using CaptureFlow.Core.Video;
using CaptureFlow.Desktop;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Qa;

internal static class QualityUiQa
{
    public static async Task VerifyAsync(MainWindow window, DashboardViewModel dashboard)
    {
        var screen = dashboard.Screen;
        if (screen.Monitors.Count == 0) throw new InvalidOperationException("BLOCKED: no monitor configuration is available.");
        dashboard.SelectedSettingsTab = 1; screen.MicrophoneEnabled = false; screen.SystemAudioEnabled = false;
        screen.SelectedEncoder = "Software"; screen.SelectedQuality = "Custom"; screen.SelectedResolution = "720p"; screen.SelectedFrameRate = "24 FPS";
        screen.CustomBitrateText = "8000";
        Check(screen.StartCommand.CanExecute(null), "Valid quality configuration cannot start.");
        screen.CustomBitrateText = "garbage"; Check(!screen.StartCommand.CanExecute(null), "Invalid bitrate text can start.");
        screen.CustomBitrateText = "100001"; Check(!screen.StartCommand.CanExecute(null), "Out-of-range bitrate can start.");
        screen.CustomBitrateText = "8000";
        await screen.SaveQualitySettingsCommand.ExecuteAsync(null);
        var store = new JsonVideoQualityPreferencesStore(Environment.GetEnvironmentVariable("CAPTUREFLOW_QUALITY_PREFERENCES")!);
        var restored = await store.LoadAsync();
        Check(restored == new VideoQualityPreferences(VideoQualityPreset.Custom, VideoOutputResolution.P720, 24, 8000, VideoEncoderChoice.Software), "Saved preferences differ from the UI.");
        screen.SelectedQuality = "Low"; await screen.LoadQualityPreferencesAsync();
        Check(screen.SelectedQuality == "Custom" && screen.SelectedFrameRate == "24 FPS", "Saved quality preferences were not restored.");
        screen.Status = VideoRecordingState.Recording; Check(!screen.CanEditQuality && !screen.SaveQualitySettingsCommand.CanExecute(null), "Quality preferences remain editable while recording.");
        screen.Status = VideoRecordingState.Idle; screen.IsBusy = false;
        screen.SelectedEncoder = "Auto";
        await screen.CheckEncodersCommand.ExecuteAsync(null);
        Check(!screen.EncoderMessage.StartsWith("Encoder checks failed"), screen.EncoderMessage);
        Console.WriteLine("PASS real Settings quality choices, invalid bitrate guards, persistent preferences and active-recording lock (state set by QA; no capture started).");
        Console.WriteLine("Quality estimate: " + screen.QualitySummary);
        Console.WriteLine(screen.EncoderMessage);
        window.UpdateLayout();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
