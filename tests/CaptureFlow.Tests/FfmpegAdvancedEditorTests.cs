using System.Diagnostics;
using System.Security.Cryptography;
using CaptureFlow.Application.Editing;
using CaptureFlow.Core.Editing;
using CaptureFlow.Infrastructure.Editing;
namespace CaptureFlow.Tests;
public partial class FfmpegEditorTests
{
    private static EditorKeyframe K(AnimationProperty p, double t, double v, KeyframeInterpolation interpolation = KeyframeInterpolation.Linear) => new(Guid.NewGuid(), p, t, v, interpolation);
    private static async Task<byte[]> Pixel(string output, double time, int x = 64, int y = 36) => await DecodeAsync(output, ["-ss", time.ToString(System.Globalization.CultureInfo.InvariantCulture)], ["-frames:v", "1", "-vf", $"format=rgb24,crop=1:1:{x}:{y}", "-f", "rawvideo", "-pix_fmt", "rgb24"]);
    [FfmpegTheory] [InlineData(true)] public async Task DissolveUsesSourceHandleAndPreviewExportHaveEquivalentBoundaryFrames(bool generated)
    {
        Assert.True(generated);
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-editor-dissolve-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var red = Path.Combine(folder, "red.mp4"); var blue = Path.Combine(folder, "blue.mp4"); await Generate(red, "color=c=red:size=128x72:rate=30:duration=1"); await Generate(blue, "color=c=blue:size=128x72:rate=30:duration=1");
            var backend = new FfmpegEditorBackend(); var r = await backend.ProbeAsync(red, Ffmpeg, default); var b = await backend.ProbeAsync(blue, Ffmpeg, default); var hashes = new[] { SHA256.HashData(File.ReadAllBytes(red)), SHA256.HashData(File.ReadAllBytes(blue)) };
            var project = EditorProject.Empty with { Version = 2, Media = [r, b], Clips = [new(Guid.NewGuid(), b.Id, 0, .6, .4, .8, Transitions: [new(TransitionType.CrossDissolve, .4, TransitionPlacement.Start)]), new(Guid.NewGuid(), r.Id, 0, 0, 0, .6)] };
            var clock = Stopwatch.StartNew(); var output = await backend.RenderAsync(project, new("MP4", 128, 72), folder, Ffmpeg, null, default); var elapsed = clock.ElapsedMilliseconds;
            var before = await Pixel(output, .1); var middle = await Pixel(output, .4); var after = await Pixel(output, .6);
            Assert.InRange((int)before[0], 240, 255); Assert.InRange((int)before[2], 0, 10);
            Assert.InRange((int)middle[0], 100, 155); Assert.InRange((int)middle[2], 100, 155);
            Assert.InRange((int)after[2], 240, 255); Assert.InRange((int)after[0], 0, 10);
            var preview = await backend.RenderAsync(project, new("MP4", 256, 144), folder, Ffmpeg, null, default);
            var previewMiddle = await Pixel(preview, .4, 128, 72); for (var i = 0; i < 3; i++) Assert.InRange(Math.Abs(previewMiddle[i] - middle[i]), 0, 6);
            Assert.Equal(hashes[0], SHA256.HashData(File.ReadAllBytes(red))); Assert.Equal(hashes[1], SHA256.HashData(File.ReadAllBytes(blue)));
            var qaFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/qa")); Directory.CreateDirectory(qaFolder); await File.AppendAllTextAsync(Path.Combine(qaFolder, "m68-generated-performance.log"), $"Dissolve 128x72 1s render/probe/full-decode: {elapsed}ms; preview 256x144: {clock.ElapsedMilliseconds - elapsed}ms (includes pixel decoding). Private bytes: {Process.GetCurrentProcess().PrivateMemorySize64}; manifest bytes: {Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)}\n");
        }
        finally { DeleteOwned(folder); }
    }
    [FfmpegTheory] [InlineData(TransitionType.FadeIn, TransitionPlacement.Start)] [InlineData(TransitionType.FadeOut, TransitionPlacement.End)] [InlineData(TransitionType.FadeToBlack, TransitionPlacement.End)]
    public async Task TransitionFadesChangeDecodedFrames(TransitionType type, TransitionPlacement placement)
    {
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-editor-transition-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(folder, "red.mp4"); await Generate(source, "color=c=red:size=128x72:rate=30:duration=1"); var backend = new FfmpegEditorBackend(); var m = await backend.ProbeAsync(source, Ffmpeg, default);
            var p = EditorProject.Empty with { Version = 2, Media = [m], Clips = [new(Guid.NewGuid(), m.Id, 0, 0, 0, 1, Transitions: [new(type, .4, placement)])] };
            var output = await backend.RenderAsync(p, new("MP4", 128, 72), folder, Ffmpeg, null, default);
            var middle = await Pixel(output, .5); var edge = await Pixel(output, placement == TransitionPlacement.Start ? 0 : .9666667);
            Assert.InRange((int)middle[0], 240, 255); Assert.True(edge[0] < 35);
        }
        finally { DeleteOwned(folder); }
    }
    [FfmpegTheory] [InlineData(AnimationProperty.PositionX)] [InlineData(AnimationProperty.PositionY)] [InlineData(AnimationProperty.Scale)] [InlineData(AnimationProperty.Rotation)] [InlineData(AnimationProperty.Opacity)]
    public async Task EveryVideoAnimationPropertyChangesRealRenderedPixels(AnimationProperty property)
    {
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-editor-animation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(folder, "red.mp4"); await Generate(source, "color=c=red:size=128x72:rate=30:duration=1"); var backend = new FfmpegEditorBackend(); var m = await backend.ProbeAsync(source, Ffmpeg, default);
            var end = property switch { AnimationProperty.PositionX or AnimationProperty.PositionY => .75, AnimationProperty.Scale => .3, AnimationProperty.Rotation => 90, _ => 0 };
            var p = EditorProject.Empty with { Version = 2, Media = [m], Clips = [new(Guid.NewGuid(), m.Id, 0, 0, 0, 1, Keyframes: [K(property, 0, EditorAnimation.Default(property)), K(property, .8, end)])] };
            var output = await backend.RenderAsync(p, new("MP4", 128, 72), folder, Ffmpeg, null, default);
            var early = await Pixel(output, 0, 12, 8); var late = await Pixel(output, .8, 12, 8); Assert.InRange((int)early[0], 240, 255); Assert.InRange((int)late[0], 0, 10);
            if (property == AnimationProperty.Opacity) { var middle = await Pixel(output, .4); Assert.InRange((int)middle[0], 100, 155); }
        }
        finally { DeleteOwned(folder); }
    }
    [FfmpegTheory] [InlineData("WAV")] [InlineData("MP3")] [InlineData("MP4")]
    public async Task VolumeAutomationAndFadesChangeDecodedSamplesAndMuteWins(string format)
    {
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-editor-automation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(folder, "tone.wav"); await Generate(source, ""); var backend = new FfmpegEditorBackend(); var m = await backend.ProbeAsync(source, Ffmpeg, default);
            var p = EditorProject.Empty with { Version = 2, Media = [m], Clips = [new(Guid.NewGuid(), m.Id, 1, .2, 0, 1, FadeIn: .05, FadeOut: .05, Keyframes: [K(AnimationProperty.Volume, 0, .1), K(AnimationProperty.Volume, .8, 1)])] };
            var output = await backend.RenderAsync(p, new(format, 128, 72), folder, Ffmpeg, null, default);
            var pcm = await DecodeAsync(output, [], ["-vn", "-ac", "1", "-ar", "48000", "-f", "s16le"]); var samples = Enumerable.Range(0, pcm.Length / 2).Select(i => BitConverter.ToInt16(pcm, i * 2) / 32768d).ToArray();
            double Rms(double t) => Math.Sqrt(samples.Skip((int)(t * 48000)).Take(960).Select(v => v * v).Average());
            Assert.True(Rms(.3) < Rms(1) * .4); Assert.True(Rms(.1) < .001); Assert.True(Rms(.205) < Rms(.3)); Assert.True(Rms(1.18) < Rms(1) * .5);
            var muted = await backend.RenderAsync(p with { Clips = [p.Clips[0] with { Muted = true }] }, new("WAV"), folder, Ffmpeg, null, default);
            Assert.All(await DecodeAsync(muted, [], ["-vn", "-f", "s16le"]), b => Assert.Equal((byte)0, b));
        }
        finally { DeleteOwned(folder); }
    }
    [FfmpegTheory] [InlineData(KeyframeInterpolation.Hold)] [InlineData(KeyframeInterpolation.Linear)]
    public async Task RenderedInterpolationMatchesModelAtExactKeyAndBetweenKeys(KeyframeInterpolation mode)
    {
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-editor-interpolation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(folder, "red.mp4"); await Generate(source, "color=c=red:size=128x72:rate=30:duration=1"); var backend = new FfmpegEditorBackend(); var m = await backend.ProbeAsync(source, Ffmpeg, default);
            var clip = new EditorClip(Guid.NewGuid(), m.Id, 0, .2, 0, .8, Keyframes: [K(AnimationProperty.Opacity, 0, 1, mode), K(AnimationProperty.Opacity, .4, 0)]);
            var project = EditorProject.Empty with { Version = 2, Media = [m], Clips = [clip] };
            var output = await backend.RenderAsync(project, new("MP4", 128, 72), folder, Ffmpeg, null, default);
            foreach (var local in new[] { 0d, .2, .4, .6 })
            { var pixel = await Pixel(output, clip.Start + local); var expected = 253 * EditorAnimation.Evaluate(clip, AnimationProperty.Opacity, local); Assert.InRange(Math.Abs(pixel[0] - expected), 0, 8); }
        }
        finally { DeleteOwned(folder); }
    }
}



