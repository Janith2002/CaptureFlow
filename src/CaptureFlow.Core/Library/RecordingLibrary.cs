namespace CaptureFlow.Core.Library;

public enum RecordingType { Screen, Webcam, ScreenAndWebcam, Audio, OtherVideo }
public sealed record RecordingEntry(Guid Id, string FullPath, string Folder, RecordingType Type, string Format,
    TimeSpan? Duration, long Bytes, DateTimeOffset Created, DateTimeOffset Modified, bool IsMissing = false, string? Warning = null)
{
    public string Name => Path.GetFileName(FullPath);
}
public sealed record PendingRename(Guid Id, string OldPath, string NewPath);
public interface IRecordingRepository
{
    Task<IReadOnlyList<RecordingEntry>> GetFolderAsync(string folder, CancellationToken token);
    Task SaveAsync(IReadOnlyList<RecordingEntry> entries, CancellationToken token);
    Task BeginRenameAsync(PendingRename rename, CancellationToken token);
    Task CompleteRenameAsync(RecordingEntry entry, CancellationToken token);
    Task CancelRenameAsync(Guid id, CancellationToken token);
    Task<IReadOnlyList<PendingRename>> GetRenamesAsync(CancellationToken token);
    Task RemoveAsync(Guid id, CancellationToken token);
}
public interface IMediaMetadataReader { Task<TimeSpan?> ReadDurationAsync(string path, CancellationToken token); }
public interface IRecordingFileActions
{
    void Rename(string oldPath, string newPath);
    Task RecycleAsync(string path, CancellationToken token);
}
public interface IRecordingLibraryService
{
    Task<IReadOnlyList<RecordingEntry>> DiscoverAsync(string folder, CancellationToken token);
    Task<RecordingEntry> RenameAsync(RecordingEntry entry, string name, CancellationToken token);
    Task<bool> DeleteAsync(RecordingEntry entry, bool confirmed, CancellationToken token);
}
public interface IRecordingThumbnailProvider { Task<string?> GetAsync(RecordingEntry entry, string ffmpeg, CancellationToken token); }
