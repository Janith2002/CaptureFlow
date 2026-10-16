using System.Runtime.InteropServices;
namespace CaptureFlow.Desktop.Services;
internal static class RecordingMetadata
{
    // Windows Shell reads metadata without starting playback or invoking a recording engine.
    public static TimeSpan? ReadDurationValue(string folderPath, string fileName)
    {
        object? shell = null;
        object? folder = null;
        object? item = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return null;
            shell = Activator.CreateInstance(type);
            if (shell is null) return null;
            folder = ((dynamic)shell).NameSpace(folderPath);
            if (folder is null) return null;
            item = ((dynamic)folder).ParseName(fileName);
            if (item is null) return null;
            object? raw = ((dynamic)item).ExtendedProperty("System.Media.Duration");
            if (raw is null || !long.TryParse(raw.ToString(), out var ticks) || ticks <= 0) return null;
            var duration = TimeSpan.FromTicks(ticks);
            return duration;
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or ArgumentException or InvalidCastException)
        { return null; }
        finally
        {
            if (item is not null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
            if (folder is not null && Marshal.IsComObject(folder)) Marshal.ReleaseComObject(folder);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }
}


