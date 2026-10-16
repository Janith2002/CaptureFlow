using System.Text.Json;
using CaptureFlow.Application.Editing;
using CaptureFlow.Core.Editing;
using CaptureFlow.Infrastructure.Editing;
namespace CaptureFlow.Tests;
public class AdvancedEditorTests
{
    private static EditorSession Fixture()
    { var s = new EditorSession(); var m = EditorTimelineTests.Media(); s.Import(m); s.Add(m.Id, 0, 0); return s; }
    private static EditorKeyframe Key(AnimationProperty p, double time, double value, KeyframeInterpolation mode = KeyframeInterpolation.Linear) => new(Guid.NewGuid(), p, time, value, mode);
    [Theory] [InlineData(-1, 0)] [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(2, .5)] [InlineData(3, 1)] [InlineData(10, 1)]
    public void LinearInterpolationExtendsEdges(double time, double expected)
    { var c = Fixture().Project.Clips.Single() with { Keyframes = [Key(AnimationProperty.Opacity, 1, 0), Key(AnimationProperty.Opacity, 3, 1)] }; Assert.Equal(expected, EditorAnimation.Evaluate(c, AnimationProperty.Opacity, time)); }
    [Theory] [InlineData(1, 0)] [InlineData(2.999, 0)] [InlineData(3, 1)]
    public void HoldChangesExactlyAtNextKey(double time, double expected)
    { var c = Fixture().Project.Clips.Single() with { Keyframes = [Key(AnimationProperty.Opacity, 1, 0, KeyframeInterpolation.Hold), Key(AnimationProperty.Opacity, 3, 1)] }; Assert.Equal(expected, EditorAnimation.Evaluate(c, AnimationProperty.Opacity, time)); }
    [Theory] [InlineData(-1)] [InlineData(0)] [InlineData(11)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidTransitionsAreAtomic(double duration)
    { var s = Fixture(); var before = s.Project; Assert.Throws<ArgumentException>(() => s.SetTransition(before.Clips.Single().Id, new(TransitionType.FadeIn, duration, TransitionPlacement.Start))); Assert.Same(before, s.Project); }
    [Theory] [InlineData(AnimationProperty.Opacity, -1, .5)] [InlineData(AnimationProperty.Opacity, 11, .5)] [InlineData(AnimationProperty.Opacity, 1, 2)] [InlineData(AnimationProperty.Volume, 1, -1)] [InlineData(AnimationProperty.Scale, 1, 0)] [InlineData(AnimationProperty.Rotation, 1, double.NaN)]
    public void InvalidKeysAreAtomic(AnimationProperty p, double time, double value)
    { var s = Fixture(); var before = s.Project; Assert.Throws<ArgumentException>(() => s.SetKeyframe(before.Clips.Single().Id, Key(p, time, value))); Assert.Same(before, s.Project); }
    [Fact] public void KeyAndTransitionOperationsUndoRedoAndLocks()
    {
        var s = Fixture(); var id = s.Project.Clips.Single().Id; var baseline = s.Project;
        var key = Key(AnimationProperty.Volume, 1, .2); s.SetKeyframe(id, key); Assert.Equal(2, s.Project.Version); var animated = s.Project;
        s.Undo(); Assert.Same(baseline, s.Project); s.Redo(); Assert.Same(animated, s.Project);
        s.SetKeyframe(id, key with { Time = 2 }); Assert.Equal(2, s.Project.Clips.Single().Keyframes!.Single().Time);
        s.DeleteKeyframe(id, key.Id); Assert.Empty(s.Project.Clips.Single().Keyframes!); s.Undo(); Assert.Single(s.Project.Clips.Single().Keyframes!);
        s.SetTransition(id, new(TransitionType.FadeToBlack, .5, TransitionPlacement.End)); var transition = s.Project;
        s.DeleteTransition(id, TransitionPlacement.End); s.Undo(); Assert.Same(transition, s.Project);
        s.Apply(s.Project with { Tracks = s.Project.Tracks.Select(t => t with { Locked = true }).ToArray() });
        Assert.Throws<InvalidOperationException>(() => s.DeleteKeyframe(id, key.Id)); Assert.Throws<InvalidOperationException>(() => s.DeleteTransition(id, TransitionPlacement.End));
    }
    [Fact] public void TrimSplitAndSpeedPreserveLocalEnvelope()
    {
        var s = Fixture(); var c = s.Project.Clips.Single(); s.SetKeyframe(c.Id, Key(AnimationProperty.Volume, 0, 0)); s.SetKeyframe(c.Id, Key(AnimationProperty.Volume, 10, 1));
        c = s.Project.Clips.Single(); s.Replace(c with { In = 2, Out = 8, Start = 2 }); var trim = s.Project.Clips.Single();
        Assert.Equal(.2, EditorAnimation.Evaluate(trim, AnimationProperty.Volume, 0), 9); Assert.Equal(.8, EditorAnimation.Evaluate(trim, AnimationProperty.Volume, 6), 9);
        var rightId = s.Split(c.Id, 5); var left = s.Project.Clips.Single(k => k.Id == c.Id); var right = s.Project.Clips.Single(k => k.Id == rightId);
        Assert.Equal(.5, EditorAnimation.Evaluate(left, AnimationProperty.Volume, left.Duration), 9); Assert.Equal(.5, EditorAnimation.Evaluate(right, AnimationProperty.Volume, 0), 9);
        s.Replace(right with { Speed = 2 }); right = s.Project.Clips.Single(k => k.Id == rightId); Assert.Equal(.8, EditorAnimation.Evaluate(right, AnimationProperty.Volume, right.Duration), 9);
    }
    [Fact] public void PointerTrimRebasesOnceAndUndoRestoresKeys()
    {
        var s = Fixture(); var c = s.Project.Clips.Single(); s.SetKeyframe(c.Id, Key(AnimationProperty.Opacity, 0, 0)); s.SetKeyframe(c.Id, Key(AnimationProperty.Opacity, 10, 1)); c = s.Project.Clips.Single();
        var proposal = TimelineGeometry.Propose(s.Project, c, TimelineGesture.TrimLeft, 2, 0, 0, 100, false); s.Replace(proposal);
        Assert.Equal(.2, EditorAnimation.Evaluate(s.Project.Clips.Single(), AnimationProperty.Opacity, 0), 9); s.Undo(); Assert.Same(c, s.Project.Clips.Single());
    }
    [Fact] public void DissolveRequiresAdjacencyHandlesAndCompatibleEdgesAndDeleteCleansDependency()
    {
        var s = Fixture(); var c = s.Project.Clips.Single(); s.Replace(c with { Out = 4 }); s.Add(c.MediaId, 0, 4); var incoming = s.Project.Clips.Last(); s.Replace(incoming with { In = 2, Out = 6 });
        s.SetTransition(incoming.Id, new(TransitionType.CrossDissolve, 1, TransitionPlacement.Start)); var p = s.Project;
        Assert.Throws<ArgumentException>(() => s.Replace(p.Clips.Last() with { In = 0 })); Assert.Throws<ArgumentException>(() => s.Move(incoming.Id, 5, 0, 0));
        Assert.Throws<ArgumentException>(() => s.Replace(p.Clips.First() with { FadeOut = 1 })); Assert.Same(p, s.Project);
        var audioId = s.Add(c.MediaId, 1, 0); var audio = s.Project.Clips.Single(k => k.Id == audioId); s.Replace(audio with { Out = 4 }); s.Delete(audioId);
        Assert.Single(s.Project.Clips.Single(k => k.Id == incoming.Id).Transitions!);
        p = s.Project;
        s.Delete(c.Id); Assert.Empty(s.Project.Clips.Single().Transitions!); s.Undo(); Assert.Same(p, s.Project);
    }
    [Fact] public async Task OldDefaultsAndVersionTwoPersistenceAndFutureFeatures()
    {
        var s = Fixture(); var old = JsonSerializer.Serialize(s.Project); var loaded = JsonSerializer.Deserialize<EditorProject>(old)!; loaded.Validate(); Assert.Null(loaded.Clips.Single().Keyframes);
        var c = s.Project.Clips.Single(); s.SetKeyframe(c.Id, Key(AnimationProperty.PositionX, 0, .2)); s.SetTransition(c.Id, new(TransitionType.FadeIn, .5, TransitionPlacement.Start));
        var path = Path.Combine(Path.GetTempPath(), "CaptureFlow-advanced-" + Guid.NewGuid().ToString("N"), "test.cfedit"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var store = new JsonEditorProjectStore(); await store.SaveAsync(s.Project, path, default); var restored = await store.LoadAsync(path, default);
        Assert.Equal(s.Project.Clips.Single().Keyframes, restored.Clips.Single().Keyframes); Assert.Equal(s.Project.Clips.Single().Transitions, restored.Clips.Single().Transitions); Assert.Equal(2, restored.Version);
        Assert.Throws<ArgumentException>(() => (restored with { Version = 3 }).Validate());
        var unsupported = old.Replace("\"VideoScale\":1", "\"VideoScale\":1,\"FutureEffect\":{}"); await File.WriteAllTextAsync(path, unsupported); await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync(path, default));
    }
}
