using System.Windows.Threading;
using Microsoft.Win32;
using Nova.Core.Logging;
using Nova.Core.Monitors;
using Nova.Core.Settings;
using Nova.Platform.Services;

namespace Nova.App.Services;

/// <summary>
/// Decides which monitor hosts the notch and reacts to display changes (connect/disconnect, resolution,
/// DPI), the active window moving (Active mode) and the mouse crossing monitors (Mouse mode).
/// </summary>
public sealed class MonitorService : IDisposable
{
    private readonly IMonitorProvider _provider;
    private readonly SettingsService _settings;
    private readonly ForegroundTracker _foreground;
    private readonly DispatcherTimer _displayDebounce;
    private readonly DispatcherTimer _mouseTimer;
    private string? _lastCursorMonitor;

    public MonitorService(IMonitorProvider provider, SettingsService settings, ForegroundTracker foreground)
    {
        _provider = provider;
        _settings = settings;
        _foreground = foreground;
        _displayDebounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _displayDebounce.Tick += (_, _) => { _displayDebounce.Stop(); Refresh(); };
        // Cheap cursor check (no global mouse hook); only runs in "Mouse" mode.
        _mouseTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _mouseTimer.Tick += (_, _) => CheckMouse();
    }

    public IReadOnlyList<MonitorInfo> Monitors { get; private set; } = Array.Empty<MonitorInfo>();
    public MonitorSelection? Current { get; private set; }

    /// <summary>Raised when the target monitor (or its geometry/DPI) changed.</summary>
    public event Action<MonitorSelection>? SelectionChanged;
    /// <summary>Raised when the set of connected monitors changed.</summary>
    public event Action? MonitorsChanged;

    public void Start()
    {
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _foreground.Changed += OnForegroundChanged;
        Refresh();
        ApplyMode();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => ScheduleRefresh();

    /// <summary>Display changes arrive in bursts (one per monitor); coalesce them.</summary>
    public void ScheduleRefresh()
    {
        _displayDebounce.Stop();
        _displayDebounce.Start();
    }

    public void ApplyMode()
    {
        if (_settings.Current.Monitor.Mode == MonitorMode.Mouse) _mouseTimer.Start();
        else _mouseTimer.Stop();
        Reevaluate(force: true);
    }

    public void Refresh()
    {
        try
        {
            var monitors = _provider.GetMonitors();
            var changed = monitors.Count != Monitors.Count || !monitors.SequenceEqual(Monitors);
            Monitors = monitors;
            if (changed)
            {
                Log.Info($"Monitors: {string.Join("; ", monitors.Select(m => $"{m.DisplayLabel} {m.Bounds} {m.Dpi}dpi{(m.IsPrimary ? " primary" : "")}"))}");
                MonitorsChanged?.Invoke();
            }
            Reevaluate(force: changed);
        }
        catch (Exception ex)
        {
            Log.Error("Monitor refresh failed", ex);
        }
    }

    private void OnForegroundChanged()
    {
        if (_settings.Current.Monitor.Mode == MonitorMode.Active) Reevaluate();
    }

    private void CheckMouse()
    {
        var cursor = _provider.GetCursorPosition();
        if (cursor is null) return;
        var monitor = _provider.GetMonitorAt(cursor.Value.X, cursor.Value.Y);
        if (monitor == _lastCursorMonitor) return;
        _lastCursorMonitor = monitor;
        Reevaluate();
    }

    public void Reevaluate(bool force = false)
    {
        if (Monitors.Count == 0) return;
        var cursor = _provider.GetCursorPosition();
        var context = new MonitorSelectionContext
        {
            Monitors = Monitors,
            ForegroundMonitor = _settings.Current.Monitor.Mode == MonitorMode.Active ? _provider.GetForegroundMonitor() : null,
            CursorMonitor = cursor is { } c ? _provider.GetMonitorAt(c.X, c.Y) : null,
            PreviousMonitor = Current?.Monitor.DeviceName,
        };
        var selection = MonitorSelector.Select(_settings.Current.Monitor, context);
        if (selection is null) return;

        var previous = Current;
        var same = previous is { } p && p.Monitor == selection.Value.Monitor && p.IsFallback == selection.Value.IsFallback;
        Current = selection;
        if (!same || force)
        {
            if (!same) Log.Info($"Notch monitor: {selection.Value.Monitor.DisplayLabel} ({selection.Value.Reason})");
            SelectionChanged?.Invoke(selection.Value);
        }
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _foreground.Changed -= OnForegroundChanged;
        _displayDebounce.Stop();
        _mouseTimer.Stop();
    }
}
