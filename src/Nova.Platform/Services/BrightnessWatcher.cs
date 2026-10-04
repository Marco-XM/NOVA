using System.Management;
using Nova.Core.Logging;

namespace Nova.Platform.Services;

/// <summary>
/// Brightness change notifications via WMI (WmiMonitorBrightnessEvent). Windows only exposes this for
/// internal panels (laptops/tablets). External monitors adjust brightness over DDC/CI, which has no
/// change notification, so the indicator is reported as unsupported there.
/// </summary>
public sealed class BrightnessWatcher : IDisposable
{
    private ManagementEventWatcher? _watcher;

    public event Action<double>? BrightnessChanged;
    public bool IsSupported { get; private set; }
    public string Status { get; private set; } = "Checking…";

    public Task StartAsync() => Task.Run(() =>
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\WMI");
            scope.Connect();
            using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness")))
            using (var results = searcher.Get())
            {
                if (results.Count == 0) throw new ManagementException("No WMI brightness instances");
            }

            _watcher = new ManagementEventWatcher(scope, new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _watcher.EventArrived += (_, e) =>
            {
                try
                {
                    var value = Convert.ToDouble(e.NewEvent.Properties["Brightness"].Value);
                    BrightnessChanged?.Invoke(Math.Clamp(value / 100.0, 0, 1));
                }
                catch (Exception ex) { Log.Debug($"Brightness event parse failed: {ex.Message}"); }
            };
            _watcher.Start();
            IsSupported = true;
            Status = "Built-in display brightness is monitored.";
        }
        catch (Exception ex)
        {
            IsSupported = false;
            Status = "Not supported on this PC — Windows only reports brightness changes for built-in laptop screens.";
            Log.Info($"Brightness indicator unavailable: {ex.Message}");
        }
    });

    public void Dispose()
    {
        try { _watcher?.Stop(); } catch { }
        _watcher?.Dispose();
        _watcher = null;
    }
}
