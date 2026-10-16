using System.Resources;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
namespace CaptureFlow.Tests;
public class AppIconHotfixTests
{
    private static string Root()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "CaptureFlow.slnx"))) folder = folder.Parent;
        return folder?.FullName ?? throw new DirectoryNotFoundException("Solution root not found.");
    }
    [Fact] public void ProjectUsesTheApprovedExecutableIconAndTheFileExists()
    {
        var project = Path.Combine(Root(), "src", "CaptureFlow.Desktop", "CaptureFlow.Desktop.csproj");
        var icon = XDocument.Load(project).Descendants("ApplicationIcon").Single().Value;
        Assert.Equal(Path.GetFullPath(Path.Combine(Root(), "assets", "branding", "CaptureFlow-New-Icon.ico")), Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, icon)));
        Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, icon))));
    }
    [Theory] [InlineData("MainWindow.xaml")] [InlineData("Views/CountdownWindow.xaml")] [InlineData("Views/FloatingToolbarWindow.xaml")] [InlineData("Views/RegionSelectionWindow.xaml")]
    public void EveryDeclaredWindowExplicitlyUsesApprovedIco(string path)
    {
        var window = XDocument.Load(Path.Combine(Root(), "src", "CaptureFlow.Desktop", path)).Root!;
        Assert.Equal(CaptureFlow.Desktop.Services.ApplicationBranding.IconUri, (string?)window.Attribute("Icon"));
    }
    [Fact] public void LogoAndShellIconAreSeparateAndDynamicWindowsHaveAnIconDefault()
    {
        var app = XDocument.Load(Path.Combine(Root(), "src", "CaptureFlow.Desktop", "App.xaml")); XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var logo = app.Descendants().Single(e => (string?)e.Attribute(x + "Key") == "CaptureFlowLogo"); Assert.EndsWith("CaptureFlow-New-Logo.png", (string?)logo.Attribute("UriSource"));
        var setter = app.Descendants().Single(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "Icon");
        Assert.Equal(CaptureFlow.Desktop.Services.ApplicationBranding.IconUri, (string?)setter.Attribute("Value"));
        Assert.Equal("CaptureFlow.Desktop", CaptureFlow.Desktop.Services.ApplicationBranding.AppUserModelId);
    }
    [Fact] public Task ApprovedIcoDecodesAllSevenEmbeddedSizes() => DesktopRecordingCommandTests.Sta(() =>
    {
        using var stream = typeof(CaptureFlow.Desktop.App).Assembly.GetManifestResourceStream("CaptureFlow.Desktop.g.resources")!;
        using var resources = new ResourceReader(stream);
        using var ico = (Stream)resources.Cast<System.Collections.DictionaryEntry>().Single(e => (string)e.Key == "assets/branding/captureflow-new-icon.ico").Value!;
        var decoder = new IconBitmapDecoder(ico, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert.Equal(new[] { 16, 24, 32, 48, 64, 128, 256 }, decoder.Frames.Select(f => f.PixelWidth));
        Assert.All(decoder.Frames, frame => Assert.Equal(frame.PixelWidth, frame.PixelHeight)); return Task.CompletedTask;
    });
}
