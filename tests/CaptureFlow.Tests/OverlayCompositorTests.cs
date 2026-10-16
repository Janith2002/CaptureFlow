using System.Buffers;
using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Tests;
public class OverlayCompositorTests
{
    [Theory]
    [InlineData(OverlayPosition.TopLeft)] [InlineData(OverlayPosition.TopRight)] [InlineData(OverlayPosition.BottomLeft)] [InlineData(OverlayPosition.BottomRight)]
    public void PresetsFitAndKeepCameraAspect(OverlayPosition position)
    {
        var screen = new VideoSize(1920, 1080); var camera = new VideoSize(640, 480);
        var layout = OverlayGeometry.Place(screen, camera, new(), position);
        var b = OverlayGeometry.Calculate(screen, camera, layout);
        Assert.InRange(b.X, 0, screen.Width - b.Width); Assert.InRange(b.Y, 0, screen.Height - b.Height);
        Assert.Equal(480, b.Width); Assert.Equal(360, b.Height);
        Assert.Equal(position is OverlayPosition.TopRight or OverlayPosition.BottomRight, b.X > screen.Width / 2);
        Assert.Equal(position is OverlayPosition.BottomLeft or OverlayPosition.BottomRight, b.Y > screen.Height / 2);
    }
    [Theory]
    [InlineData(OverlayShape.Rectangle)] [InlineData(OverlayShape.RoundedRectangle)] [InlineData(OverlayShape.Circle)]
    public void DragAndOversizedResizeRemainWithinPortraitScreen(OverlayShape shape)
    {
        var screen = new VideoSize(1080, 1920); var camera = new VideoSize(1920, 1080);
        var clamped = OverlayGeometry.Clamp(screen, camera, new(-3, 8, 10, shape));
        var b = OverlayGeometry.Calculate(screen, camera, clamped);
        Assert.Equal(0, b.X); Assert.InRange(b.Y + b.Height, 0, screen.Height); Assert.InRange(b.Width, 1, screen.Width);
        if (shape == OverlayShape.Circle) Assert.Equal(b.Width, b.Height);
        else Assert.InRange(Math.Abs(b.Width / (double)b.Height - 16 / 9d), 0, .01);
    }
    [Fact]
    public void InvalidLayoutIsRejected() => Assert.Throws<ArgumentException>(() => OverlayGeometry.Calculate(new(100, 100), new(10, 10), new(double.NaN)));
    [Fact]
    public void ResizeMaintainsAspectAcrossMismatchedScreenAndCamera()
    {
        var first = OverlayGeometry.Calculate(new(2560, 1440), new(640, 480), new(Width: .15));
        var second = OverlayGeometry.Calculate(new(2560, 1440), new(640, 480), new(Width: .3));
        Assert.Equal(first.Width * 2, second.Width); Assert.Equal(first.Height * 2, second.Height);
    }
    [Theory]
    [InlineData(OverlayShape.Rectangle)] [InlineData(OverlayShape.RoundedRectangle)] [InlineData(OverlayShape.Circle)]
    public async Task MasksAffectCornersButNotCenterOrUnrelatedScreenPixels(OverlayShape shape)
    {
        using var screen = Frame(40, 30, 20, 20, 20);
        using var camera = Frame(20, 20, 0, 0, 255);
        using var result = await new BgraVideoCompositor().ComposeAsync(screen, [new(camera, 5, 5, 20, 20, shape)], CancellationToken.None);
        Assert.Equal((byte)255, Pixel(result, 15, 15, 2)); Assert.Equal((byte)20, Pixel(result, 0, 0, 2));
        Assert.Equal((byte)20, Pixel(screen, 15, 15, 2)); // primary input is not mutated.
        if (shape == OverlayShape.Rectangle) Assert.Equal((byte)255, Pixel(result, 5, 5, 2));
        else Assert.True(Pixel(result, 5, 5, 2) < 255);
        if (shape == OverlayShape.Circle) Assert.Equal((byte)20, Pixel(result, 5, 5, 2));
    }
    [Fact]
    public async Task CircleUsesCenteredSquareCropWithoutStretching()
    {
        using var screen = Frame(30, 30, 0, 0, 0);
        using var owner = MemoryPool<byte>.Shared.Rent(40 * 20 * 4);
        for (var y = 0; y < 20; y++) for (var x = 0; x < 40; x++)
        {
            var pixel = owner.Memory.Span.Slice((y * 40 + x) * 4, 4);
            pixel.Clear(); pixel[3] = 255; pixel[x is >= 10 and < 30 ? 1 : 2] = 255;
        }
        using var camera = new VideoFrame(new Borrowed(owner.Memory), new(40, 20), TimeSpan.Zero);
        using var result = await new BgraVideoCompositor().ComposeAsync(screen, [new(camera, 5, 5, 20, 20, OverlayShape.Circle)], CancellationToken.None);
        Assert.Equal((byte)255, Pixel(result, 15, 15, 1)); Assert.Equal((byte)0, Pixel(result, 15, 15, 2));
        Assert.Equal((byte)255, Pixel(result, 8, 15, 1));
    }
    [Fact]
    public async Task BorderHasRequestedThicknessAndDoesNotReplaceCameraCenter()
    {
        using var screen = Frame(30, 30, 0, 0, 0); using var camera = Frame(20, 20, 0, 0, 255);
        using var result = await new BgraVideoCompositor().ComposeAsync(screen, [new(camera, 5, 5, 20, 20, OverlayShape.Rectangle, 2)], CancellationToken.None);
        Assert.Equal((byte)255, Pixel(result, 5, 15, 0)); Assert.Equal((byte)117, Pixel(result, 5, 15, 1));
        Assert.Equal((byte)255, Pixel(result, 15, 15, 2)); Assert.Equal((byte)0, Pixel(result, 15, 15, 0));
        Assert.Equal((byte)255, Pixel(result, 7, 15, 2));
    }
    [Fact]
    public async Task EmptyOverlayCompositionSharesPrimaryAndCancellationDoesNotReturnOutput()
    {
        using var screen = Frame(20, 20, 12, 34, 56); using var camera = Frame(4, 4, 0, 0, 255);
        using var result = await new BgraVideoCompositor().ComposeAsync(screen, [], CancellationToken.None);
        Assert.Equal(screen.Timestamp, result.Timestamp); Assert.Equal(screen.Pixels.ToArray(), result.Pixels.ToArray());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new BgraVideoCompositor().ComposeAsync(screen, [new(camera, 0, 0, 4, 4)], cancellation.Token));
    }
    [Fact]
    public async Task OutOfBoundsOverlayIsRejected()
    {
        using var screen = Frame(20, 20, 0, 0, 0); using var camera = Frame(4, 4, 0, 0, 255);
        await Assert.ThrowsAsync<ArgumentException>(async () => await new BgraVideoCompositor().ComposeAsync(screen, [new(camera, 19, 19, 4, 4)], CancellationToken.None));
    }
    internal static VideoFrame Frame(int width, int height, byte blue, byte green, byte red)
    {
        var owner = MemoryPool<byte>.Shared.Rent(width * height * 4);
        var pixels = owner.Memory.Span[..(width * height * 4)];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = blue; pixels[i + 1] = green; pixels[i + 2] = red; pixels[i + 3] = 255; }
        return new(owner, new(width, height), TimeSpan.FromTicks((long)RecordingTimeline.NowQpc));
    }
    internal static byte Pixel(VideoFrame frame, int x, int y, int channel) => frame.Pixels.Span[(y * frame.Size.Width + x) * 4 + channel];
    private sealed class Borrowed(Memory<byte> memory) : IMemoryOwner<byte> { public Memory<byte> Memory => memory; public void Dispose() { } }
}
