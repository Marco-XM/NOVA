using System.Runtime.InteropServices;
using Nova.Core.Logging;
using Nova.Core.Monitors;
using static Nova.Platform.Interop.NativeMethods;

namespace Nova.Platform.Monitors;

/// <summary>
/// Real monitor enumeration. Requires the process to be Per-Monitor-V2 DPI aware (see app.manifest) so
/// every coordinate here is in physical pixels and never virtualized.
/// </summary>
public sealed class Win32MonitorProvider : IMonitorProvider
{
    private readonly Func<IntPtr>? _ownWindow;

    /// <param name="ownWindow">Returns NOVA's own notch window, which is ignored as a foreground window.</param>
    public Win32MonitorProvider(Func<IntPtr>? ownWindow = null) => _ownWindow = ownWindow;

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var names = QueryFriendlyNames();
        var result = new List<MonitorInfo>();
        MonitorEnumProc callback = (IntPtr hMonitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            try
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (!GetMonitorInfo(hMonitor, ref info)) return true;

                var dpi = 96u;
                if (GetDpiForMonitor(hMonitor, 0 /* MDT_EFFECTIVE_DPI */, out var dx, out _) == 0 && dx > 0) dpi = dx;

                var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                var refresh = EnumDisplaySettings(info.szDevice, ENUM_CURRENT_SETTINGS, ref mode) ? mode.dmDisplayFrequency : 0;

                names.TryGetValue(info.szDevice, out var name);
                result.Add(new MonitorInfo
                {
                    DeviceName = info.szDevice,
                    DeviceId = name.Path ?? info.szDevice,
                    FriendlyName = string.IsNullOrWhiteSpace(name.Friendly) ? "" : name.Friendly!,
                    Bounds = ToRect(info.rcMonitor),
                    WorkArea = ToRect(info.rcWork),
                    Dpi = (int)dpi,
                    IsPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                    RefreshRate = refresh > 1 ? refresh : 0,
                });
            }
            catch (Exception ex)
            {
                Log.Warn("Reading a monitor failed", ex);
            }
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        // Stable, left-to-right order matches the Windows display settings layout.
        return result.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
    }

    public string? GetMonitorAt(int x, int y)
    {
        var h = MonitorFromPoint(new POINT(x, y), MONITOR_DEFAULTTONULL);
        return h == IntPtr.Zero ? null : DeviceNameOf(h);
    }

    public (int X, int Y)? GetCursorPosition() => GetCursorPos(out var p) ? (p.X, p.Y) : null;

    public string? GetForegroundMonitor()
    {
        var hwnd = GetForegroundWindow();
        if (!IsMeaningfulWindow(hwnd)) return null;
        var h = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONULL);
        return h == IntPtr.Zero ? null : DeviceNameOf(h);
    }

    internal bool IsMeaningfulWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || hwnd == GetShellWindow() || hwnd == GetDesktopWindow()) return false;
        if (_ownWindow != null && hwnd == _ownWindow()) return false;
        var cls = ClassName(hwnd);
        // Desktop, taskbar, Start, search, notification and alt-tab surfaces don't represent "where the user works".
        return cls is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow"
            or "XamlExplorerHostIslandWindow" or "ForegroundStaging" or "MultitaskingViewFrame" or "TaskListThumbnailWnd" or "NotifyIconOverflowWindow");
    }

    public static string ClassName(IntPtr hwnd)
    {
        var buffer = new char[256];
        var len = GetClassName(hwnd, buffer, buffer.Length);
        return len > 0 ? new string(buffer, 0, len) : "";
    }

    private static string? DeviceNameOf(IntPtr hMonitor)
    {
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        return GetMonitorInfo(hMonitor, ref info) ? info.szDevice : null;
    }

    private static PixelRect ToRect(RECT r) => new(r.Left, r.Top, r.Width, r.Height);

    /// <summary>Maps GDI device names to EDID friendly names and device interface paths via the DisplayConfig API.</summary>
    private static Dictionary<string, (string? Friendly, string? Path)> QueryFriendlyNames()
    {
        var map = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0) return map;
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return map;

            for (var i = 0; i < pathCount; i++)
            {
                var path = paths[i];
                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = path.sourceInfo.adapterId,
                        id = path.sourceInfo.id,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;

                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref target) != 0) continue;

                var friendly = target.monitorFriendlyDeviceName;
                // Internal laptop panels usually have no EDID name.
                if (string.IsNullOrWhiteSpace(friendly) && target.outputTechnology is 0x80000000 or 11 or 13 /* internal, eDP, DSI */)
                    friendly = "Built-in display";
                if (!map.ContainsKey(source.viewGdiDeviceName))
                    map[source.viewGdiDeviceName] = (friendly, string.IsNullOrWhiteSpace(target.monitorDevicePath) ? null : target.monitorDevicePath);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("QueryDisplayConfig failed; monitors will use device names", ex);
        }
        return map;
    }
}
