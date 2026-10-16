using System.Windows;
namespace CaptureFlow.Desktop.Views;
public partial class WorkspacePanel : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(nameof(Heading), typeof(string), typeof(WorkspacePanel), new PropertyMetadata(""));
    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(nameof(Body), typeof(object), typeof(WorkspacePanel));
    public string Heading { get => (string)GetValue(HeadingProperty); set => SetValue(HeadingProperty, value); }
    public object? Body { get => GetValue(BodyProperty); set => SetValue(BodyProperty, value); }
    public WorkspacePanel() => InitializeComponent();
}
