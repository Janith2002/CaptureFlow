using CaptureFlow.Core.Library;

namespace CaptureFlow.Desktop.Services;

public sealed class WindowsShellMetadataReader : IMediaMetadataReader
{
    public Task<TimeSpan?> ReadDurationAsync(string path, CancellationToken token) => Task.Run<TimeSpan?>(() =>
    {
        token.ThrowIfCancellationRequested();
        var duration = RecordingMetadata.ReadDurationValue(System.IO.Path.GetDirectoryName(path)!, System.IO.Path.GetFileName(path));
        token.ThrowIfCancellationRequested();
        return duration;
    }, token);
}
