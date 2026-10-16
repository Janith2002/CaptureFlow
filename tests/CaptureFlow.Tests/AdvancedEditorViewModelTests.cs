using CaptureFlow.Core.Editing;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Tests;
public partial class EditorViewModelTests
{
    [Fact] public Task AdvancedInspectorSelectionMoveDeleteUndoAndInvalidInput() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var backend = new Backend(); await using var vm = new EditorViewModel(backend, new Store()); await vm.ImportFileAsync(backend.Media.Path, true);
        vm.Playhead = 2; vm.AnimationProperty = AnimationProperty.Opacity; vm.KeyframeValue = .5; vm.AddKeyframeCommand.Execute(null);
        Assert.Equal(2, vm.SelectedKeyframe!.Time); Assert.True(vm.UpdateKeyframeCommand.CanExecute(null));
        vm.KeyframeTime = 3; vm.UpdateKeyframeCommand.Execute(null); Assert.Equal(3, vm.SelectedKeyframe!.Time);
        var valid = vm.Project; vm.KeyframeValue = 2; vm.UpdateKeyframeCommand.Execute(null); Assert.Same(valid, vm.Project); Assert.Contains("Invalid keyframe", vm.Message);
        vm.DeleteKeyframeCommand.Execute(null); Assert.Empty(vm.ClipKeyframes); vm.UndoCommand.Execute(null); Assert.Single(vm.ClipKeyframes); vm.RedoCommand.Execute(null); Assert.Empty(vm.ClipKeyframes);
        vm.TransitionType = TransitionType.FadeIn; vm.TransitionDuration = 1; vm.ApplyTransitionCommand.Execute(null); Assert.Single(vm.SelectedClip!.Transitions!);
        vm.DeleteTransitionCommand.Execute(null); Assert.Empty(vm.SelectedClip!.Transitions!); vm.UndoCommand.Execute(null); Assert.Single(vm.SelectedClip!.Transitions!);
        vm.Session.Add(backend.Media.Id, 1, 0); vm.SelectedClip = vm.Project.Clips.Last(); Assert.Equal(new[] { AnimationProperty.Volume }, vm.AnimationProperties); Assert.Equal(AnimationProperty.Volume, vm.AnimationProperty); Assert.Null(vm.SelectedKeyframe);
        vm.SetAudioFade(true, .5); Assert.Equal(.5, vm.SelectedClip!.FadeIn); vm.UndoCommand.Execute(null); Assert.Equal(0, vm.SelectedClip!.FadeIn);
    });
    [Fact] public Task AdvancedPreviewCancelledOnResetCannotPublishOldAnimation() => DesktopRecordingCommandTests.Sta(async () =>
    {
        var backend = new Backend { Controlled = true }; await using var vm = new EditorViewModel(backend, new Store()); await vm.ImportFileAsync(backend.Media.Path, true);
        vm.AnimationProperty = AnimationProperty.Scale; vm.KeyframeValue = .5; vm.AddKeyframeCommand.Execute(null); vm.TransitionDuration = 1; vm.ApplyTransitionCommand.Execute(null);
        var published = false; vm.PreviewRequested += _ => published = true; var pending = vm.PreviewCommand.ExecuteAsync(null); await backend.Entered.Task;
        vm.ResetProject(vm.Project with { Clips = [] }); backend.Release.SetResult(); await pending; Assert.False(published); Assert.Null(vm.PreviewPath); Assert.True(vm.PreviewStale);
    });
    [Fact] public Task AdvancedAutosaveRetainsAnimationTransitionsAndUndo() => DesktopRecordingCommandTests.Sta(async () =>
    {
        using var scratch = new RecoveryTests.Scratch(); var journal = new CaptureFlow.Infrastructure.Recovery.JsonRecoveryJournal(scratch.Path);
        var backend = new Backend(); var vm = new EditorViewModel(backend, new Store(), journal); await vm.ImportFileAsync(backend.Media.Path, true);
        vm.KeyframeValue = .5; vm.AddKeyframeCommand.Execute(null); vm.TransitionDuration = 1; vm.ApplyTransitionCommand.Execute(null); await vm.DisposeAsync();
        var candidate = Assert.Single(await journal.ScanAsync()); var snapshot = await journal.LoadEditorAsync(candidate.Record.Id);
        await using var restored = new EditorViewModel(backend, new Store(), journal); restored.RestoreAutosave(snapshot, candidate.Record.Id);
        Assert.Equal(2, restored.Project.Version); Assert.Single(restored.Project.Clips.Single().Keyframes!); Assert.Single(restored.Project.Clips.Single().Transitions!);
        restored.UndoCommand.Execute(null); Assert.Null(restored.Project.Clips.Single().Transitions); Assert.Single(restored.Project.Clips.Single().Keyframes!);
    });
}
