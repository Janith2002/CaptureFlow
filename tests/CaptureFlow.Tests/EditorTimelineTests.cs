using CaptureFlow.Application.Editing;
using CaptureFlow.Core.Editing;
using CaptureFlow.Infrastructure.Editing;
namespace CaptureFlow.Tests;

public class EditorTimelineTests
{
    internal static EditorMedia Media(bool video = true) => new(Guid.NewGuid(), Path.GetFullPath("synthetic." + (video ? "mp4" : "wav")), video ? "MP4" : "WAV", 10, video ? 1920 : 0, video ? 1080 : 0, video, true, 123, 456);
    private static (EditorSession Session, Guid Clip) Fixture(bool video = true)
    { var session = new EditorSession(); var media = Media(video); session.Import(media); var clip = session.Add(media.Id, video ? 0 : 1, 0); return (session, clip); }
    [Fact] public void TrimSpeedAndSplitMaintainSourceAndTimelineSynchronization()
    {
        var (session, id) = Fixture(); session.Replace(session.Project.Clips.Single() with { In = 2, Out = 8, Speed = 2, Start = 1 });
        Assert.Equal(3, session.Project.Duration - 1); var rightId = session.Split(id, 2);
        var left = session.Project.Clips.Single(c => c.Id == id); var right = session.Project.Clips.Single(c => c.Id == rightId);
        Assert.Equal(4, left.Out); Assert.Equal(left.Out, right.In); Assert.Equal(left.End, right.Start); Assert.Equal(4, session.Project.Duration);
        Assert.Equal(.5, (right.In - 3) / right.Speed); // Same source/time mapping is used for video and original audio.
    }
    [Fact] public void MoveSnapsBothEdgesAndRejectsVideoOverlapAtomically()
    {
        var (session, id) = Fixture(); session.Replace(session.Project.Clips.Single() with { Out = 2 });
        var second = session.Add(session.Project.Media.Single().Id, 0, 4);
        session.Move(second, 2.05, 10, .1); Assert.Equal(2, session.Project.Clips.Single(c => c.Id == second).Start);
        var before = session.Project; Assert.Throws<ArgumentException>(() => session.Move(second, 1, 20, 0)); Assert.Same(before, session.Project);
        session.Move(id, 16.95, 19, .1); Assert.Equal(17, session.Project.Clips.Single(c => c.Id == id).Start);
    }
    [Fact] public void DeleteKeepsGapRippleDeleteMovesAllDownstreamTracksTogether()
    {
        var (session, id) = Fixture(); session.Replace(session.Project.Clips.Single() with { Out = 2 });
        session.Add(session.Project.Media.Single().Id, 0, 2); session.Add(session.Project.Media.Single().Id, 1, 2);
        session.Delete(id, true); Assert.All(session.Project.Clips, c => Assert.Equal(0, c.Start)); session.Undo(); session.Delete(id);
        Assert.All(session.Project.Clips, c => Assert.Equal(2, c.Start));
    }
    [Fact] public void JoinCopyPasteUndoRedoAndSavedIdentityAreReversible()
    {
        var (session, id) = Fixture(false); session.MarkSaved(); var before = session.Project;
        session.Paste(session.Project.Clips.Single(), 15); session.JoinTrack(1); Assert.Equal(20, session.Project.Duration); Assert.True(session.Dirty);
        session.Undo(); Assert.Equal(25, session.Project.Duration); session.Undo(); Assert.Same(before, session.Project); Assert.False(session.Dirty);
        session.Redo(); Assert.True(session.Dirty); session.Delete(id); Assert.True(session.CanUndo);
    }
    [Theory] [InlineData(-1, 5, 1)] [InlineData(4, 3, 1)] [InlineData(0, 11, 1)] [InlineData(0, 5, 0)] [InlineData(0, 5, 5)]
    public void InvalidTrimsAndSpeedsCannotChangeProject(double start, double end, double speed)
    { var (session, _) = Fixture(); var before = session.Project; Assert.Throws<ArgumentException>(() => session.Replace(before.Clips.Single() with { In = start, Out = end, Speed = speed })); Assert.Same(before, session.Project); }
    [Theory] [InlineData(0)] [InlineData(90)] [InlineData(180)] [InlineData(270)]
    public void CropAndRotationValidateWithinActualSource(int rotation)
    {
        var (session, _) = Fixture(); var media = session.Project.Media.Single(); var crop = EditorSession.CropFromNormalized(.1, .2, .5, .5, media);
        Assert.Equal(new(192, 216, 960, 540), crop); session.Replace(session.Project.Clips.Single() with { Crop = crop, Rotation = rotation });
        Assert.Throws<ArgumentException>(() => EditorSession.CropFromNormalized(.9, 0, .5, .5, media));
    }
    [Theory] [InlineData(-.1, 0)] [InlineData(11, 0)] [InlineData(6, 6)]
    public void InvalidFadeLengthsAreRejected(double fadeIn, double fadeOut)
    { var (session, _) = Fixture(false); Assert.Throws<ArgumentException>(() => session.Replace(session.Project.Clips.Single() with { FadeIn = fadeIn, FadeOut = fadeOut })); }
    [Theory] [InlineData(-1)] [InlineData(3)] [InlineData(double.NaN)]
    public void InvalidVolumeIsRejected(double volume)
    { var (session, _) = Fixture(false); Assert.Throws<ArgumentException>(() => session.Replace(session.Project.Clips.Single() with { Volume = volume })); }
    [Fact] public void SilenceRemovalUsesSourceTimesAndSpeedAndPreservesRemainingOperations()
    {
        var (session, id) = Fixture(false); session.Replace(session.Project.Clips.Single() with { Speed = 2, Volume = .5 });
        session.RemoveSilence(id, [new(2, 4), new(6, 8)]); Assert.Equal(3, session.Project.Clips.Length); Assert.Equal(3, session.Project.Duration);
        Assert.All(session.Project.Clips, c => { Assert.Equal(2, c.Speed); Assert.Equal(.5, c.Volume); });
        Assert.Equal(new[] { 0d, 1d, 2d }, session.Project.Clips.OrderBy(c => c.Start).Select(c => c.Start));
        session.Undo(); Assert.Single(session.Project.Clips);
    }
    [Fact] public void ImportDetectsDuplicatePathsAndMissingFilesDoNotPreventProjectOpening()
    { var (session, _) = Fixture(); var media = session.Project.Media.Single(); session.Import(media with { Id = Guid.NewGuid() }); Assert.Single(session.Project.Media); Assert.Single(session.Project.MissingSources); session.Open(session.Project); Assert.False(session.Dirty); }
    [Theory] [InlineData("MP4", 192)] [InlineData("MP3", 128)] [InlineData("MP3", 192)] [InlineData("MP3", 256)] [InlineData("MP3", 320)] [InlineData("WAV", 192)]
    public void ExportSettingsValidateSupportedFormats(string format, int bitrate) => new EditorExportSettings(format, Mp3Bitrate: bitrate).Validate();
    [Fact] public void UnsafeExportSettingsAreRejected()
    { Assert.Throws<ArgumentException>(() => new EditorExportSettings("AVI").Validate()); Assert.Throws<ArgumentException>(() => new EditorExportSettings(Width: 1919).Validate()); Assert.Throws<ArgumentException>(() => new EditorExportSettings(Mp3Bitrate: 193).Validate()); }
    [Fact] public async Task ProjectRoundTripAndCancelledSavePreserveExistingFile()
    {
        var (session, _) = Fixture(); var path = Path.Combine(Path.GetTempPath(), "CaptureFlow-project-" + Guid.NewGuid().ToString("N") + ".cfedit");
        try
        {
            var store = new JsonEditorProjectStore(); await store.SaveAsync(session.Project, path, default); var bytes = await File.ReadAllBytesAsync(path);
            var loaded = await store.LoadAsync(path, default); Assert.Equal(session.Project.Clips, loaded.Clips); Assert.Equal(session.Project.Media, loaded.Media);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(session.Project with { Name = "Changed" }, path, new(true)));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            var roundtrip = new EditorSession(); roundtrip.Open(loaded); Assert.False(roundtrip.Dirty); Assert.Single(loaded.MissingSources);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    [Fact] public void AccurateTimecodeIncludesMilliseconds() => Assert.Equal("01:02:03.125", EditorSession.Timecode(3723.125));
    [Fact] public void ReimportRefreshesMetadataWithoutDuplicatesAndRejectsIncompatibleEdits()
    {
        var (session, _) = Fixture(); var original = session.Project.Media.Single();
        session.Import(original with { Id = Guid.NewGuid(), Bytes = 456, ModifiedTicks = 789 }); Assert.Single(session.Project.Media);
        Assert.Equal(original.Id, session.Project.Media.Single().Id); Assert.Equal(456, session.Project.Media.Single().Bytes);
        var before = session.Project; Assert.Throws<ArgumentException>(() => session.Import(original with { Duration = 5 })); Assert.Same(before, session.Project);
    }
}
