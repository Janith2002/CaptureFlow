using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Ffmpeg;
namespace CaptureFlow.Infrastructure.Video;
public sealed class FfmpegVideoExportProcess : IVideoExportProcess
{
    public Task RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        FfmpegProcess.RunAsync(executable, arguments, TimeSpan.FromHours(2), cancellationToken);
}
