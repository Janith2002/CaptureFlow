namespace CaptureFlow.Core.Editing;

public sealed record EditorExportProgress(string Stage, string? OutputPath = null, double? Fraction = null)
{
    public static double? FractionFor(long microseconds, double durationSeconds) =>
        microseconds >= 0 && double.IsFinite(durationSeconds) && durationSeconds > 0
            ? Math.Clamp(microseconds / 1_000_000d / durationSeconds, 0, 1) : null;
}

/// <summary>Optional richer export reporting; preview and existing backends keep their contract.</summary>
public interface IEditorExportBackend
{
    Task<string> RenderExportAsync(EditorProject project, EditorExportSettings settings, string outputFolder,
        string ffmpeg, IProgress<EditorExportProgress> progress, CancellationToken token);
}
