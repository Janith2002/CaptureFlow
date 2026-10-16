using System.Diagnostics;
using System.Text;
namespace CaptureFlow.Infrastructure.Ffmpeg;
internal sealed class FfmpegProcess : IDisposable
{
    public static void CheckStorage(string outputDirectory)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(outputDirectory))!);
        if (drive.IsReady && drive.AvailableFreeSpace < 256L * 1024 * 1024)
            throw new IOException("Less than 256 MB of free disk space remains. Choose another recording folder.");
    }
    public static async Task RunAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = new FfmpegProcess(executable, arguments, false);
        await process.FinishAsync(timeout, token).ConfigureAwait(false);
    }
    private readonly Process process;
    private readonly Task errors;
    private readonly StreamReader? outputReader;
    private readonly StreamReader? errorReader;
    private readonly StreamWriter? inputWriter;
    private readonly StringBuilder tail = new();
    private bool disposed;
    public Stream Input => inputWriter?.BaseStream ?? throw new InvalidOperationException("Standard input is not redirected.");
    public Stream Output => outputReader?.BaseStream ?? throw new InvalidOperationException("Standard output is not redirected.");
    public string ErrorTail { get { lock (tail) return tail.ToString(); } }
    public FfmpegProcess(string executable, IEnumerable<string> arguments, bool input, bool output = false, string? workingDirectory = null)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = input, RedirectStandardError = true, RedirectStandardOutput = output };
        if (workingDirectory is not null) info.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        process = new Process { StartInfo = info };
        try
        {
            info.FileName = FfmpegToolPaths.ResolveExecutable(executable);
            if (!process.Start()) throw new IOException("FFmpeg could not be started.");
            inputWriter = input ? process.StandardInput : null;
            outputReader = output ? process.StandardOutput : null;
            errorReader = process.StandardError;
            errors = ReadErrorsAsync();
        }
        catch (Exception ex)
        {
            CloseStreams(); process.Dispose();
            throw new IOException("Cannot start FFmpeg. Install a build with the required encoders (libx264, AAC or libmp3lame), or configure its executable path. " + ex.Message, ex);
        }
    }
    private async Task ReadErrorsAsync()
    {
        var buffer = new char[2048];
        int read;
        while ((read = await errorReader!.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            lock (tail)
            {
                tail.Append(buffer, 0, read);
                if (tail.Length > 8192) tail.Remove(0, tail.Length - 8192);
            }
        }
    }
    public async Task FinishAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(bounded.Token).ConfigureAwait(false);
            await errors.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException($"FFmpeg exited with code {process.ExitCode}. {ErrorTail}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("FFmpeg did not finish within the allowed time. " + ErrorTail); }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); }
            errors.GetAwaiter().GetResult();
        }
        finally { CloseStreams(); process.Dispose(); }
    }
    private void CloseStreams()
    {
        // Process.Close deliberately leaves caller-accessed redirected streams open.
        // This wrapper owns them, including stderr drained through ReadAsync rather than BeginErrorReadLine.
        foreach (var stream in new IDisposable?[] { inputWriter, outputReader, errorReader })
            try { stream?.Dispose(); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            { /* A completed/killed encoder or a caller closing stdin may already have closed its pipe. */ }
    }
}
