using System.Runtime.CompilerServices;
using System.Windows;
using CaptureFlow.Application.Library;
using CaptureFlow.Desktop.ViewModels;
using CaptureFlow.Desktop.Views;
namespace CaptureFlow.Qa;

internal static class ManagedPlaybackLifetimeQa
{
    public static async Task RunAsync(Window owner, EditorViewModel vm, string generatedSource)
    {
        // Keep the real VM alive: an orphaned page subscription would retain a closed page.
        var weak = new List<WeakReference>();
        ResourceSnapshot.Log("owned-views-before");
        for (var i = 1; i <= 12; i++)
        {
            weak.AddRange(await CreateAndCloseAsync(owner, vm, generatedSource));
            if (i % 4 == 0) ResourceSnapshot.Log("owned-views-cycle-" + i);
        }
        await Task.Delay(30000);
        ResourceSnapshot.Log("owned-views-natural-settle-30s");
        ResourceSnapshot.LogThreadOwners();
        // Forensic reachability check only, after natural settling; never application cleanup.
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        await Task.Delay(1000);
        Console.WriteLine($"MANAGED_VIEWS unique objects={weak.Count}; alive after forensic collection={weak.Count(w => w.IsAlive)}; VM deliberately remains live; no production GC change.");
        GC.KeepAlive(vm);
        ResourceSnapshot.Log("owned-views-after-forensic-collection");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference[]> CreateAndCloseAsync(Window owner, EditorViewModel vm, string source)
    {
        var player = new LibraryMediaPlayer();
        var host = new Window { Owner = owner, Width = 900, Height = 650, ShowInTaskbar = false, Title = "Generated media ownership QA", Content = player };
        EditorPage? page = null;
        try
        {
            host.Show(); player.Open(source, false);
            for (var wait = 0; wait < 100 && player.Session.State == PlaybackState.Loading; wait++) await Task.Delay(50);
            if (player.Session.State == PlaybackState.Error || player.Session.State == PlaybackState.Loading)
                throw new InvalidOperationException("Owned player did not open generated fixture: " + player.Session.Error);
            player.SeekTo(TimeSpan.FromSeconds(.2)); player.Stop(); player.CloseMedia();
            page = new EditorPage { DataContext = vm }; host.Content = page;
            await host.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            // The page may restore the VM's existing generated preview. This is a view
            // lifecycle workload, not a count of exactly twelve native Open calls.
            host.Content = null;
            await host.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        finally { player.CloseMedia(); host.Content = null; host.Close(); }
        return [new(player), new(page!), new(host)];
    }
}
