using System.Resources;
using System.Security.Cryptography;
namespace CaptureFlow.Tests;
public class Branding2026Tests
{
    [Theory]
    [InlineData(1.0)] [InlineData(1.25)] [InlineData(1.5)]
    public void FractionalDpiCardsCannotOverflowAndToggleAutomaticScrollbars(double dpi)
    {
        var converter = new CaptureFlow.Desktop.Converters.CardWidthConverter();
        for (var width = 500d; width <= 1400; width += .1)
        {
            var card = (double)converter.Convert(width, typeof(double), null!, System.Globalization.CultureInfo.InvariantCulture);
            var used = Math.Ceiling(card * dpi) * (width >= 820 ? 2 : 1) + Math.Ceiling(16 * dpi);
            Assert.True(used <= Math.Floor(width * dpi), $"Cards overflow at {width} DIP / {dpi} scaling.");
        }
    }
    [Fact]
    public void ThemeContainsNoDuplicateTopLevelResourceKeys()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "CaptureFlow.slnx"))) folder = folder.Parent;
        Assert.NotNull(folder);
        var document = System.Xml.Linq.XDocument.Load(Path.Combine(folder!.FullName, "src", "CaptureFlow.Desktop", "Themes", "DarkTheme.xaml"));
        System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var keys = document.Root!.Elements().Select(e => (string?)e.Attribute(x + "Key")
            ?? (e.Name.LocalName == "Style" ? "type:" + (string?)e.Attribute("TargetType") : null)).Where(k => k is not null).ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }
    [Theory]
    [InlineData("assets/branding/captureflow-new-logo.png", "11B5F68092AF11143AB54E9980D4CB05D6C31F30D66A25DB4B1C713A0CD541AC")]
    [InlineData("assets/branding/captureflow-new-icon.ico", "A991D8AA30788750CDC137D7DAC40B7B044D0E4EAD47CEA175805A0DE62D9056")]
    public void SuppliedBrandingIsEmbeddedWithoutAlteringArtwork(string name, string expectedHash)
    {
        using var stream = typeof(CaptureFlow.Desktop.App).Assembly.GetManifestResourceStream("CaptureFlow.Desktop.g.resources")!;
        using var resources = new ResourceReader(stream);
        var entry = resources.Cast<System.Collections.DictionaryEntry>().Single(e => (string)e.Key == name);
        using var asset = (Stream)entry.Value!;
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(asset)));
    }
}
