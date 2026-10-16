using System.Collections.ObjectModel;
using System.Windows.Threading;
using CaptureFlow.Core.Audio;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CaptureFlow.Core.Desktop;
using CaptureFlow.Application.Desktop;
namespace CaptureFlow.Desktop.ViewModels;
public partial class AudioRecorderViewModel : ObservableObject, IDisposable
{
    private readonly IAudioRecordingService recorder;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer timer;
    private readonly IRecordingCountdown countdown;
    private readonly RecordingExperienceViewModel? experience;
    private CancellationTokenSource? startCancellation;
    [ObservableProperty] private string selectedCountdown = "3 seconds";
    public string[] CountdownOptions { get; } = ["Off", "3 seconds", "5 seconds", "10 seconds"];
    public bool IsCountingDown => startCancellation is not null && countdown.IsRunning && countdown.Remaining > 0;
    partial void OnSelectedCountdownChanged(string value) { if (experience is not null) experience.SelectedCountdown = value; StartCommand.NotifyCanExecuteChanged(); }
    private void ExperienceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(RecordingExperienceViewModel.SelectedCountdown)) SelectedCountdown = experience!.SelectedCountdown; }
    private void CountdownChanged(object? sender, EventArgs e) { OnPropertyChanged(nameof(IsCountingDown)); CancelCountdownCommand.NotifyCanExecuteChanged(); if (IsCountingDown) StatusMessage = $"Recording starts in {countdown.Remaining}…"; }
    public event EventHandler? RecordingSaved;
    public AudioRecorderViewModel(IAudioRecordingService recorder, IRecordingCountdown? countdown = null, RecordingExperienceViewModel? experience = null)
    {
        this.recorder = recorder;
        this.countdown = countdown ?? new RecordingCountdown(); this.experience = experience;
        this.countdown.Changed += CountdownChanged;
        if (experience is not null) { SelectedCountdown = experience.SelectedCountdown; experience.PropertyChanged += ExperienceChanged; }
        dispatcher = Dispatcher.CurrentDispatcher;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => UpdateTimer(), dispatcher);
        timer.Stop();
        recorder.StateChanged += OnRecordingStateChanged;
    }
    [ObservableProperty] private string selectedMode = "Microphone Only";
    [ObservableProperty] private string selectedFormat = "WAV";
    [ObservableProperty] private int selectedMp3Bitrate = 192;
    public int[] Mp3BitrateOptions { get; } = [128, 192, 256, 320];
    public bool IsMp3 => SelectedFormat == "MP3";
    [ObservableProperty] private string ffmpegPath = "ffmpeg.exe";
    [ObservableProperty] private string selectedQuality = "High quality";
    [ObservableProperty] private bool microphoneMuted;
    [ObservableProperty] private bool systemAudioMuted;
    [ObservableProperty] private double systemAudioVolume = 100;
    [ObservableProperty] private AudioOutputDevice? selectedOutputDevice;
    [ObservableProperty] private double microphoneVolume = 80;
    [ObservableProperty] private MicrophoneDevice? selectedMicrophone;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private AudioRecordingState status = AudioRecordingState.Idle;
    [ObservableProperty] private string elapsedText = "00:00:00";
    [ObservableProperty] private string statusMessage = "Choose an audio mode, devices and WAV or MP3 format.";
    public string OutputDirectory { get; set; } = "";
    public ObservableCollection<MicrophoneDevice> Microphones { get; } = [];
    public ObservableCollection<AudioOutputDevice> OutputDevices { get; } = [];
    public bool MicrophoneEnabled => SelectedMode != "System Audio Only";
    public bool SystemAudioEnabled => SelectedMode != "Microphone Only";
    public string[] ModeOptions { get; } = ["Microphone Only", "System Audio Only", "Microphone + System Audio"];
    public string[] FormatOptions { get; } = ["WAV", "MP3"];
    public string[] QualityOptions { get; } = ["High quality", "Balanced", "Smaller file"];
    public bool CanEditPreferences => !IsBusy && Status is not (AudioRecordingState.Recording or AudioRecordingState.Paused or AudioRecordingState.Saving or AudioRecordingState.Exporting);
    public string StatusLabel => Status.ToString();
    public bool IsExporting => Status == AudioRecordingState.Exporting;
    private bool CanStart() => CanEditPreferences && CountdownOptions.Contains(SelectedCountdown) && (!MicrophoneEnabled || SelectedMicrophone is not null) && (!SystemAudioEnabled || SelectedOutputDevice is not null) && ModeOptions.Contains(SelectedMode) && FormatOptions.Contains(SelectedFormat) && (!IsMp3 || Mp3BitrateOptions.Contains(SelectedMp3Bitrate) && !string.IsNullOrWhiteSpace(FfmpegPath));
    private bool CanCancelExport() => Status == AudioRecordingState.Exporting;
    private bool CanPause() => !IsBusy && Status == AudioRecordingState.Recording;
    private bool CanResume() => !IsBusy && Status == AudioRecordingState.Paused;
    private bool CanStop() => !IsBusy && Status is AudioRecordingState.Recording or AudioRecordingState.Paused;
    partial void OnIsBusyChanged(bool value) => RefreshCommands();
    partial void OnStatusChanged(AudioRecordingState value) { OnPropertyChanged(nameof(StatusLabel)); OnPropertyChanged(nameof(IsExporting)); RefreshCommands(); }
    partial void OnSelectedMicrophoneChanged(MicrophoneDevice? value) => RefreshCommands();
    partial void OnSelectedModeChanged(string value) { OnPropertyChanged(nameof(MicrophoneEnabled)); OnPropertyChanged(nameof(SystemAudioEnabled)); RefreshCommands(); }
    partial void OnSelectedOutputDeviceChanged(AudioOutputDevice? value) => RefreshCommands();
    partial void OnSystemAudioVolumeChanged(double value) => recorder.SetSystemVolume((float)(value / 100), SystemAudioMuted);
    partial void OnSystemAudioMutedChanged(bool value) => recorder.SetSystemVolume((float)(SystemAudioVolume / 100), value);
    partial void OnSelectedFormatChanged(string value) { OnPropertyChanged(nameof(IsMp3)); RefreshCommands(); }
    partial void OnSelectedMp3BitrateChanged(int value) => RefreshCommands();
    partial void OnFfmpegPathChanged(string value) => RefreshCommands();
    partial void OnMicrophoneVolumeChanged(double value) => recorder.SetVolume((float)(value / 100), MicrophoneMuted);
    partial void OnMicrophoneMutedChanged(bool value) => recorder.SetVolume((float)(MicrophoneVolume / 100), value);
    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanEditPreferences));
        StartCommand.NotifyCanExecuteChanged(); PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged(); StopCommand.NotifyCanExecuteChanged(); RefreshDevicesCommand.NotifyCanExecuteChanged();
        CancelExportCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute = nameof(CanEditPreferences))]
    private async Task RefreshDevicesAsync()
    {
        IsBusy = true;
        try
        {
            var selectedId = SelectedMicrophone?.Id;
            var selectedOutputId = SelectedOutputDevice?.Id;
            IReadOnlyList<MicrophoneDevice> microphones = [];
            IReadOnlyList<AudioOutputDevice> outputs = [];
            var errors = new List<string>();
            try { microphones = await recorder.GetDevicesAsync(); }
            catch (Exception ex) { errors.Add("Microphone discovery: " + ex.Message); }
            try { outputs = await recorder.GetOutputDevicesAsync(); }
            catch (Exception ex) { errors.Add("Playback device discovery: " + ex.Message); }
            Microphones.Clear(); OutputDevices.Clear();
            foreach (var device in microphones) Microphones.Add(device);
            foreach (var device in outputs) OutputDevices.Add(device);
            SelectedMicrophone = Microphones.FirstOrDefault(d => d.Id == selectedId) ?? Microphones.FirstOrDefault();
            SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == selectedOutputId) ?? OutputDevices.FirstOrDefault();
            StatusMessage = $"Detected {microphones.Count} microphone(s) and {outputs.Count} playback device(s). Select devices for your recording mode. If a device is missing, connect or enable it and refresh.";
            if (errors.Count > 0) StatusMessage += " " + string.Join(" ", errors);
            if (recorder.State is AudioRecordingState.Idle or AudioRecordingState.Error) Status = AudioRecordingState.Idle;
        }
        catch (Exception ex)
        {
            Microphones.Clear(); OutputDevices.Clear(); SelectedMicrophone = null; SelectedOutputDevice = null; Status = AudioRecordingState.Error;
            StatusMessage = "Could not list audio devices: " + ex.Message + " Check Windows microphone permissions and refresh devices.";
        }
        finally { IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        using var cancellation = new CancellationTokenSource(); startCancellation = cancellation;
        IsBusy = true; Status = AudioRecordingState.Idle; StatusMessage = "Opening selected audio devices…"; ElapsedText = "00:00:00";
        try
        {
            var seconds = new RecordingExperienceViewModel { SelectedCountdown = SelectedCountdown }.CountdownSeconds;
            await countdown.RunAsync(new(seconds), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested(); StatusMessage = "Opening selected audio devices…";
            var sampleRate = SelectedQuality switch { "Balanced" => 44100, "Smaller file" => 16000, _ => 48000 };
            var mode = SelectedMode switch { "System Audio Only" => AudioRecordingMode.SystemAudioOnly, "Microphone + System Audio" => AudioRecordingMode.MicrophoneAndSystemAudio, _ => AudioRecordingMode.MicrophoneOnly };
            await recorder.StartAsync(new(SelectedMicrophone?.Id ?? "", OutputDirectory, sampleRate,
                (float)(MicrophoneVolume / 100), MicrophoneMuted, mode, SelectedOutputDevice?.Id,
                (float)(SystemAudioVolume / 100), SystemAudioMuted, OutputFormat: IsMp3 ? AudioOutputFormat.Mp3 : AudioOutputFormat.Wav,
                Mp3BitrateKbps: SelectedMp3Bitrate, FfmpegPath: FfmpegPath));
            ApplyState();
        }
        catch (OperationCanceledException) { Status = AudioRecordingState.Cancelled; StatusMessage = "Countdown cancelled. No recording started."; }
        catch (Exception ex) { Status = AudioRecordingState.Error; StatusMessage = ex.Message; }
        finally { startCancellation = null; IsBusy = false; OnPropertyChanged(nameof(IsCountingDown)); CancelCountdownCommand.NotifyCanExecuteChanged(); }
    }
    [RelayCommand(CanExecute = nameof(IsCountingDown))] private void CancelCountdown() { startCancellation?.Cancel(); countdown.Cancel(); }
    [RelayCommand(CanExecute = nameof(CanPause))] private Task PauseAsync() => RunOperationAsync(recorder.PauseAsync);
    [RelayCommand(CanExecute = nameof(CanResume))] private Task ResumeAsync() => RunOperationAsync(recorder.ResumeAsync);
    [RelayCommand(CanExecute = nameof(CanStop))] private Task StopAsync() => RunOperationAsync(recorder.StopAsync);
    [RelayCommand(CanExecute = nameof(CanCancelExport))] private Task CancelExportAsync() => recorder.CancelExportAsync();
    private async Task RunOperationAsync(Func<Task> operation)
    {
        IsBusy = true;
        try { await operation(); ApplyState(); }
        catch (Exception ex) { Status = AudioRecordingState.Error; StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }
    private void OnRecordingStateChanged(object? sender, EventArgs e)
    {
        if (dispatcher.CheckAccess()) ApplyState();
        else dispatcher.BeginInvoke(ApplyState);
    }
    private void ApplyState()
    {
        var wasCompleted = Status == AudioRecordingState.Completed;
        Status = recorder.State;
        UpdateTimer();
        if (Status == AudioRecordingState.Recording) timer.Start(); else timer.Stop();
        StatusMessage = Status switch
        {
            AudioRecordingState.Recording => "Recording the selected audio sources. Independent volume and mute controls affect the saved audio.",
            AudioRecordingState.Paused => "Paused. Incoming audio is omitted; the selected devices stay open.",
            AudioRecordingState.Saving => "Finalizing and checking the WAV file…",
            AudioRecordingState.Exporting => "Exporting MP3… Encoding and validating audio. You can cancel export; the WAV will be retained.",
            AudioRecordingState.Cancelled => recorder.ErrorMessage ?? "Export cancelled.",
            AudioRecordingState.Completed => "Saved audio: " + recorder.SavedFilePath,
            AudioRecordingState.Error => recorder.ErrorMessage ?? "Recording failed.",
            _ => "Choose an audio mode, devices and WAV or MP3 format."
        };
        if (Status == AudioRecordingState.Completed && !wasCompleted) RecordingSaved?.Invoke(this, EventArgs.Empty);
    }
    private void UpdateTimer()
    {
        var elapsed = recorder.Duration;
        ElapsedText = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }
    public void Dispose() { startCancellation?.Cancel(); countdown.Changed -= CountdownChanged; if (experience is not null) experience.PropertyChanged -= ExperienceChanged; timer.Stop(); recorder.StateChanged -= OnRecordingStateChanged; }
}




