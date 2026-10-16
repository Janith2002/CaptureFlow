using CaptureFlow.Application.Desktop;
using CaptureFlow.Core.Audio;
using CaptureFlow.Core.Video;
using CaptureFlow.Desktop.ViewModels;
using CommunityToolkit.Mvvm.Input;
namespace CaptureFlow.Desktop.Services;

/// <summary>Adapter to the recorder's existing commands, including their validation and error presentation.</summary>
public sealed class ViewModelRecordingTarget : IRecordingCommandTarget
{
    private readonly AudioRecorderViewModel? audio;
    private readonly ScreenRecorderViewModel? video;
    public ViewModelRecordingTarget(AudioRecorderViewModel audio) => this.audio = audio;
    public ViewModelRecordingTarget(ScreenRecorderViewModel video) => this.video = video;
    public bool Active => audio is not null ? audio.Status is AudioRecordingState.Recording or AudioRecordingState.Paused :
        video!.Status is VideoRecordingState.Recording or VideoRecordingState.Paused;
    public bool Busy => audio?.IsBusy ?? video!.IsBusy;
    private IAsyncRelayCommand Start => audio?.StartCommand ?? video!.StartCommand;
    private IAsyncRelayCommand Stop => audio?.StopCommand ?? video!.StopCommand;
    private IAsyncRelayCommand Pause => audio?.PauseCommand ?? video!.PauseCommand;
    private IAsyncRelayCommand Resume => audio?.ResumeCommand ?? video!.ResumeCommand;
    public async Task ExecuteAsync(RecordingAction action)
    {
        if (action == RecordingAction.ToggleMicrophone)
        {
            if (audio is not null) audio.MicrophoneMuted = !audio.MicrophoneMuted;
            else video!.MicrophoneMuted = !video.MicrophoneMuted;
            return;
        }
        var command = action switch
        {
            RecordingAction.Start => Start,
            RecordingAction.Stop => Stop,
            RecordingAction.StartStop => Active ? Stop : Start,
            RecordingAction.PauseResume => Pause.CanExecute(null) ? Pause : Resume,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        if (!command.CanExecute(null))
            throw new InvalidOperationException("Recording action is unavailable. Open the recorder page and check devices, preview, output folder and FFmpeg settings.");
        await command.ExecuteAsync(null);
        if (audio?.Status == AudioRecordingState.Error || video?.Status == VideoRecordingState.Error)
            throw new InvalidOperationException(audio?.StatusMessage ?? video!.StatusMessage);
    }
    public async Task FinalizeAsync()
    {
        var hadPendingRecording = Active || Start.IsRunning || Pause.IsRunning || Resume.IsRunning || Stop.IsRunning;
        if (Start.IsRunning)
        {
            if (audio?.CancelCountdownCommand.CanExecute(null) == true) audio.CancelCountdownCommand.Execute(null);
            if (video?.CancelCommand.CanExecute(null) == true) await video.CancelCommand.ExecuteAsync(null);
            if (Start.ExecutionTask is { } starting) await starting;
        }
        foreach (var command in new[] { Pause, Resume, Stop })
            if (command.IsRunning && command.ExecutionTask is { } pending) await pending;
        if (Stop.CanExecute(null)) await Stop.ExecuteAsync(null);
        if (hadPendingRecording && (audio?.Status == AudioRecordingState.Error || video?.Status == VideoRecordingState.Error))
            throw new InvalidOperationException("Recording could not be finalized: " + (audio?.StatusMessage ?? video!.StatusMessage));
    }
}
