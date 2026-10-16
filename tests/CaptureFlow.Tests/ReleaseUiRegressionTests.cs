using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace CaptureFlow.Tests;
public class ReleaseUiRegressionTests
{
    [Theory] [InlineData(true)] [InlineData(false)]
    public Task ThemedComboShowsDisplayNameInsteadOfRecordDump(bool device) => DesktopRecordingCommandTests.Sta(() =>
    {
        var theme = (ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/CaptureFlow.Desktop;component/Themes/DarkTheme.xaml", UriKind.Relative));
        var combo = new ComboBox { Width = 320, DisplayMemberPath = "Name", ItemsSource = device ? new object[] { new CaptureFlow.Core.Audio.MicrophoneDevice("opaque-id", "QA microphone") } : new object[] { new CaptureFlow.Core.Audio.AudioOutputDevice("opaque-id", "QA speakers") }, SelectedIndex = 0 };
        combo.Resources.MergedDictionaries.Add(theme); combo.Measure(new Size(320, 50)); combo.Arrange(new Rect(0, 0, 320, 50)); combo.UpdateLayout();
        var text = Descendants(combo).OfType<TextBlock>().Select(t => t.Text).ToArray(); Assert.Contains(device ? "QA microphone" : "QA speakers", text); Assert.DoesNotContain(text, t => t.Contains("opaque-id"));
        return Task.CompletedTask;
    });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
}
