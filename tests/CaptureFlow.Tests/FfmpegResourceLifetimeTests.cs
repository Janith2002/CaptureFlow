using System.Diagnostics;
using System.Reflection;
using CaptureFlow.Infrastructure.Ffmpeg;
namespace CaptureFlow.Tests;
public class FfmpegResourceLifetimeTests
{
    [FfmpegTheory] [InlineData("success")] [InlineData("error")] [InlineData("cancel")]
    public async Task OwnedPipesAreClosedImmediatelyWithoutCollectionOnEveryExitPath(string outcome)
    {
        var arguments = outcome switch
        {
            "success" => new[] { "-v", "error", "-nostdin", "-version" },
            "error" => ["-v", "error", "-nostdin", "-f", "lavfi", "-i", "invalid_filter_for_qa", "-f", "null", "-"],
            _ => ["-v", "error", "-nostdin", "-re", "-f", "lavfi", "-i", "color=s=16x16:r=30:d=30", "-f", "null", "-"]
        };
        var owned = new FfmpegProcess(Executable.Find("ffmpeg.exe")!, arguments, true, true);
        var native = (Process)typeof(FfmpegProcess).GetField("process", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owned)!;
        var pid = native.Id; var input = owned.Input; var output = owned.Output; var error = native.StandardError.BaseStream;
        using var sink = new MemoryStream(); var read = output.CopyToAsync(sink);
        using var cancel = new CancellationTokenSource(); if (outcome == "cancel") cancel.CancelAfter(100);
        try
        {
            if (outcome == "success") { await owned.FinishAsync(TimeSpan.FromSeconds(10), cancel.Token); await read; }
            else if (outcome == "error") await Assert.ThrowsAsync<IOException>(() => owned.FinishAsync(TimeSpan.FromSeconds(10), cancel.Token));
            else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owned.FinishAsync(TimeSpan.FromSeconds(10), cancel.Token));
        }
        finally { owned.Dispose(); owned.Dispose(); try { await read; } catch (ObjectDisposedException) { } }
        Assert.False(input.CanWrite); Assert.False(output.CanRead); Assert.False(error.CanRead);
        try { using var child = Process.GetProcessById(pid); Assert.True(child.HasExited); } catch (ArgumentException) { /* Owned PID is no longer present. */ }
    }
}
