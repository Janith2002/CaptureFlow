using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Tests;
public class ScreenSourceSelectionTests
{
    private static ApplicationWindowDescriptor Window => new("window", 12345, 123, 456, "Fixture", "FixtureApp", 640, 480);
    [Theory] [InlineData(120)] [InlineData(144)] [InlineData(168)] [InlineData(192)]
    public void RestoringOddPixelOriginsAtFractionalDpiDoesNotShiftTheRegion(double dpi)
    {
        var scale = dpi / 96;
        for (var x = 1; x < 150; x++)
        {
            var restored = RegionSelectionGeometry.FromDrag("monitor", 1920, 1080, x / scale, 17 / scale, (x + 100) / scale, 137 / scale, dpi);
            Assert.Equal(new ScreenRegion("monitor", x, 17, 100, 120, 1920, 1080), restored);
        }
    }
    [Theory] [InlineData(-2560, -400)] [InlineData(1920, 0)] [InlineData(0, -1440)]
    public void DesktopCoordinatesResolveWithinTheChosenMonitorAcrossLayouts(int left, int top)
    {
        var region = RegionSelectionGeometry.FromDesktopPixels("chosen", left, top, 2560, 1440, left + 33, top + 51, left + 234, top + 152);
        Assert.Equal(new ScreenRegion("chosen", 33, 51, 200, 100, 2560, 1440), region);
        Assert.False(region.Matches(new("other", "Other monitor", 2560, 1440, false)));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void MappedTextureCopyUsesPhysicalCropOffsetsAndOmitsRowPadding(bool crop)
    {
        const int width = 32, height = 24, pitch = width * 4 + 16;
        var bytes = new byte[pitch * height];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        { var at = y * pitch + x * 4; bytes[at] = (byte)x; bytes[at + 1] = (byte)y; bytes[at + 3] = 255; }
        var native = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, native, bytes.Length);
            var region = crop ? new ScreenRegion("monitor", 8, 4, 16, 16, width, height) : null;
            var w = region?.Width ?? width; var h = region?.Height ?? height; var result = new byte[w * h * 4];
            BgraCaptureCopy.Copy(native, pitch, new(width, height), region, result);
            for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
            { var at = (y * w + x) * 4; Assert.Equal((byte)(x + (region?.X ?? 0)), result[at]); Assert.Equal((byte)(y + (region?.Y ?? 0)), result[at + 1]); Assert.Equal((byte)255, result[at + 3]); }
            Assert.Throws<ArgumentException>(() => BgraCaptureCopy.Copy(native, 1, new(width, height), region, result));
            Assert.Throws<ArgumentException>(() => BgraCaptureCopy.Copy(native, pitch, new(width, height), region, []));
            if (crop) Assert.Throws<IOException>(() => BgraCaptureCopy.Copy(native, pitch, new(width, height + 2), region, result));
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(native); }
    }
    [Theory] [InlineData(VideoSourceKind.Monitor)] [InlineData(VideoSourceKind.Window)] [InlineData(VideoSourceKind.Region)]
    public void ScreenOnlyAndCombinedValidateAllSupportedSources(VideoSourceKind kind)
    {
        var region = kind == VideoSourceKind.Region ? new ScreenRegion("monitor", 10, 20, 320, 240, 1920, 1080) : null;
        var source = new VideoSourceOptions(kind, kind == VideoSourceKind.Window ? Window.Id : "monitor", Region: region, Window: kind == VideoSourceKind.Window ? Window : null);
        new VideoRecordingConfiguration(source, "unused").Validate();
        var combined = new VideoSourceOptions(VideoSourceKind.ScreenAndWebcam, source.Id, CameraFormat: new("native", 640, 480, 30, 1, "BGRA"), CameraId: "camera", ScreenKind: kind, Region: region, Window: source.Window);
        new VideoRecordingConfiguration(combined, "unused").Validate(); Assert.Equal(source, combined.ScreenSource);
    }
    [Theory] [InlineData(VideoSourceKind.Window)] [InlineData(VideoSourceKind.Region)]
    public void MissingIdentityOrRegionPreventsStart(VideoSourceKind kind) => Assert.Throws<ArgumentException>(() => new VideoRecordingConfiguration(new(kind, "missing"), "unused").Validate());
    [Fact] public void MixingRegionOrWindowMetadataWithOtherSourceTypesIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new VideoRecordingConfiguration(new(VideoSourceKind.Webcam, "camera", CameraFormat: new("native", 640, 480, 30, 1, "BGRA"), Window: Window), "unused").Validate());
        Assert.Throws<ArgumentException>(() => new VideoSourceOptions(VideoSourceKind.Monitor, "monitor", Window: Window).ValidateScreen());
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void WindowFilteringExcludesEachIneligibleCondition(int condition)
    {
        var eligible = new WindowEligibility(true, false, false, false, false, false, true, "A window"); Assert.True(eligible.Eligible);
        var rejected = condition switch { 0 => eligible with { Visible = false }, 1 => eligible with { Minimized = true }, 2 => eligible with { Cloaked = true }, 3 => eligible with { ToolWindow = true }, 4 => eligible with { OwnProcess = true }, 5 => eligible with { CaptureProtected = true }, 6 => eligible with { Accessible = false }, _ => eligible with { Title = " " } };
        Assert.False(rejected.Eligible);
    }
    [Theory] [InlineData(false, true, true, false)] [InlineData(true, false, true, false)] [InlineData(true, true, false, false)] [InlineData(true, true, true, true)]
    public void ClosedRecycledHiddenAndMinimizedWindowsFailWithoutDesktopFallback(bool exists, bool same, bool visible, bool minimized) => Assert.Throws<IOException>(() => ScreenCaptureLifecycle.EnsureWindowAvailable(exists, same, visible, minimized));
    [Theory] [InlineData(true)] [InlineData(false)]
    public void ResizingStopsWithSourceSpecificError(bool window)
    {
        var error = Assert.Throws<IOException>(() => ScreenCaptureLifecycle.EnsureDimensionsUnchanged(new(640, 480), new(800, 600), window));
        Assert.Contains(window ? "window resized" : "monitor resolution changed", error.Message);
    }
    [Theory] [InlineData(96, 100, 120, 100, 120)] [InlineData(120, 100, 120, 126, 150)] [InlineData(144, 100, 120, 150, 180)] [InlineData(192, 100, 120, 200, 240)]
    public void DipCoordinatesBecomeEvenPhysicalPixels(double dpi, double widthDip, double heightDip, int width, int height)
    {
        var region = RegionSelectionGeometry.FromDrag("monitor", 1920, 1080, 10, 20, 10 + widthDip, 20 + heightDip, dpi);
        region.Validate(); Assert.Equal(width, region.Width); Assert.Equal(height, region.Height);
    }
    [Fact] public void ReverseDragAndOutOfBoundsCoordinatesClampWithinChosenMonitor()
    {
        var region = RegionSelectionGeometry.FromDrag("left-monitor", 1920, 1080, 2000, 1200, -100, -50, 96);
        Assert.Equal(new("left-monitor", 0, 0, 1920, 1080, 1920, 1080), region);
        Assert.Equal(new("left-monitor", 1720, 880, 200, 200, 1920, 1080), RegionSelectionGeometry.Move(new("left-monitor", 20, 30, 200, 200, 1920, 1080), 9999, 9999));
    }
    [Fact] public void NegativeDesktopOriginsDoNotChangeMonitorRelativeCrop()
    {
        // Overlay local coordinates are independent of its physical desktop origin (e.g. -1920,-200).
        var region = RegionSelectionGeometry.FromDrag("left-monitor", 1920, 1080, 40, 60, 240, 260, 96);
        Assert.Equal(40, region.X); Assert.Equal(60, region.Y); Assert.False(region.Matches(new("other-monitor", "Right", 1920, 1080, false)));
        Assert.False(region.Matches(new("left-monitor", "Resized", 2560, 1440, false)));
    }
    [Theory] [InlineData(0, 0, 15, 16)] [InlineData(-1, 0, 16, 16)] [InlineData(1910, 0, 16, 16)] [InlineData(0, 0, 17, 16)]
    public void InvalidRegionsCannotReachEncoder(int x, int y, int width, int height) => Assert.Throws<ArgumentException>(() => new ScreenRegion("monitor", x, y, width, height, 1920, 1080).Validate());
    [Fact] public async Task RegionPreferencesRoundTripAndCancellationPreservesExistingGeometry()
    {
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-region-tests-" + Guid.NewGuid().ToString("N")); var file = Path.Combine(folder, "region.json");
        try
        {
            var store = new JsonRegionPreferencesStore(file); Assert.Null(await store.LoadAsync());
            var region = new ScreenRegion("monitor", 20, 30, 320, 240, 1920, 1080); await store.SaveAsync(region); Assert.Equal(region, await store.LoadAsync());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(region with { X = 40 }, new CancellationToken(true)));
            Assert.Equal(region, await store.LoadAsync()); Assert.Single(Directory.GetFiles(folder));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact] public async Task DestroyedWindowIdentityIsRejectedBeforeCaptureCreation()
    {
        var options = new VideoSourceOptions(VideoSourceKind.Window, Window.Id, Window: Window);
        await Assert.ThrowsAsync<IOException>(() => new WindowsGraphicsCaptureSourceFactory().CreateAsync(options, default));
    }
}
