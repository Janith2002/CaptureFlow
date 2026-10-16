using CaptureFlow.Core.Video;
using CaptureFlow.Infrastructure.Video;
namespace CaptureFlow.Tests;

public class MouseEffectsTests
{
    [Fact] public void DefaultsPreserveNativeCursorWithoutEffects()
    { var p = new MouseEffectsPreferences(); p.Validate(); Assert.True(p.ShowCursor); Assert.False(p.Highlight); Assert.False(p.ClickEffects); }
    [Theory] [InlineData(96)] [InlineData(120)] [InlineData(144)] [InlineData(192)]
    public void PhysicalCoordinatesRemainCorrectAcrossDpiAndNegativeOrigins(double dpi)
    {
        var physical = MouseCoordinates.DipToPhysical(new(40, 80), dpi);
        var desktop = new MousePoint(physical.X - 1920, physical.Y - 300);
        Assert.Equal(physical, MouseCoordinates.ToFrame(desktop, -1920, -300));
        var region = new ScreenRegion("left", 10, 20, 100, 100, 1920, 1080);
        Assert.Equal(new MousePoint(physical.X - 10, physical.Y - 20), MouseCoordinates.ToFrame(desktop, -1920, -300, region));
    }
    [Fact] public void WindowMovementMapsTheSameDesktopPositionIntoCurrentCaptureBounds()
    { Assert.Equal(new MousePoint(50, 40), MouseCoordinates.ToFrame(new(250, 340), 200, 300)); Assert.Equal(new MousePoint(30, 10), MouseCoordinates.ToFrame(new(250, 340), 220, 330)); }
    [Theory] [InlineData(-1, 20)] [InlineData(100, 20)] [InlineData(20, -1)] [InlineData(20, 100)]
    public void OutsideCentersNeverRenderIntoRegion(double x, double y)
    {
        var pixels = new byte[100 * 100 * 4]; var original = pixels.ToArray(); var point = new MousePoint(x, y);
        MouseEffectsRenderer.Render(pixels, new(100, 100), new(Highlight: true, ClickEffects: true), point, [new(point, MouseClickButton.Left, TimeSpan.Zero)], TimeSpan.FromMilliseconds(100));
        Assert.Equal(original, pixels);
    }
    [Fact] public void HighlightBlendsColorAndOpacityWithoutChangingAlphaOrFarPixels()
    {
        var pixels = new byte[64 * 64 * 4]; Array.Fill(pixels, (byte)10);
        MouseEffectsRenderer.Render(pixels, new(64, 64), new(Highlight: true, HighlightColor: "#FF0000", Radius: 8, Opacity: .5), new(32, 32), [], TimeSpan.Zero);
        var at = (32 * 64 + 32) * 4; Assert.Equal(5, pixels[at]); Assert.Equal(132, pixels[at + 2]); Assert.Equal(10, pixels[at + 3]); Assert.Equal(10, pixels[0]);
        Assert.InRange(pixels[(32 * 64 + 40) * 4 + 2], (byte)10, (byte)133);
    }
    [Theory] [InlineData(MouseClickButton.Left)] [InlineData(MouseClickButton.Right)]
    public void ClickRingsExpandFadeAndUseDistinctColors(MouseClickButton button)
    {
        var settings = new MouseEffectsPreferences(Radius: 20, ClickEffects: true, LeftColor: "#FF0000", RightColor: "#0000FF", ClickDurationMilliseconds: 1000);
        var pixels = new byte[100 * 100 * 4]; MouseClick[] clicks = [new(new(50.5, 50.5), button, TimeSpan.Zero)];
        MouseEffectsRenderer.Render(pixels, new(100, 100), settings, null, clicks, TimeSpan.FromMilliseconds(500));
        var at = (50 * 100 + 70) * 4; Assert.InRange(pixels[at + (button == MouseClickButton.Left ? 2 : 0)], (byte)105, (byte)110);
        Assert.Equal(0, pixels[at + (button == MouseClickButton.Left ? 0 : 2)]); Assert.Equal(0, pixels[(50 * 100 + 50) * 4]);
        Array.Clear(pixels); MouseEffectsRenderer.Render(pixels, new(100, 100), settings, null, clicks, TimeSpan.FromSeconds(1)); Assert.All(pixels, p => Assert.Equal(0, p));
    }
    [Theory] [InlineData(MouseClickButton.Left)] [InlineData(MouseClickButton.Right)]
    public void DisabledButtonsAndFutureClicksDoNotRender(MouseClickButton button)
    {
        var pixels = new byte[32 * 32 * 4]; var p = new MouseEffectsPreferences(ClickEffects: true, LeftClicks: false, RightClicks: false);
        MouseEffectsRenderer.Render(pixels, new(32, 32), p, null, [new(new(16, 16), button, TimeSpan.Zero)], TimeSpan.FromMilliseconds(100)); Assert.All(pixels, v => Assert.Equal(0, v));
        MouseEffectsRenderer.Render(pixels, new(32, 32), p with { LeftClicks = true, RightClicks = true }, null, [new(new(16, 16), button, TimeSpan.FromSeconds(1))], TimeSpan.Zero); Assert.All(pixels, v => Assert.Equal(0, v));
    }
    [Fact] public void RapidClicksAreBoundedAndPauseResumeDiscardPausedAndPendingEvents()
    {
        var buffer = new MouseClickBuffer(); buffer.SetActive(true);
        for (var i = 0; i < 1000; i++) buffer.Add(new(new(i, 1), MouseClickButton.Left, TimeSpan.FromMilliseconds(i)));
        var snapshot = buffer.Snapshot(TimeSpan.FromMilliseconds(1000), TimeSpan.FromSeconds(2)); Assert.Equal(MouseClickBuffer.Capacity, snapshot.Length); Assert.Equal(968, snapshot[0].Position.X);
        buffer.SetActive(false); buffer.Add(new(new(10, 1), MouseClickButton.Right, TimeSpan.Zero)); Assert.Empty(buffer.Snapshot(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        buffer.SetActive(true); Assert.Empty(buffer.Snapshot(TimeSpan.Zero, TimeSpan.FromSeconds(1))); buffer.Add(new(new(1, 1), MouseClickButton.Right, TimeSpan.Zero));
        Assert.Single(buffer.Snapshot(TimeSpan.FromMilliseconds(499), TimeSpan.FromMilliseconds(500))); Assert.Empty(buffer.Snapshot(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500)));
    }
    [Fact] public async Task PersistenceRoundTripsAndCancellationPreservesPreviousSettings()
    {
        var folder = Path.Combine(Path.GetTempPath(), "CaptureFlow-mouse-" + Guid.NewGuid().ToString("N")); var path = Path.Combine(folder, "preferences.json");
        try
        {
            var store = new JsonMouseEffectsPreferencesStore(path); Assert.Equal(new(), await store.LoadAsync());
            var p = new MouseEffectsPreferences(false, true, "#34D399", 40, .7, true, false, true, "#FFFFFF", "#C084FC", 900);
            await store.SaveAsync(p); Assert.Equal(p, await store.LoadAsync());
            using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new(), cancel.Token));
            Assert.Equal(p, await store.LoadAsync()); Assert.Single(Directory.GetFiles(folder));
            await File.WriteAllTextAsync(path, "{\"Radius\":0}"); await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.LoadAsync());
        }
        finally { var target = Path.GetFullPath(folder); if (!target.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "CaptureFlow-mouse-"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(); if (Directory.Exists(target)) Directory.Delete(target, true); }
    }
    [Theory] [InlineData(0, .3, 500)] [InlineData(101, .3, 500)] [InlineData(24, 0, 500)] [InlineData(24, 1.1, 500)] [InlineData(24, .3, 99)] [InlineData(24, .3, 2001)]
    public void UnsafeValuesAreRejected(double radius, double opacity, double duration) => Assert.Throws<ArgumentOutOfRangeException>(() => new MouseEffectsPreferences(Radius: radius, Opacity: opacity, ClickDurationMilliseconds: duration).Validate());
    [Theory] [InlineData("red")] [InlineData("#FFFFFFFF")] [InlineData("#GG0000")]
    public void InvalidColorsAreRejected(string color) => Assert.Throws<ArgumentException>(() => new MouseEffectsPreferences(HighlightColor: color).Validate());
    [Fact] public void WebcamOnlyRejectsEffectsAndCombinedPreservesScreenSettings()
    {
        var p = new MouseEffectsPreferences(Highlight: true); var combined = new VideoSourceOptions(VideoSourceKind.ScreenAndWebcam, "screen", CameraId: "camera", CameraFormat: new("test", 16, 16, 30, 1, "BGRA"), MouseEffects: p);
        Assert.Equal(p, combined.ScreenSource.MouseEffects);
        Assert.Throws<ArgumentException>(() => new VideoRecordingConfiguration(new(VideoSourceKind.Webcam, "camera", CameraFormat: combined.CameraFormat, MouseEffects: p), "qa").Validate());
        Assert.Throws<ArgumentException>(() => new VideoSourceOptions(VideoSourceKind.Monitor, "screen", false, MouseEffects: p).ValidateScreen());
    }
}
