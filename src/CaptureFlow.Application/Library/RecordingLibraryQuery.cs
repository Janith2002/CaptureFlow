using CaptureFlow.Core.Library;

namespace CaptureFlow.Application.Library;

public static class RecordingLibraryQuery
{
    public static readonly string[] Filters = ["All Recordings", "Screen Recordings", "Webcam Recordings", "Screen + Webcam", "Audio Recordings"];
    public static readonly string[] Sorts = ["Newest first", "Oldest first", "Filename", "Duration", "File size"];
    public static bool Matches(RecordingEntry entry, string search, string filter) => entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase) && filter switch
    {
        "Screen Recordings" => entry.Type == RecordingType.Screen,
        "Webcam Recordings" => entry.Type == RecordingType.Webcam,
        "Screen + Webcam" => entry.Type == RecordingType.ScreenAndWebcam,
        "Audio Recordings" => entry.Type == RecordingType.Audio,
        _ => true
    };
    public static IEnumerable<RecordingEntry> Apply(IEnumerable<RecordingEntry> entries, string search, string filter, string sort)
    {
        var selected = entries.Where(e => Matches(e, search, filter));
        return sort switch
        {
            "Oldest first" => selected.OrderBy(e => e.Created).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            "Filename" => selected.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
            "Duration" => selected.OrderByDescending(e => e.Duration?.Ticks ?? -1).ThenBy(e => e.Name),
            "File size" => selected.OrderByDescending(e => e.Bytes).ThenBy(e => e.Name),
            _ => selected.OrderByDescending(e => e.Created).ThenBy(e => e.Name)
        };
    }
    public static RecordingType InferType(string path)
    {
        if (!Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)) return RecordingType.Audio;
        var name = Path.GetFileName(path);
        if (name.StartsWith("CaptureFlow_ScreenWebcam_", StringComparison.OrdinalIgnoreCase)) return RecordingType.ScreenAndWebcam;
        if (name.StartsWith("CaptureFlow_Webcam_", StringComparison.OrdinalIgnoreCase)) return RecordingType.Webcam;
        if (name.StartsWith("CaptureFlow_Screen_", StringComparison.OrdinalIgnoreCase)) return RecordingType.Screen;
        return RecordingType.OtherVideo;
    }
}
