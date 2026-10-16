using CaptureFlow.Core.Editing;
using CaptureFlow.Application.Editing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Data;
using System.ComponentModel;
namespace CaptureFlow.Desktop.ViewModels;
public partial class EditorViewModel
{
    [ObservableProperty] private bool snappingEnabled = true;
    public EditorTrack VideoTrack => Project.Tracks.Single(t => t.Id == 0);
    public EditorTrack AudioTrack => Project.Tracks.Single(t => t.Id == 1);
    public EditorTrack MusicTrack => Project.Tracks.Single(t => t.Id == 2);
    [ObservableProperty] private string activeTimelineTool = "Select";
    [ObservableProperty] private string mediaSearch = "";
    [ObservableProperty] private EditorWorkspace workspace = new();
    [ObservableProperty] private bool isMonitorFocused;
    private Task workspaceSave = Task.CompletedTask;
    public async Task LoadWorkspaceAsync()
    { try { if (workspaceStore is not null) Workspace = await workspaceStore.LoadAsync(); } catch (Exception ex) { Message = "Workspace preferences unavailable: " + ex.Message; } }
    public Task SaveWorkspaceAsync(EditorWorkspace value)
    { Workspace = value; return workspaceSave = PersistWorkspaceAsync(value); }
    private async Task PersistWorkspaceAsync(EditorWorkspace value)
    { try { if (workspaceStore is not null) await workspaceStore.SaveAsync(value); } catch (Exception ex) { Message = "Workspace not saved: " + ex.Message; } }
    [RelayCommand] private Task ResetEditorWorkspaceAsync()
    { IsMonitorFocused = false; return SaveWorkspaceAsync(new()); }
    [RelayCommand] private Task ToggleEditorMediaAsync() => SaveWorkspaceAsync(Workspace with { MediaVisible = !Workspace.MediaVisible });
    [RelayCommand] private Task ToggleEditorInspectorAsync() => SaveWorkspaceAsync(Workspace with { InspectorVisible = !Workspace.InspectorVisible });
    public ICollectionView FilteredMedia => CollectionViewSource.GetDefaultView(Media);
    partial void OnMediaSearchChanged(string value)
    {
        FilteredMedia.Filter = item => item is EditorMediaItem media && System.IO.Path.GetFileName(media.Media.Path).Contains(MediaSearch, StringComparison.OrdinalIgnoreCase);
        FilteredMedia.Refresh();
    }
    [RelayCommand] private void SelectTool() => ActiveTimelineTool = "Select";
    [RelayCommand] private void TrimTool() => ActiveTimelineTool = "Trim";
    [RelayCommand] private void ZoomIn() => Zoom = Math.Clamp(Zoom * 1.25, .005, 2000);
    [RelayCommand] private void ZoomOut() => Zoom = Math.Clamp(Zoom / 1.25, .005, 2000);
    public void FitTimeline(double viewport) => Zoom = Math.Clamp(Math.Max(100, viewport - 100) / Math.Max(.1, Project.Duration), .005, 2000);
    public EditorClip ProposeGesture(EditorClip clip, TimelineGesture gesture, double delta, int track)
    {
        if (!Project.Tracks.Any(t => t.Id == track)) throw new ArgumentException("Drop within the Video, Audio or Music track.");
        if (!CanEdit || Project.Tracks.Single(t => t.Id == clip.TrackId).Locked || Project.Tracks.Single(t => t.Id == track).Locked) throw new InvalidOperationException("Track is locked or editing is busy.");
        return TimelineGeometry.Propose(Project, clip, gesture, delta, track, Playhead, Zoom, SnappingEnabled);
    }
    public void CompleteGesture(EditorClip proposed) => Commit(() => Session.Replace(proposed));
    public EditorClip ProposeDrop(Guid media, int track, double position)
    {
        if (!CanEdit || Project.Tracks.Single(t => t.Id == track).Locked) throw new InvalidOperationException("Unlock the destination track and finish processing.");
        var asset = Project.Media.Single(m => m.Id == media); var start = TimelineGeometry.Quantize(Math.Max(0, position));
        if (SnappingEnabled) { var boundaries = Project.Clips.SelectMany(c => new[] { c.Start, c.End }).Append(Playhead).Append(0).ToArray(); start = TimelineGeometry.Snap(start, boundaries.Concat(boundaries.Select(b => b - asset.Duration)), 8 / Zoom); }
        var clip = new EditorClip(Guid.NewGuid(), media, track, start, 0, asset.Duration); (Project with { Clips = [.. Project.Clips, clip] }).Validate(); return clip;
    }
    public void DropMedia(Guid media, int track, double position) => Commit(() =>
    { var clip = ProposeDrop(media, track, position); Session.Apply(Project with { Clips = [.. Project.Clips, clip] }); SelectedClip = clip; });
    [RelayCommand(CanExecute = nameof(CanEdit))] private void ToggleTrackLock(string id)
    { if (int.TryParse(id, out var track)) Commit(() => Session.Apply(Project with { Tracks = Project.Tracks.Select(t => t.Id == track ? t with { Locked = !t.Locked } : t).ToArray() })); }
    [RelayCommand(CanExecute = nameof(CanEdit))] private void ToggleTrackMute(string id)
    { if (int.TryParse(id, out var track)) Commit(() => Session.Apply(Project with { Tracks = Project.Tracks.Select(t => t.Id == track ? t with { Muted = !t.Muted } : t).ToArray() })); }
    public void SelectAdjacentClip(int direction)
    {
        var clips = Project.Clips.OrderBy(c => c.Start).ThenBy(c => c.TrackId).ToArray(); if (clips.Length == 0) return;
        var index = Array.FindIndex(clips, c => c.Id == SelectedClip?.Id); SelectedClip = clips[Math.Clamp(index + direction, 0, clips.Length - 1)];
    }
}
