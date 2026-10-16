using System.Windows;
using CaptureFlow.Core.Editing;
using CaptureFlow.Desktop.Views;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Tests;
public partial class EditorViewModelTests
{
    private static async Task<(EditorViewModel Vm, EditorTimeline Timeline)> InteractionFixture()
    {
        var backend = new Backend(); var vm = new EditorViewModel(backend, new Store()); await vm.ImportFileAsync(backend.Media.Path, true);
        vm.TrimIn = 2; vm.TrimOut = 8; vm.ClipSpeed = 2; vm.ClipStart = 1; vm.ApplyClipCommand.Execute(null); vm.Zoom = 100; vm.SnappingEnabled = false;
        var timeline = new EditorTimeline { DataContext = vm }; timeline.Measure(new Size(1000, 193)); timeline.Arrange(new Rect(0, 0, 1000, 193));
        return (vm, timeline);
    }
    [Theory] [InlineData(250, 300, 1.5, 2, 8)] [InlineData(101, 151, 1.5, 3, 8)] [InlineData(399, 349, 1, 2, 7)]
    public Task WpfCoordinateBodyAndEdgeRoutingCommitsOnce(double from, double to, double start, double input, double output) => DesktopRecordingCommandTests.Sta(async () =>
    {
        var (vm, timeline) = await InteractionFixture(); await using var owner = vm; var original = vm.Project;
        Assert.True(timeline.BeginInteraction(new(from, 45))); timeline.UpdateInteraction(new(to, 45)); Assert.Same(original, vm.Project); timeline.CompleteInteraction();
        var clip = vm.Project.Clips.Single(); Assert.Equal(start, clip.Start); Assert.Equal(input, clip.In); Assert.Equal(output, clip.Out);
        vm.UndoCommand.Execute(null); Assert.Same(original, vm.Project); vm.RedoCommand.Execute(null); Assert.Equal(clip, vm.Project.Clips.Single());
    });
    [Fact] public Task WpfCoordinateTrackMoveSnapSeekCancelAndLock() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var (vm, timeline) = await InteractionFixture(); await using var owner = vm; var original = vm.Project;
        timeline.BeginInteraction(new(250, 45)); timeline.UpdateInteraction(new(250, 101)); timeline.CompleteInteraction(); Assert.Equal(1, vm.SelectedClip!.TrackId); vm.UndoCommand.Execute(null);
        vm.SnappingEnabled = true; vm.Playhead = 3; timeline.BeginInteraction(new(250, 45)); timeline.UpdateInteraction(new(453, 45)); timeline.CompleteInteraction(); Assert.Equal(3, vm.SelectedClip!.Start); vm.UndoCommand.Execute(null);
        timeline.BeginInteraction(new(250, 45)); timeline.UpdateInteraction(new(350, 45)); timeline.CancelInteraction(); timeline.CompleteInteraction(); Assert.Same(original, vm.Project);
        timeline.BeginInteraction(new(167, 10)); timeline.UpdateInteraction(new(200, 10)); timeline.CompleteInteraction(); Assert.Equal(2, vm.Playhead);
        vm.ToggleTrackLockCommand.Execute("0"); var locked = vm.Project; timeline.BeginInteraction(new(250, 45)); timeline.UpdateInteraction(new(350, 45)); timeline.CompleteInteraction(); Assert.Same(locked, vm.Project); Assert.Contains("locked", vm.Message);
    });
    [Fact] public Task WpfCoordinateInvalidTargetsAndOverlapLeaveProjectUnchanged() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var (vm, timeline) = await InteractionFixture(); await using var owner = vm;
        vm.Session.Add(vm.Project.Media.Single().Id, 0, 5); var original = vm.Project;
        timeline.BeginInteraction(new(250, 45)); timeline.UpdateInteraction(new(450, 45)); timeline.CompleteInteraction(); Assert.Same(original, vm.Project); Assert.Contains("overlap", vm.Message);
        timeline.BeginInteraction(new(250, 45)); timeline.UpdateInteraction(new(250, 230)); timeline.CompleteInteraction(); Assert.Same(original, vm.Project); Assert.Contains("Drop within", vm.Message);
    });
    [Fact] public Task WpfCoordinateMarkersSelectActualKeysAndTransitionOwners() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var (vm, timeline) = await InteractionFixture(); await using var owner = vm; var id = vm.SelectedClip!.Id;
        var key = new EditorKeyframe(Guid.NewGuid(), AnimationProperty.Opacity, .5, .7); vm.Session.SetKeyframe(id, key);
        Assert.False(timeline.BeginInteraction(new(150, 72))); Assert.Equal(key, vm.SelectedKeyframe); Assert.Equal(.5, vm.KeyframeTime);
        vm.Session.SetTransition(id, new(TransitionType.FadeIn, .4, TransitionPlacement.Start)); Assert.False(timeline.BeginInteraction(new(120, 60))); Assert.Equal(TransitionType.FadeIn, vm.TransitionType); Assert.Equal(.4, vm.TransitionDuration);
        var incomingId = vm.Session.Add(vm.Project.Media.Single().Id, 0, 4); vm.Session.Replace(vm.Project.Clips.Last() with { In = 2, Out = 8, Speed = 2 }); vm.Session.SetTransition(incomingId, new(TransitionType.CrossDissolve, .5, TransitionPlacement.Start));
        Assert.False(timeline.BeginInteraction(new(375, 60))); Assert.Equal(incomingId, vm.SelectedClip!.Id); Assert.Equal(TransitionType.CrossDissolve, vm.TransitionType);
    });
    [Fact] public Task WpfFadeHandleUsesAnchorEvenIfKeyboardSelectionChanges() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var (vm, timeline) = await InteractionFixture(); await using var owner = vm; var clip = vm.SelectedClip!;
        var otherId = vm.Session.Add(vm.Project.Media.Single().Id, 1, 0); var other = vm.Project.Clips.Last();
        Assert.True(timeline.BeginInteraction(new(100, 31))); timeline.UpdateInteraction(new(150, 31)); vm.SelectedClip = other; timeline.CompleteInteraction();
        Assert.Equal(.5, vm.Project.Clips.Single(c => c.Id == clip.Id).FadeIn); Assert.Equal(0, vm.Project.Clips.Single(c => c.Id == otherId).FadeIn);
        vm.UndoCommand.Execute(null); Assert.Equal(0, vm.Project.Clips.Single(c => c.Id == clip.Id).FadeIn);
        timeline.BeginInteraction(new(100, 31)); timeline.UpdateInteraction(new(180, 31)); timeline.CancelInteraction(); Assert.Equal(0, vm.Project.Clips.Single(c => c.Id == clip.Id).FadeIn);
    });
    [Fact] public Task WpfLostCaptureCancelsInvalidGestureAndRestoresCursor() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var (vm, timeline) = await InteractionFixture(); await using var owner = vm; var original = vm.Project;
        timeline.BeginInteraction(new(250, 45)); timeline.UpdateInteraction(new(250, 230)); Assert.Equal(System.Windows.Input.Cursors.No, timeline.Cursor);
        timeline.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.LostMouseCaptureEvent });
        timeline.CompleteInteraction(); Assert.Same(original, vm.Project); Assert.Equal(System.Windows.Input.Cursors.Arrow, timeline.Cursor);
    });
}
