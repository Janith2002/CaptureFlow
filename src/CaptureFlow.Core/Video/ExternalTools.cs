namespace CaptureFlow.Core.Video;
public interface IFfmpegPreferencesStore
{
    Task<string> LoadAsync(CancellationToken token = default);
    Task SaveAsync(string executable, CancellationToken token = default);
}
