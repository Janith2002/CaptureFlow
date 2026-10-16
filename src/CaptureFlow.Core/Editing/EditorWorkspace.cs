namespace CaptureFlow.Core.Editing;
public sealed record EditorWorkspace(double MediaWidth = 128, double InspectorWidth = 224, double PreviewRatio = 1.3, double TimelineRatio = 2.7, bool MediaVisible = true, bool InspectorVisible = true)
{
    public void Validate()
    {
        if (!double.IsFinite(MediaWidth) || MediaWidth is < 120 or > 600 || !double.IsFinite(InspectorWidth) || InspectorWidth is < 180 or > 700 || !double.IsFinite(PreviewRatio) || PreviewRatio is < .5 or > 10 || !double.IsFinite(TimelineRatio) || TimelineRatio is < .5 or > 10) throw new ArgumentException("Invalid editor workspace dimensions.");
    }
}
public interface IEditorWorkspaceStore
{
    Task<EditorWorkspace> LoadAsync(CancellationToken token = default);
    Task SaveAsync(EditorWorkspace settings, CancellationToken token = default);
}
