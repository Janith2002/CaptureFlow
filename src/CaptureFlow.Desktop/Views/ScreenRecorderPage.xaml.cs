using System.Windows.Controls;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.ComponentModel;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Desktop.Views;
public partial class ScreenRecorderPage : UserControl
{
    public ScreenRecorderPage() => InitializeComponent();
    private ScreenRecorderViewModel? model;
    private bool applying;
    private bool explicitProperties;
    private double lastWidth;
    private void PremiumSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(lastWidth - e.NewSize.Width) > 1) { lastWidth = e.NewSize.Width; explicitProperties = false; }
        ApplyLayout();
    }
    private void ToggleSources(object sender, RoutedEventArgs e) { if (model is not null) model.ShowStudioSources = !SourcesDock.IsVisible; ApplyLayout(); }
    private void ToggleInspector(object sender, RoutedEventArgs e) { if (model is not null) { explicitProperties = true; model.ShowStudioProperties = !PropertiesDock.IsVisible; ApplyLayout(); } }
    private void ResetResponsivePanels(object sender, RoutedEventArgs e) { explicitProperties = false; ApplyLayout(); }
    private void WorkspaceLoaded(object sender, RoutedEventArgs e)
    {
        WorkspaceUnloaded(sender, e); model = (DataContext as DashboardViewModel)?.Screen;
        if (model is null) return;
        model.PreviewDpiScale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        model.PropertyChanged += ModelChanged; ApplyLayout(); model.RefreshStudio();
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    { base.OnDpiChanged(oldDpi, newDpi); if (model is not null) model.PreviewDpiScale = newDpi.DpiScaleX; }
    private void WorkspaceUnloaded(object sender, RoutedEventArgs e) { if (model is not null) model.PropertyChanged -= ModelChanged; model = null; }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScreenRecorderViewModel.ShowStudioProperties)) explicitProperties = true;
        if (e.PropertyName == nameof(ScreenRecorderViewModel.ShowStudioMixer)) ApplyLayout();
        if (e.PropertyName == nameof(ScreenRecorderViewModel.ShowStudioSources)) ApplyLayout();
        if (e.PropertyName is nameof(ScreenRecorderViewModel.StudioSourcesWidth) or nameof(ScreenRecorderViewModel.StudioPropertiesWidth) or nameof(ScreenRecorderViewModel.StudioMixerHeight) or nameof(ScreenRecorderViewModel.ShowStudioProperties)) ApplyLayout();
        if (e.PropertyName is nameof(ScreenRecorderViewModel.SelectedMode) or nameof(ScreenRecorderViewModel.SelectedMonitor) or nameof(ScreenRecorderViewModel.SelectedApplicationWindow) or nameof(ScreenRecorderViewModel.SelectedCamera) or nameof(ScreenRecorderViewModel.MicrophoneEnabled) or nameof(ScreenRecorderViewModel.SystemAudioEnabled) or nameof(ScreenRecorderViewModel.MicrophoneMuted) or nameof(ScreenRecorderViewModel.SystemAudioMuted) or nameof(ScreenRecorderViewModel.SelectedResolution) or nameof(ScreenRecorderViewModel.SelectedFrameRate) or nameof(ScreenRecorderViewModel.SelectedEncoder) or nameof(ScreenRecorderViewModel.SelectedMicrophone) or nameof(ScreenRecorderViewModel.SelectedOutputDevice) or nameof(ScreenRecorderViewModel.IsCameraPreviewing) or nameof(ScreenRecorderViewModel.IsCameraBusy)) model?.RefreshStudio();
    }
    private void ApplyLayout()
    {
        if (model is null) return; applying = true;
        try
        {
            var sources = model.ShowStudioSources && ActualWidth >= 700;
            var properties = model.ShowStudioProperties && (ActualWidth >= 1200 || explicitProperties);
            SourcesDock.Visibility = sources ? Visibility.Visible : Visibility.Collapsed;
            PropertiesDock.Visibility = properties ? Visibility.Visible : Visibility.Collapsed;
            SourcesColumn.MinWidth = 0; SourcesColumn.Width = new(sources ? Math.Clamp(model.StudioSourcesWidth, 160, Math.Max(160, ActualWidth * .18)) : 0);
            PropertiesColumn.MinWidth = 0; PropertiesColumn.Width = new(properties ? Math.Clamp(model.StudioPropertiesWidth, 220, Math.Max(220, ActualWidth * .23)) : 0);
            MixerDock.Visibility = model.ShowStudioMixer ? Visibility.Visible : Visibility.Collapsed;
            MixerRow.MinHeight = 0; MixerRow.Height = new(model.ShowStudioMixer ? Math.Clamp(model.StudioMixerHeight * .5, 94, Math.Max(94, ActualHeight * .23)) : 0);
        }
        finally { applying = false; }
    }
    private async void PanelDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (applying || model is null) return;
        if (SourcesDock.IsVisible) model.StudioSourcesWidth = Math.Clamp(SourcesColumn.ActualWidth, 160, 600);
        if (PropertiesDock.IsVisible) model.StudioPropertiesWidth = Math.Clamp(PropertiesColumn.ActualWidth, 220, 700);
        // The compact dock displays half the legacy persisted height. Retain that
        // representation so existing workspaces round-trip without shrinking on every drag.
        if (e.VerticalChange != 0 && model.ShowStudioMixer) model.StudioMixerHeight = Math.Clamp(MixerRow.ActualHeight * 2, 180, 600);
        await model.SaveStudioAsync();
    }
}
