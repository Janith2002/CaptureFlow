namespace CaptureFlow.Core.Editing;

public sealed record EditorMedia(Guid Id, string Path, string Format, double Duration, int Width, int Height,
    bool HasVideo, bool HasAudio, long Bytes, long ModifiedTicks, double FramesPerSecond = 0);
public sealed record EditorCrop(int X, int Y, int Width, int Height);
public sealed record EditorTrack(int Id, string Name, bool Video, double Volume = 1, bool Muted = false, bool Locked = false);
public sealed record EditorText(string Content = "", double X = .05, double Y = .08, int Size = 32, string Color = "#FFFFFF");
public sealed record EditorClip(Guid Id, Guid MediaId, int TrackId, double Start, double In, double Out,
    double Speed = 1, double Volume = 1, bool Muted = false, double FadeIn = 0, double FadeOut = 0,
    EditorCrop? Crop = null, int Rotation = 0, EditorText? Text = null, double VideoScale = 1, EditorKeyframe[]? Keyframes = null, EditorTransition[]? Transitions = null)
{
    public double Duration => (Out - In) / Speed;
    public double End => Start + Duration;
}
public sealed record EditorProject(int Version, Guid Id, string Name, EditorMedia[] Media, EditorClip[] Clips, EditorTrack[] Tracks)
{
    public static EditorProject Empty => new(1, Guid.NewGuid(), "Untitled edit", [], [], [new(0, "Video", true), new(1, "Audio", false), new(2, "Music", false)]);
    public double Duration => Clips.Length == 0 ? 0 : Clips.Max(c => c.End);
    public string[] MissingSources => Media.Where(m => !File.Exists(m.Path)).Select(m => m.Path).ToArray();
    public void Validate()
    {
        if (Version is not (1 or 2)) throw new ArgumentException($"Editor project version {Version} is unsupported. This build supports versions 1 and 2.");
        if (Version is not (1 or 2) || Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Media is null || Clips is null || Tracks is null ||
            Media.Length > 256 || Clips.Length > 64 || Tracks.Length != 3 || Media.Any(m => m is null) || Clips.Any(c => c is null) || Tracks.Any(t => t is null) ||
            !Tracks.Select(t => t.Id).Order().SequenceEqual(new[] { 0, 1, 2 }) || !Tracks.Single(t => t.Id == 0).Video || Tracks.Any(t => t.Id != 0 && t.Video) ||
            Media.Select(m => m.Id).Distinct().Count() != Media.Length || Clips.Select(c => c.Id).Distinct().Count() != Clips.Length || Tracks.Select(t => t.Id).Distinct().Count() != Tracks.Length)
            throw new ArgumentException("Invalid editor project, version or limits (64 timeline clips, 256 imported files)." );
        foreach (var media in Media)
            if (media.Id == Guid.Empty || !Path.IsPathFullyQualified(media.Path) || media.Format is not ("MP4" or "MP3" or "WAV") ||
                !double.IsFinite(media.Duration) || media.Duration <= 0 || media.Duration > 86400 || !double.IsFinite(media.FramesPerSecond) || media.FramesPerSecond is < 0 or > 1000 || !media.HasAudio && !media.HasVideo || media.HasVideo && (media.Width < 2 || media.Height < 2 || media.Width > 16384 || media.Height > 8640 || (long)media.Width * media.Height > 33_554_432))
                throw new ArgumentException("Invalid imported media metadata.");
        foreach (var track in Tracks) if (string.IsNullOrWhiteSpace(track.Name) || !double.IsFinite(track.Volume) || track.Volume is < 0 or > 2) throw new ArgumentException("Track name and volume must be valid.");
        foreach (var clip in Clips)
        {
            var media = Media.SingleOrDefault(m => m.Id == clip.MediaId) ?? throw new ArgumentException("Clip refers to unknown media.");
            var track = Tracks.SingleOrDefault(t => t.Id == clip.TrackId) ?? throw new ArgumentException("Clip refers to unknown track.");
            if (clip.Id == Guid.Empty || new[] { clip.Start, clip.In, clip.Out, clip.Speed, clip.Volume, clip.FadeIn, clip.FadeOut, clip.VideoScale }.Any(v => !double.IsFinite(v)) ||
                clip.Start < 0 || clip.In < 0 || clip.Out > media.Duration + .001 || clip.Out - clip.In < .02 || clip.Speed is < .25 or > 4 || clip.Volume is < 0 or > 2 ||
                clip.FadeIn < 0 || clip.FadeOut < 0 || clip.FadeIn + clip.FadeOut > clip.Duration + .00001 || clip.VideoScale is < .1 or > 3 || clip.Rotation is not (0 or 90 or 180 or 270) || track.Video && !media.HasVideo || !track.Video && !media.HasAudio)
                throw new ArgumentException("Invalid clip range, track, speed, volume or fade lengths.");
            EditorAnimation.Validate(clip, track.Video);
            if (!media.HasAudio && (clip.Keyframes ?? []).Any(k => k.Property == AnimationProperty.Volume)) throw new ArgumentException("Volume automation requires source audio.");
            if (Version == 1 && (clip.Keyframes is { Length: > 0 } || clip.Transitions is { Length: > 0 })) throw new ArgumentException("Advanced edits require project version 2.");
            if (clip.Crop is { } crop && (!track.Video || crop.X < 0 || crop.Y < 0 || crop.Width < 2 || crop.Height < 2 || crop.Width % 2 != 0 || crop.Height % 2 != 0 || (long)crop.X + crop.Width > media.Width || (long)crop.Y + crop.Height > media.Height))
                throw new ArgumentException("Crop must have even dimensions within the source video.");
            if (clip.Text is { } text && (text.Content.Length > 4096 || !double.IsFinite(text.X) || !double.IsFinite(text.Y) || text.X is < 0 or > 1 || text.Y is < 0 or > 1 || text.Size is < 8 or > 200 || !System.Text.RegularExpressions.Regex.IsMatch(text.Color, "^#[0-9a-fA-F]{6}$")))
                throw new ArgumentException("Invalid text overlay content, position, size or RGB color.");
        }
        var video = Clips.Where(c => Tracks.Single(t => t.Id == c.TrackId).Video).OrderBy(c => c.Start).ToArray();
        for (var i = 1; i < video.Length; i++) if (video[i].Start < video[i - 1].End - .00001) throw new ArgumentException("Video clips cannot overlap. Move them to a free part of the video track.");
        foreach (var clip in video)
        {
            var dissolve = (clip.Transitions ?? []).SingleOrDefault(t => t.Type == TransitionType.CrossDissolve);
            if (dissolve is null) continue;
            var previous = video.LastOrDefault(c => c.Start < clip.Start);
            if (previous is null || Math.Abs(previous.End - clip.Start) > .00001 || dissolve.Duration > previous.Duration || clip.In + 1e-9 < dissolve.Duration * clip.Speed || clip.FadeIn > 0 || previous.FadeOut > 0 || (previous.Transitions ?? []).Any(t => t.Placement == TransitionPlacement.End) || dissolve.Duration + Math.Max(previous.FadeIn, (previous.Transitions ?? []).Where(t => t.Placement == TransitionPlacement.Start).Sum(t => t.Duration)) > previous.Duration + 1e-9)
                throw new ArgumentException("Cross dissolve requires adjacent video clips, an incoming source handle, and an outgoing edge without fades.");
        }
        if (Duration > 86400) throw new ArgumentException("An edited timeline is limited to 24 hours.");
    }
}
public sealed record EditorExportSettings(string Format = "MP4", int Width = 1920, int Height = 1080, int FramesPerSecond = 30, int Crf = 20, int Mp3Bitrate = 192)
{
    public void Validate()
    {
        if (Format is not ("MP4" or "MP3" or "WAV") || Width is < 16 or > 3840 || Height is < 16 or > 2160 || Width % 2 != 0 || Height % 2 != 0 || FramesPerSecond is not (24 or 30 or 60) || Crf is < 16 or > 30 || Mp3Bitrate is not (128 or 192 or 256 or 320))
            throw new ArgumentException("Invalid export format, dimensions, FPS, quality or MP3 bitrate.");
    }
}
public sealed record EditorAnalysis(string? Thumbnail, float[] Waveform, string? Filmstrip = null, int FilmstripFrames = 0,
    double ThumbnailMilliseconds = 0, double FilmstripMilliseconds = 0, double WaveformMilliseconds = 0);
public sealed record EditorRange(double Start, double End);
public interface IEditorMediaBackend
{
    Task<EditorMedia> ProbeAsync(string path, string ffmpeg, CancellationToken token);
    Task<EditorAnalysis> AnalyzeAsync(EditorMedia media, string ffmpeg, CancellationToken token);
    Task<EditorRange[]> DetectSilenceAsync(EditorMedia media, double thresholdDb, double minimumSeconds, string ffmpeg, CancellationToken token);
    Task<string> RenderAsync(EditorProject project, EditorExportSettings settings, string outputFolder, string ffmpeg, IProgress<double>? progress, CancellationToken token);
}
public interface IEditorProjectStore
{
    Task SaveAsync(EditorProject project, string path, CancellationToken token);
    Task<EditorProject> LoadAsync(string path, CancellationToken token);
}

