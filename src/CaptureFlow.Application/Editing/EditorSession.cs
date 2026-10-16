using CaptureFlow.Core.Editing;
namespace CaptureFlow.Application.Editing;

/// <summary>Non-destructive timeline operations and bounded project history. Source files are never written.</summary>
public sealed class EditorSession
{
    private readonly List<EditorProject> undo = [], redo = [];
    private EditorProject saved;
    public EditorProject Project { get; private set; } = EditorProject.Empty;
    public bool CanUndo => undo.Count != 0; public bool CanRedo => redo.Count != 0;
    public bool Dirty => Project != saved && !(Project.Version == saved.Version && Project.Id == saved.Id && Project.Name == saved.Name
        && Project.Media.SequenceEqual(saved.Media) && Project.Clips.SequenceEqual(saved.Clips) && Project.Tracks.SequenceEqual(saved.Tracks));
    public EditorSession() { saved = Project; }
    public void Open(EditorProject project) { project.Validate(); Project = saved = project; undo.Clear(); redo.Clear(); }
    public void MarkSaved() => saved = Project;
    public CaptureFlow.Core.Recovery.EditorAutosave Snapshot() => new(Project, saved, undo.TakeLast(20).ToArray(), redo.TakeLast(20).ToArray());
    public void Restore(CaptureFlow.Core.Recovery.EditorAutosave snapshot)
    {
        foreach (var project in snapshot.Undo.Concat(snapshot.Redo).Append(snapshot.Project).Append(snapshot.Saved)) project.Validate();
        Project = snapshot.Project; saved = snapshot.Saved;
        undo.Clear(); undo.AddRange(snapshot.Undo.TakeLast(20)); redo.Clear(); redo.AddRange(snapshot.Redo.TakeLast(20));
    }
    public void Apply(EditorProject project)
    {
        if (project.Clips.Any(c => c.Keyframes is { Length: > 0 } || c.Transitions is { Length: > 0 })) project = project with { Version = 2 };
        project.Validate(); if (project == Project) return;
        foreach (var locked in Project.Tracks.Where(t => t.Locked))
            if (!Project.Clips.Where(c => c.TrackId == locked.Id).SequenceEqual(project.Clips.Where(c => c.TrackId == locked.Id)))
                throw new InvalidOperationException("Unlock the track before editing its clips.");
        undo.Add(Project); if (undo.Count > 100) undo.RemoveAt(0); redo.Clear(); Project = project;
    }
    public void Undo() { if (!CanUndo) return; redo.Add(Project); Project = undo[^1]; undo.RemoveAt(undo.Count - 1); }
    public void Redo() { if (!CanRedo) return; undo.Add(Project); Project = redo[^1]; redo.RemoveAt(redo.Count - 1); }
    public void Import(EditorMedia media)
    {
        var existing = Project.Media.FirstOrDefault(m => string.Equals(m.Path, media.Path, StringComparison.OrdinalIgnoreCase));
        if (existing is null) Apply(Project with { Media = [.. Project.Media, media] });
        else
        {
            var refreshed = media with { Id = existing.Id };
            if (existing != refreshed) Apply(Project with { Media = Project.Media.Select(m => m.Id == existing.Id ? refreshed : m).ToArray() });
        }
    }
    public Guid Add(Guid mediaId, int trackId, double start)
    {
        var media = Project.Media.Single(m => m.Id == mediaId); var id = Guid.NewGuid();
        Apply(Project with { Clips = [.. Project.Clips, new(id, mediaId, trackId, start, 0, media.Duration)] }); return id;
    }
    public void Replace(EditorClip clip)
    {
        var original = Project.Clips.Single(c => c.Id == clip.Id);
        if ((original.In != clip.In || original.Out != clip.Out || original.Speed != clip.Speed) && ReferenceEquals(original.Keyframes, clip.Keyframes))
            clip = EditorAnimation.Retime(original, clip);
        Apply(Project with { Clips = Project.Clips.Select(c => c.Id == clip.Id ? clip : c).ToArray() });
    }
    public void SetKeyframe(Guid clipId, EditorKeyframe key)
    {
        var clip = Project.Clips.Single(c => c.Id == clipId);
        Replace(clip with { Keyframes = (clip.Keyframes ?? []).Where(k => k.Id != key.Id).Append(key).ToArray() });
    }
    public void DeleteKeyframe(Guid clipId, Guid keyId)
    { var clip = Project.Clips.Single(c => c.Id == clipId); Replace(clip with { Keyframes = (clip.Keyframes ?? []).Where(k => k.Id != keyId).ToArray() }); }
    public void SetTransition(Guid clipId, EditorTransition transition)
    { var clip = Project.Clips.Single(c => c.Id == clipId); Replace(clip with { Transitions = (clip.Transitions ?? []).Where(t => t.Placement != transition.Placement).Append(transition).ToArray() }); }
    public void DeleteTransition(Guid clipId, TransitionPlacement placement)
    { var clip = Project.Clips.Single(c => c.Id == clipId); Replace(clip with { Transitions = (clip.Transitions ?? []).Where(t => t.Placement != placement).ToArray() }); }
    public void Delete(Guid id, bool ripple = false)
    {
        var removed = Project.Clips.Single(c => c.Id == id);
        Apply(Project with { Clips = Project.Clips.Where(c => c.Id != id).Select(c => removed.TrackId == 0 && c.TrackId == 0 && Math.Abs(c.Start - removed.End) < .00001 && (c.Transitions ?? []).Any(t => t.Type == TransitionType.CrossDissolve) ? c with { Transitions = (c.Transitions ?? []).Where(t => t.Type != TransitionType.CrossDissolve).ToArray() } : c).Select(c => ripple && c.Start >= removed.End - .00001 ? c with { Start = Math.Max(0, c.Start - removed.Duration) } : c).ToArray() });
    }
    public void Move(Guid id, double start, double playhead, double snapTolerance)
    {
        start = Math.Max(0, start); var clip = Project.Clips.Single(c => c.Id == id);
        var boundaries = Project.Clips.Where(c => c.Id != id).SelectMany(c => new[] { c.Start, c.End }).Append(playhead).Append(0).ToArray();
        var candidates = boundaries.SelectMany(b => new[] { b, b - clip.Duration }).Where(b => b >= 0).OrderBy(b => Math.Abs(start - b));
        var snapped = candidates.First(); if (Math.Abs(start - snapped) <= snapTolerance) start = snapped;
        Replace(clip with { Start = start });
    }
    public Guid Split(Guid id, double playhead)
    {
        var clip = Project.Clips.Single(c => c.Id == id); var source = clip.In + (playhead - clip.Start) * clip.Speed;
        if (source - clip.In < .02 || clip.Out - source < .02) throw new ArgumentException("Place the playhead inside the selected clip, away from its edges.");
        var right = clip with { Id = Guid.NewGuid(), Start = playhead, In = source, FadeIn = 0, Transitions = (clip.Transitions ?? []).Where(t => t.Placement == TransitionPlacement.End).ToArray() };
        var left = clip with { Out = source, FadeOut = 0, Transitions = (clip.Transitions ?? []).Where(t => t.Placement == TransitionPlacement.Start).ToArray() };
        left = EditorAnimation.Slice(clip, left, 0); right = EditorAnimation.Slice(clip, right, playhead - clip.Start);
        left = left with { FadeIn = Math.Min(left.FadeIn, left.Duration) }; right = right with { FadeOut = Math.Min(right.FadeOut, right.Duration) };
        Apply(Project with { Clips = Project.Clips.Where(c => c.Id != id).Concat([left, right]).ToArray() }); return right.Id;
    }
    public Guid Paste(EditorClip copied, double at, int? track = null)
    { var pasted = copied with { Id = Guid.NewGuid(), Start = Math.Max(0, at), TrackId = track ?? copied.TrackId }; Apply(Project with { Clips = [.. Project.Clips, pasted] }); return pasted.Id; }
    public void JoinTrack(int trackId)
    {
        var cursor = 0d; var joined = new Dictionary<Guid, EditorClip>();
        foreach (var clip in Project.Clips.Where(c => c.TrackId == trackId).OrderBy(c => c.Start)) { joined.Add(clip.Id, clip with { Start = cursor }); cursor += clip.Duration; }
        Apply(Project with { Clips = Project.Clips.Select(c => joined.GetValueOrDefault(c.Id) ?? c).ToArray() });
    }
    public void RemoveSilence(Guid id, IReadOnlyList<EditorRange> silence)
    {
        var clip = Project.Clips.Single(c => c.Id == id); var segments = new List<EditorClip>(); var source = clip.In; var start = clip.Start;
        foreach (var range in silence.OrderBy(r => r.Start))
        {
            var left = Math.Clamp(range.Start, clip.In, clip.Out); var right = Math.Clamp(range.End, clip.In, clip.Out);
            if (right <= source || right <= left) continue;
            if (left - source >= .02) { var kept = clip with { Id = Guid.NewGuid(), In = source, Out = left, Start = start, FadeIn = 0, FadeOut = 0 }; kept = EditorAnimation.Slice(clip, kept with { Transitions = [] }, (source - clip.In) / clip.Speed); segments.Add(kept); start += kept.Duration; }
            source = Math.Max(source, right);
        }
        if (clip.Out - source >= .02) segments.Add(EditorAnimation.Slice(clip, clip with { Id = Guid.NewGuid(), In = source, Start = start, FadeIn = 0, FadeOut = 0, Transitions = [] }, (source - clip.In) / clip.Speed));
        var remainingDuration = segments.Sum(c => c.Duration); var removedDuration = clip.Duration - remainingDuration;
        Apply(Project with { Clips = Project.Clips.Where(c => c.Id != id).Select(c => c.Start >= clip.End - .00001 ? c with { Start = Math.Max(0, c.Start - removedDuration) } : c).Concat(segments).ToArray() });
    }
    public static EditorCrop CropFromNormalized(double x, double y, double width, double height, EditorMedia media)
    {
        if (new[] { x, y, width, height }.Any(v => !double.IsFinite(v)) || x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > 1.00001 || y + height > 1.00001) throw new ArgumentException("Draw a crop inside the video.");
        var px = (int)Math.Floor(x * media.Width); var py = (int)Math.Floor(y * media.Height);
        var w = Math.Min(media.Width - px, (int)Math.Round(width * media.Width)) / 2 * 2; var h = Math.Min(media.Height - py, (int)Math.Round(height * media.Height)) / 2 * 2;
        if (w < 2 || h < 2) throw new ArgumentException("Crop is too small."); return new(px, py, w, h);
    }
    public static string Timecode(double seconds) { var t = TimeSpan.FromSeconds(Math.Max(0, seconds)); return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}"; }
}


