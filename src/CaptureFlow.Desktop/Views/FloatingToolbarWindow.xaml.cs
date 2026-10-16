using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Desktop.Views;
public partial class FloatingToolbarWindow : Window
{
    public event Action? HideRequested;
    private readonly FloatingToolbarViewModel model;
    public FloatingToolbarWindow(FloatingToolbarViewModel model)
    {
        InitializeComponent(); this.model = model; DataContext = model;
        model.PropertyChanged += Changed; Closed += (_, _) => model.PropertyChanged -= Changed;
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(model.Collapsed)) { Controls.Visibility = model.Collapsed ? Visibility.Collapsed : Visibility.Visible; Height = model.Collapsed ? 70 : 152; } }
    private void Drag(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void HideClicked(object sender, RoutedEventArgs e) => HideRequested?.Invoke();
}
