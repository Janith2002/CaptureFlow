using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Tests;
public class FfmpegRegionOutputTests
{
    [FfmpegTheory] [InlineData(false)] [InlineData(true)]
    public async Task NativePixelCropEncodesAtRegionSizeAndComposesWithinCroppedFrame(bool withCamera)
    {
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-region-output-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        const int width = 192, height = 144, pitch = width * 4 + 16;
        var region = new ScreenRegion("chosen", 16, 22, 160, 96, width, height); var full = new byte[pitch * height];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        { var at = y * pitch + x * 4; var inside = x >= region.X && x < region.X + region.Width && y >= region.Y && y < region.Y + region.Height; full[at + (inside ? 1 : 2)] = 255; full[at + 3] = 255; }
        var native = Marshal.AllocHGlobal(full.Length);
        try
        {
            Marshal.Copy(full, 0, native, full.Length); var pixels = new byte[region.Width * region.Height * 4]; BgraCaptureCopy.Copy(native, pitch, new(width, height), region, pixels);
            var source = withCamera ? new VideoSourceOptions(VideoSourceKind.ScreenAndWebcam, "chosen", CameraId: "generated", CameraFormat: new("fixture", 16, 12, 30, 1, "BGRA"), ScreenKind: VideoSourceKind.Region, Region: region) : new(VideoSourceKind.Region, "chosen", Region: region);
            var config = new VideoRecordingConfiguration(source, folder, FfmpegPath: Executable.Find("ffmpeg.exe")!); config.Validate(); var backend = new FfmpegVideoEncodingBackend();
            var intermediate = Path.Combine(folder, "intermediate.mp4");
            await using (var encoder = await backend.CreateAsync(config, new(region.Width, region.Height), intermediate, default))
            {
                for (var i = 0; i < 9; i++)
                {
                    using var frame = new VideoFrame(new Pixels(pixels), new(region.Width, region.Height), TimeSpan.FromSeconds(i / 30d));
                    if (withCamera)
                    {
                        using var camera = OverlayCompositorTests.Frame(16, 12, 255, 0, 0);
                        using var composed = await new BgraVideoCompositor().ComposeAsync(frame, [new(camera, 120, 70, 32, 24)], default); await encoder.WriteFrameAsync(composed, default);
                    }
                    else await encoder.WriteFrameAsync(frame, default);
                }
                await encoder.FinishAsync(default);
            }
            var output = Path.Combine(folder, "output.mp4"); await backend.MuxAsync(config, intermediate, null, output, TimeSpan.FromSeconds(.3), default); await backend.ValidateAsync(output, config.FfmpegPath, default);
            var probeBytes = await RunAsync(Executable.Find("ffprobe.exe")!, ["-v", "error", "-show_streams", "-of", "json", output]); using var probe = JsonDocument.Parse(probeBytes);
            var stream = Assert.Single(probe.RootElement.GetProperty("streams").EnumerateArray()); Assert.Equal(160, stream.GetProperty("width").GetInt32()); Assert.Equal(96, stream.GetProperty("height").GetInt32()); Assert.Equal("30/1", stream.GetProperty("avg_frame_rate").GetString());
            var decoded = await RunAsync(config.FfmpegPath, ["-v", "error", "-i", output, "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1"]);
            Assert.Equal(160 * 96 * 3 * 9, decoded.Length);
            var green = 0; var blue = 0;
            for (var i = 0; i < decoded.Length; i += 3) { Assert.InRange((int)decoded[i], 0, 20); if (decoded[i + 1] > 230) green++; if (decoded[i + 2] > 230) blue++; }
            Assert.True(green > 160 * 96 * 8); if (withCamera) Assert.True(blue > 32 * 24 * 7); else Assert.Equal(0, blue);
            Assert.True(File.Exists(intermediate));
        }
        finally
        {
            Marshal.FreeHGlobal(native); var fullFolder = Path.GetFullPath(folder); var prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "CaptureFlow-region-output-");
            if (!fullFolder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid cleanup target."); Directory.Delete(fullFolder, true);
        }
    }
    private static async Task<byte[]> RunAsync(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }; foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; using var bytes = new MemoryStream(); var read = process.StandardOutput.BaseStream.CopyToAsync(bytes); var errors = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); await read; Assert.True(process.ExitCode == 0, await errors); return bytes.ToArray();
    }
    private sealed class Pixels(byte[] bytes) : IMemoryOwner<byte> { public Memory<byte> Memory => bytes; public void Dispose() { } }
}
