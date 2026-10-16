using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureFlow.Application.Editing;
using CaptureFlow.Core.Editing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
namespace CaptureFlow.Desktop.ViewModels;

public sealed class EditorMediaItem(EditorMedia media, ImageSource? thumbnail, float[] waveform, ImageSource[]? filmstrip = null)
{
    public EditorMedia Media { get; } = media; public ImageSource? Thumbnail { get; } = thumbnail; public float[] Waveform { get; } = waveform;
    public ImageSource[] Filmstrip { get; } = filmstrip ?? [];
    public string Label => $"{Path.GetFileName(Media.Path)} · {Media.Format} · {EditorSession.Timecode(Media.Duration)}" + (Media.HasVideo ? $" · {Media.Width} × {Media.Height} · {(Media.FramesPerSecond > 0 ? $"{Media.FramesPerSecond:0.##} FPS" : "FPS unknown")}" : "") + (Media.HasAudio ? " · audio" : " · no audio");
}
public partial class EditorViewModel(IEditorMediaBackend backend, IEditorProjectStore store, CaptureFlow.Core.Recovery.IRecoveryJournal? recovery = null, IEditorWorkspaceStore? workspaceStore = null) : ObservableObject, IAsyncDisposable
{
    private Guid autosaveId = Guid.NewGuid();
    private System.Windows.Threading.DispatcherTimer? autosaveTimer;
    private EditorProject? autosavedProject;
    private EditorProject? observedProject;
    private DateTime autosaveDue;
    private DateTime autosaveDeadline;
    private Task autosaveJob = Task.CompletedTask;
    private void ScheduleAutosave()
    {
        if (recovery is null || disposed) return;
        if (autosaveTimer is null)
        {
            autosaveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
            autosaveTimer.Tick += AutosaveTick; autosaveTimer.Start();
        }
        if (observedProject != Project)
        {
            observedProject = Project; var now = DateTime.UtcNow;
            if (autosaveDeadline == default || autosaveDeadline < now) autosaveDeadline = now.AddSeconds(recovery.Preferences.AutosaveSeconds);
            autosaveDue = new[] { now.AddSeconds(3), autosaveDeadline }.Min();
        }
    }
    private async void AutosaveTick(object? sender, EventArgs e)
    {
        if (disposed || recovery is null || !recovery.Preferences.EditorAutosaveEnabled || !Session.Dirty || !autosaveJob.IsCompleted) return;
        if (DateTime.UtcNow < autosaveDue) return;
        await SaveAutosaveAsync();
    }
    private Task SaveAutosaveAsync()
    {
        if (recovery is null || !recovery.Preferences.EditorAutosaveEnabled || !Session.Dirty || autosavedProject == Project) return Task.CompletedTask;
        var snapshot = Session.Snapshot(); var id = autosaveId; var prior = autosaveJob;
        async Task SaveAsync()
        {
            await prior;
            try { await recovery.SaveEditorAsync(id, snapshot); autosavedProject = snapshot.Project; }
            catch (Exception ex) { Message = "Editor autosave failed: " + ex.Message; }
            autosaveDeadline = DateTime.UtcNow.AddSeconds(recovery.Preferences.AutosaveSeconds);
            autosaveDue = autosavedProject == Project ? autosaveDeadline : DateTime.UtcNow.AddSeconds(3);
        }
        return autosaveJob = SaveAsync();
    }
    private void RetireAutosave()
    {
        if (recovery is null || (autosavedProject is null && autosaveJob.IsCompleted)) return;
        var id = autosaveId; var prior = autosaveJob;
        async Task RetireAsync()
        {
            await prior;
            try { await recovery.DismissAsync(id); }
            catch (FileNotFoundException) { /* A failed autosave may not have created a manifest. */ }
            catch (Exception ex) { Message = "Could not dismiss the previous editor autosave: " + ex.Message; }
        }
        autosaveJob = RetireAsync();
    }
    public void RestoreAutosave(CaptureFlow.Core.Recovery.EditorAutosave snapshot, Guid id)
    {
        ResetProject(snapshot.Project); Session.Restore(snapshot); autosaveId = id; autosavedProject = null;
        SyncMedia(); Notify(); Changed?.Invoke();
        Message = Project.MissingSources.Length == 0 ? "Editor autosave restored with available undo/redo history. Save Project to keep it explicitly." : "Autosave restored; missing imported media: " + string.Join("; ", Project.MissingSources);
    }
    public EditorSession Session { get; } = new();
    public EditorProject Project => Session.Project;
    public ObservableCollection<EditorMediaItem> Media { get; } = [];
    public event Action<string>? PreviewRequested; public event Action? PreviewReleaseRequested;
    public event Action? Changed; public event Action<string>? Exported;
    private CancellationTokenSource? operation; private CancellationTokenSource? previewCancellation; private int projectGeneration; private readonly List<Task> jobs = []; private readonly List<string> artwork = [];
    private EditorClip? copied; private bool disposed;
    private bool batchCancelled;
    private readonly Dictionary<Guid, EditorMediaItem> mediaCache = [];
    private readonly Dictionary<Guid, string[]> artworkPaths = [];
    public string FfmpegPath { get; set; } = "ffmpeg.exe";
    public string OutputFolder { get; set; } = "";
    public string? PreviewPath { get; private set; }
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string message = "Import a recording, add it to a track, then trim, split or adjust it. Sources remain unchanged.";
    [ObservableProperty] private EditorMediaItem? selectedMedia;
    [ObservableProperty] private EditorClip? selectedClip;
    [ObservableProperty] private int selectedTrack = 0;
    [ObservableProperty] private double playhead;
    [ObservableProperty] private double zoom = 40;
    [ObservableProperty] private double progress;
    [ObservableProperty] private bool previewStale = true;
    [ObservableProperty] private string exportFormat = "MP4";
    [ObservableProperty] private string exportResolution = "1080p";
    [ObservableProperty] private int exportFps = 30;
    [ObservableProperty] private int mp3Bitrate = 192;
    [ObservableProperty] private int exportCrf = 20;
    [ObservableProperty] private string previewQuality = "540p";
    public string[] PreviewQualityOptions { get; } = ["360p", "540p", "720p"];
    [ObservableProperty] private double trimIn;
    [ObservableProperty] private double trimOut;
    [ObservableProperty] private double clipStart;
    [ObservableProperty] private double clipSpeed = 1;
    [ObservableProperty] private double clipVolume = 1;
    [ObservableProperty] private bool clipMuted;
    [ObservableProperty] private double fadeIn;
    [ObservableProperty] private double fadeOut;
    [ObservableProperty] private int rotation;
    [ObservableProperty] private double videoScale = 1;
    [ObservableProperty] private string text = "";
    [ObservableProperty] private double textX = .05;
    [ObservableProperty] private double textY = .08;
    [ObservableProperty] private int textSize = 32;
    [ObservableProperty] private string textColor = "#FFFFFF";
    [ObservableProperty] private double trackVolume = 1;
    [ObservableProperty] private bool trackMuted;
    [ObservableProperty] private double silenceThreshold = -40;
    [ObservableProperty] private double minimumSilence = .5;
    private EditorRange[] detectedSilence = []; private Guid? silenceMediaId;
    public string[] FormatOptions { get; } = ["MP4", "MP3", "WAV"];
    public string[] ResolutionOptions { get; } = ["720p", "1080p", "1440p", "4K"];
    public int[] FpsOptions { get; } = [24, 30, 60]; public int[] BitrateOptions { get; } = [128, 192, 256, 320];
    public int[] RotationOptions { get; } = [0, 90, 180, 270]; public string[] TrackOptions { get; } = ["Video", "Audio", "Music"];
    public bool CanEdit => !IsBusy && !disposed;
    public bool HasSelection => SelectedClip is not null && CanEdit && !Project.Tracks.Single(t => t.Id == SelectedClip.TrackId).Locked;
    public bool HasVideoSelection => HasSelection && Project.Tracks.Single(t => t.Id == SelectedClip!.TrackId).Video;
    public string CropDescription => SelectedClip?.Crop is { } c ? $"Crop: {c.Width} × {c.Height} at ({c.X}, {c.Y}) source pixels" : "Crop: original source bounds";
    public string TimeDisplay => $"{EditorSession.Timecode(Playhead)} / {EditorSession.Timecode(Project.Duration)}";
    public string ProjectDisplay => Project.Name + (Session.Dirty ? " · unsaved edits" : "");
    public bool CanUndo => CanEdit && Session.CanUndo; public bool CanRedo => CanEdit && Session.CanRedo;
    partial void OnPlayheadChanged(double value) { OnPropertyChanged(nameof(TimeDisplay)); OnPropertyChanged(nameof(AnimationReadout)); Changed?.Invoke(); }
    partial void OnZoomChanged(double value) { if (!double.IsFinite(value) || value is < .005 or > 2000) zoom = 40; Changed?.Invoke(); }
    partial void OnSelectedMediaChanged(EditorMediaItem? value) => Notify();
    partial void OnIsBusyChanged(bool value) { Notify(); RefreshExportPresentation(); }
    partial void OnSelectedTrackChanged(int value)
    { var track = Project.Tracks.Single(t => t.Id == value); TrackVolume = track.Volume; TrackMuted = track.Muted; Changed?.Invoke(); }
    partial void OnSelectedClipChanged(EditorClip? value)
    {
        if (value is not null)
        {
            TrimIn = value.In; TrimOut = value.Out; ClipStart = value.Start; ClipSpeed = value.Speed; ClipVolume = value.Volume; ClipMuted = value.Muted; FadeIn = value.FadeIn; FadeOut = value.FadeOut; Rotation = value.Rotation; VideoScale = value.VideoScale;
            var overlay = value.Text ?? new(); Text = overlay.Content; TextX = overlay.X; TextY = overlay.Y; TextSize = overlay.Size; TextColor = overlay.Color; SelectedTrack = value.TrackId;
        }
        SelectedKeyframe = value?.Keyframes?.FirstOrDefault(k => k.Id == SelectedKeyframe?.Id);
        if (value is not null && !Project.Tracks.Single(t => t.Id == value.TrackId).Video) AnimationProperty = AnimationProperty.Volume;
        else if (value is not null && AnimationProperty == AnimationProperty.Volume && !Project.Media.Single(m => m.Id == value.MediaId).HasAudio) AnimationProperty = AnimationProperty.Opacity;
        LoadTransition();
        Notify(); Changed?.Invoke();
    }
    private void Notify()
    {
        NotifyPlayback();
        NotifyAdvanced();
        ScheduleAutosave();
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(VideoTrack)); OnPropertyChanged(nameof(AudioTrack)); OnPropertyChanged(nameof(MusicTrack));
        ToggleTrackLockCommand.NotifyCanExecuteChanged(); ToggleTrackMuteCommand.NotifyCanExecuteChanged();
        foreach (var name in new[] { nameof(CanEdit), nameof(HasSelection), nameof(HasVideoSelection), nameof(CropDescription), nameof(CanUndo), nameof(CanRedo), nameof(ProjectDisplay), nameof(TimeDisplay) }) OnPropertyChanged(name);
        foreach (var command in new IRelayCommand[] { ImportCommand, NewCommand, OpenProjectCommand, SaveProjectCommand, AddClipCommand, ApplyClipCommand, ApplyTrackCommand, SplitCommand, DeleteCommand, RippleDeleteCommand, JoinCommand, CopyCommand, CutCommand, PasteCommand, UndoCommand, RedoCommand, ClearCropCommand, PreviewCommand, ExportCommand, DetectSilenceCommand, RemoveSilenceCommand }) command.NotifyCanExecuteChanged();
    }
    private void Commit(Action edit)
    {
        if (!CanEdit) return;
        try
        {
            var selected = SelectedClip?.Id; edit(); var desired = SelectedClip?.Id ?? selected; SelectedClip = Project.Clips.FirstOrDefault(c => c.Id == desired);
            SyncMedia(); PreviewStale = true; PreviewPath = null; ReleasePlayback(); Notify(); Changed?.Invoke(); Message = "Edit applied. Update edited preview to see and hear the result; originals unchanged.";
        }
        catch (Exception ex) { Message = ex.Message; }
    }
    public bool ConfirmDiscard() => !Session.Dirty || MessageBox.Show("Discard unsaved editor changes? Save Project keeps your editable timeline separately from media exports.", "CaptureFlow editor", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    public void ResetProject(EditorProject project) { projectGeneration++; if (previewCancellation is { } pendingPreview) { try { pendingPreview.Cancel(); } catch (ObjectDisposedException) { } } RetireAutosave(); autosaveId = Guid.NewGuid(); autosavedProject = null; observedProject = null; autosaveDue = autosaveDeadline = default; ReleasePlayback(); Session.Open(project); Media.Clear(); SelectedClip = null; SelectedMedia = null; Playhead = 0; PreviewStale = true; PreviewPath = null; Notify(); Changed?.Invoke(); }
    [RelayCommand(CanExecute = nameof(CanEdit))] private void New() { if (ConfirmDiscard()) ResetProject(EditorProject.Empty); }
    [RelayCommand(CanExecute = nameof(CanEdit))] private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Media|*.mp4;*.mp3;*.wav", Multiselect = true, Title = "Import media without modifying sources" };
        batchCancelled = false;
        if (dialog.ShowDialog() == true) foreach (var file in dialog.FileNames) { await ImportFileAsync(file); if (batchCancelled || disposed) break; }
    }
    public async Task ImportFileAsync(string file, bool add = false)
    {
        if (!CanEdit) return;
        await RunAsync(async token =>
        {
            Message = "Importing metadata and generating bounded thumbnail/waveform caches…";
            var importGeneration = projectGeneration;
            var existing = Project.Media.FirstOrDefault(m => string.Equals(m.Path, Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));
            var probed = await backend.ProbeAsync(file, FfmpegPath, token);
            if (disposed) return;
            if (importGeneration != projectGeneration) { Message = "Import result ignored because the project changed."; return; }
            var asset = probed with { Id = existing?.Id ?? probed.Id }; Session.Import(asset);
            if (existing is not null && existing != asset) { var item = Media.FirstOrDefault(m => m.Media.Id == asset.Id); if (item is not null) Media.Remove(item); }
            if (!Media.Any(m => m.Media.Id == asset.Id)) { Media.Add(new(asset, null, [])); SelectedMedia = Media.Last(); await AddArtworkAsync(asset, token); }
            if (disposed) return;
            if (importGeneration != projectGeneration) { Message = "Import result ignored because the project changed."; return; }
            SelectedMedia = Media.Single(m => m.Media.Id == asset.Id);
            if (add) { var track = asset.HasVideo ? 0 : 1; var end = Project.Clips.Where(c => c.TrackId == track).Select(c => c.End).DefaultIfEmpty(0).Max(); var id = Session.Add(asset.Id, track, end); SelectedClip = Project.Clips.Single(c => c.Id == id); }
            PreviewStale = true; PreviewPath = null; ReleasePlayback(); Message = "Imported " + Path.GetFileName(file) + ". Source remains unchanged.";
        });
    }
    private async Task AddArtworkAsync(EditorMedia asset, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (File.Exists(asset.Path) && !SourceStampMatches(asset)) { Message = "Source changed: reimport it before artwork, preview or export."; var staleItem = Media.FirstOrDefault(m => m.Media.Id == asset.Id); if (staleItem is not null) Media[Media.IndexOf(staleItem)] = new(asset, null, []); else Media.Add(new(asset, null, [])); return; }
        if (mediaCache.TryGetValue(asset.Id, out var cached) && cached.Media == asset && SourceStampMatches(asset))
        { var old = Media.FirstOrDefault(m => m.Media.Id == asset.Id); if (old is not null) Media[Media.IndexOf(old)] = cached; else Media.Add(cached); return; }
        // New projects assign new media IDs; unchanged sources must reuse decoded artwork by source identity too.
        var shared = mediaCache.Values.FirstOrDefault(item => string.Equals(item.Media.Path, asset.Path, StringComparison.OrdinalIgnoreCase)
            && (item.Media with { Id = asset.Id, Path = asset.Path }) == asset);
        if (shared is not null && SourceStampMatches(asset))
        {
            var reused = new EditorMediaItem(asset, shared.Thumbnail, shared.Waveform, shared.Filmstrip);
            mediaCache[asset.Id] = reused; TrimMediaCache();
            var prior = Media.FirstOrDefault(m => m.Media.Id == asset.Id); if (prior is not null) Media[Media.IndexOf(prior)] = reused; else Media.Add(reused);
            return;
        }
        var analysisGeneration = projectGeneration;
        EditorAnalysis analysis;
        try { analysis = await backend.AnalyzeAsync(asset, FfmpegPath, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Message = "Artwork unavailable: " + ex.Message; analysis = new(null, []); }
        bool Obsolete() => disposed || analysisGeneration != projectGeneration || !Project.Media.Contains(asset);
        void ReleaseUnpublishedArtwork()
        { foreach (var file in new[] { analysis.Thumbnail, analysis.Filmstrip }.OfType<string>()) DeleteOwnedArtwork(file); }
        if (Obsolete()) { ReleaseUnpublishedArtwork(); return; }
        BitmapImage? thumbnail = null;
        if (analysis.Thumbnail is { } path)
        {
            artwork.Add(path);
            thumbnail = await Task.Run(() => { using var stream = File.OpenRead(path); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image; }, token);
        }
        ImageSource[] filmstrip = [];
        if (analysis.Filmstrip is { } sheetPath && analysis.FilmstripFrames > 0)
        {
            artwork.Add(sheetPath);
            filmstrip = await Task.Run(() =>
            {
                using var stream = File.OpenRead(sheetPath); var sheet = new BitmapImage(); sheet.BeginInit(); sheet.CacheOption = BitmapCacheOption.OnLoad; sheet.StreamSource = stream; sheet.EndInit(); sheet.Freeze();
                return Enumerable.Range(0, analysis.FilmstripFrames).Select(i => { var frame = new CroppedBitmap(sheet, new Int32Rect(i * (sheet.PixelWidth / analysis.FilmstripFrames), 0, sheet.PixelWidth / analysis.FilmstripFrames, sheet.PixelHeight)); frame.Freeze(); return (ImageSource)frame; }).ToArray();
            }, token);
        }
        if (Obsolete()) { ReleaseUnpublishedArtwork(); return; }
        if (artworkPaths.Remove(asset.Id, out var previousPaths)) foreach (var oldPath in previousPaths) DeleteOwnedArtwork(oldPath);
        artworkPaths[asset.Id] = new[] { analysis.Thumbnail, analysis.Filmstrip }.OfType<string>().ToArray();
        var item = new EditorMediaItem(asset, thumbnail, analysis.Waveform, filmstrip); mediaCache[asset.Id] = item;
        TrimMediaCache();
        var previous = Media.FirstOrDefault(m => m.Media.Id == asset.Id); if (previous is not null) Media[Media.IndexOf(previous)] = item; else Media.Add(item);
    }
    private void TrimMediaCache()
    {
        while (mediaCache.Count > 256)
        {
            var evicted = mediaCache.Keys.First(k => !Project.Media.Any(m => m.Id == k)); mediaCache.Remove(evicted);
            if (artworkPaths.Remove(evicted, out var paths)) foreach (var oldPath in paths) DeleteOwnedArtwork(oldPath);
        }
    }
    private static bool SourceStampMatches(EditorMedia asset)
    { try { var info = new FileInfo(asset.Path); return info.Exists && info.Length == asset.Bytes && info.LastWriteTimeUtc.Ticks == asset.ModifiedTicks; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } }
    private void DeleteOwnedArtwork(string path)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CaptureFlow-editor-artwork")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _) || Path.GetExtension(path) != ".jpg") return;
        try { File.Delete(path); artwork.Remove(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private void SyncMedia()
    {
        var selected = SelectedMedia?.Media.Id;
        foreach (var item in Media.Where(item => !Project.Media.Any(m => m.Id == item.Media.Id)).ToArray()) Media.Remove(item);
        foreach (var asset in Project.Media)
            if (!Media.Any(item => item.Media.Id == asset.Id)) Media.Add(mediaCache.TryGetValue(asset.Id, out var cached) && cached.Media == asset && SourceStampMatches(asset) ? cached : new(asset, null, []));
        SelectedMedia = Media.FirstOrDefault(item => item.Media.Id == selected);
    }
    [RelayCommand(CanExecute = nameof(CanEdit))] private async Task OpenProjectAsync()
    {
        if (!ConfirmDiscard()) return; var dialog = new OpenFileDialog { Filter = "CaptureFlow editable project|*.cfedit" };
        if (dialog.ShowDialog() != true) return;
        await RunAsync(async token =>
        {
            var project = await store.LoadAsync(dialog.FileName, token); ResetProject(project);
            foreach (var asset in project.Media) if (File.Exists(asset.Path)) await AddArtworkAsync(asset, token); else Media.Add(new(asset, null, []));
            Message = project.MissingSources.Length == 0 ? "Project reopened. Update edited preview." : "Missing sources — restore these files before preview/export: " + string.Join("; ", project.MissingSources);
        });
    }
    [RelayCommand(CanExecute = nameof(CanEdit))] private async Task SaveProjectAsync()
    {
        var dialog = new SaveFileDialog { Filter = "CaptureFlow editable project|*.cfedit", DefaultExt = ".cfedit", FileName = Project.Name + ".cfedit", OverwritePrompt = true };
        if (dialog.ShowDialog() == true) await RunAsync(async token =>
        {
            await store.SaveAsync(Project, dialog.FileName, token); await autosaveJob; Session.MarkSaved();
            if (recovery is not null && autosavedProject is not null)
            {
                await using var file = File.OpenRead(dialog.FileName);
                await recovery.PrepareOutputAsync(autosaveId, Path.GetFullPath(dialog.FileName), Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(file, token)), token);
                await recovery.UpdateAsync(autosaveId, "EditorSaved", Project.Duration, Path.GetFullPath(dialog.FileName), token);
            }
            Message = "Editable project saved; imported media must stay at their referenced locations.";
        });
    }
    private bool CanAdd => CanEdit && SelectedMedia is not null;
    [RelayCommand(CanExecute = nameof(CanAdd))] private void AddClip() => Commit(() => { var id = Session.Add(SelectedMedia!.Media.Id, SelectedTrack, Playhead); SelectedClip = Project.Clips.Single(c => c.Id == id); });
    [RelayCommand(CanExecute = nameof(HasSelection))] private void ApplyClip() => Commit(() => Session.Replace(SelectedClip! with { Start = ClipStart, In = TrimIn, Out = TrimOut, Speed = ClipSpeed, Volume = ClipVolume, Muted = ClipMuted, FadeIn = FadeIn, FadeOut = FadeOut, Rotation = Rotation, VideoScale = VideoScale, Text = new(Text, TextX, TextY, TextSize, TextColor) }));
    [RelayCommand(CanExecute = nameof(CanEdit))] private void ApplyTrack() => Commit(() => Session.Apply(Project with { Tracks = Project.Tracks.Select(t => t.Id == SelectedTrack ? t with { Volume = TrackVolume, Muted = TrackMuted } : t).ToArray() }));
    [RelayCommand(CanExecute = nameof(HasSelection))] private void Split() => Commit(() => Session.Split(SelectedClip!.Id, Playhead));
    [RelayCommand(CanExecute = nameof(HasSelection))] private void Delete() => Commit(() => Session.Delete(SelectedClip!.Id));
    [RelayCommand(CanExecute = nameof(HasSelection))] private void RippleDelete() => Commit(() => Session.Delete(SelectedClip!.Id, true));
    [RelayCommand(CanExecute = nameof(CanEdit))] private void Join() => Commit(() => Session.JoinTrack(SelectedTrack));
    [RelayCommand(CanExecute = nameof(HasSelection))] private void Copy() { copied = SelectedClip; Notify(); Message = "Clip copied. Move the playhead to a free position and Paste."; }
    [RelayCommand(CanExecute = nameof(HasSelection))] private void Cut() { copied = SelectedClip; Delete(); }
    private bool CanPaste => CanEdit && copied is not null && Project.Media.Any(m => m.Id == copied.MediaId);
    [RelayCommand(CanExecute = nameof(CanPaste))] private void Paste() => Commit(() => Session.Paste(copied!, Playhead));
    [RelayCommand(CanExecute = nameof(CanUndo))] private void Undo() => Commit(Session.Undo);
    [RelayCommand(CanExecute = nameof(CanRedo))] private void Redo() => Commit(Session.Redo);
    [RelayCommand(CanExecute = nameof(HasSelection))] private void ClearCrop() => Commit(() => Session.Replace(SelectedClip! with { Crop = null }));
    public void SetCrop(EditorCrop crop) => Commit(() => { if (SelectedClip is { } clip) Session.Replace(clip with { Crop = crop }); });
    public void MoveClip(Guid id, double start) => Commit(() => Session.Move(id, start, Playhead, 8 / Zoom));
    private bool CanRender => CanEdit && Project.Clips.Length != 0;
    [RelayCommand(CanExecute = nameof(CanRender))] private async Task PreviewAsync() => await RunAsync(async token =>
    {
        PreviewStale = true; PreviewPath = null; ReleasePlayback(); Message = "Rendering edited preview…";
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-editor-preview", Guid.NewGuid().ToString("N"));
        var hasVideo = Project.Clips.Any(c => Project.Tracks.Single(t => t.Id == c.TrackId).Video);
        var size = PreviewQuality switch { "360p" => (640, 360), "720p" => (1280, 720), _ => (960, 540) };
        var snapshot = Project;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token); previewCancellation = linked;
        string path;
        try { path = await backend.RenderAsync(snapshot, new(hasVideo ? "MP4" : "MP3", size.Item1, size.Item2, 30, 28, 128), folder, FfmpegPath, new Progress<double>(v => Progress = v), linked.Token); }
        finally { if (ReferenceEquals(previewCancellation, linked)) previewCancellation = null; }
        linked.Token.ThrowIfCancellationRequested();
        if (disposed || !ReferenceEquals(snapshot, Project)) { Message = "Obsolete preview ignored; update the current project preview."; return; }
        PreviewPath = path; PreviewStale = false; PreviewRequested?.Invoke(path); Message = "Edited preview ready. " + (hasVideo ? PreviewQuality + " video, 30 FPS; " : "128 kbps audio; ") + "rendered transforms, gaps and audio mix use the export engine.";
    });
    private EditorExportSettings ExportSettings => new(ExportFormat, ExportResolution switch { "720p" => 1280, "1440p" => 2560, "4K" => 3840, _ => 1920 }, ExportResolution switch { "720p" => 720, "1440p" => 1440, "4K" => 2160, _ => 1080 }, ExportFps, ExportCrf, Mp3Bitrate);
    [RelayCommand(CanExecute = nameof(CanRender))] private Task ExportAsync() => RunExportAsync();
    [RelayCommand] private void Cancel() => operation?.Cancel();
    [RelayCommand(CanExecute = nameof(HasSelection))] private async Task DetectSilenceAsync()
    {
        var clip = SelectedClip!; var media = Project.Media.Single(m => m.Id == clip.MediaId);
        await RunAsync(async token => { detectedSilence = await backend.DetectSilenceAsync(media, SilenceThreshold, MinimumSilence, FfmpegPath, token); silenceMediaId = media.Id; Message = "Detected silence (source times): " + string.Join("; ", detectedSilence.Take(20).Select(r => $"{r.Start:0.000}–{r.End:0.000}s")) + ". Review source audio before Remove silence; downstream clips ripple together."; });
    }
    private bool CanRemoveSilence => HasSelection && SelectedClip!.MediaId == silenceMediaId && detectedSilence.Length > 0;
    [RelayCommand(CanExecute = nameof(CanRemoveSilence))] private void RemoveSilence() => Commit(() => Session.RemoveSilence(SelectedClip!.Id, detectedSilence));
    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        if (!CanEdit) return; IsBusy = true; Progress = 0;
        using var owned = new CancellationTokenSource(); operation = owned;
        try { var job = work(owned.Token); jobs.RemoveAll(t => t.IsCompleted); jobs.Add(job); await job; }
        catch (OperationCanceledException) { batchCancelled = true; Message = "Cancelled. Sources unchanged; render recovery folders are retained."; }
        catch (Exception ex) { Message = ex.Message; }
        finally { operation = null; IsBusy = false; Notify(); Changed?.Invoke(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; operation?.Cancel(); StopExportClock(); ReleasePlayback();
        await workspaceSave;
        if (autosaveTimer is not null) { autosaveTimer.Stop(); autosaveTimer.Tick -= AutosaveTick; }
        try { await SaveAutosaveAsync().WaitAsync(TimeSpan.FromSeconds(5)); await autosaveJob.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError("Editor autosave shutdown: {0}", ex.Message); }
        try { await Task.WhenAll(jobs); } catch (Exception) { /* Failed jobs already report errors; shutdown still releases assets. */ }
        foreach (var file in artwork.ToArray()) DeleteOwnedArtwork(file);
        PreviewRequested = null; PreviewReleaseRequested = null; Changed = null; Exported = null;
    }
}

