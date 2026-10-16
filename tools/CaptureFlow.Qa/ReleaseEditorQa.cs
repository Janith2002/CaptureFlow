using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;
using CaptureFlow.Core.Editing;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Desktop.Views;
namespace CaptureFlow.Qa;
internal static class ReleaseEditorQa
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Entry
    { public uint Size, Usage, Id; public UIntPtr Heap; public uint Module, Threads, Parent; public int Priority; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool First(SafeFileHandle snapshot, ref Entry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Next(SafeFileHandle snapshot, ref Entry entry);
    private static int[] OwnedTools()
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var entry = new Entry { Size = (uint)Marshal.SizeOf<Entry>(), Name = "" }; var ids = new List<int>();
        if (!First(snapshot, ref entry)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        do { if (entry.Parent == Environment.ProcessId && entry.Name is "ffmpeg.exe" or "ffprobe.exe") ids.Add((int)entry.Id); } while (Next(snapshot, ref entry));
        return ids.ToArray();
    }
    public static async Task RunAsync(EditorViewModel vm, string source, string folder)
    {
        var snapshot = vm.Project; var originalFormat = vm.ExportFormat; var originalQuality = vm.PreviewQuality;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) }; var watch = Stopwatch.StartNew(); var last = watch.Elapsed.TotalMilliseconds;
        var ticks = new List<double>(); using var process = Process.GetCurrentProcess(); process.Refresh();
        var before = process.PrivateMemorySize64; var handles = process.HandleCount; var peak = before;
        ResourceSnapshot.Log("repeat-start");
        timer.Tick += (_, _) => { var now = watch.Elapsed.TotalMilliseconds; ticks.Add(now - last); last = now; process.Refresh(); peak = Math.Max(peak, process.PrivateMemorySize64); };
        timer.Start();
        try
        {
            for (var cycle = 0; cycle < 24; cycle++)
            {
                vm.ResetProject(EditorProject.Empty); await vm.ImportFileAsync(source, true);
                if (cycle == 0) ResourceSnapshot.Log("first-import");
                vm.UndoCommand.Execute(null); vm.UndoCommand.Execute(null); Check(vm.Project.Media.Length == 0 && vm.Project.Clips.Length == 0, "Undo import left media behind.");
                vm.ResetProject(snapshot); await vm.ImportFileAsync(source);
                if (cycle == 0) ResourceSnapshot.Log("first-reopen");
                if (cycle % 6 == 0)
                {
                    vm.PreviewQuality = "360p"; await vm.PreviewCommand.ExecuteAsync(null); Check(!vm.PreviewStale, vm.Message);
                    vm.ExportFormat = cycle % 12 == 0 ? "WAV" : "MP3"; await vm.ExportCommand.ExecuteAsync(null); Check(vm.Message.StartsWith("Completed"), vm.Message);
                    Check(OwnedTools().Length == 0, "An owned FFmpeg/probe process survived completed rendering.");
                    process.Refresh(); Console.WriteLine($"Release editor cycle={cycle + 1}/24 private_bytes={process.PrivateMemorySize64} handles={process.HandleCount}; preview + {vm.ExportFormat} export validated.");
                    ResourceSnapshot.Log("cycle-" + (cycle + 1));
                }
            }
            // Test shared coordinate handlers directly, without moving the global pointer or claiming native input acceptance.
            vm.SelectedClip = vm.Project.Clips.First(); var anchor = vm.SelectedClip; var timeline = new EditorTimeline { DataContext = vm }; vm.Zoom = 200;
            var baseline = vm.Project; var oldPlayhead = vm.Playhead; vm.Playhead = 0;
            timeline.BeginInteraction(new((anchor.Start + anchor.Duration / 2) * vm.Zoom, 45)); timeline.UpdateInteraction(new((anchor.Start + anchor.Duration / 2 + 2) * vm.Zoom, 101));
            // Advanced video effects cannot move to audio: rejection is the expected safe behavior.
            timeline.CompleteInteraction(); Check(ReferenceEquals(baseline, vm.Project), "Incompatible track move changed the project."); vm.Playhead = oldPlayhead;
            // Lengthen only this owned generated clip so cancellation can be observed during an active encoder.
            var longClip = anchor with { Start = 0, In = 0, Out = 3, Speed = .25, Transitions = [] };
            vm.ResetProject(snapshot with { Clips = [longClip] }); await vm.ImportFileAsync(source); vm.ExportFormat = "MP4";
            var existing = Directory.GetFiles(folder, "CaptureFlow_Edit_*.mp4").Length; var cancelledOnChild = false;
            var cancellationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            cancellationTimer.Tick += (_, _) => { if (OwnedTools().Length == 0) return; cancelledOnChild = true; cancellationTimer.Stop(); vm.CancelCommand.Execute(null); };
            cancellationTimer.Start(); try { await vm.ExportCommand.ExecuteAsync(null); } finally { cancellationTimer.Stop(); }
            Check(cancelledOnChild && vm.Message.StartsWith("Cancelled"), "Active export cancellation was not observed: " + vm.Message);
            Check(Directory.GetFiles(folder, "CaptureFlow_Edit_*.mp4").Length == existing, "Cancelled export published a completed output.");
            Check(OwnedTools().Length == 0, "An owned FFmpeg/probe process survived cancellation.");
            using (new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None)) { }
            Console.WriteLine("PASS 24 repeated project reset/import/remove/reopen cycles; four preview + MP3/WAV exports; active owned-child cancellation; no surviving owned FFmpeg/probe; source handle released.");
        }
        finally
        {
            timer.Stop(); vm.ResetProject(snapshot); await vm.ImportFileAsync(source); vm.SelectedClip = vm.Project.Clips.First(); vm.ExportFormat = originalFormat; vm.PreviewQuality = originalQuality;
            process.Refresh(); ticks.Sort(); Console.WriteLine($"Release editor bounded soak: wall_ms={watch.Elapsed.TotalMilliseconds:0.0}; private_bytes={before}->{process.PrivateMemorySize64}; sampled_peak={peak}; handles={handles}->{process.HandleCount}; heartbeat_samples={ticks.Count}; heartbeat_p95_ms={(ticks.Count == 0 ? 0 : ticks[(int)((ticks.Count - 1) * .95)]):0.0}; heartbeat_max_ms={(ticks.Count == 0 ? 0 : ticks[^1]):0.0}. 50-ms DispatcherTimer; not an extended leak certification.");
            ResourceSnapshot.Log("repeat-cleanup");
            await Task.Delay(1000); process.Refresh();
            Console.WriteLine($"Natural cleanup diagnostic: private_bytes={process.PrivateMemorySize64}; handles={process.HandleCount}; owned_tools={OwnedTools().Length}; no forced collection.");
            ResourceSnapshot.Log("repeat-settled");
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
