using Nova.Core.Events;
using Nova.Core.Logging;
using Windows.System.Power;

namespace Nova.Platform.Services;

/// <summary>Battery / power-source events from the WinRT PowerManager (no polling).</summary>
public sealed class PowerWatcher : IDisposable
{
    private bool _started;
    private bool? _pluggedIn;
    private int _lastPercent = -1;
    private bool _announcedFull;

    public event Action<BatteryChangeKind, int, bool>? BatteryEvent;
    public event Action? StatusChanged;

    public bool HasBattery { get; private set; }
    public int Percent { get; private set; }
    public bool Charging { get; private set; }
    public bool PluggedIn { get; private set; }

    public void Start()
    {
        try
        {
            HasBattery = PowerManager.BatteryStatus != BatteryStatus.NotPresent;
            if (!HasBattery) return;
            Read();
            PowerManager.BatteryStatusChanged += OnChanged;
            PowerManager.RemainingChargePercentChanged += OnChanged;
            PowerManager.PowerSupplyStatusChanged += OnChanged;
            _started = true;
        }
        catch (Exception ex)
        {
            HasBattery = false;
            Log.Warn("PowerManager unavailable", ex);
        }
    }

    private void Read()
    {
        Percent = PowerManager.RemainingChargePercent;
        Charging = PowerManager.BatteryStatus == BatteryStatus.Charging;
        PluggedIn = PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;
        _pluggedIn ??= PluggedIn;
        if (_lastPercent < 0) _lastPercent = Percent;
    }

    private void OnChanged(object? sender, object e)
    {
        try
        {
            Read();
            if (_pluggedIn != PluggedIn)
            {
                _pluggedIn = PluggedIn;
                BatteryEvent?.Invoke(PluggedIn ? BatteryChangeKind.PluggedIn : BatteryChangeKind.Unplugged, Percent, Charging);
                if (!PluggedIn) _announcedFull = false;
            }
            else if (!PluggedIn && _lastPercent > 10 && Percent <= 10)
                BatteryEvent?.Invoke(BatteryChangeKind.Critical, Percent, Charging);
            else if (!PluggedIn && _lastPercent > 20 && Percent <= 20)
                BatteryEvent?.Invoke(BatteryChangeKind.Low, Percent, Charging);
            else if (PluggedIn && Percent >= 100 && !_announcedFull)
            {
                _announcedFull = true;
                BatteryEvent?.Invoke(BatteryChangeKind.Full, Percent, Charging);
            }
            _lastPercent = Percent;
            StatusChanged?.Invoke();
        }
        catch (Exception ex) { Log.Warn("Battery update failed", ex); }
    }

    public void Dispose()
    {
        if (!_started) return;
        PowerManager.BatteryStatusChanged -= OnChanged;
        PowerManager.RemainingChargePercentChanged -= OnChanged;
        PowerManager.PowerSupplyStatusChanged -= OnChanged;
        _started = false;
    }
}
