using System.Buffers;
using CaptureFlow.Application.Video;
using CaptureFlow.Core.Audio;
using CaptureFlow.Core.Video;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Infrastructure.Video;

namespace CaptureFlow.Tests;

public class WebcamStartAvailabilityTests
{
    [Fact] public void MainPreviewToolbarExposesCameraPrerequisiteOutsideResponsiveInspector()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CaptureFlow.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var xml = System.Xml.Linq.XDocument.Load(Path.Combine(directory.FullName, "src", "CaptureFlow.Desktop", "Views", "ScreenRecorderPage.xaml"));
        System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var preview = Assert.Single(xml.Descendants(), e => (string?)e.Attribute(x + "Name") == "StudioPreview");
        foreach (var command in new[] { "StartStudioPreviewCommand", "StopStudioPreviewCommand" })
        {
            var button = Assert.Single(preview.Descendants(), e => (string?)e.Attribute("Command") == "{Binding " + command + "}");
            Assert.DoesNotContain(button.Ancestors(), e => ((string?)e.Attribute("Visibility"))?.Contains("IsScreenOnly") == true);
        }
    }
    [Theory]
    [InlineData("Webcam Only")]
    [InlineData("Screen + Webcam")]
    public Task MainToolbarStartsRequiredPreviewAndNotifiesRecordingAvailability(string mode) => DesktopRecordingCommandTests.Sta(async () =>
    {
        await using var f = new Fixture(); await f.Select(mode);
        var changes = 0; f.Vm.StartCommand.CanExecuteChanged += (_, _) => changes++;
        Assert.False(f.Vm.StartCommand.CanExecute(null));
        Assert.False(f.Vm.StartScreenPreviewCommand.CanExecute(null));
        Assert.True(f.Vm.StartStudioPreviewCommand.CanExecute(null));
        await f.Vm.StartStudioPreviewCommand.ExecuteAsync(null);
        Assert.True(f.Vm.StartCommand.CanExecute(null)); Assert.True(changes > 0);
        using var frame = mode == "Webcam Only" ? await f.Camera.ReadPreviewFrameAsync() : await f.Combined.ReadPreviewFrameAsync();
        Assert.Equal(new VideoSize(16, 16), frame.Size);
        Assert.Equal(0, f.Video.Starts); // Only generated preview frames; never a physical recorder.
        await f.Vm.StopStudioPreviewCommand.ExecuteAsync(null);
        Assert.False(f.Vm.StartCommand.CanExecute(null)); Assert.Null(f.Camera.DeviceId);
        Assert.Equal(f.Backend.Opens, f.Backend.Disposals);
    });

    [Fact] public Task CombinedModeStillRequiresValidScreenSource() => DesktopRecordingCommandTests.Sta(async () =>
    {
        await using var f = new Fixture(); await f.Select("Screen + Webcam");
        f.Vm.SelectedMonitor = null;
        Assert.False(f.Vm.StartStudioPreviewCommand.CanExecute(null)); Assert.False(f.Vm.StartCommand.CanExecute(null));
        f.Vm.SelectedMonitor = new("fixture", "Generated screen", 16, 16, true);
        Assert.True(f.Vm.StartStudioPreviewCommand.CanExecute(null));
        await f.Vm.StartStudioPreviewCommand.ExecuteAsync(null); Assert.True(f.Vm.StartCommand.CanExecute(null));
    });

    [Theory] [InlineData("Webcam Only")] [InlineData("Screen + Webcam")]
    public Task NoCameraNeverEnablesPreviewOrRecording(string mode) => DesktopRecordingCommandTests.Sta(async () =>
    {
        await using var f = new Fixture(); f.Vm.SelectedMode = mode; await f.Vm.CameraTransition;
        Assert.False(f.Vm.StartStudioPreviewCommand.CanExecute(null)); Assert.False(f.Vm.StartCommand.CanExecute(null));
        Assert.Equal(0, f.Backend.Opens); Assert.True(f.Vm.HasNoCameras);
    });

    [Theory] [InlineData(true)] [InlineData(false)]
    public Task InitializationErrorsStayDisabledAndRecoverAfterReload(bool denied) => DesktopRecordingCommandTests.Sta(async () =>
    {
        await using var f = new Fixture();
        f.Backend.Error = CameraErrors.Describe(denied ? new UnauthorizedAccessException("fixture denied") : new IOException("fixture open failed"), false);
        await f.Select("Webcam Only");
        Assert.False(f.Vm.StartCommand.CanExecute(null)); Assert.False(f.Vm.StartStudioPreviewCommand.CanExecute(null));
        Assert.Contains(denied ? "Camera access denied" : "Cannot open camera", f.Vm.CameraMessage);
        f.Backend.Error = null; await f.Vm.LoadCameraCommand.ExecuteAsync(null);
        Assert.True(f.Vm.StartStudioPreviewCommand.CanExecute(null));
        await f.Vm.StartStudioPreviewCommand.ExecuteAsync(null); Assert.True(f.Vm.StartCommand.CanExecute(null));
    });

    [Fact] public Task CameraSelectionInvalidatesOldPreviewAndRecoveryNotifiesToolbar() => DesktopRecordingCommandTests.Sta(async () =>
    {
        await using var f = new Fixture(); await f.Select("Webcam Only"); await f.Vm.StartStudioPreviewCommand.ExecuteAsync(null);
        var changes = 0; f.Vm.StartStudioPreviewCommand.CanExecuteChanged += (_, _) => changes++;
        f.Vm.SelectedCamera = new("second", "Second generated camera"); await f.Vm.CameraTransition;
        Assert.Equal("second", f.Camera.DeviceId); Assert.False(f.Vm.StartCommand.CanExecute(null)); Assert.True(changes > 0);
        await f.Vm.StartStudioPreviewCommand.ExecuteAsync(null); Assert.True(f.Vm.StartCommand.CanExecute(null));
    });

    [Fact] public Task RepeatedModeSwitchesRetainSelectionAndPublishModeBindings() => DesktopRecordingCommandTests.Sta(async () =>
    {
        await using var f = new Fixture(); var changed = new List<string?>(); f.Vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        for (var i = 0; i < 3; i++)
        {
            await f.Select("Webcam Only"); Assert.Contains(nameof(f.Vm.IsScreenOnly), changed); Assert.False(f.Vm.IsScreenOnly);
            await f.Vm.StartStudioPreviewCommand.ExecuteAsync(null); Assert.True(f.Vm.StartCommand.CanExecute(null));
            f.Vm.SelectedMode = "Screen + Webcam"; await f.Vm.CameraTransition;
            Assert.Equal("fixture", f.Vm.SelectedCamera!.Id); Assert.False(f.Vm.StartCommand.CanExecute(null));
            await f.Vm.StartStudioPreviewCommand.ExecuteAsync(null); Assert.True(f.Vm.StartCommand.CanExecute(null));
            f.Vm.SelectedMode = "Screen Only"; await f.Vm.CameraTransition;
            Assert.True(f.Vm.IsScreenOnly); Assert.True(f.Vm.StartCommand.CanExecute(null)); Assert.False(f.Camera.IsPreviewing);
        }
    });

    [Fact] public Task CameraFormatChangeRequiresMatchingPreviewBeforeRecording() => DesktopRecordingCommandTests.Sta(async () =>
    {
        await using var f = new Fixture(); await f.Select("Webcam Only"); await f.Vm.StartStudioPreviewCommand.ExecuteAsync(null);
        f.Vm.SelectedCameraFormat = new("different", 16, 16, 24, 1, "BGRA");
        Assert.False(f.Vm.StartCommand.CanExecute(null));
    });

    [Fact] public Task MainToolbarStillDispatchesExistingScreenPreviewAndStop() => DesktopRecordingCommandTests.Sta(async () =>
    {
        await using var f = new Fixture();
        Assert.True(f.Vm.StartStudioPreviewCommand.CanExecute(null));
        await f.Vm.StartStudioPreviewCommand.ExecuteAsync(null);
        Assert.Equal(f.Vm.SelectedScreenOptions, f.Screen.ActiveOptions); Assert.True(f.Vm.IsCameraPreviewing);
        await f.Vm.StopStudioPreviewCommand.ExecuteAsync(null); Assert.Null(f.Screen.ActiveOptions); Assert.False(f.Vm.IsCameraPreviewing);
        Assert.Equal(0, f.Backend.Opens); Assert.Equal(0, f.Video.Starts);
    });

    private sealed class Fixture : IAsyncDisposable
    {
        public Backend Backend { get; } = new();
        public CameraPreviewService Camera { get; }
        public CombinedVideoPreviewService Combined { get; }
        public ScreenPreviewService Screen { get; }
        public Video Video { get; } = new();
        public ScreenRecorderViewModel Vm { get; }
        public Fixture()
        {
            Camera = new(Backend); var layouts = new Layouts();
            Combined = new(new Screens(), Camera, new BgraVideoCompositor(), layouts); Screen = new(new Screens());
            Vm = new(Video, new Audio(), Camera, Combined, layouts, screenPreview: Screen)
            { SelectedMonitor = new("fixture", "Generated screen", 16, 16, true), MicrophoneEnabled = false, SystemAudioEnabled = false, SelectedCountdown = "Off" };
        }
        public async Task Select(string mode)
        {
            if (Vm.SelectedCamera is null) Vm.SelectedCamera = new("fixture", "Generated camera");
            Vm.SelectedMode = mode; await Vm.CameraTransition;
        }
        public async ValueTask DisposeAsync() { await Vm.StopCameraOperationsAsync(); Vm.Dispose(); await Screen.DisposeAsync(); await Combined.DisposeAsync(); await Camera.DisposeAsync(); }
    }
    private sealed class Backend : ICameraCaptureBackend
    {
        public int Opens, Disposals; public Exception? Error;
        public Task<IReadOnlyList<CameraDevice>> GetDevicesAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<CameraDevice>>([new("fixture", "Generated camera")]);
        public Task<ICameraCaptureSession> OpenAsync(string id, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (Error is not null) throw Error; Opens++; return Task.FromResult<ICameraCaptureSession>(new Session(this)); }
        private sealed class Session(Backend owner) : Frames, ICameraCaptureSession
        {
            public IReadOnlyList<CameraFormat> SupportedFormats => [new("fixture", 16, 16, 30, 1, "BGRA")];
            public Task ConfigureAsync(CameraFormat format, CancellationToken token) => Task.CompletedTask;
            public override ValueTask DisposeAsync() { owner.Disposals++; return ValueTask.CompletedTask; }
        }
    }
    private class Frames : IVideoFrameSource
    {
        public event EventHandler<Exception>? Failed { add { } remove { } }
        public VideoSize Size => new(16, 16);
        public Task StartAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public ValueTask<VideoFrame> ReadFrameAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var pixels = MemoryPool<byte>.Shared.Rent(1024); pixels.Memory.Span[..1024].Fill(127);
            return ValueTask.FromResult(new VideoFrame(pixels, Size, TimeSpan.Zero));
        }
        public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Screens : IMonitorCaptureBackend
    {
        public Task<IReadOnlyList<MonitorDescriptor>> GetMonitorsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<MonitorDescriptor>>([]);
        public Task<IVideoFrameSource> CreateAsync(VideoSourceOptions options, CancellationToken token) => Task.FromResult<IVideoFrameSource>(new Frames());
    }
    private sealed class Layouts : IOverlayLayoutStore { public OverlayLayout Current { get; set; } = new(); }
    private sealed class Audio : IAudioCaptureBackend
    {
        public Task<IReadOnlyList<MicrophoneDevice>> GetDevicesAsync() => Task.FromResult<IReadOnlyList<MicrophoneDevice>>([]);
        public Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync() => Task.FromResult<IReadOnlyList<AudioOutputDevice>>([]);
        public Task<IAudioCaptureSession> CreateSessionAsync(AudioRecordingOptions options) => throw new InvalidOperationException("Physical capture forbidden in these tests.");
    }
    private sealed class Video : IVideoRecordingService
    {
        public int Starts;
        public event EventHandler? StateChanged { add { } remove { } }
        public VideoRecordingState State => VideoRecordingState.Idle; public TimeSpan Duration => TimeSpan.Zero;
        public string? SavedFilePath => null; public string? WorkDirectory => null; public string? ErrorMessage => null;
        public Task StartAsync(VideoRecordingConfiguration config, CancellationToken token = default) { Starts++; throw new InvalidOperationException("Recording forbidden in these tests."); }
        public Task PauseAsync() => Task.CompletedTask; public Task ResumeAsync() => Task.CompletedTask; public Task StopAsync(CancellationToken token = default) => Task.CompletedTask; public Task CancelAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<MonitorDescriptor>> GetMonitorsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<MonitorDescriptor>>([]);
        public void SetAudioVolume(float microphone, bool microphoneMuted, float system, bool systemMuted) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
