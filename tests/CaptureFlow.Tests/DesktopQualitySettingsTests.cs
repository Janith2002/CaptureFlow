using CaptureFlow.Core.Audio;
using CaptureFlow.Core.Video;
using CaptureFlow.Application.Video;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Tests;

public class DesktopQualitySettingsTests
{
    [Theory] [InlineData(1)] [InlineData(1.25)] [InlineData(1.5)]
    public Task PreviewActualSizeMapsSourcePixelsToPhysicalDisplayPixelsWithoutChangingOutput(double dpi) => DesktopRecordingCommandTests.Sta(() =>
    {
        using var f = new Fixture(); var source = f.Vm.SelectedScreenOptions;
        f.Vm.PreviewDpiScale = dpi; f.Vm.ActualPreviewSizeCommand.Execute(null);
        Assert.Equal(1920 / dpi, f.Vm.StudioPreviewWidth); Assert.Equal(1080 / dpi, f.Vm.StudioPreviewHeight); Assert.Equal(source, f.Vm.SelectedScreenOptions);
        f.Vm.FitPreviewCommand.Execute(null); Assert.True(double.IsNaN(f.Vm.StudioPreviewWidth)); Assert.True(double.IsNaN(f.Vm.StudioPreviewHeight)); Assert.Null(f.Video.LastConfiguration); return Task.CompletedTask;
    });
    [Fact] public Task LoadingCameraPresetNeverOpensThePhysicalDeviceOrStartsPreview() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var camera = new ControlledCamera(); using var f = new Fixture(cameraBackend: camera); f.Vm.Cameras.Add(new("camera", "Fixture"));
        f.Vm.SelectedStudioPreset = new(Guid.NewGuid(), "Camera", "Webcam Only", "Entire Screen", null, null, null, "camera", new("format", 1280, 720, 30, 1, "BGRA"), false, null, false, null, new(), "Off");
        f.Vm.CameraResolutions.Add("stale camera format"); f.Vm.SelectedCameraFormat = new("stale", 640, 480, 30, 1, "BGRA");
        await f.Vm.ApplyPresetCommand.ExecuteAsync(null);
        Assert.Empty(f.Vm.CameraResolutions); Assert.Null(f.Vm.SelectedCameraFormat); Assert.False(f.Vm.StartPreviewCommand.CanExecute(null));
        Assert.Equal("Webcam Only", f.Vm.SelectedMode); Assert.Equal("camera", f.Vm.SelectedCamera!.Id); Assert.Equal(0, camera.Opens); Assert.False(camera.Entered.Task.IsCompleted); Assert.False(f.Vm.IsCameraPreviewing); Assert.False(f.Vm.StartCommand.CanExecute(null));
        await f.Vm.StopCameraOperationsAsync();
    });
    [Fact] public Task ExplicitHardwarePresetNeverSilentlyBecomesAuto() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var f = new Fixture(); f.Vm.Monitors.Add(f.Vm.SelectedMonitor!); f.Vm.SelectedStudioPreset = new(Guid.NewGuid(), "GPU", "Screen Only", "Entire Screen", f.Vm.SelectedMonitor!.Id, null, null, null, null, false, null, false, null, new(Encoder: VideoEncoderChoice.Nvenc), "Off");
        await f.Vm.ApplyPresetCommand.ExecuteAsync(null); Assert.Equal("NVIDIA NVENC", f.Vm.SelectedEncoder); Assert.False(f.Vm.StartCommand.CanExecute(null));
    });
    [Fact] public Task StudioPresetRoundTripAppliesConfigurationAndRejectsMissingDevices() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var f = new Fixture(); f.Vm.Monitors.Add(f.Vm.SelectedMonitor!); f.Vm.PresetName = "Desktop"; f.Vm.SelectedFrameRate = "60 FPS";
        await f.Vm.SavePresetCommand.ExecuteAsync(null); Assert.Single(f.Vm.StudioPresets);
        f.Vm.SelectedFrameRate = "24 FPS"; await f.Vm.ApplyPresetCommand.ExecuteAsync(null); Assert.Equal("60 FPS", f.Vm.SelectedFrameRate);
        f.Vm.PresetName = "Renamed"; await f.Vm.RenamePresetCommand.ExecuteAsync(null); Assert.Equal("Renamed", f.Vm.SelectedStudioPreset!.Name);
        f.Vm.Monitors.Clear(); f.Vm.SelectedFrameRate = "24 FPS"; await f.Vm.ApplyPresetCommand.ExecuteAsync(null);
        Assert.Contains("unavailable", f.Vm.StudioMessage); Assert.Equal("24 FPS", f.Vm.SelectedFrameRate); Assert.Null(f.Video.LastConfiguration);
        await f.Vm.DeletePresetCommand.ExecuteAsync(null); Assert.Empty(f.Vm.StudioPresets);
    });
    [Fact] public Task StudioSourceActionsKeepRequiredVideoAndUseExistingAudioConfiguration() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var f = new Fixture(); f.Vm.SourceToAdd = "Microphone"; await f.Vm.AddStudioSourceCommand.ExecuteAsync(null); Assert.True(f.Vm.MicrophoneEnabled);
        f.Vm.SelectedStudioSource = f.Vm.StudioSources.Single(s => s.Kind == "Microphone"); await f.Vm.RemoveStudioSourceCommand.ExecuteAsync(null); Assert.False(f.Vm.MicrophoneEnabled);
        f.Vm.SelectedStudioSource = f.Vm.StudioSources.Single(); await f.Vm.RemoveStudioSourceCommand.ExecuteAsync(null); Assert.Equal("Screen Only", f.Vm.SelectedMode); Assert.Contains("last video", f.Vm.StudioMessage);
    });
    [Theory] [InlineData(VideoRecordingState.Starting)] [InlineData(VideoRecordingState.Recording)] [InlineData(VideoRecordingState.Paused)] [InlineData(VideoRecordingState.Saving)]
    public Task StudioConfigurationCommandsAreLockedDuringRecording(VideoRecordingState state) => DesktopRecordingCommandTests.Sta(() =>
    {
        using var f = new Fixture(); f.Vm.Status = state;
        Assert.False(f.Vm.SavePresetCommand.CanExecute(null)); Assert.False(f.Vm.ApplyPresetCommand.CanExecute(null)); Assert.False(f.Vm.AddStudioSourceCommand.CanExecute(null)); Assert.False(f.Vm.RemoveStudioSourceCommand.CanExecute(null)); Assert.False(f.Vm.StartScreenPreviewCommand.CanExecute(null)); return Task.CompletedTask;
    });
    [Fact] public Task StudioMixerVolumeMuteRoutesToTheExistingRecorder() => DesktopRecordingCommandTests.Sta(() =>
    {
        using var f = new Fixture(); f.Vm.MicrophoneVolume = 37; f.Vm.MicrophoneMuted = true; f.Vm.SystemAudioVolume = 62; f.Vm.SystemAudioMuted = true;
        Assert.Equal((.37f, true, .62f, true), f.Video.LastVolume); Assert.Null(f.Video.LastConfiguration); return Task.CompletedTask;
    });
    [Fact] public Task RapidModeRequestsDuringInitializationAreRejectedAndCleanupPrecedesReuse() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var camera = new ControlledCamera(); using var f = new Fixture(cameraBackend: camera); f.Vm.SelectedCamera = new("fixture", "Generated camera"); f.Vm.SelectedMode = "Webcam Only";
        await camera.Entered.Task; Assert.True(f.Vm.IsCameraBusy);
        for (var i = 0; i < 20; i++) f.Vm.SelectedMode = i % 2 == 0 ? "Screen Only" : "Screen + Webcam";
        Assert.Equal("Webcam Only", f.Vm.SelectedMode); Assert.Equal(1, camera.Opens); Assert.False(f.Vm.CanEditPreferences);
        camera.Released.SetResult(); await f.Vm.CameraTransition; Assert.False(f.Vm.IsCameraBusy);
        f.Vm.SelectedMode = "Screen Only"; await f.Vm.CameraTransition; Assert.Equal(1, camera.Disposals);
        f.Vm.SelectedMode = "Webcam Only"; await f.Vm.CameraTransition; Assert.Equal(2, camera.Opens);
        await f.Vm.StopCameraOperationsAsync();
    });
    [Fact] public Task ShutdownWaitsForTrackedCameraLoadingAndDiscardsCancelledInitialization() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var camera = new ControlledCamera(); using var f = new Fixture(cameraBackend: camera); f.Vm.SelectedCamera = new("fixture", "Generated camera"); f.Vm.SelectedMode = "Webcam Only";
        await camera.Entered.Task; var shutdown = f.Vm.StopCameraOperationsAsync(); Assert.False(shutdown.IsCompleted); Assert.Equal(0, camera.Disposals);
        camera.Released.SetResult(); await shutdown; Assert.Equal(1, camera.Disposals); Assert.False(f.Vm.IsCameraBusy);
    });
    [Theory] [InlineData(VideoRecordingState.Starting)] [InlineData(VideoRecordingState.Recording)] [InlineData(VideoRecordingState.Paused)] [InlineData(VideoRecordingState.Saving)]
    public Task ActiveRecordingStatesRejectModeChangesWithoutOpeningCamera(VideoRecordingState state) => DesktopRecordingCommandTests.Sta(() =>
    {
        using var f = new Fixture(); f.Vm.Status = state; f.Vm.SelectedMode = "Webcam Only";
        Assert.Equal("Screen Only", f.Vm.SelectedMode); Assert.Contains("before changing", f.Vm.CameraMessage); return Task.CompletedTask;
    });
    [Fact] public Task MousePreferencesPersistAndReachScreenConfigurationWhileWebcamRemainsIndependent() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var f = new Fixture(); f.Mouse.Preferences = new(ShowCursor: false, Highlight: true, Radius: 40, ClickEffects: true, LeftColor: "#34D399");
        await f.Vm.LoadMousePreferencesAsync(); Assert.False(f.Vm.ShowMouseCursor); Assert.True(f.Vm.HighlightCursor); Assert.Equal(40, f.Vm.HighlightRadius);
        f.Vm.HighlightRadius = 48; await f.Vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(f.Vm.MousePreferences, f.Video.LastConfiguration!.Source.MouseEffects); Assert.False(f.Vm.CanEditPreferences);
        await f.Vm.StopCommand.ExecuteAsync(null); f.Vm.SelectedMode = "Webcam Only"; Assert.False(f.Vm.IsScreen);
        await f.Vm.StopScreenSelectionOperationsAsync(); Assert.Equal(48, f.Mouse.Preferences.Radius);
    });
    [Fact] public Task InvalidMouseSettingsPreventScreenStartAndExposePersistenceErrors() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var f = new Fixture(); f.Vm.HighlightColor = "invalid"; Assert.False(f.Vm.StartCommand.CanExecute(null));
        f.Mouse.Failure = new IOException("fixture preference failure"); f.Vm.HighlightColor = "#60A5FA";
        await f.Vm.StopScreenSelectionOperationsAsync(); Assert.Contains("fixture preference failure", f.Vm.MouseEffectsMessage);
    });
    [Fact] public Task WindowRefreshPreservesIdentityAndRemovesClosedSelectionsWithoutFallback() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var f = new Fixture(); f.Vm.SelectedScreenSource = "Application Window";
        await f.Vm.RefreshWindowsCommand.ExecuteAsync(null); Assert.Single(f.Vm.ApplicationWindows); Assert.Null(f.Vm.SelectedApplicationWindow);
        f.Vm.SelectedApplicationWindow = f.Windows.Items.Single();
        await f.Vm.RefreshWindowsCommand.ExecuteAsync(null); Assert.True(f.Vm.StartCommand.CanExecute(null));
        f.Windows.Items = []; await f.Vm.RefreshWindowsCommand.ExecuteAsync(null);
        Assert.Null(f.Vm.SelectedApplicationWindow); Assert.False(f.Vm.StartCommand.CanExecute(null)); Assert.Contains("No eligible", f.Vm.SourceSelectionMessage);
        f.Windows.Failure = new IOException("enumeration fixture failure"); await f.Vm.RefreshWindowsCommand.ExecuteAsync(null);
        Assert.Contains("enumeration fixture failure", f.Vm.SourceSelectionMessage); Assert.False(f.Vm.IsBusy);
    });
    [Fact] public Task CancelledRegionSelectionKeepsPreviousConfirmedPreferences() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var f = new Fixture(); f.Vm.SelectedScreenSource = "Custom Region";
        f.RegionSelector.Result = new("qa-source", 20, 30, 320, 240, 1920, 1080);
        await f.Vm.SelectRegionCommand.ExecuteAsync(null); var previous = f.Vm.SelectedRegion;
        Assert.Equal(previous, f.Regions.Saved); Assert.Equal(1, f.Regions.Saves);
        f.RegionSelector.Result = null; await f.Vm.SelectRegionCommand.ExecuteAsync(null);
        Assert.Equal(previous, f.Vm.SelectedRegion); Assert.Equal(1, f.Regions.Saves);
        await f.Vm.LoadRegionPreferencesAsync(); Assert.Equal(previous, f.Vm.SelectedRegion);
        f.Vm.SelectedMonitor = new("qa-source", "Resized", 2560, 1440, true);
        Assert.Null(f.Vm.SelectedRegion); Assert.False(f.Vm.StartCommand.CanExecute(null));
    });
    [Fact] public Task ShutdownCancelsRegionOverlayAndWaitsForCleanup() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var f = new Fixture(); f.Vm.SelectedScreenSource = "Custom Region"; f.RegionSelector.Block = true;
        var selection = f.Vm.SelectRegionCommand.ExecuteAsync(null); Assert.True(f.Vm.IsBusy);
        Assert.False(f.Vm.StartCommand.CanExecute(null)); Assert.False(f.Vm.SelectRegionCommand.CanExecute(null));
        await f.Vm.StopScreenSelectionOperationsAsync(); await selection;
        Assert.Equal(1, f.RegionSelector.Cleanups); Assert.False(f.Vm.IsBusy); Assert.Null(f.Video.LastConfiguration); Assert.Equal(0, f.Regions.Saves);
    });
    [Theory] [InlineData("Application Window")] [InlineData("Custom Region")]
    public Task ScreenSourceCommandsUseExistingSessionAndLockPreferences(string selection) => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var f = new Fixture(); f.Vm.SelectedScreenSource = selection;
        Assert.False(f.Vm.StartCommand.CanExecute(null));
        var window = new ApplicationWindowDescriptor("fixture", 12345, 123, 456, "Fixture", "Fixture", 640, 480);
        f.Vm.SelectedApplicationWindow = window;
        if (selection == "Application Window") f.Vm.SelectedMonitor = null; // Window capture does not require a monitor selection.
        f.Vm.SelectedRegion = new("qa-source", 20, 30, 320, 240, 1920, 1080);
        Assert.True(f.Vm.StartCommand.CanExecute(null));
        Assert.Equal(selection == "Application Window" ? new VideoSize(640, 480) : new VideoSize(320, 240), f.Vm.SelectedScreenSize);
        await f.Vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(selection == "Application Window" ? VideoSourceKind.Window : VideoSourceKind.Region, f.Video.LastConfiguration!.Source.Kind);
        Assert.False(f.Vm.CanEditPreferences); Assert.False(f.Vm.SelectRegionCommand.CanExecute(null)); Assert.False(f.Vm.RefreshWindowsCommand.CanExecute(null));
        await f.Vm.PauseCommand.ExecuteAsync(null); Assert.Equal(VideoRecordingState.Paused, f.Vm.Status);
        await f.Vm.ResumeCommand.ExecuteAsync(null); Assert.Equal(VideoRecordingState.Recording, f.Vm.Status);
        await f.Vm.StopCommand.ExecuteAsync(null); Assert.True(f.Vm.CanEditPreferences);
    });
    [Fact] public Task MonitorChangesInvalidateRegionAndWebcamModeHidesScreenControls() => DesktopRecordingCommandTests.Sta(() =>
    {
        using var f = new Fixture(); f.Vm.SelectedScreenSource = "Custom Region";
        f.Vm.SelectedRegion = new("qa-source", 20, 30, 320, 240, 1920, 1080);
        Assert.True(f.Vm.StartCommand.CanExecute(null));
        f.Vm.SelectedMonitor = new("other", "Other", 1920, 1080, false);
        Assert.Null(f.Vm.SelectedScreenOptions); Assert.False(f.Vm.StartCommand.CanExecute(null));
        f.Vm.SelectedMode = "Webcam Only";
        Assert.False(f.Vm.IsScreen); Assert.False(f.Vm.UsesMonitor); Assert.False(f.Vm.IsRegionSource); Assert.False(f.Vm.IsWindowSource);
        return Task.CompletedTask;
    });
    [Fact] public Task VideoCountdownCancellationPreventsCaptureAndReleasesBusyState() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var countdown = new CaptureFlow.Application.Desktop.RecordingCountdown(timeProvider: new CountdownToolbarTests.Clock());
        using var f = new Fixture(countdown); f.Vm.SelectedCountdown = "3 seconds";
        var start = f.Vm.StartCommand.ExecuteAsync(null); Assert.True(f.Vm.IsCountingDown); Assert.Null(f.Video.LastConfiguration);
        await f.Vm.CancelCommand.ExecuteAsync(null); await start;
        Assert.Null(f.Video.LastConfiguration); Assert.False(f.Vm.IsBusy); Assert.Equal(VideoRecordingState.Cancelled, f.Vm.Status);
    });
    [Theory] [InlineData("Low")] [InlineData("Standard")] [InlineData("High")] [InlineData("Ultra")] [InlineData("Custom")]
    public Task UiCommandsSupplyQualityPreferencesToExistingRecordingService(string preset) => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var f = new Fixture(); f.Vm.SelectedQuality = preset; f.Vm.SelectedEncoder = "Software"; f.Vm.SelectedFrameRate = "24 FPS";
        await f.Vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(Enum.Parse<VideoQualityPreset>(preset), f.Video.LastConfiguration!.QualityPreferences!.Preset);
        Assert.Equal(24, f.Video.LastConfiguration.EffectiveFramesPerSecond); Assert.False(f.Vm.CanEditQuality);
        Assert.False(f.Vm.SaveQualitySettingsCommand.CanExecute(null)); Assert.False(f.Vm.CheckEncodersCommand.CanExecute(null));
        await f.Vm.StopCommand.ExecuteAsync(null); Assert.True(f.Vm.CanEditQuality);
    });
    [Theory] [InlineData("bad")] [InlineData("499")] [InlineData("100001")] [InlineData("999999999999999999999999")]
    public Task InvalidBitrateCannotStartEvenThroughSharedRecordingCommand(string text) => DesktopRecordingCommandTests.Sta(() =>
    {
        using var f = new Fixture(); f.Vm.SelectedQuality = "Custom"; f.Vm.CustomBitrateText = text;
        Assert.False(f.Vm.StartCommand.CanExecute(null)); Assert.NotNull(f.Vm.QualitySummary); Assert.Null(f.Video.LastConfiguration);
        return Task.CompletedTask;
    });
    [Fact] public Task FourKSourceRestrictionDisablesStartWithClearExplanation() => DesktopRecordingCommandTests.Sta(() =>
    {
        using var f = new Fixture(); f.Vm.SelectedResolution = "4K";
        Assert.False(f.Vm.StartCommand.CanExecute(null)); Assert.Contains("4K output requires", f.Vm.QualitySummary); return Task.CompletedTask;
    });
    [Fact] public Task VerifiedHardwareCanBeSelectedAndChangedOutputInvalidatesVerification() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var f = new Fixture(); await f.Vm.CheckEncodersCommand.ExecuteAsync(null);
        Assert.Contains("NVIDIA NVENC", f.Vm.EncoderOptions); f.Vm.SelectedEncoder = "NVIDIA NVENC";
        Assert.True(f.Vm.StartCommand.CanExecute(null));
        f.Vm.SelectedFrameRate = "60 FPS"; Assert.False(f.Vm.StartCommand.CanExecute(null));
        await f.Vm.CheckEncodersCommand.ExecuteAsync(null); Assert.True(f.Vm.StartCommand.CanExecute(null));
    });
    [Fact] public Task SavedHardwareSelectionRemainsUnverifiedAfterRestart() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var f = new Fixture(); f.Store.Preferences = new(Encoder: VideoEncoderChoice.Nvenc);
        await f.Vm.LoadQualityPreferencesAsync(); Assert.Equal("NVIDIA NVENC", f.Vm.SelectedEncoder);
        Assert.False(f.Vm.StartCommand.CanExecute(null));
        await f.Vm.CheckEncodersCommand.ExecuteAsync(null); Assert.True(f.Vm.StartCommand.CanExecute(null));
    });
    [Fact] public Task SaveAndLoadRestoreQualityWithoutChangingAudioSelections() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var f = new Fixture(); f.Vm.SelectedQuality = "Ultra"; f.Vm.SelectedFrameRate = "60 FPS"; f.Vm.SelectedResolution = "1080p";
        await f.Vm.SaveQualitySettingsCommand.ExecuteAsync(null); f.Vm.SelectedQuality = "Low"; await f.Vm.LoadQualityPreferencesAsync();
        Assert.Equal("Ultra", f.Vm.SelectedQuality); Assert.Equal("60 FPS", f.Vm.SelectedFrameRate); Assert.False(f.Vm.MicrophoneEnabled); Assert.False(f.Vm.SystemAudioEnabled);
    });
    [Fact] public Task ShutdownCancelsAndAwaitsOwnedEncoderProbe() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var f = new Fixture(); f.Capabilities.Block = true;
        var probe = f.Vm.CheckEncodersCommand.ExecuteAsync(null); Assert.True(f.Vm.IsCheckingEncoders);
        Assert.False(f.Vm.StartCommand.CanExecute(null));
        await f.Vm.StopQualityOperationsAsync(); await probe;
        Assert.Equal(1, f.Capabilities.Cleanups); Assert.False(f.Vm.IsCheckingEncoders);
    });
    private sealed class Fixture : IDisposable
    {
        public ScreenRecorderViewModel Vm { get; }
        public Video Video { get; } = new(); public Store Store { get; } = new(); public Capabilities Capabilities { get; } = new();
        public Windows Windows { get; } = new(); public RegionSelector RegionSelector { get; } = new(); public Regions Regions { get; } = new();
        public MouseStore Mouse { get; } = new();
        public Fixture(CaptureFlow.Core.Desktop.IRecordingCountdown? countdown = null, ICameraCaptureBackend? cameraBackend = null)
        {
            var cameras = new CameraPreviewService(cameraBackend ?? new Camera()); var layouts = new Layouts();
            Vm = new(Video, new Audio(), cameras, new CombinedVideoPreviewService(new Monitors(), cameras, new SingleSourceVideoCompositor(), layouts), layouts, Store, Capabilities, countdown, windowDiscovery: Windows, regionSelection: RegionSelector, regionStore: Regions, mouseStore: Mouse)
            { SelectedMonitor = new("qa-source", "Fixture", 1920, 1080, true), MicrophoneEnabled = false, SystemAudioEnabled = false, OutputDirectory = "unused", SelectedCountdown = "Off" };
        }
        public void Dispose() => Vm.Dispose();
    }
    private sealed class Windows : IApplicationWindowDiscovery
    {
        public IReadOnlyList<ApplicationWindowDescriptor> Items = [new("fixture", 12345, 123, 456, "Fixture", "Fixture", 640, 480)]; public Exception? Failure;
        public Task<IReadOnlyList<ApplicationWindowDescriptor>> GetWindowsAsync(CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); if (Failure is not null) throw Failure; return Task.FromResult(Items); }
    }
    private sealed class RegionSelector : IRegionSelection
    {
        public ScreenRegion? Result; public bool Block; public int Cleanups;
        public async Task<ScreenRegion?> SelectAsync(MonitorDescriptor monitor, ScreenRegion? previous, CancellationToken token)
        { try { if (Block) await Task.Delay(Timeout.Infinite, token); return Result; } finally { Cleanups++; } }
    }
    private sealed class Regions : IRegionPreferencesStore
    {
        public ScreenRegion? Saved; public int Saves;
        public Task<ScreenRegion?> LoadAsync(CancellationToken token = default) => Task.FromResult(Saved);
        public Task SaveAsync(ScreenRegion region, CancellationToken token = default) { Saved = region; Saves++; return Task.CompletedTask; }
    }
    private sealed class Store : IVideoQualityPreferencesStore
    {
        public VideoQualityPreferences Preferences = new();
        public Task<VideoQualityPreferences> LoadAsync(CancellationToken token = default) => Task.FromResult(Preferences);
        public Task SaveAsync(VideoQualityPreferences preferences, CancellationToken token = default) { Preferences = preferences; return Task.CompletedTask; }
    }
    private sealed class MouseStore : IMouseEffectsPreferencesStore
    {
        public MouseEffectsPreferences Preferences = new(); public Exception? Failure;
        public Task<MouseEffectsPreferences> LoadAsync(CancellationToken token = default) => Task.FromResult(Preferences);
        public Task SaveAsync(MouseEffectsPreferences preferences, CancellationToken token = default) { if (Failure is not null) throw Failure; Preferences = preferences; return Task.CompletedTask; }
    }
    private sealed class Capabilities : IVideoEncoderCapabilities
    {
        public bool Block; public int Cleanups;
        public async Task<IReadOnlyList<VideoEncoderCapability>> ProbeAsync(string executable, VideoSize size, int fps, int bitrate, CancellationToken token)
        {
            try { if (Block) await Task.Delay(Timeout.Infinite, token); return [new(VideoEncoderChoice.Software, true, "test fixture"), new(VideoEncoderChoice.Nvenc, true, "test fixture")]; }
            finally { Cleanups++; }
        }
    }
    private sealed class Video : IVideoRecordingService
    {
        public event EventHandler? StateChanged;
        public VideoRecordingState State { get; private set; } public TimeSpan Duration => TimeSpan.Zero;
        public string? SavedFilePath => null; public string? WorkDirectory => null; public string? ErrorMessage => null; public VideoRecordingConfiguration? LastConfiguration;
        private Task Set(VideoRecordingState state) { State = state; StateChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
        public Task StartAsync(VideoRecordingConfiguration config, CancellationToken token = default) { LastConfiguration = config; config.Validate(); return Set(VideoRecordingState.Recording); }
        public Task PauseAsync() => Set(VideoRecordingState.Paused); public Task ResumeAsync() => Set(VideoRecordingState.Recording);
        public Task StopAsync(CancellationToken token = default) => Set(VideoRecordingState.Completed); public Task CancelAsync() => Set(VideoRecordingState.Cancelled);
        public Task<IReadOnlyList<MonitorDescriptor>> GetMonitorsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<MonitorDescriptor>>([]);
        public (float, bool, float, bool) LastVolume;
        public void SetAudioVolume(float microphone, bool microphoneMuted, float system, bool systemMuted) { LastVolume = (microphone, microphoneMuted, system, systemMuted); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Audio : IAudioCaptureBackend
    {
        public Task<IReadOnlyList<MicrophoneDevice>> GetDevicesAsync() => Task.FromResult<IReadOnlyList<MicrophoneDevice>>([]);
        public Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync() => Task.FromResult<IReadOnlyList<AudioOutputDevice>>([]);
        public Task<IAudioCaptureSession> CreateSessionAsync(AudioRecordingOptions options) => throw new NotSupportedException("No capture in quality UI tests.");
    }
    private sealed class Camera : ICameraCaptureBackend
    {
        public Task<IReadOnlyList<CameraDevice>> GetDevicesAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<CameraDevice>>([]);
        public Task<ICameraCaptureSession> OpenAsync(string deviceId, CancellationToken token) => throw new NotSupportedException("No camera in quality UI tests.");
    }
    private sealed class ControlledCamera : ICameraCaptureBackend
    {
        public int Opens, Disposals;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<CameraDevice>> GetDevicesAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<CameraDevice>>([]);
        public async Task<ICameraCaptureSession> OpenAsync(string id, CancellationToken token) { Opens++; Entered.TrySetResult(); await Released.Task; return new Session(this); }
        private sealed class Session(ControlledCamera owner) : ICameraCaptureSession
        {
            public event EventHandler<Exception>? Failed { add { } remove { } }
            public VideoSize Size => new(16, 16); public IReadOnlyList<CameraFormat> SupportedFormats => [new("fixture", 16, 16, 30, 1, "BGRA")];
            public Task ConfigureAsync(CameraFormat format, CancellationToken token) => Task.CompletedTask;
            public Task StartAsync(CancellationToken token) => Task.CompletedTask;
            public ValueTask<VideoFrame> ReadFrameAsync(CancellationToken token) => throw new InvalidOperationException("No frame capture in mode-switch tests.");
            public ValueTask DisposeAsync() { owner.Disposals++; return ValueTask.CompletedTask; }
        }
    }
    private sealed class Monitors : IMonitorCaptureBackend
    {
        public Task<IReadOnlyList<MonitorDescriptor>> GetMonitorsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<MonitorDescriptor>>([]);
        public Task<IVideoFrameSource> CreateAsync(VideoSourceOptions options, CancellationToken token) => throw new NotSupportedException("No screen capture in quality UI tests.");
    }
    private sealed class Layouts : IOverlayLayoutStore { public OverlayLayout Current { get; set; } = new(); }
}
