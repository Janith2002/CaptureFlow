namespace CaptureFlow.Tests;

public class InstallerSafetyTests
{
    private static string Read(string name)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "CaptureFlow.slnx"))) folder = folder.Parent;
        Assert.NotNull(folder);
        return File.ReadAllText(Path.Combine(folder!.FullName, "installer", name));
    }
    [Fact] public void InstallerNeverSchedulesUserDataDeletionOrAutomaticCaptureLaunch()
    {
        var script = Read("CaptureFlow.iss");
        Assert.DoesNotContain("[UninstallDelete]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[InstallDelete]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[Run]", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CloseApplications=no", script);
        Assert.Contains("RestartApplications=no", script);
        Assert.Contains("PrivilegesRequired=lowest", script);
        Assert.Contains("DefaultDirName={localappdata}\\Programs\\{#ProductName}", script);
    }
    [Fact] public void InstallerKeepsExternalToolsAndUserMediaOutOfItsPayload()
    {
        var script = Read("CaptureFlow.iss");
        foreach (var extension in new[] { "ffmpeg.exe", "ffprobe.exe", "*.mp4", "*.mp3", "*.wav", "*.cfedit", "*.db" }) Assert.Contains(extension, script);
        Assert.Contains("Uninstallable=yes", script);
        Assert.Contains("SetupMutex=", script);
        Assert.Contains("UsePreviousAppDir=yes", script);
        Assert.DoesNotContain("AppPublisher=", script);
        Assert.Contains("assets\\branding\\CaptureFlow-New-Icon.ico", script);
        var build = Read("Build-Installer.ps1");
        Assert.Contains("--self-contained true", build);
        Assert.Contains("Unverified external FFmpeg must not be bundled", build);
    }
}
