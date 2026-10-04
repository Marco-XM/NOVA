using Nova.Core.Settings;

namespace Nova.Core.Monitors;

public readonly record struct MonitorSelection(MonitorInfo Monitor, bool IsFallback, string Reason);

public sealed record MonitorSelectionContext
{
    public required IReadOnlyList<MonitorInfo> Monitors { get; init; }
    public string? ForegroundMonitor { get; init; }
    public string? CursorMonitor { get; init; }
    /// <summary>Device name the notch is currently on; keeps "Active" mode stable when focus is ambiguous.</summary>
    public string? PreviousMonitor { get; init; }
}

/// <summary>Pure decision logic: which monitor should host the notch.</summary>
public static class MonitorSelector
{
    public static MonitorSelection? Select(MonitorSettings settings, MonitorSelectionContext context)
    {
        var monitors = context.Monitors;
        if (monitors.Count == 0) return null;
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

        switch (settings.Mode)
        {
            case MonitorMode.Primary:
                return new(primary, false, "Primary monitor");

            case MonitorMode.Selected:
            {
                var match = FindSelected(settings, monitors);
                return match != null
                    ? new(match, false, "Selected monitor")
                    : new(primary, true, "Selected monitor is disconnected — using primary");
            }

            case MonitorMode.Active:
            {
                var fg = ByName(monitors, context.ForegroundMonitor);
                if (fg != null) return new(fg, false, "Monitor with the active window");
                var prev = ByName(monitors, context.PreviousMonitor);
                if (prev != null) return new(prev, false, "Monitor with the active window");
                var cursor = ByName(monitors, context.CursorMonitor);
                if (cursor != null) return new(cursor, false, "Monitor with the mouse");
                return new(primary, true, "No active window — using primary");
            }

            case MonitorMode.Mouse:
            {
                var cursor = ByName(monitors, context.CursorMonitor);
                if (cursor != null) return new(cursor, false, "Monitor with the mouse");
                var prev = ByName(monitors, context.PreviousMonitor);
                return prev != null ? new(prev, false, "Monitor with the mouse") : new(primary, true, "Cursor position unknown — using primary");
            }
        }
        return new(primary, true, "Primary monitor");
    }

    /// <summary>Matches the stored selection by device path, then friendly name, then GDI name.</summary>
    public static MonitorInfo? FindSelected(MonitorSettings settings, IReadOnlyList<MonitorInfo> monitors)
    {
        if (!string.IsNullOrEmpty(settings.SelectedDeviceId))
        {
            var byId = monitors.FirstOrDefault(m => string.Equals(m.DeviceId, settings.SelectedDeviceId, StringComparison.OrdinalIgnoreCase));
            if (byId != null) return byId;
        }
        if (!string.IsNullOrEmpty(settings.SelectedFriendlyName))
        {
            var byName = monitors.Where(m => string.Equals(m.FriendlyName, settings.SelectedFriendlyName, StringComparison.OrdinalIgnoreCase)).ToList();
            // Two identical models: the friendly name is ambiguous, fall through to the GDI name.
            if (byName.Count == 1) return byName[0];
        }
        return ByName(monitors, settings.SelectedDeviceName);
    }

    public static void StoreSelection(MonitorSettings settings, MonitorInfo monitor)
    {
        settings.SelectedDeviceId = monitor.DeviceId;
        settings.SelectedFriendlyName = monitor.FriendlyName;
        settings.SelectedDeviceName = monitor.DeviceName;
    }

    private static MonitorInfo? ByName(IReadOnlyList<MonitorInfo> monitors, string? deviceName) =>
        string.IsNullOrEmpty(deviceName) ? null : monitors.FirstOrDefault(m => string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
}

/// <summary>DPI conversions. All window placement is done in physical pixels to avoid DPI virtualization bugs.</summary>
public static class DpiMath
{
    public static int ToPhysical(double dip, int dpi) => (int)Math.Round(dip * dpi / 96.0, MidpointRounding.AwayFromZero);
    public static double ToDip(int physical, int dpi) => physical * 96.0 / dpi;

    /// <summary>
    /// Computes the physical window rectangle for a notch window of the given DIP size, horizontally
    /// centered on the monitor and attached to its top edge (plus an optional DIP offset).
    /// </summary>
    public static PixelRect PlaceTopCenter(MonitorInfo monitor, double widthDip, double heightDip, double topOffsetDip = 0)
    {
        var w = ToPhysical(widthDip, monitor.Dpi);
        var h = ToPhysical(heightDip, monitor.Dpi);
        var x = monitor.Bounds.Left + (monitor.Bounds.Width - w) / 2;
        var y = monitor.Bounds.Top + ToPhysical(topOffsetDip, monitor.Dpi);
        return new PixelRect(x, y, w, h);
    }

    /// <summary>Like <see cref="PlaceTopCenter"/>, but flush with the monitor's left or right edge when asked.</summary>
    public static PixelRect PlaceTop(MonitorInfo monitor, double widthDip, double heightDip, Settings.NotchPosition position)
    {
        var rect = PlaceTopCenter(monitor, widthDip, heightDip);
        return position switch
        {
            Settings.NotchPosition.Left => rect with { Left = monitor.Bounds.Left },
            Settings.NotchPosition.Right => rect with { Left = monitor.Bounds.Right - rect.Width },
            _ => rect,
        };
    }
}
