using System.Buffers;
using System.Diagnostics;
using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Tests;

public class FfmpegMouseEffectsTests
{
    [FfmpegTheory] [InlineData(false)] [InlineData(true)]
    public async Task ExportedPixelsContainHighlightsAndDistinctClickRingsBehindWebcam(bool webcam)
    {
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-mouse-mp4-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var size = new VideoSize(128, 96); var settings = new MouseEffectsPreferences(Highlight: true, HighlightColor: "#00FF00", Radius: 12, Opacity: .8,
                ClickEffects: true, LeftColor: "#FF0000", RightColor: "#0000FF", ClickDurationMilliseconds: 200);
            var config = new VideoRecordingConfiguration(new(VideoSourceKind.Monitor, "synthetic", MouseEffects: settings), folder, FfmpegPath: Executable.Find("ffmpeg.exe")!);
            var backend = new FfmpegVideoEncodingBackend(); var intermediate = Path.Combine(folder, "capture.mp4");
            await using (var encoder = await backend.CreateAsync(config, size, intermediate, default))
            {
                for (var i = 0; i < 9; i++)
                {
                    var pixels = new byte[128 * 96 * 4]; for (var p = 3; p < pixels.Length; p += 4) pixels[p] = 255;
                    MouseEffectsRenderer.Render(pixels, size, settings, new(24, 24), [new(new(80.5, 30.5), MouseClickButton.Left, TimeSpan.Zero), new(new(80.5, 70.5), MouseClickButton.Right, TimeSpan.Zero)], TimeSpan.FromSeconds(i / 30d));
                    using var frame = new VideoFrame(new Pixels(pixels), size, TimeSpan.FromSeconds(i / 30d));
                    if (webcam)
                    {
                        using var camera = OverlayCompositorTests.Frame(16, 16, 255, 0, 0);
                        using var composed = await new BgraVideoCompositor().ComposeAsync(frame, [new(camera, 16, 16, 16, 16)], default); await encoder.WriteFrameAsync(composed, default);
                    }
                    else await encoder.WriteFrameAsync(frame, default);
                }
                await encoder.FinishAsync(default);
            }
            var final = Path.Combine(folder, "effects.mp4"); await backend.MuxAsync(config, intermediate, null, final, TimeSpan.FromSeconds(.3), default); await backend.ValidateAsync(final, config.FfmpegPath, default);
            var start = new ProcessStartInfo(config.FfmpegPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-v", "error", "-nostdin", "-i", final, "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!; using var decoded = new MemoryStream(); var errors = process.StandardError.ReadToEndAsync();
            try
            {
                var read = process.StandardOutput.BaseStream.CopyToAsync(decoded); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); await read; Assert.True(process.ExitCode == 0, await errors);
            }
            finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
            var data = decoded.ToArray(); Assert.Equal(128 * 96 * 3 * 9, data.Length);
            int At(int frame, int x, int y) => (frame * 128 * 96 + y * 128 + x) * 3;
            var highlight = At(3, 24, 24); Assert.True(data[highlight + (webcam ? 2 : 1)] > 180); Assert.True(data[highlight + (webcam ? 1 : 2)] < 30);
            var left = At(3, 94, 30); var right = At(3, 94, 70);
            Assert.True(data[left] > 70 && data[left + 2] < 30); Assert.True(data[right + 2] > 70 && data[right] < 30);
            Assert.True(data[At(8, 94, 30)] < 20 && data[At(8, 94, 70) + 2] < 20); // Expired on the video timeline.
            for (var i = 0; i < 9; i++) Assert.True(data[At(i, 0, 0)] < 20 && data[At(i, 127, 95) + 1] < 20);
        }
        finally
        {
            var target = Path.GetFullPath(folder); if (!target.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "CaptureFlow-mouse-mp4-"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid fixture cleanup target."); Directory.Delete(target, true);
        }
    }
    private sealed class Pixels(byte[] pixels) : IMemoryOwner<byte> { public Memory<byte> Memory => pixels; public void Dispose() { } }
}
