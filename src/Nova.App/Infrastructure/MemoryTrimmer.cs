using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Nova.Core.Logging;

namespace Nova.App.Infrastructure;

/// <summary>
/// Returns memory to Windows after bursts of allocation (startup, closing the settings window with its
/// live previews). Compacts the managed heap, then lets Windows trim pages NOVA isn't using.
/// </summary>
public static class MemoryTrimmer
{
    private static DispatcherTimer? _pending;

    /// <summary>Schedules a trim once the app has been quiet for a moment (coalesces repeated calls).</summary>
    public static void Schedule(TimeSpan delay)
    {
        _pending?.Stop();
        _pending = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = delay };
        _pending.Tick += (_, _) =>
        {
            _pending?.Stop();
            _pending = null;
            TrimNow();
        };
        _pending.Start();
    }

    public static void TrimNow()
    {
        try
        {
            var before = Process.GetCurrentProcess().WorkingSet64;
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            using var process = Process.GetCurrentProcess();
            EmptyWorkingSet(process.Handle);
            process.Refresh();
            Log.Debug($"Memory trimmed: {before / 1048576} MB → {process.WorkingSet64 / 1048576} MB working set");
        }
        catch (Exception ex)
        {
            Log.Debug($"Memory trim failed: {ex.Message}");
        }
    }

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);
}
