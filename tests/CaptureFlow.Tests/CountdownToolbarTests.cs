using CaptureFlow.Application.Desktop;
using CaptureFlow.Core.Desktop;
using CaptureFlow.Infrastructure.Desktop;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Tests;
public class CountdownToolbarTests
{
    [Theory] [InlineData(0)] [InlineData(3)] [InlineData(5)] [InlineData(10)]
    public async Task CountdownUsesExactMonotonicSecondsAndClosesBeforeStart(int seconds)
    {
        var clock = new Clock(); var presentation = new Presentation(); var countdown = new RecordingCountdown(presentation, clock);
        var run = countdown.RunAsync(new(seconds), default);
        for (var number = seconds; number > 0; number--)
        {
            Assert.Equal(number, countdown.Remaining); Assert.True(countdown.IsRunning); Assert.False(run.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(1)); await Until(() => run.IsCompleted || countdown.Remaining == number - 1);
            await Task.Delay(10); // Let the next monotonic deadline's timer register before advancing again.
        }
        await run; Assert.False(countdown.IsRunning); Assert.Equal(0, countdown.Remaining);
        Assert.Equal(Enumerable.Range(1, seconds).Reverse(), presentation.Numbers);
        Assert.Equal(seconds == 0 ? 0 : 1, presentation.Closures);
    }
    [Fact] public async Task CancelClosesOverlayAndNeverCompletesAsSuccessfulCountdown()
    {
        var presentation = new Presentation(); var countdown = new RecordingCountdown(presentation, new Clock());
        var run = countdown.RunAsync(new(10), default); countdown.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run); Assert.False(countdown.IsRunning); Assert.Equal(1, presentation.Closures);
    }
    [Fact] public async Task ShutdownWaitsForPresentationCleanupBeforeCompleting()
    {
        var presentation = new BlockingPresentation(); var countdown = new RecordingCountdown(presentation, new Clock());
        var run = countdown.RunAsync(new(3), default); var shutdown = countdown.CancelAndWaitAsync();
        await Until(() => presentation.Closing); Assert.False(shutdown.IsCompleted);
        presentation.Closed.SetResult(); await shutdown; await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.False(countdown.IsRunning);
    }
    [Fact] public async Task RepeatedStartsAreRejectedAndCancellationAllowsNextCountdown()
    {
        var countdown = new RecordingCountdown(timeProvider: new Clock()); var first = countdown.RunAsync(new(3), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => countdown.RunAsync(new(3), default));
        countdown.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await countdown.RunAsync(new(0), default);
    }
    [Fact] public async Task PresentationFailureStillClosesAndReleasesCountdown()
    {
        var presentation = new Presentation { Fail = true }; var countdown = new RecordingCountdown(presentation, new Clock());
        await Assert.ThrowsAsync<IOException>(() => countdown.RunAsync(new(3), default));
        Assert.Equal(1, presentation.Closures); Assert.False(countdown.IsRunning);
    }
    [Theory] [InlineData(1)] [InlineData(-1)] [InlineData(15)]
    public async Task UnsupportedCountdownCannotStart(int seconds) => await Assert.ThrowsAsync<ArgumentException>(() => new RecordingCountdown().RunAsync(new(seconds), default));
    [Theory] [InlineData(96, 410)] [InlineData(144, 615)] [InlineData(192, 820)]
    public void PlacementUsesMonitorDpiAndClampsToWorkArea(double dpi, double width)
    {
        var area = new DesktopWorkArea("right", 1920, -200, 2560, 1400, dpi);
        var r = ToolbarPlacement.Resolve(new("right", 99999, 99999), [area], 410, 152);
        Assert.Equal(width, r.Width); Assert.Equal(area.Left + area.Width - r.Width, r.Left); Assert.Equal(area.Top + area.Height - r.Height, r.Top);
    }
    [Fact] public void MissingMonitorRestoresOnPrimaryAndNegativeCoordinatesRemainValid()
    {
        DesktopWorkArea[] areas = [new("left", -1920, 0, 1920, 1040, Primary: true), new("right", 0, 0, 1920, 1040)];
        var r = ToolbarPlacement.Resolve(new("disconnected", 100, 30), areas, 410, 152);
        Assert.Equal(-1820, r.Left); Assert.Equal(30, r.Top);
        Assert.Equal(new("left", 100, 30), ToolbarPlacement.Capture(r, areas));
    }
    [Fact] public void PositionSurvivesDpiChangeAndMonitorRotation()
    {
        var anchor = ToolbarPlacement.Capture(new(300, 150, 615, 228), [new("monitor", 0, 0, 2560, 1400, 144)]);
        Assert.Equal(new("monitor", 200, 100), anchor);
        var resized = ToolbarPlacement.Resolve(anchor, [new("monitor", 0, 0, 1080, 1900, 192)], 410, 152);
        Assert.Equal(260, resized.Left); Assert.Equal(200, resized.Top); Assert.Equal(820, resized.Width);
    }
    [Fact] public void InvalidSavedPositionAndEmptyDesktopAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new ToolbarAnchor("m", double.NaN, 0).Validate());
        Assert.Throws<ArgumentException>(() => ToolbarPlacement.Resolve(null, [], 410, 152));
        Assert.Throws<ArgumentException>(() => ToolbarPlacement.Resolve(null, [new("m", 0, 0, 1920, 1080)], double.NaN, 152));
    }
    [Fact] public async Task PreferencesPersistCountdownVisibilityTopmostAndMonitorRelativePosition()
    {
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-toolbar-tests-" + Guid.NewGuid().ToString("N")); var path = Path.Combine(folder, "preferences.json");
        try
        {
            var store = new JsonRecordingExperienceStore(path); Assert.Equal(new RecordingExperiencePreferences(), await store.LoadAsync());
            var preferences = new RecordingExperiencePreferences(5, false, false, new("monitor", 20, 30));
            await store.SaveAsync(preferences); Assert.Equal(preferences, await new JsonRecordingExperienceStore(path).LoadAsync());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new(), new CancellationToken(true)));
            Assert.Equal(preferences, await store.LoadAsync()); Assert.Single(Directory.GetFiles(folder));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact] public Task ToolbarUsesProvidedSharedCommandsAndCancelsInsteadOfStoppingCountdown() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var actions = new List<RecordingAction>(); var cancellations = 0;
        var toolbar = new FloatingToolbarViewModel(action => { actions.Add(action); return Task.CompletedTask; }, () => cancellations++, () => { }) { CanPause = true, CanStop = true, CanMute = true };
        await toolbar.PauseResumeCommand.ExecuteAsync(null); await toolbar.ToggleMicrophoneCommand.ExecuteAsync(null); await toolbar.StopCommand.ExecuteAsync(null);
        toolbar.IsCountdown = true; await toolbar.StopCommand.ExecuteAsync(null);
        Assert.Equal(new[] { RecordingAction.PauseResume, RecordingAction.ToggleMicrophone, RecordingAction.Stop }, actions); Assert.Equal(1, cancellations);
        toolbar.Collapsed = true; Assert.Equal("Cancel", toolbar.StopText);
    });
    [Fact] public Task DisabledToolbarControlsNeverRoute() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var routes = 0; var toolbar = new FloatingToolbarViewModel(_ => { routes++; return Task.CompletedTask; }, () => { }, () => { });
        await toolbar.PauseResumeCommand.ExecuteAsync(null); await toolbar.StopCommand.ExecuteAsync(null); await toolbar.ToggleMicrophoneCommand.ExecuteAsync(null); Assert.Equal(0, routes);
    });
    internal static async Task Until(Func<bool> condition)
    { for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(5); Assert.True(condition()); }
    internal sealed class Presentation : ICountdownPresentation
    {
        public List<int> Numbers = []; public int Closures; public bool Fail;
        public void Show(CountdownRequest request, int remaining, Action cancel) { if (Fail) throw new IOException("presentation failed"); Numbers.Add(remaining); }
        public Task CloseAsync() { Closures++; return Task.CompletedTask; }
    }
    private sealed class BlockingPresentation : ICountdownPresentation
    {
        public bool Closing; public TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Show(CountdownRequest request, int remaining, Action cancel) { }
        public Task CloseAsync() { Closing = true; return Closed.Task; }
    }
    internal sealed class Clock : TimeProvider
    {
        private long ticks; private readonly List<Timer> timers = [];
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { var timer = new Timer(this, callback, state); timer.Change(dueTime, period); lock (timers) timers.Add(timer); return timer; }
        public void Advance(TimeSpan duration)
        { ticks += duration.Ticks; Timer[] copy; lock (timers) copy = timers.ToArray(); foreach (var timer in copy) timer.Fire(); }
        private sealed class Timer(Clock clock, TimerCallback callback, object? state) : ITimer
        {
            private long due; private bool disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) { due = clock.ticks + dueTime.Ticks; return !disposed; }
            public void Fire() { if (!disposed && due <= clock.ticks) { disposed = true; callback(state); } }
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
