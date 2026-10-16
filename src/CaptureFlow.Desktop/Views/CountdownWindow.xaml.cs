using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace CaptureFlow.Desktop.Views;
public partial class CountdownWindow : Window
{
    public event Action? CancelRequested;
    public CountdownWindow() { InitializeComponent(); KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) CancelRequested?.Invoke(); }; }
    public void Display(int remaining)
    {
        Number.Text = remaining.ToString();
        Number.BeginAnimation(OpacityProperty, new DoubleAnimation(.4, 1, TimeSpan.FromMilliseconds(220)));
        var scale = (ScaleTransform)Number.RenderTransform;
        var animation = new DoubleAnimation(.82, 1, TimeSpan.FromMilliseconds(350)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation); scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
    private void CancelClicked(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
}
