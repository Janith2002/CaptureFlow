using System.Windows.Threading;
using CaptureFlow.Application.Desktop;
using CaptureFlow.Core.Audio;
using CaptureFlow.Core.Desktop;
using CaptureFlow.Desktop.Services;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Tests;

public class DesktopRecordingCommandTests
{
    [Theory] [InlineData("Microphone Only")] [InlineData("System Audio Only")] [InlineData("Microphone + System Audio")]
    public Task CountdownDoesNotOpenDevicesAndItsTimeIsExcludedFromRecordedDuration(string mode) => Sta(async () =>
    {
        var clock = new CountdownToolbarTests.Clock(); var presentation = new CountdownToolbarTests.Presentation();
        var countdown = new RecordingCountdown(presentation, clock); var service = new AudioService { Clock = clock };
        using var vm = new AudioRecorderViewModel(service, countdown) { SelectedMode = mode, SelectedMicrophone = new("qa", "QA"), SelectedOutputDevice = new("qa", "QA"), OutputDirectory = "unused" };
        var start = vm.StartCommand.ExecuteAsync(null); Assert.True(vm.IsCountingDown); Assert.Empty(service.Calls); Assert.Equal("00:00:00", vm.ElapsedText);
        for (var i = 0; i < 3; i++) { clock.Advance(TimeSpan.FromSeconds(1)); await Task.Delay(20); }
        await start; Assert.Equal(1, presentation.Closures); Assert.Equal(new[] { "Start" }, service.Calls);
        clock.Advance(TimeSpan.FromSeconds(2)); Assert.Equal(TimeSpan.FromSeconds(2), service.Duration); await vm.StopCommand.ExecuteAsync(null);
    });
    [Fact] public Task CountdownCancellationAndShutdownNeverOpenAudioSession() => Sta(async () =>
    {
        var countdown = new RecordingCountdown(timeProvider: new CountdownToolbarTests.Clock()); var service = new AudioService();
        using var vm = new AudioRecorderViewModel(service, countdown) { SelectedMicrophone = new("qa", "QA"), OutputDirectory = "unused" };
        var start = vm.StartCommand.ExecuteAsync(null); Assert.True(vm.IsCountingDown);
        await new ViewModelRecordingTarget(vm).FinalizeAsync(); await start;
        Assert.Empty(service.Calls); Assert.False(vm.IsCountingDown); Assert.Equal(AudioRecordingState.Cancelled, vm.Status);
    });
    [Fact] public Task ToolbarAndMainUiOperateOneExistingAudioSessionAndDiscardRepeatedStops() => Sta(async () =>
    {
        var service = new AudioService { Export = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var vm = new AudioRecorderViewModel(service) { SelectedCountdown = "Off", SelectedMicrophone = new("qa", "QA"), OutputDirectory = "unused" };
        var target = new ViewModelRecordingTarget(vm); var router = new RecordingCommandRouter(new IdleTarget(), target) { PreferAudio = true };
        var toolbar = new FloatingToolbarViewModel(a => router.RouteAsync(a), () => { }, () => { }) { CanPause = true, CanStop = true, CanMute = true };
        await vm.StartCommand.ExecuteAsync(null);
        await toolbar.PauseResumeCommand.ExecuteAsync(null); Assert.Equal(AudioRecordingState.Paused, vm.Status);
        await toolbar.PauseResumeCommand.ExecuteAsync(null); await toolbar.ToggleMicrophoneCommand.ExecuteAsync(null); Assert.True(service.Muted);
        var stop = toolbar.StopCommand.ExecuteAsync(null);
        Assert.False(await router.RouteAsync(RecordingAction.Stop)); Assert.Single(service.Calls, c => c == "Stop");
        service.Export.SetResult(); await stop; Assert.Equal(AudioRecordingState.Completed, vm.Status); Assert.Single(service.Calls, c => c == "Start");
    });
    private sealed class IdleTarget : IRecordingCommandTarget
    {
        public bool Active => false; public bool Busy => false;
        public Task ExecuteAsync(RecordingAction action) => throw new InvalidOperationException("Wrong target.");
        public Task FinalizeAsync() => Task.CompletedTask;
    }
    [Theory]
    [InlineData("Microphone Only")] [InlineData("System Audio Only")] [InlineData("Microphone + System Audio")]
    public Task AdapterReusesActualAudioCommandsForStartPauseResumeMuteStop(string mode) => Sta(async () =>
    {
        var service = new AudioService(); using var vm = new AudioRecorderViewModel(service)
        { SelectedCountdown = "Off", SelectedMode = mode, SelectedMicrophone = new("qa-mic", "Test microphone"), SelectedOutputDevice = new("qa-output", "Test audio"), OutputDirectory = "isolated-test-placeholder" };
        var target = new ViewModelRecordingTarget(vm);
        await target.ExecuteAsync(RecordingAction.StartStop); Assert.True(target.Active);
        await target.ExecuteAsync(RecordingAction.PauseResume); Assert.Equal(AudioRecordingState.Paused, vm.Status);
        await target.ExecuteAsync(RecordingAction.PauseResume); Assert.Equal(AudioRecordingState.Recording, vm.Status);
        await target.ExecuteAsync(RecordingAction.ToggleMicrophone); Assert.True(vm.MicrophoneMuted); Assert.True(service.Muted);
        await target.ExecuteAsync(RecordingAction.StartStop); Assert.Equal(AudioRecordingState.Completed, vm.Status);
        Assert.Equal(new[] { "Start", "Pause", "Resume", "Stop" }, service.Calls);
    });
    [Fact] public Task InvalidConfigurationNeverInvokesCapture() => Sta(async () =>
    {
        var service = new AudioService(); using var vm = new AudioRecorderViewModel(service);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ViewModelRecordingTarget(vm).ExecuteAsync(RecordingAction.Start));
        Assert.Empty(service.Calls);
    });
    [Fact] public Task RecorderErrorsArePresentedToBackgroundCommandCaller() => Sta(async () =>
    {
        var service = new AudioService { FailStart = true }; using var vm = new AudioRecorderViewModel(service) { SelectedCountdown = "Off", SelectedMicrophone = new("qa", "QA"), OutputDirectory = "unused" };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new ViewModelRecordingTarget(vm).ExecuteAsync(RecordingAction.Start));
        Assert.Contains("QA device failure", error.Message); Assert.Equal(AudioRecordingState.Error, vm.Status);
    });
    [Fact] public Task GracefulFinalizationUsesStopAndWaitsForMp3ExportWithoutCancellingIt() => Sta(async () =>
    {
        var service = new AudioService { Export = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var vm = new AudioRecorderViewModel(service) { SelectedCountdown = "Off", SelectedMicrophone = new("qa", "QA"), OutputDirectory = "unused", SelectedFormat = "MP3" };
        var target = new ViewModelRecordingTarget(vm); await target.ExecuteAsync(RecordingAction.Start);
        var finalize = target.FinalizeAsync(); Assert.False(finalize.IsCompleted); Assert.Equal(AudioRecordingState.Exporting, vm.Status);
        service.Export.SetResult(); await finalize;
        Assert.Equal(AudioRecordingState.Completed, vm.Status); Assert.Equal(new[] { "Start", "Stop" }, service.Calls); Assert.Equal(0, service.Cancellations);
    });
    [Fact] public Task ExistingUiExportIsAwaitedRatherThanStoppedTwice() => Sta(async () =>
    {
        var service = new AudioService { Export = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var vm = new AudioRecorderViewModel(service) { SelectedCountdown = "Off", SelectedMicrophone = new("qa", "QA"), OutputDirectory = "unused" };
        await vm.StartCommand.ExecuteAsync(null); var stop = vm.StopCommand.ExecuteAsync(null);
        var finalize = new ViewModelRecordingTarget(vm).FinalizeAsync(); Assert.False(finalize.IsCompleted);
        service.Export.SetResult(); await stop; await finalize; Assert.Single(service.Calls, c => c == "Stop");
    });
    [Fact] public Task SettingsSaveFailureRestoresPreviousNativeAssignments() => Sta(async () =>
    {
        var store = new PreferenceStore(); var native = new Registrar(); using var registration = new ShortcutRegistration(native);
        var settings = new ShortcutSettingsViewModel(store); await settings.InitializeAsync(registration);
        settings.StartStop = "Ctrl+Shift+F10"; store.FailSave = true; await settings.SaveCommand.ExecuteAsync(null);
        Assert.Contains("QA storage failure", settings.Message); Assert.Equal("Ctrl+Alt+R", native.Active[ShortcutAction.StartStop].Display);
    });
    [Fact] public Task OldRecordingErrorDoesNotTurnIdleExitIntoFinalizationFailure() => Sta(async () =>
    {
        var service = new AudioService(); using var vm = new AudioRecorderViewModel(service) { Status = AudioRecordingState.Error };
        await new ViewModelRecordingTarget(vm).FinalizeAsync(); Assert.Empty(service.Calls);
    });
    internal static Task Sta(Func<Task> body)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await body(); done.SetResult(); } catch (Exception ex) { done.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return done.Task;
    }
    private sealed class AudioService : IAudioRecordingService
    {
        public event EventHandler? StateChanged;
        public AudioRecordingState State { get; private set; }
        public CountdownToolbarTests.Clock? Clock; private long started;
        public TimeSpan Duration => Clock?.GetElapsedTime(started) ?? TimeSpan.Zero; public string? SavedFilePath => null;
        public string? ErrorMessage => null; public List<string> Calls = []; public bool FailStart, Muted; public int Cancellations;
        public TaskCompletionSource? Export;
        private void Set(AudioRecordingState state) { State = state; StateChanged?.Invoke(this, EventArgs.Empty); }
        public Task StartAsync(AudioRecordingOptions options) { Calls.Add("Start"); started = Clock?.GetTimestamp() ?? 0; if (FailStart) throw new IOException("QA device failure"); Set(AudioRecordingState.Recording); return Task.CompletedTask; }
        public Task PauseAsync() { Calls.Add("Pause"); Set(AudioRecordingState.Paused); return Task.CompletedTask; }
        public Task ResumeAsync() { Calls.Add("Resume"); Set(AudioRecordingState.Recording); return Task.CompletedTask; }
        public async Task StopAsync() { Calls.Add("Stop"); if (Export is not null) { Set(AudioRecordingState.Exporting); await Export.Task; } Set(AudioRecordingState.Completed); }
        public Task CancelExportAsync() { Cancellations++; return Task.CompletedTask; }
        public void SetVolume(float gain, bool muted) => Muted = muted;
        public void SetSystemVolume(float gain, bool muted) { }
        public Task<IReadOnlyList<MicrophoneDevice>> GetDevicesAsync() => Task.FromResult<IReadOnlyList<MicrophoneDevice>>([]);
        public Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync() => Task.FromResult<IReadOnlyList<AudioOutputDevice>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class PreferenceStore : IShortcutPreferencesStore
    {
        public bool FailSave;
        public Task<ShortcutPreferences> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(ShortcutPreferences.Defaults);
        public Task SaveAsync(ShortcutPreferences preferences, CancellationToken cancellationToken = default) => FailSave ? throw new IOException("QA storage failure") : Task.CompletedTask;
    }
    private sealed class Registrar : IGlobalShortcutRegistrar
    {
        public Dictionary<ShortcutAction, ShortcutBinding> Active = [];
        public void Register(ShortcutBinding binding) => Active.Add(binding.Action, binding);
        public void Unregister(ShortcutAction action) => Active.Remove(action);
        public void Dispose() { }
    }
}

