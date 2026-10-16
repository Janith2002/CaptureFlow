using System.Diagnostics;
using System.Text.Json;
using CaptureFlow.Core.Editing;
using CaptureFlow.Application.Editing;
using CaptureFlow.Infrastructure.Editing;
namespace CaptureFlow.Tests;
public partial class FfmpegEditorTests
{
    [FfmpegTheory] [InlineData(true)]
    public async Task UndoRedoProducesCorrectDecodedFramesAudioAndStreamTimestamps(bool generatedOnly)
    {
        Assert.True(generatedOnly); var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-editor-release-history-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(folder, "source.mp4"); await Generate(source, "color=c=red:size=128x72:rate=30:duration=1");
            var backend = new FfmpegEditorBackend(); var media = await backend.ProbeAsync(source, Ffmpeg, default); var session = new EditorSession(); session.Import(media); var id = session.Add(media.Id, 0, 0); var baseline = session.Project;
            session.SetKeyframe(id, K(AnimationProperty.Opacity, 0, .2)); session.SetKeyframe(id, K(AnimationProperty.Volume, 0, .25));
            var edited = await backend.RenderAsync(session.Project, new("MP4", 128, 72), folder, Ffmpeg, null, default);
            session.Undo(); session.Undo(); Assert.Same(baseline, session.Project);
            var original = await backend.RenderAsync(session.Project, new("MP4", 128, 72), folder, Ffmpeg, null, default);
            session.Redo(); session.Redo(); var redo = await backend.RenderAsync(session.Project, new("MP4", 128, 72), folder, Ffmpeg, null, default);
            foreach (var (file, min, max) in new[] { (edited, 40, 60), (original, 240, 255), (redo, 40, 60) })
            {
                var frames = await DecodeAsync(file, [], ["-vf", "format=rgb24,crop=1:1:64:36", "-pix_fmt", "rgb24", "-f", "rawvideo"]);
                Assert.Equal(30 * 3, frames.Length); for (var i = 0; i < frames.Length; i += 3) Assert.InRange((int)frames[i], min, max);
                await CheckStreamTiming(file);
            }
            async Task<double> Rms(string file)
            { var pcm = await DecodeAsync(file, [], ["-vn", "-ac", "1", "-ar", "48000", "-f", "s16le"]); var values = Enumerable.Range(4800, 38400).Select(i => BitConverter.ToInt16(pcm, i * 2) / 32768d); return Math.Sqrt(values.Select(v => v * v).Average()); }
            var loud = await Rms(original); Assert.True(loud > .05); Assert.InRange(await Rms(edited) / loud, .22, .28); Assert.InRange(await Rms(redo) / loud, .22, .28);
        }
        finally { DeleteOwned(folder); }
    }
    private static async Task CheckStreamTiming(string file)
    {
        var info = new ProcessStartInfo(Executable.Find("ffprobe.exe")!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-count_frames", "-show_entries", "stream=codec_type,start_time,duration,nb_read_frames", "-of", "json", file }) info.ArgumentList.Add(arg);
        using var probe = Process.Start(info)!; var output = probe.StandardOutput.ReadToEndAsync(); var errors = probe.StandardError.ReadToEndAsync(); await probe.WaitForExitAsync(); Assert.True(probe.ExitCode == 0, await errors);
        using var document = JsonDocument.Parse(await output); var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray(); Assert.Equal(2, streams.Length);
        foreach (var stream in streams)
        { Assert.InRange(double.Parse(stream.GetProperty("start_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture), 0, .001); Assert.InRange(double.Parse(stream.GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture), .99, 1.01); }
        Assert.Equal("30", streams.Single(s => s.GetProperty("codec_type").GetString() == "video").GetProperty("nb_read_frames").GetString());
    }
}
