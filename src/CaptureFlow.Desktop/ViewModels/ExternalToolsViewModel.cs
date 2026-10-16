using System.IO;
using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Ffmpeg;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
namespace CaptureFlow.Desktop.ViewModels;

public partial class ExternalToolsViewModel(IFfmpegPreferencesStore store, ScreenRecorderViewModel screen, AudioRecorderViewModel audio, EditorViewModel editor) : ObservableObject
{
    [ObservableProperty] private string executable = "ffmpeg.exe";
    [ObservableProperty] private string message = "FFmpeg is installed externally. It is not bundled with CaptureFlow.";
    [ObservableProperty] private bool busy;
    private bool CanChange => screen.CanEditPreferences && audio.CanEditPreferences && editor.CanEdit;
    private void Apply(string path) { screen.FfmpegPath = path; audio.FfmpegPath = path; editor.FfmpegPath = path; }
    public async Task LoadAsync()
    {
        try
        {
            Executable = await store.LoadAsync(); Apply(Executable);
            var path = FfmpegToolPaths.ResolveExecutable(Executable); _ = FfmpegToolPaths.ProbeFor(path); Apply(path); Executable = path;
            Message = "External FFmpeg pair located: " + path + ". Validate and Save to check matching versions and persist this path.";
        }
        catch (Exception ex) { Message = "External tools need attention: " + ex.Message + " WAV recording remains available; configure tools for video, MP3, editor export and recovery."; }
    }
    [RelayCommand] private void Browse()
    {
        if (!CanChange || Busy) return;
        var dialog = new OpenFileDialog { Filter = "FFmpeg executable|ffmpeg.exe", Title = "Select externally installed FFmpeg" };
        if (dialog.ShowDialog() == true) Executable = dialog.FileName;
    }
    [RelayCommand] private async Task SaveAsync()
    {
        if (Busy || !CanChange) { Message = "Finish recording or editor processing before changing tools."; return; }
        Busy = true;
        try
        {
            var path = FfmpegToolPaths.ResolveExecutable(Executable); var version = await FfmpegToolPaths.VerifyPairAsync(path);
            if (!CanChange) { Message = "A recording or editor operation started; tool preferences were not changed."; return; }
            await store.SaveAsync(path); Apply(path); Executable = path; Message = "External FFmpeg / FFprobe " + version + " verified and saved. No media capture was started.";
        }
        catch (Exception ex) { Message = "External tools were not changed: " + ex.Message; }
        finally { Busy = false; }
    }
}
