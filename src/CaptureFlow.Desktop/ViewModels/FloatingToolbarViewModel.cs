using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CaptureFlow.Application.Desktop;
namespace CaptureFlow.Desktop.ViewModels;
public partial class FloatingToolbarViewModel(Func<RecordingAction, Task> route, Action cancelCountdown, Action open) : ObservableObject
{
    [ObservableProperty] private string status = "Ready";
    [ObservableProperty] private string elapsed = "00:00:00";
    [ObservableProperty] private string pauseText = "Pause";
    [ObservableProperty] private bool canPause;
    [ObservableProperty] private bool canStop;
    [ObservableProperty] private bool canMute;
    [ObservableProperty] private bool microphoneMuted;
    [ObservableProperty] private bool isCountdown;
    [ObservableProperty] private bool collapsed;
    [ObservableProperty] private string captureNotice = "Capture exclusion has not been verified in real recording output.";
    public string StopText => IsCountdown ? "Cancel" : "Stop";
    public string MuteText => MicrophoneMuted ? "Unmute mic" : "Mute mic";
    partial void OnIsCountdownChanged(bool value) => OnPropertyChanged(nameof(StopText));
    partial void OnMicrophoneMutedChanged(bool value) => OnPropertyChanged(nameof(MuteText));
    [RelayCommand] private Task PauseResumeAsync() => CanPause ? route(RecordingAction.PauseResume) : Task.CompletedTask;
    [RelayCommand] private Task StopAsync() { if (IsCountdown) { cancelCountdown(); return Task.CompletedTask; } return CanStop ? route(RecordingAction.Stop) : Task.CompletedTask; }
    [RelayCommand] private Task ToggleMicrophoneAsync() => CanMute ? route(RecordingAction.ToggleMicrophone) : Task.CompletedTask;
    [RelayCommand] private void Collapse() => Collapsed = !Collapsed;
    [RelayCommand] private void OpenMain() => open();
}
