using System.Windows.Controls;
namespace CaptureFlow.Desktop.Views;
using System.Windows;
using System.Windows.Input;
using CaptureFlow.Desktop.ViewModels;
public partial class RecordingsLibrary : UserControl
{
    private DashboardViewModel? owner;
    public RecordingsLibrary()
    {
        InitializeComponent(); DataContextChanged += (_, _) => Attach(); Loaded += (_, _) => Attach();
        Unloaded += (_, _) => { Detach(); Player.CloseMedia(); };
    }
    private void Attach()
    {
        Detach(); owner = DataContext as DashboardViewModel;
        if (owner is not null) { owner.PlaybackRequested += Player.Open; owner.PlaybackReleaseRequested += Player.CloseMedia; }
    }
    private void Detach() { if (owner is not null) { owner.PlaybackRequested -= Player.Open; owner.PlaybackReleaseRequested -= Player.CloseMedia; } owner = null; }
    private void RecordingDoubleClick(object sender, MouseButtonEventArgs e) { if (owner?.PlayRecordingCommand.CanExecute(null) == true) owner.PlayRecordingCommand.Execute(null); }
}


