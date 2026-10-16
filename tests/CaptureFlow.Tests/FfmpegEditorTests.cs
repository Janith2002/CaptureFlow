using System.Diagnostics;
using System.Security.Cryptography;
using CaptureFlow.Application.Editing;
using CaptureFlow.Core.Editing;
using CaptureFlow.Infrastructure.Editing;
namespace CaptureFlow.Tests;

public partial class FfmpegEditorTests
{
    private static void DeleteOwned(string folder)
    {
        var full = Path.GetFullPath(folder); var prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "CaptureFlow-editor-");
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Refusing cleanup outside the owned editor test folders.");
        Directory.Delete(full, true);
    }
    private static string Ffmpeg => Executable.Find("ffmpeg.exe")!;
    private static async Task Generate(string output, string video, int rate = 44100, bool silence = false)
    {
        var info = new ProcessStartInfo(Ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        var args = new List<string> { "-v", "error", "-nostdin", "-n" };
        if (video.Length > 0) args.AddRange(["-f", "lavfi", "-i", video]);
        args.AddRange(["-f", "lavfi", "-i", silence ? $"anullsrc=r={rate}:cl=stereo:d=1" : $"sine=frequency=440:sample_rate={rate}:duration=1", "-t", "1"]);
        if (video.Length > 0) args.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac"]);
        else args.AddRange(["-c:a", output.EndsWith(".mp3") ? "libmp3lame" : "pcm_s16le"]);
        foreach (var arg in args.Append(output)) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!; var errors = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); Assert.True(process.ExitCode == 0, await errors);
    }
    [FfmpegTheory] [InlineData(30)] [InlineData(60)]
    public async Task CancellationAfterEncoderStartsRetainsRecoveryAndDoesNotPublish(int framesPerSecond)
    {
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-editor-active-cancel-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var input = Path.Combine(folder, "input.wav"); await Generate(input, ""); var backend = new FfmpegEditorBackend(); var media = await backend.ProbeAsync(input, Ffmpeg, default);
            var session = new EditorSession(); session.Import(media); session.Add(media.Id, 1, 0);
            using var cancel = new CancellationTokenSource(); var reports = 0;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backend.RenderAsync(session.Project, new("MP4", 1920, 1080, framesPerSecond), folder, Ffmpeg, new InlineProgress(_ => { reports++; cancel.Cancel(); }), cancel.Token));
            Assert.True(reports > 0); Assert.Single(Directory.GetDirectories(folder, ".edit-*")); Assert.Empty(Directory.GetFiles(folder, "CaptureFlow_Edit*"));
            // The input and partial output are no longer held by FFmpeg; exclusive opens must succeed after cancellation.
            using var original = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.None);
            foreach (var partial in Directory.GetFiles(folder, "render.mp4", SearchOption.AllDirectories)) { using var released = new FileStream(partial, FileMode.Open, FileAccess.Read, FileShare.None); }
        }
        finally { DeleteOwned(folder); }
    }
    private sealed class InlineProgress(Action<double> report) : IProgress<double> { public void Report(double value) => report(value); }
    [FfmpegTheory] [InlineData(-40)] [InlineData(-60)]
    public async Task RealSilenceDetectionAndWaveformDistinguishSilenceFromTone(double threshold)
    {
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-editor-silence-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var silent = Path.Combine(folder, "silence.wav"); var tone = Path.Combine(folder, "tone.wav");
            await Generate(silent, "", silence: true); await Generate(tone, ""); var backend = new FfmpegEditorBackend();
            var silentMedia = await backend.ProbeAsync(silent, Ffmpeg, default); var toneMedia = await backend.ProbeAsync(tone, Ffmpeg, default);
            var ranges = await backend.DetectSilenceAsync(silentMedia, threshold, .1, Ffmpeg, default);
            var range = Assert.Single(ranges); Assert.InRange(range.Start, 0, .01); Assert.InRange(range.End, .99, 1.01);
            Assert.Empty(await backend.DetectSilenceAsync(toneMedia, threshold, .1, Ffmpeg, default));
            Assert.All((await backend.AnalyzeAsync(silentMedia, Ffmpeg, default)).Waveform, p => Assert.Equal(0, p));
        }
        finally { DeleteOwned(folder); }
    }
    [FfmpegTheory] [InlineData("MP4")] [InlineData("MP3")] [InlineData("WAV")]
    public async Task MixedFixturesRenderTrimSpeedCropRotateFadeTextAndPreserveSources(string format)
    {
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-editor-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var first = Path.Combine(folder, "first.mp4"); var second = Path.Combine(folder, "second.mp4"); var music = Path.Combine(folder, "music.mp3"); var speech = Path.Combine(folder, "speech.wav");
            await Generate(first, "testsrc2=size=128x96:rate=30:duration=1"); await Generate(second, "color=c=blue:size=160x90:rate=24:duration=1", 48000);
            await Generate(music, "", 32000); await Generate(speech, "", 16000);
            var backend = new FfmpegEditorBackend(); var media = new[] { await backend.ProbeAsync(first, Ffmpeg, default), await backend.ProbeAsync(second, Ffmpeg, default), await backend.ProbeAsync(music, Ffmpeg, default), await backend.ProbeAsync(speech, Ffmpeg, default) };
            var originals = media.ToDictionary(m => m.Path, m => SHA256.HashData(File.ReadAllBytes(m.Path)));
            var project = EditorProject.Empty with { Media = media, Clips = [
                new(Guid.NewGuid(), media[0].Id, 0, 0, .1, .9, 2, .5, false, .1, .1, new(16, 16, 64, 48), 90, new("Text: 100% 'safe'", .1, .1, 12, "#AAFFEE")),
                new(Guid.NewGuid(), media[1].Id, 0, .6, 0, 1), new(Guid.NewGuid(), media[2].Id, 2, .2, 0, .8, 1, .25, false, .1, .1), new(Guid.NewGuid(), media[3].Id, 1, 0, 0, 1, 1, .3)] };
            var executable = format == "WAV" ? Path.GetRelativePath(Environment.CurrentDirectory, Ffmpeg) : Ffmpeg;
            var output = await backend.RenderAsync(project, new(format, 128, 72), folder, executable, null, default);
            var metadata = await backend.ProbeAsync(output, Ffmpeg, default);
            Assert.Equal(format, metadata.Format); Assert.True(metadata.HasAudio); Assert.InRange(metadata.Duration, 1.5, 1.7);
            if (format == "MP4") { Assert.True(metadata.HasVideo); Assert.Equal(128, metadata.Width); Assert.Equal(72, metadata.Height); }
            else Assert.False(metadata.HasVideo);
            if (format == "MP4")
            {
                var gap = await DecodeAsync(output, ["-ss", "0.5"], ["-frames:v", "1", "-vf", "format=rgb24,crop=1:1:64:36", "-f", "rawvideo", "-pix_fmt", "rgb24"]);
                Assert.Equal(3, gap.Length); Assert.All(gap, c => Assert.InRange((int)c, 0, 5));
                var blue = await DecodeAsync(output, ["-ss", "0.8"], ["-frames:v", "1", "-vf", "format=rgb24,crop=1:1:64:36", "-f", "rawvideo", "-pix_fmt", "rgb24"]);
                Assert.Equal(3, blue.Length); Assert.InRange((int)blue[2], 240, 255); Assert.InRange((int)blue[0], 0, 10);
            }
            foreach (var m in media) Assert.Equal(originals[m.Path], SHA256.HashData(File.ReadAllBytes(m.Path)));
            var analysis = await backend.AnalyzeAsync(media[2], Ffmpeg, default); Assert.Equal(512, analysis.Waveform.Length); Assert.True(analysis.Waveform.Max() > .01);
        }
        finally { DeleteOwned(folder); }
    }
    [FfmpegTheory] [InlineData(.5)] [InlineData(1.5)]
    public async Task ScalingAudioFadesVolumeAndMuteChangeRealDecodedOutput(double scale)
    {
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-editor-effects-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var input = Path.Combine(folder, "tone.mp4"); await Generate(input, "color=c=red:size=128x96:rate=30:duration=1");
            var backend = new FfmpegEditorBackend(); var media = await backend.ProbeAsync(input, Ffmpeg, default); var session = new EditorSession(); session.Import(media); var id = session.Add(media.Id, 0, 0);
            session.Replace(session.Project.Clips.Single() with { Volume = .5, FadeIn = .2, FadeOut = .2, VideoScale = scale });
            session.Apply(session.Project with { Tracks = session.Project.Tracks.Select(t => t.Id == 0 ? t with { Volume = .5 } : t).ToArray() });
            var output = await backend.RenderAsync(session.Project, new("MP4", 128, 72), folder, Ffmpeg, null, default);
            var pcm = await DecodeAsync(output, [], ["-vn", "-ac", "1", "-ar", "48000", "-f", "s16le"]);
            var samples = Enumerable.Range(0, pcm.Length / 2).Select(i => BitConverter.ToInt16(pcm, i * 2) / 32768d).ToArray();
            static double Rms(IEnumerable<double> values) => Math.Sqrt(values.Select(v => v * v).Average());
            var middle = Rms(samples.Skip(24000).Take(4800)); var first = Rms(samples.Take(480)); var last = Rms(samples.Skip(47520).Take(480));
            Assert.InRange(middle, .015, .04); Assert.True(first < middle * .2); Assert.True(last < middle * .2);
            session.Replace(session.Project.Clips.Single() with { Muted = true });
            var muted = await backend.RenderAsync(session.Project, new("WAV"), folder, Ffmpeg, null, default);
            Assert.All(await DecodeAsync(muted, [], ["-vn", "-f", "s16le"]), b => Assert.Equal((byte)0, b));
        }
        finally { DeleteOwned(folder); }
    }
    private static async Task<byte[]> DecodeAsync(string file, string[] before, string[] after)
    {
        var info = new ProcessStartInfo(Ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-nostdin" }.Concat(before).Concat(["-i", file]).Concat(after).Append("pipe:1")) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!; using var bytes = new MemoryStream(); var read = process.StandardOutput.BaseStream.CopyToAsync(bytes); var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); await read; Assert.True(process.ExitCode == 0, await errors); return bytes.ToArray();
    }
    [FfmpegTheory] [InlineData(true)] [InlineData(false)]
    public async Task CancellationAndEncoderFailureNeverPublishCompletedFileAndRetainSources(bool cancel)
    {
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-editor-failure-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var input = Path.Combine(folder, "input.wav"); await Generate(input, ""); var backend = new FfmpegEditorBackend(); var media = await backend.ProbeAsync(input, Ffmpeg, default);
            var session = new EditorSession(); session.Import(media); session.Add(media.Id, 1, 0); var hash = SHA256.HashData(File.ReadAllBytes(input));
            using var cts = new CancellationTokenSource(); if (cancel) cts.Cancel();
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backend.RenderAsync(session.Project, new("WAV"), folder, Ffmpeg, null, cts.Token));
            else
            {
                var error = await Assert.ThrowsAsync<IOException>(() => backend.RenderAsync(session.Project, new("WAV"), folder, Path.Combine(folder, "missing-ffmpeg.exe"), null, default));
                Assert.Contains("recovery", error.Message); Assert.Single(Directory.GetDirectories(folder, ".edit-*"));
            }
            Assert.Empty(Directory.GetFiles(folder, "CaptureFlow_Edit*")); Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(input)));
        }
        finally { DeleteOwned(folder); }
    }
}

