using CaptureFlow.Application.Desktop;
using CaptureFlow.Core.Desktop;
using CaptureFlow.Infrastructure.Desktop;
namespace CaptureFlow.Tests;

public class GlobalShortcutTests
{
    [Fact] public void DefaultsMatchRequiredActions()
    {
        var p = ShortcutPreferences.Defaults; p.Validate();
        Assert.True(p.Enabled); Assert.False(p.MinimizeToTray);
        Assert.Equal(new[] { "Ctrl+Alt+R", "Ctrl+Alt+P", "Ctrl+Alt+M", "Ctrl+Alt+C" }, p.Bindings.Select(b => b.Display));
    }
    [Theory]
    [InlineData("ctrl+alt+r", "Ctrl+Alt+R")]
    [InlineData("Ctrl+Shift+F24", "Ctrl+Shift+F24")]
    [InlineData("Win+1", "Win+1")]
    [InlineData("Alt+F1", "Alt+F1")]
    public void ParsesCustomizedAssignments(string text, string display) => Assert.Equal(display, ShortcutBinding.Parse(ShortcutAction.StartStop, text).Display);
    [Theory]
    [InlineData("")] [InlineData("R")] [InlineData("Ctrl+F25")] [InlineData("Super+R")] [InlineData("Ctrl+Enter")] [InlineData("Ctrl+!")]
    public void RejectsUnsupportedKeys(string text) => Assert.Throws<ArgumentException>(() => ShortcutBinding.Parse(ShortcutAction.StartStop, text));
    [Fact] public void DuplicateCombinationsRejectedBeforeTouchingRegistrations()
    {
        var fake = new Registrar(); using var registration = new ShortcutRegistration(fake);
        registration.Apply(ShortcutPreferences.Defaults);
        var p = ShortcutPreferences.Defaults; p.Bindings[1] = p.Bindings[0] with { Action = ShortcutAction.PauseResume };
        Assert.Throws<ArgumentException>(() => registration.Apply(p)); Assert.Equal(4, fake.Active.Count);
    }
    [Fact] public void MissingActionRejected()
    {
        var p = ShortcutPreferences.Defaults;
        Assert.Throws<ArgumentException>(() => (p with { Bindings = p.Bindings.Take(3).ToArray() }).Validate());
    }
    [Fact] public void ConflictRollsBackAllPartialRegistrations()
    {
        var fake = new Registrar { Fail = ShortcutAction.ToggleMicrophone }; using var registration = new ShortcutRegistration(fake);
        var ex = Assert.Throws<InvalidOperationException>(() => registration.Apply(ShortcutPreferences.Defaults));
        Assert.Contains("conflict", ex.Message); Assert.Empty(fake.Active);
    }
    [Fact] public void DisableAndDisposeReleaseAllResourcesIdempotently()
    {
        var fake = new Registrar(); var registration = new ShortcutRegistration(fake);
        registration.Apply(ShortcutPreferences.Defaults); Assert.Equal(4, fake.Active.Count);
        registration.Apply(ShortcutPreferences.Defaults with { Enabled = false }); Assert.Empty(fake.Active);
        registration.Apply(ShortcutPreferences.Defaults); registration.Dispose(); registration.Dispose();
        Assert.Empty(fake.Active); Assert.Equal(1, fake.Disposals);
        Assert.Throws<ObjectDisposedException>(() => registration.Apply(ShortcutPreferences.Defaults));
    }
    [Fact] public async Task PreferencesPersistCustomizedBindingsAndTrayBehavior()
    {
        using var f = new PreferenceFixture();
        Assert.Equal("Ctrl+Alt+R", (await f.Store.LoadAsync()).Bindings[0].Display);
        var p = ShortcutPreferences.Defaults with { Enabled = false, MinimizeToTray = true };
        p.Bindings[0] = ShortcutBinding.Parse(ShortcutAction.StartStop, "Ctrl+Shift+F10");
        await f.Store.SaveAsync(p); var restored = await new JsonShortcutPreferencesStore(f.Path).LoadAsync();
        Assert.False(restored.Enabled); Assert.True(restored.MinimizeToTray); Assert.Equal(p.Bindings, restored.Bindings);
        Assert.Single(Directory.GetFiles(f.Folder));
    }
    [Fact] public async Task InvalidPreferencesDoNotOverwriteExistingSettings()
    {
        using var f = new PreferenceFixture(); await f.Store.SaveAsync(ShortcutPreferences.Defaults);
        var original = await File.ReadAllTextAsync(f.Path);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Store.SaveAsync(ShortcutPreferences.Defaults with { Bindings = [] }));
        Assert.Equal(original, await File.ReadAllTextAsync(f.Path));
    }
    [Fact] public async Task CancelledSavePreservesPreviousPreferencesAndCleansTemporaryFile()
    {
        using var f = new PreferenceFixture(); await f.Store.SaveAsync(ShortcutPreferences.Defaults);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Store.SaveAsync(ShortcutPreferences.Defaults with { Enabled = false }, new CancellationToken(true)));
        Assert.True((await f.Store.LoadAsync()).Enabled); Assert.Single(Directory.GetFiles(f.Folder));
    }
    [Fact] public async Task CorruptPreferencesAreReported()
    {
        using var f = new PreferenceFixture(); await File.WriteAllTextAsync(f.Path, "{");
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => f.Store.LoadAsync());
    }
    [Theory]
    [InlineData(false, false, true, false)] [InlineData(true, false, true, true)]
    [InlineData(true, true, true, false)] [InlineData(true, false, false, false)]
    public void CloseBehaviorHonorsPreferenceExplicitExitAndTrayAvailability(bool setting, bool exit, bool tray, bool hide)
        => Assert.Equal(hide, WindowClosePolicy.HideInsteadOfClose(setting, exit, tray));
    [Theory] [InlineData(false, false, false)] [InlineData(true, false, true)] [InlineData(false, true, true)]
    public void ShutdownConfirmsActiveOrPendingWork(bool active, bool busy, bool confirmation)
        => Assert.Equal(confirmation, WindowClosePolicy.NeedsConfirmation(active, busy));
    [Fact] public async Task TrayAndShortcutActionsUseSameSelectedTarget()
    {
        var video = new Target(); var audio = new Target(); var router = new RecordingCommandRouter(video, audio) { PreferAudio = true };
        await router.RouteAsync(RecordingAction.StartStop); await router.RouteAsync(RecordingAction.PauseResume);
        await router.RouteAsync(RecordingAction.PauseResume); await router.RouteAsync(RecordingAction.ToggleMicrophone); await router.RouteAsync(RecordingAction.Stop);
        Assert.Equal(new[] { RecordingAction.StartStop, RecordingAction.PauseResume, RecordingAction.PauseResume, RecordingAction.ToggleMicrophone, RecordingAction.Stop }, audio.Calls);
        Assert.False(audio.Active); Assert.False(audio.Paused); Assert.True(audio.Muted); Assert.Empty(video.Calls);
    }
    [Fact] public async Task ActiveRecorderTakesPriorityOverLastSelectedPage()
    {
        var video = new Target { Active = true }; var audio = new Target(); var router = new RecordingCommandRouter(video, audio) { PreferAudio = true };
        await router.RouteAsync(RecordingAction.Stop); Assert.False(video.Active); Assert.Empty(audio.Calls);
    }
    [Fact] public async Task BusyRecorderPreventsOtherRecorderStarting()
    {
        var router = new RecordingCommandRouter(new Target { Busy = true }, new Target()) { PreferAudio = true };
        Assert.False(await router.RouteAsync(RecordingAction.Start));
    }
    [Fact] public async Task RapidRepeatedActionsAreDiscardedInsteadOfQueued()
    {
        var wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = new Target { Wait = wait.Task }; var router = new RecordingCommandRouter(target, new Target());
        var first = router.RouteAsync(RecordingAction.Start);
        Assert.False(await router.RouteAsync(RecordingAction.StartStop)); Assert.Single(target.Calls);
        wait.SetResult(); Assert.True(await first);
    }
    [Fact] public async Task FailedCommandReleasesGuardForNextAction()
    {
        var target = new Target { Error = true }; var router = new RecordingCommandRouter(target, new Target());
        await Assert.ThrowsAsync<InvalidOperationException>(() => router.RouteAsync(RecordingAction.Start));
        target.Error = false; Assert.True(await router.RouteAsync(RecordingAction.Start));
    }
    [Fact] public async Task GracefulShutdownWaitsForInFlightWorkFinalizesBothAndRejectsNewActions()
    {
        var wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var video = new Target { Wait = wait.Task }; var audio = new Target { Active = true };
        var router = new RecordingCommandRouter(video, audio) { PreferAudio = false };
        // Active audio has priority: route that target's start/stop and let it finish first.
        audio.Wait = wait.Task; var start = router.RouteAsync(RecordingAction.StartStop); var shutdown = router.FinalizeAsync();
        Assert.False(shutdown.IsCompleted); Assert.False(await router.RouteAsync(RecordingAction.Start));
        wait.SetResult(); await start; await shutdown;
        Assert.Equal(1, video.Finalizations); Assert.Equal(1, audio.Finalizations); Assert.False(audio.Active);
    }
    [Fact] public async Task ShutdownFailureStillFinalizesOtherRecorder()
    {
        var video = new Target { Error = true }; var audio = new Target { Active = true }; var router = new RecordingCommandRouter(video, audio);
        await Assert.ThrowsAsync<AggregateException>(() => router.FinalizeAsync()); Assert.Equal(1, audio.Finalizations); Assert.False(audio.Active);
    }
    private sealed class Registrar : IGlobalShortcutRegistrar
    {
        public HashSet<ShortcutAction> Active = []; public ShortcutAction? Fail; public int Disposals;
        public void Register(ShortcutBinding binding) { if (binding.Action == Fail) throw new InvalidOperationException("shortcut conflict"); Active.Add(binding.Action); }
        public void Unregister(ShortcutAction action) => Active.Remove(action);
        public void Dispose() => Disposals++;
    }
    private sealed class Target : IRecordingCommandTarget
    {
        public bool Active { get; set; } public bool Busy { get; set; } public bool Paused, Muted, Error; public Task? Wait; public int Finalizations;
        public List<RecordingAction> Calls = [];
        public async Task ExecuteAsync(RecordingAction action)
        {
            Calls.Add(action); if (Error) throw new InvalidOperationException("invalid configuration"); if (Wait is not null) await Wait;
            switch (action)
            {
                case RecordingAction.Start: Active = true; break;
                case RecordingAction.StartStop: Active = !Active; break;
                case RecordingAction.Stop: Active = false; Paused = false; break;
                case RecordingAction.PauseResume: Paused = !Paused; break;
                case RecordingAction.ToggleMicrophone: Muted = !Muted; break;
            }
        }
        public Task FinalizeAsync() { Finalizations++; if (Error) throw new InvalidOperationException("finalization failed"); Active = false; return Task.CompletedTask; }
    }
    private sealed class PreferenceFixture : IDisposable
    {
        public string Folder { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CaptureFlow-shortcut-tests-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Folder, "shortcuts.json");
        public JsonShortcutPreferencesStore Store => new(Path);
        public PreferenceFixture() => Directory.CreateDirectory(Folder);
        public void Dispose() => Directory.Delete(Folder, true);
    }
}
