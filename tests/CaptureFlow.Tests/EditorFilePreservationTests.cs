using CaptureFlow.Application.Editing;
using CaptureFlow.Core.Editing;
using CaptureFlow.Infrastructure.Editing;
namespace CaptureFlow.Tests;
public class EditorFilePreservationTests
{
    [Fact] public async Task MissingOrChangedSourcesFailBeforeStartingFfmpegOrCreatingExport()
    {
        var path = Path.Combine(Path.GetTempPath(), "CaptureFlow-editor-source-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[10]); var info = new FileInfo(path);
            var media = new EditorMedia(Guid.NewGuid(), path, "WAV", 1, 0, 0, false, true, info.Length, info.LastWriteTimeUtc.Ticks);
            var session = new EditorSession(); session.Import(media); session.Add(media.Id, 1, 0);
            await File.WriteAllBytesAsync(path, new byte[20]);
            var backend = new FfmpegEditorBackend();
            var changed = await Assert.ThrowsAsync<IOException>(() => backend.RenderAsync(session.Project, new("WAV"), Path.GetTempPath(), "never-start-this.exe", null, default));
            Assert.Contains("changed", changed.Message); Assert.Equal(20, new FileInfo(path).Length);
            File.Delete(path);
            var missing = await Assert.ThrowsAsync<IOException>(() => backend.RenderAsync(session.Project, new("WAV"), Path.GetTempPath(), "never-start-this.exe", null, default));
            Assert.Contains("missing", missing.Message);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
