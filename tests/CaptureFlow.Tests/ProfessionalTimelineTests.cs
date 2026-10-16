using CaptureFlow.Core.Editing;
using CaptureFlow.Application.Editing;
using CaptureFlow.Infrastructure.Editing;
namespace CaptureFlow.Tests;
public class ProfessionalTimelineTests
{
    private static (EditorSession Session, EditorClip Clip) Fixture()
    { var session = new EditorSession(); var media = EditorTimelineTests.Media(); session.Import(media); session.Add(media.Id, 0, 1); var clip = session.Project.Clips.Single() with { In = 2, Out = 8, Speed = 2 }; session.Replace(clip); return (session, clip); }
    [Theory] [InlineData(TimelineGesture.TrimLeft, .5, 1.5, 3, 8)] [InlineData(TimelineGesture.TrimRight, -.5, 1, 2, 7)] [InlineData(TimelineGesture.Move, 2, 3, 2, 8)]
    public void GestureUsesOriginalAnchorAndSpeed(TimelineGesture gesture, double delta, double start, double input, double output)
    {
        var (session, clip) = Fixture(); var proposed = TimelineGeometry.Propose(session.Project, clip, gesture, delta, 0, 0, 100, false);
        Assert.Equal(start, proposed.Start); Assert.Equal(input, proposed.In); Assert.Equal(output, proposed.Out); Assert.Equal(clip, session.Project.Clips.Single());
        session.Replace(proposed); session.Undo(); Assert.Equal(clip, session.Project.Clips.Single()); session.Redo(); Assert.Equal(proposed, session.Project.Clips.Single());
    }
    [Fact] public void InvalidTrimAndVideoOverlapLeaveProjectAndHistoryUnchanged()
    {
        var (session, clip) = Fixture(); session.Add(clip.MediaId, 0, 5); var project = session.Project;
        Assert.Throws<ArgumentException>(() => TimelineGeometry.Propose(project, clip, TimelineGesture.TrimLeft, 10, 0, 0, 100, false));
        Assert.Throws<ArgumentException>(() => TimelineGeometry.Propose(project, clip, TimelineGesture.Move, 3, 0, 0, 100, false)); Assert.Same(project, session.Project);
    }
    [Fact] public void CompatibleTrackMoveDoesNotAlterOtherClips()
    {
        var (session, clip) = Fixture(); var other = session.Add(clip.MediaId, 2, 4); var before = session.Project.Clips.Single(c => c.Id == other);
        var proposed = TimelineGeometry.Propose(session.Project, clip, TimelineGesture.Move, 1, 1, 0, 40, false); session.Replace(proposed);
        Assert.Equal(1, session.Project.Clips.Single(c => c.Id == clip.Id).TrackId); Assert.Equal(before, session.Project.Clips.Single(c => c.Id == other));
        var audio = EditorTimelineTests.Media(false); session.Import(audio); var audioId = session.Add(audio.Id, 1, 10); var a = session.Project.Clips.Single(c => c.Id == audioId);
        Assert.Throws<ArgumentException>(() => TimelineGeometry.Propose(session.Project, a, TimelineGesture.Move, 0, 0, 0, 40, false));
    }
    [Theory] [InlineData(0, .05)] [InlineData(2, 2.06)] [InlineData(5, 4.94)]
    public void SnapHasDeterministicPixelThreshold(double boundary, double request)
    { Assert.Equal(boundary, TimelineGeometry.Snap(request, [boundary], 8d / 100)); Assert.Equal(request, TimelineGeometry.Snap(request, [boundary], 0)); }
    [Theory] [InlineData(24)] [InlineData(30)] [InlineData(60)]
    public void FrameConversionHasNoCumulativePointerDrift(int fps)
    {
        for (var frame = 0; frame < 100000; frame += 137) { var value = frame / (double)fps; Assert.Equal(value, TimelineGeometry.Quantize(value + .1 / fps, fps), 8); }
    }
    [Fact] public void LocksRejectAllClipMutationsAndCanBeUndoneOrUnlocked()
    {
        var (session, clip) = Fixture(); session.Apply(session.Project with { Tracks = session.Project.Tracks.Select(t => t.Id == 0 ? t with { Locked = true } : t).ToArray() }); var before = session.Project;
        Assert.Throws<InvalidOperationException>(() => session.Delete(clip.Id)); Assert.Throws<InvalidOperationException>(() => session.Replace(clip with { Start = 2 })); Assert.Same(before, session.Project);
        session.Undo(); session.Replace(clip with { Start = 2 }); Assert.Equal(2, session.Project.Clips.Single().Start);
    }
    [Fact] public async Task NewOptionalMetadataAndLocksRoundTripInVersionOneProjects()
    {
        var (session, clip) = Fixture(); var project = session.Project with { Media = session.Project.Media.Select(m => m with { FramesPerSecond = 30000d / 1001 }).ToArray(), Tracks = session.Project.Tracks.Select(t => t with { Locked = true, Muted = true }).ToArray() };
        var path = Path.Combine(Path.GetTempPath(), "CaptureFlowTimelineTests", Guid.NewGuid().ToString("N"), "project.cfedit"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); var store = new JsonEditorProjectStore(); await store.SaveAsync(project, path, default); var restored = await store.LoadAsync(path, default);
        Assert.Equal(1, restored.Version); Assert.Equal(project.Clips, restored.Clips); Assert.Equal(project.Media, restored.Media); Assert.Equal(project.Tracks, restored.Tracks);
    }
    [Fact] public async Task WorkspacePersistenceValidatesAndPreservesCancelledSave()
    {
        var path = Path.Combine(Path.GetTempPath(), "CaptureFlowTimelineTests", Guid.NewGuid().ToString("N"), "workspace.json"); var store = new JsonEditorWorkspaceStore(path); Assert.Equal(new EditorWorkspace(), await store.LoadAsync());
        var value = new EditorWorkspace(200, 300, 2, 2, false, true); await store.SaveAsync(value); Assert.Equal(value, await store.LoadAsync());
        using var cts = new CancellationTokenSource(); cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new(), cts.Token)); Assert.Equal(value, await store.LoadAsync());
        Assert.Throws<ArgumentException>(() => new EditorWorkspace(MediaWidth: double.NaN).Validate()); Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }
}
