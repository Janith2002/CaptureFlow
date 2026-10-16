namespace CaptureFlow.Infrastructure.Desktop;
public static class ApplicationDataPaths
{
    public static string Root => ForUser(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    public static string ForUser(string localApplicationData)
    {
        if (!Path.IsPathFullyQualified(localApplicationData)) throw new IOException("A valid per-user LocalApplicationData directory is required.");
        return Path.Combine(Path.GetFullPath(localApplicationData), "CaptureFlow");
    }
}
