using System.Windows.Controls;
namespace CaptureFlow.Desktop.Views;
public partial class CameraControls : UserControl
{
    public static readonly System.Windows.DependencyProperty ShowPreviewProperty = System.Windows.DependencyProperty.Register(nameof(ShowPreview), typeof(bool), typeof(CameraControls), new System.Windows.PropertyMetadata(true));
    public bool ShowPreview { get => (bool)GetValue(ShowPreviewProperty); set => SetValue(ShowPreviewProperty, value); }
    public CameraControls() => InitializeComponent();
}
