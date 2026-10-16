using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CaptureFlow.Application.Editing;
using CaptureFlow.Core.Editing;
using CaptureFlow.Desktop.ViewModels;
namespace CaptureFlow.Desktop.Views;
public partial class EditorPage : UserControl
{
    private EditorViewModel? vm; private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private EditorMedia? cropMedia; private Point cropAnchor; private Rect crop; private Rect imageBounds; private bool cropping;
    private double cropIn;
    private Guid cropClipId;
    private Point mediaAnchor;
    public EditorPage()
    {
        InitializeComponent(); DataContextChanged += (_, _) => Attach();
        Player.PlaybackStateChanged += (_, _) => vm?.UpdatePreviewPlaybackState(Player.Session.State, Player.Session.Error);
        SourceVideo.MediaOpened += (_, _) => { if (cropMedia is not null) { SourceVideo.Position = TimeSpan.FromSeconds(cropIn); SourceVideo.Pause(); } };
        SourceVideo.MediaFailed += (_, e) => { if (vm is not null) vm.Message = "Cannot open crop source: " + e.ErrorException.Message; CloseCrop(); };
        Loaded += (_, _) => { Attach(); timer.Start(); };
        Unloaded += (_, _) => { timer.Stop(); Detach(); Release(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) Release(); else RestorePreview(); };
        Timeline.SeekRequested += position => { if (vm?.PreviewStale == false) Player.SeekTo(TimeSpan.FromSeconds(position)); };
        timer.Tick += (_, _) => { if (vm?.PreviewStale == false && Player.Session.State == CaptureFlow.Application.Library.PlaybackState.Playing) vm.Playhead = Player.Session.Position.TotalSeconds; };
    }
    private void Attach() { Detach(); vm = DataContext as EditorViewModel; if (vm is not null) { vm.PreviewRequested += OpenPreview; vm.PreviewReleaseRequested += Release; vm.PlayPreviewRequested += Player.Play; vm.PausePreviewRequested += Player.Pause; vm.StopPreviewRequested += Player.Stop; vm.PropertyChanged += WorkspaceChanged; vm.UpdatePreviewPlaybackState(Player.Session.State, Player.Session.Error); ApplyWorkspace(); } RestorePreview(); }
    private void Detach() { if (vm is not null) { vm.PreviewRequested -= OpenPreview; vm.PreviewReleaseRequested -= Release; vm.PlayPreviewRequested -= Player.Play; vm.PausePreviewRequested -= Player.Pause; vm.StopPreviewRequested -= Player.Stop; vm.PropertyChanged -= WorkspaceChanged; } }
    private void WorkspaceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.Workspace)) { explicitPanels = true; ApplyWorkspace(); }
        else if (e.PropertyName == nameof(EditorViewModel.IsMonitorFocused)) ApplyWorkspace();
    }
    private bool explicitPanels;
    private bool monitorFocus => vm?.IsMonitorFocused == true;
    private void ToggleMonitorFocus(object sender, RoutedEventArgs e)
    {
        if (vm is not null) vm.IsMonitorFocused = !vm.IsMonitorFocused;
    }
    private double lastWidth;
    private void PremiumSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (TimelineRow is not null) TimelineRow.MinHeight = monitorFocus ? 0 : e.NewSize.Height >= 560 ? 260 : e.NewSize.Height >= 450 ? 210 : 154;
        if (Math.Abs(e.NewSize.Width - lastWidth) > 1) { explicitPanels = false; lastWidth = e.NewSize.Width; ApplyWorkspace(); }
    }
    private void ToggleMediaDock(object sender, RoutedEventArgs e)
    {
        if (vm is null) return; var show = !MediaDock.IsVisible; explicitPanels = true;
        if (vm.Workspace.MediaVisible != show) vm.ToggleEditorMediaCommand.Execute(null); else ApplyWorkspace();
    }
    private void ToggleInspectorDock(object sender, RoutedEventArgs e)
    {
        if (vm is null) return; var show = !InspectorDock.IsVisible; explicitPanels = true;
        if (vm.Workspace.InspectorVisible != show) vm.ToggleEditorInspectorCommand.Execute(null); else ApplyWorkspace();
    }
    private void PreviewFullscreen(object sender, RoutedEventArgs e) => Player.ToggleFullscreen();
    private void PreviewMute(object sender, RoutedEventArgs e) => Player.ToggleMute();
    private void ShowExport(object sender, RoutedEventArgs e) => ExportPanel.Visibility = Visibility.Visible;
    private void HideExport(object sender, RoutedEventArgs e) => ExportPanel.Visibility = Visibility.Collapsed;
    private void TimelineViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Timeline is not null) Timeline.TrackHeight = Math.Clamp((e.NewSize.Height - 42) / 3, 56, 200);
    }
    private void ApplyWorkspace()
    {
        if (vm is null) return; var value = vm.Workspace;
        TimelineDock.Visibility = TimelineDivider.Visibility = monitorFocus ? Visibility.Collapsed : Visibility.Visible;
        TimelineRow.MinHeight = monitorFocus ? 0 : ActualHeight >= 560 ? 260 : ActualHeight >= 450 ? 210 : 154;
        var media = value.MediaVisible && (ActualWidth >= 880 || explicitPanels);
        var inspector = value.InspectorVisible && (ActualWidth >= 1200 || explicitPanels);
        MediaDock.Visibility = media ? Visibility.Visible : Visibility.Collapsed;
        InspectorDock.Visibility = inspector ? Visibility.Visible : Visibility.Collapsed;
        MediaColumn.MinWidth = 0; MediaColumn.Width = new(media ? Math.Clamp(value.MediaWidth == 128 ? 200 : value.MediaWidth, 120, Math.Max(200, ActualWidth * .3)) : 0);
        InspectorColumn.MinWidth = 0; InspectorColumn.Width = new(inspector ? Math.Clamp(value.InspectorWidth == 224 ? 260 : value.InspectorWidth, 180, Math.Max(260, ActualWidth * .3)) : 0);
        var legacy = value.PreviewRatio == 1.3 && value.TimelineRatio == 2.7;
        PreviewRow.Height = new(legacy ? 3 : value.PreviewRatio, GridUnitType.Star); TimelineRow.Height = new(legacy ? 2 : value.TimelineRatio, GridUnitType.Star);
        if (monitorFocus) TimelineRow.Height = new(0);
    }
    private async void WorkspaceDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (vm is null) return; UpdateLayout(); var total = Math.Max(1, PreviewRow.ActualHeight + TimelineRow.ActualHeight);
        await vm.SaveWorkspaceAsync(vm.Workspace with { MediaWidth = MediaDock.IsVisible ? Math.Clamp(MediaColumn.ActualWidth, 120, 600) : vm.Workspace.MediaWidth, InspectorWidth = InspectorDock.IsVisible ? Math.Clamp(InspectorColumn.ActualWidth, 180, 700) : vm.Workspace.InspectorWidth, PreviewRatio = Math.Max(.5, PreviewRow.ActualHeight / total * 4), TimelineRatio = Math.Max(.5, TimelineRow.ActualHeight / total * 4) });
    }
    private void FitTimeline(object sender, RoutedEventArgs e) { vm?.FitTimeline(TimelineScroll.ViewportWidth); TimelineScroll.ScrollToHorizontalOffset(0); }
    private void FrameBack(object sender, RoutedEventArgs e) => SeekEdited((vm?.Playhead ?? 0) - 1d / 30);
    private void FrameForward(object sender, RoutedEventArgs e) => SeekEdited((vm?.Playhead ?? 0) + 1d / 30);
    private void JumpStart(object sender, RoutedEventArgs e) => SeekEdited(0);
    private void JumpEnd(object sender, RoutedEventArgs e) => SeekEdited(vm?.Project.Duration ?? 0);
    private void SeekEdited(double position)
    { if (vm is null) return; vm.Playhead = Math.Clamp(EditorSessionTime(position), 0, vm.Project.Duration); Player.Pause(); if (!vm.PreviewStale) Player.SeekTo(TimeSpan.FromSeconds(vm.Playhead)); else vm.Message = "Timeline position updated; render the edited preview to view this frame."; }
    private static double EditorSessionTime(double value) => TimelineGeometry.Quantize(value);
    private void MediaDragBegin(object sender, MouseButtonEventArgs e) => mediaAnchor = e.GetPosition(MediaList);
    private void MediaDragMove(object sender, MouseEventArgs e)
    {
        if (vm?.CanEdit != true || e.LeftButton != MouseButtonState.Pressed || MediaList.SelectedItem is not EditorMediaItem item) return;
        var delta = e.GetPosition(MediaList) - mediaAnchor;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(MediaList, new DataObject(EditorTimeline.MediaDragFormat, item.Media.Id), DragDropEffects.Copy); e.Handled = true;
    }
    private void OpenPreview(string path) { Release(); if (IsVisible) Player.Open(path, true, TimeSpan.FromSeconds(vm?.Playhead ?? 0)); }
    private void PreviewSizeChanged(object sender, SizeChangedEventArgs e) { if (Player is not null) Player.Height = Math.Max(175, e.NewSize.Height - 60); }
    private void RestorePreview() { if (IsVisible && vm?.PreviewStale == false && vm.PreviewPath is { } path && System.IO.File.Exists(path)) Player.Open(path, false, TimeSpan.FromSeconds(vm.Playhead)); }
    private void PlayEdited(object sender, RoutedEventArgs e) { if (vm?.PlayPreviewCommand.CanExecute(null) == true) vm.PlayPreviewCommand.Execute(null); }
    private void PauseEdited(object sender, RoutedEventArgs e) { if (vm?.PausePreviewCommand.CanExecute(null) == true) vm.PausePreviewCommand.Execute(null); }
    private void StopEdited(object sender, RoutedEventArgs e) { if (vm?.StopPreviewCommand.CanExecute(null) == true) vm.StopPreviewCommand.Execute(null); }
    private void Release() { Player.CloseMedia(); SourcePlayer.CloseMedia(); AuditionPanel.Visibility = Visibility.Collapsed; CloseCrop(); }
    private void AuditionSource(object sender, RoutedEventArgs e)
    {
        if (vm?.SelectedClip is not { } clip || !vm.CanEdit) return;
        var media = vm.Project.Media.Single(m => m.Id == clip.MediaId); Player.Stop(); AuditionPanel.Visibility = Visibility.Visible; SourcePlayer.Open(media.Path);
    }
    private void CloseAudition(object sender, RoutedEventArgs e) { SourcePlayer.CloseMedia(); AuditionPanel.Visibility = Visibility.Collapsed; }
    private void BeginCrop(object sender, RoutedEventArgs e)
    {
        if (vm?.SelectedClip is not { } clip || !vm.CanEdit) return;
        cropMedia = vm.Project.Media.Single(m => m.Id == clip.MediaId);
        if (!cropMedia.HasVideo || !vm.Project.Tracks.Single(t => t.Id == clip.TrackId).Video) { vm.Message = "Select a video clip on the video track to crop."; return; }
        Player.CloseMedia(); CropPanel.Visibility = Visibility.Visible; crop = new(); CropRectangle.Width = CropRectangle.Height = 0;
        cropClipId = clip.Id; cropIn = clip.In; SourceVideo.Source = new Uri(cropMedia.Path); SourceVideo.Volume = 0;
        SourceVideo.Play();
    }
    private Point Clamp(Point p) => new(Math.Clamp(p.X, imageBounds.Left, imageBounds.Right), Math.Clamp(p.Y, imageBounds.Top, imageBounds.Bottom));
    private void CropBegin(object sender, MouseButtonEventArgs e)
    {
        if (cropMedia is null) return;
        var scale = Math.Min(CropSurface.ActualWidth / cropMedia.Width, CropSurface.ActualHeight / cropMedia.Height);
        var w = cropMedia.Width * scale; var h = cropMedia.Height * scale;
        imageBounds = new((CropSurface.ActualWidth - w) / 2, (CropSurface.ActualHeight - h) / 2, w, h);
        cropAnchor = Clamp(e.GetPosition(CropSurface)); cropping = true; CropSurface.CaptureMouse(); e.Handled = true;
    }
    private void CropMove(object sender, MouseEventArgs e)
    {
        if (!cropping) return; crop = new(cropAnchor, Clamp(e.GetPosition(CropSurface))); Canvas.SetLeft(CropRectangle, crop.Left); Canvas.SetTop(CropRectangle, crop.Top); CropRectangle.Width = crop.Width; CropRectangle.Height = crop.Height;
    }
    private void CropEnd(object sender, MouseButtonEventArgs e) { cropping = false; CropSurface.ReleaseMouseCapture(); }
    private void ApplyCrop(object sender, RoutedEventArgs e)
    {
        if (vm is null || cropMedia is null || !vm.CanEdit) return;
        if (vm.SelectedClip?.Id != cropClipId) { vm.Message = "The selected clip changed. Reopen source crop for that clip."; CloseCrop(); return; }
        try { vm.SetCrop(EditorSession.CropFromNormalized((crop.X - imageBounds.X) / imageBounds.Width, (crop.Y - imageBounds.Y) / imageBounds.Height, crop.Width / imageBounds.Width, crop.Height / imageBounds.Height, cropMedia)); CloseCrop(); }
        catch (Exception ex) { vm.Message = ex.Message; }
    }
    private void CancelCrop(object sender, RoutedEventArgs e) => CloseCrop();
    private void CloseCrop() { SourceVideo.Stop(); SourceVideo.Close(); SourceVideo.Source = null; CropPanel.Visibility = Visibility.Collapsed; cropping = false; CropSurface.ReleaseMouseCapture(); cropMedia = null; }
    private void EditorKeyDown(object sender, KeyEventArgs e)
    {
        if (vm is null || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        CommunityToolkit.Mvvm.Input.IRelayCommand? command = ctrl && e.Key == Key.S ? vm.SaveProjectCommand : ctrl && e.Key == Key.O ? vm.OpenProjectCommand : null;
        if (e.OriginalSource is not TextBox) command ??= (ctrl, e.Key) switch { (true, Key.Z) => vm.UndoCommand, (true, Key.Y) => vm.RedoCommand, (true, Key.C) => vm.CopyCommand, (true, Key.X) => vm.CutCommand, (true, Key.V) => vm.PasteCommand, (false, Key.Delete) => vm.DeleteCommand, (false, Key.S) => vm.SplitCommand, _ => null };
        if (e.OriginalSource is not TextBox && !ctrl && e.Key is Key.V or Key.T) command = e.Key == Key.V ? vm.SelectToolCommand : vm.TrimToolCommand;
        if (command?.CanExecute(null) == true) { command.Execute(null); e.Handled = true; }
    }
}
