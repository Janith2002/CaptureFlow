using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CaptureFlow.Core.Editing;
namespace CaptureFlow.Desktop.ViewModels;
public partial class EditorViewModel
{
    [ObservableProperty] private AnimationProperty animationProperty = AnimationProperty.Opacity;
    [ObservableProperty] private KeyframeInterpolation interpolation;
    [ObservableProperty] private EditorKeyframe? selectedKeyframe;
    [ObservableProperty] private double keyframeTime;
    [ObservableProperty] private double keyframeValue = 1;
    [ObservableProperty] private TransitionType transitionType;
    [ObservableProperty] private TransitionPlacement transitionPlacement;
    [ObservableProperty] private double transitionDuration = .3;
    public AnimationProperty[] AnimationProperties => HasVideoSelection ? Enum.GetValues<AnimationProperty>().Where(p => p != AnimationProperty.Volume || Project.Media.Single(m => m.Id == SelectedClip!.MediaId).HasAudio).ToArray() : [AnimationProperty.Volume];
    public KeyframeInterpolation[] Interpolations { get; } = Enum.GetValues<KeyframeInterpolation>();
    public TransitionType[] TransitionTypes { get; } = Enum.GetValues<TransitionType>();
    public TransitionPlacement[] TransitionPlacements { get; } = Enum.GetValues<TransitionPlacement>();
    public EditorKeyframe[] ClipKeyframes => (SelectedClip?.Keyframes ?? []).OrderBy(k => k.Time).ThenBy(k => k.Property).ToArray();
    public string AnimationReadout => SelectedClip is { } c ? $"Local {Math.Clamp(Playhead - c.Start, 0, c.Duration):0.000}s: {AnimationProperty} = {EditorAnimation.Evaluate(c, AnimationProperty, Math.Clamp(Playhead - c.Start, 0, c.Duration)):0.###}. Update preview after edits." : "Select a clip to animate.";
    partial void OnSelectedKeyframeChanged(EditorKeyframe? value)
    { if (value is not null) { AnimationProperty = value.Property; KeyframeTime = value.Time; KeyframeValue = value.Value; Interpolation = value.Interpolation; } UpdateKeyframeCommand.NotifyCanExecuteChanged(); DeleteKeyframeCommand.NotifyCanExecuteChanged(); Changed?.Invoke(); }
    partial void OnAnimationPropertyChanged(AnimationProperty value) { KeyframeValue = EditorAnimation.Default(value); OnPropertyChanged(nameof(AnimationReadout)); }
    partial void OnTransitionPlacementChanged(TransitionPlacement value) => LoadTransition();
    private void LoadTransition()
    {
        var transition = (SelectedClip?.Transitions ?? []).SingleOrDefault(t => t.Placement == TransitionPlacement);
        if (transition is not null) { TransitionType = transition.Type; TransitionDuration = transition.Duration; }
        else { TransitionType = TransitionPlacement == TransitionPlacement.Start ? TransitionType.FadeIn : TransitionType.FadeOut; TransitionDuration = Math.Min(.3, SelectedClip?.Duration / 2 ?? .3); }
    }
    public void SelectTransition(TransitionPlacement placement) { TransitionPlacement = placement; LoadTransition(); Changed?.Invoke(); }
    private void NotifyAdvanced()
    {
        OnPropertyChanged(nameof(ClipKeyframes)); OnPropertyChanged(nameof(AnimationProperties)); OnPropertyChanged(nameof(AnimationReadout));
        foreach (var command in new IRelayCommand[] { AddKeyframeCommand, UpdateKeyframeCommand, DeleteKeyframeCommand, ApplyTransitionCommand, DeleteTransitionCommand, ResetAnimationCommand, ResetTransformCommand }) command.NotifyCanExecuteChanged();
    }
    private bool CanUpdateKeyframe => HasSelection && SelectedKeyframe is not null;
    [RelayCommand(CanExecute = nameof(HasSelection))] private void AddKeyframe()
    {
        var time = Math.Clamp(Playhead - SelectedClip!.Start, 0, SelectedClip.Duration);
        var key = new EditorKeyframe(Guid.NewGuid(), AnimationProperty, time, KeyframeValue, Interpolation);
        Commit(() => Session.SetKeyframe(SelectedClip.Id, key));
        SelectedKeyframe = ClipKeyframes.FirstOrDefault(k => k.Id == key.Id); NotifyAdvanced();
    }
    [RelayCommand(CanExecute = nameof(CanUpdateKeyframe))] private void UpdateKeyframe()
    { var key = SelectedKeyframe! with { Property = AnimationProperty, Time = KeyframeTime, Value = KeyframeValue, Interpolation = Interpolation }; Commit(() => Session.SetKeyframe(SelectedClip!.Id, key)); SelectedKeyframe = ClipKeyframes.FirstOrDefault(k => k.Id == key.Id); NotifyAdvanced(); }
    [RelayCommand(CanExecute = nameof(CanUpdateKeyframe))] private void DeleteKeyframe()
    { Commit(() => Session.DeleteKeyframe(SelectedClip!.Id, SelectedKeyframe!.Id)); SelectedKeyframe = null; NotifyAdvanced(); }
    [RelayCommand(CanExecute = nameof(HasVideoSelection))] private void ApplyTransition() => Commit(() => Session.SetTransition(SelectedClip!.Id, new(TransitionType, TransitionDuration, TransitionPlacement)));
    [RelayCommand(CanExecute = nameof(HasVideoSelection))] private void DeleteTransition() => Commit(() => Session.DeleteTransition(SelectedClip!.Id, TransitionPlacement));
    [RelayCommand(CanExecute = nameof(HasSelection))] private void ResetAnimation() => Commit(() => Session.Replace(SelectedClip! with { Keyframes = [] }));
    [RelayCommand(CanExecute = nameof(HasVideoSelection))] private void ResetTransform() => Commit(() => Session.Replace(SelectedClip! with { Crop = null, Rotation = 0, VideoScale = 1, Keyframes = (SelectedClip!.Keyframes ?? []).Where(k => k.Property == AnimationProperty.Volume).ToArray() }));
    public void SetAudioFade(bool start, double duration)
    {
        if (!HasSelection) return;
        var clip = SelectedClip!;
        var value = Math.Clamp(duration, 0, clip.Duration - (start ? clip.FadeOut : clip.FadeIn));
        Commit(() => Session.Replace(start ? clip with { FadeIn = value } : clip with { FadeOut = value }));
    }
}
