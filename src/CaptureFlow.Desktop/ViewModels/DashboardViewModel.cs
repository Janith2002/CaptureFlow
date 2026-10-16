using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaptureFlow.Application.Library;
using CaptureFlow.Core.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
namespace CaptureFlow.Desktop.ViewModels;
public partial class DashboardViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IRecordingLibraryService library;
    private readonly IRecordingThumbnailProvider thumbnails;
    private readonly List<Task> jobs = [];
    private CancellationTokenSource? loadCancellation;
    private int libraryRefreshVersion;
    private bool disposed;
    public event Action<string>? PlaybackRequested;
    public event Action? PlaybackReleaseRequested;
    private string selectedPage = "Dashboard";
    public string SelectedPage
    {
        get => selectedPage;
        set
        {
            if (selectedPage == value) return;
            if (!SetProperty(ref selectedPage, value)) return;
            foreach (var name in new[] { nameof(IsDashboard), nameof(IsScreenRecorder), nameof(IsAudioRecorder), nameof(IsRecordings), nameof(IsSettings), nameof(IsEditor), nameof(PageTitle), nameof(WorkspaceTitle), nameof(PageDescription) }) OnPropertyChanged(name);
            OnSelectedPageChanged(value);
        }
    }
    public EditorViewModel? Editor { get; }
    public bool IsEditor => SelectedPage == "Video & Audio Editor";
    public bool IsDashboard => SelectedPage == "Dashboard";
    public bool IsScreenRecorder => SelectedPage == "Screen Recorder";
    public bool IsAudioRecorder => SelectedPage == "Audio Recorder";
    public bool IsRecordings => SelectedPage == "Recordings";
    public bool IsSettings => SelectedPage == "Settings";
    public string PageTitle => SelectedPage;
    public string WorkspaceTitle => SelectedPage switch
    {
        "Screen Recorder" => "Recording Studio",
        "Video & Audio Editor" => "Video Editor",
        "Recordings" => "Recordings Library",
        _ => SelectedPage
    };
    public string PageDescription => SelectedPage switch
    {
        "Screen Recorder" => "Record a screen, application window, custom region or webcam with optional audio.",
        "Audio Recorder" => "Capture preferences for voice, desktop audio, or both — without video.",
        "Recordings" => "Your captures, organized and ready to play.",
        "Settings" => "Recording preferences, including saved video quality, shortcut and tray settings.",
        "Video & Audio Editor" => "A non-destructive timeline for your recordings. Preview edits and export new media.",
        _ => "Welcome to CaptureFlow. Choose what you want to record."
    };
    [ObservableProperty] private int selectedSettingsTab;
    [RelayCommand] private void ManageStorage() { SelectedSettingsTab = 3; SelectedPage = "Settings"; }
    public ScreenRecorderViewModel Screen { get; }
    public AudioRecorderViewModel Audio { get; }
    public ShortcutSettingsViewModel Shortcuts { get; }
    public RecordingExperienceViewModel Experience { get; }
    public RecoveryViewModel? Recovery { get; }
    public ExternalToolsViewModel? ExternalTools { get; }
    [RelayCommand] private void ShowRecovery() { SelectedSettingsTab = 6; SelectedPage = "Settings"; }
    [ObservableProperty] private string libraryStatus = "Choose a folder to browse recordings.";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    private string outputFolder = Environment.GetEnvironmentVariable("CAPTUREFLOW_LIBRARY_INITIAL_FOLDER") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "CaptureFlow");
    [ObservableProperty] private string selectedFilter = "All Recordings";
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string selectedSort = "Newest first";
    [ObservableProperty] private bool isLibraryBusy;
    [ObservableProperty] private string renameText = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayRecordingCommand), nameof(EditRecordingCommand), nameof(OpenRecordingFolderCommand), nameof(RenameRecordingCommand), nameof(DeleteRecordingCommand))]
    private RecordingItem? selectedRecording;
    public string[] FilterOptions => RecordingLibraryQuery.Filters;
    public string[] SortOptions => RecordingLibraryQuery.Sorts;
    public RecordingCollection<RecordingItem> Recordings { get; } = new();
    public ICollectionView RecordingsView { get; }
    public IEnumerable<RecordingItem> RecentRecordings => Recordings.Where(e => !e.IsMissing).OrderByDescending(r => r.Created).Take(5);
    public bool HasNoRecordings => !Recordings.Any(e => !e.IsMissing);
    public bool HasNoMatchingRecordings => RecordingsView.IsEmpty;
    public DashboardViewModel(AudioRecorderViewModel audio, ScreenRecorderViewModel screen, IRecordingLibraryService library, IRecordingThumbnailProvider thumbnails, ShortcutSettingsViewModel shortcuts, RecordingExperienceViewModel experience, EditorViewModel? editor = null, RecoveryViewModel? recovery = null, ExternalToolsViewModel? externalTools = null)
    {
        Audio = audio; Screen = screen; this.library = library; this.thumbnails = thumbnails; Shortcuts = shortcuts; Experience = experience;
        Editor = editor;
        Recovery = recovery;
        ExternalTools = externalTools;
        if (recovery is not null) { recovery.FfmpegPath = () => Screen.FfmpegPath; recovery.Recovered += () => { _ = RefreshRecordingsAsync(); }; }
        if (editor is not null)
        {
            editor.OutputFolder = OutputFolder; editor.FfmpegPath = screen.FfmpegPath;
            editor.Exported += path => { _ = RefreshRecordingsAsync(); };
            editor.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(EditorViewModel.CanEdit)) EditRecordingCommand.NotifyCanExecuteChanged(); };
        }
        Audio.FfmpegPath = Screen.FfmpegPath;
        Screen.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(Screen.FfmpegPath)) { Audio.FfmpegPath = Screen.FfmpegPath; if (Editor is not null) Editor.FfmpegPath = Screen.FfmpegPath; } };
        Audio.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(Audio.FfmpegPath)) Screen.FfmpegPath = Audio.FfmpegPath; };
        Screen.OutputDirectory = OutputFolder; Audio.OutputDirectory = OutputFolder;
        Screen.RecordingSaved += (_, _) => _ = RefreshRecordingsAsync();
        Audio.RecordingSaved += (_, _) => _ = RefreshRecordingsAsync();
        Screen.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(Screen.CanEditPreferences)) ChooseFolderCommand.NotifyCanExecuteChanged(); };
        Audio.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(Audio.CanEditPreferences)) ChooseFolderCommand.NotifyCanExecuteChanged(); };
        RecordingsView = CollectionViewSource.GetDefaultView(Recordings);
        RecordingsView.Filter = item => item is RecordingItem recording && RecordingLibraryQuery.Matches(recording.Entry, SearchText, SelectedFilter);
        ApplySort(); _ = RefreshRecordingsAsync();
    }
    [RelayCommand] private void Navigate(string page) { if (page is "Dashboard" or "Screen Recorder" or "Audio Recorder" or "Recordings" or "Settings" or "Video & Audio Editor") SelectedPage = page; }
    private void OnSelectedPageChanged(string value) { if (value != "Recordings") PlaybackReleaseRequested?.Invoke(); else _ = RefreshRecordingsAsync(); }
    partial void OnSelectedFilterChanged(string value) => UpdateView();
    partial void OnSearchTextChanged(string value) => UpdateView();
    partial void OnSelectedSortChanged(string value) { ApplySort(); UpdateView(); }
    partial void OnSelectedRecordingChanged(RecordingItem? value) => RenameText = value?.Name ?? "";
    partial void OnOutputFolderChanged(string value) { Audio.OutputDirectory = value; Screen.OutputDirectory = value; if (Editor is not null) Editor.OutputFolder = value; PlaybackReleaseRequested?.Invoke(); Recordings.Clear(); SelectedRecording = null; OnPropertyChanged(nameof(HasNoRecordings)); OnPropertyChanged(nameof(RecentRecordings)); _ = RefreshRecordingsAsync(); }
    partial void OnIsLibraryBusyChanged(bool value) { RenameRecordingCommand.NotifyCanExecuteChanged(); DeleteRecordingCommand.NotifyCanExecuteChanged(); }
    private void ApplySort()
    {
        RecordingsView.SortDescriptions.Clear();
        RecordingsView.SortDescriptions.Add(SelectedSort switch
        {
            "Oldest first" => new(nameof(RecordingItem.Created), ListSortDirection.Ascending),
            "Filename" => new(nameof(RecordingItem.Name), ListSortDirection.Ascending),
            "Duration" => new(nameof(RecordingItem.DurationTicks), ListSortDirection.Descending),
            "File size" => new(nameof(RecordingItem.Bytes), ListSortDirection.Descending),
            _ => new(nameof(RecordingItem.Created), ListSortDirection.Descending)
        });
    }
    private void UpdateView() { RecordingsView.Refresh(); OnPropertyChanged(nameof(HasNoMatchingRecordings)); }
    [RelayCommand(CanExecute = nameof(CanChooseFolder))] private void ChooseFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder for screen and audio recordings", Multiselect = false };
        if (Directory.Exists(OutputFolder)) dialog.InitialDirectory = OutputFolder;
        if (dialog.ShowDialog() == true) OutputFolder = dialog.FolderName;
    }
    private bool CanChooseFolder() => Audio.CanEditPreferences && Screen.CanEditPreferences;
    private bool CanOpenFolder() => Directory.Exists(OutputFolder);
    [RelayCommand(CanExecute = nameof(CanOpenFolder))] private void OpenFolder() => OpenPath(OutputFolder);
    private bool CanUseRecording() => SelectedRecording is { IsMissing: false };
    private bool CanManageRecording() => SelectedRecording is not null && !IsLibraryBusy;
    private bool CanEditRecording() => CanUseRecording() && Editor?.CanEdit == true;
    [RelayCommand(CanExecute = nameof(CanEditRecording))] private async Task EditRecordingAsync()
    {
        if (SelectedRecording is null || Editor is null || !Editor.CanEdit || !Editor.ConfirmDiscard()) return;
        Editor.ResetProject(CaptureFlow.Core.Editing.EditorProject.Empty);
        SelectedPage = "Video & Audio Editor"; await Editor.ImportFileAsync(SelectedRecording.FullPath, true);
    }
    private bool CanRenameRecording() => CanManageRecording() && CanUseRecording();
    [RelayCommand(CanExecute = nameof(CanUseRecording))] private void PlayRecording() { if (SelectedRecording is not null) PlaybackRequested?.Invoke(SelectedRecording.FullPath); }
    [RelayCommand(CanExecute = nameof(CanManageRecording))] private void OpenRecordingFolder() { if (SelectedRecording is not null) OpenPath(Path.GetDirectoryName(SelectedRecording.FullPath)!); }
    private void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException) { LibraryStatus = "Could not open the selected location. " + ex.Message; }
    }
    [RelayCommand(CanExecute = nameof(CanRenameRecording))] private async Task RenameRecordingAsync()
    {
        if (SelectedRecording is null) return;
        var entry = SelectedRecording.Entry; PlaybackReleaseRequested?.Invoke(); IsLibraryBusy = true;
        try { var operation = library.RenameAsync(entry, RenameText, CancellationToken.None); jobs.Add(operation); await operation; IsLibraryBusy = false; await RefreshRecordingsAsync(); LibraryStatus = "Recording renamed."; }
        catch (Exception ex) { LibraryStatus = "Rename failed: " + ex.Message; }
        finally { IsLibraryBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanManageRecording))] private async Task DeleteRecordingAsync()
    {
        if (SelectedRecording is null) return;
        var entry = SelectedRecording.Entry;
        var prompt = entry.IsMissing ? "Remove this missing entry from the catalog?" : "Move this recording to the Windows Recycle Bin?";
        if (MessageBox.Show(prompt + "\n\n" + entry.FullPath, "CaptureFlow · Delete recording", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        PlaybackReleaseRequested?.Invoke(); IsLibraryBusy = true;
        try { var operation = library.DeleteAsync(entry, true, CancellationToken.None); jobs.Add(operation); await operation; IsLibraryBusy = false; await RefreshRecordingsAsync(); LibraryStatus = entry.IsMissing ? "Missing entry removed." : "Delete completed. Refresh reconciled the catalog."; }
        catch (OperationCanceledException) { LibraryStatus = "Delete cancelled."; }
        catch (Exception ex) { LibraryStatus = "Delete failed: " + ex.Message + " Refresh to reconcile the catalog."; }
        finally { IsLibraryBusy = false; }
    }
    [RelayCommand] private async Task RefreshRecordingsAsync()
    {
        if (disposed) return;
        var version = ++libraryRefreshVersion;
        loadCancellation?.Cancel(); loadCancellation?.Dispose(); loadCancellation = new(); var token = loadCancellation.Token;
        IsLibraryBusy = true; LibraryStatus = "Loading recordings and metadata…";
        try
        {
            var selectedId = SelectedRecording?.Entry.Id;
            var load = library.DiscoverAsync(OutputFolder, token); jobs.RemoveAll(t => t.IsCompleted); jobs.Add(load);
            var entries = await load;
            if (disposed || version != libraryRefreshVersion) return;
            Recordings.ReplaceAll(entries.Select(entry => new RecordingItem(entry)));
            SelectedRecording = Recordings.FirstOrDefault(e => e.Entry.Id == selectedId);
            LibraryStatus = $"{entries.Count} recordings · {entries.Count(e => e.IsMissing)} missing · MP4, MP3 and WAV";
            UpdateView(); OnPropertyChanged(nameof(HasNoRecordings)); OnPropertyChanged(nameof(RecentRecordings)); OpenFolderCommand.NotifyCanExecuteChanged();
            var thumbnailsJob = LoadThumbnailsAsync(Recordings.ToArray(), token); jobs.Add(thumbnailsJob);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (version == libraryRefreshVersion) LibraryStatus = "Unable to load the library. " + ex.Message; }
        finally { if (version == libraryRefreshVersion && !disposed) IsLibraryBusy = false; }
    }
    private async Task LoadThumbnailsAsync(RecordingItem[] items, CancellationToken token)
    {
        try
        {
            foreach (var batch in items.Where(i => i.Format == "MP4" && !i.IsMissing).Chunk(2))
                await Task.WhenAll(batch.Select(async item =>
                {
                    var path = await thumbnails.GetAsync(item.Entry, Screen.FfmpegPath, token);
                    if (path is null || token.IsCancellationRequested || disposed) return;
                    var bitmap = await Task.Run(() => { using var stream = File.OpenRead(path); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image; }, token);
                    if (!token.IsCancellationRequested && !disposed) item.Thumbnail = bitmap;
                }));
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Optional artwork never prevents catalog access or playback. */ }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true; loadCancellation?.Cancel(); PlaybackReleaseRequested?.Invoke();
        if (Recovery is not null) await Recovery.DisposeAsync();
        if (Editor is not null) await Editor.DisposeAsync();
        try { await Task.WhenAll(jobs.ToArray()); } catch (Exception) { }
        loadCancellation?.Dispose(); loadCancellation = null;
    }
}
public partial class RecordingItem(RecordingEntry entry) : ObservableObject
{
    public RecordingEntry Entry { get; } = entry;
    public string FullPath => Entry.FullPath;
    public string Name => Entry.Name;
    public DateTimeOffset Created => Entry.Created;
    public DateTime Modified => Entry.Modified.LocalDateTime;
    public long Bytes => Entry.Bytes;
    public string Format => Entry.Format;
    public string Kind => Format == "MP4" ? "Video" : "Audio";
    public bool IsMissing => Entry.IsMissing;
    public string Type => Entry.Type switch { RecordingType.Screen => "Screen", RecordingType.Webcam => "Webcam", RecordingType.ScreenAndWebcam => "Screen + Webcam", RecordingType.Audio => "Audio", _ => "Video · Imported" };
    public string Duration => Entry.Duration is { } duration ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}" : "Unavailable";
    public long DurationTicks => Entry.Duration?.Ticks ?? -1;
    public string Date => Entry.Created.LocalDateTime.ToString("dd MMM yyyy · HH:mm");
    public string Size => Bytes >= 1073741824 ? $"{Bytes / 1073741824d:0.0} GB" : Bytes >= 1048576 ? $"{Bytes / 1048576d:0.0} MB" : $"{Bytes / 1024d:0.0} KB";
    public string Availability => IsMissing ? "Missing" : Entry.Warning is not null ? "Check media" : "Available";
    public string? Warning => Entry.Warning;
    public string Icon => Format == "MP4" ? "\uE714" : "\uE8D6";
    [ObservableProperty] private ImageSource? thumbnail;
}

