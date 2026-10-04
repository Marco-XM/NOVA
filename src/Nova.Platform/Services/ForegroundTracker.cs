using System.Diagnostics;
using System.Windows.Threading;
using Nova.Core.Logging;
using Nova.Core.Monitors;
using Nova.Platform.Monitors;
using static Nova.Platform.Interop.NativeMethods;

namespace Nova.Platform.Services;

public enum FullscreenKind { None, Game, Video, Presentation }

/// <summary>
/// Event-driven tracking of the foreground window (WinEvent hooks, no polling). Drives "Active monitor"
/// mode and fullscreen auto-hide. The location hook is scoped to the foreground process only, so we
/// are not woken up for every window move or cursor blink in the system.
/// </summary>
public sealed class ForegroundTracker : IDisposable
{
    private static readonly HashSet<string> VideoProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "vlc", "mpc-hc", "mpc-hc64", "mpc-be64", "mpv",
        "wmplayer", "Microsoft.Media.Player", "PotPlayerMini64", "PotPlayerMini", "Video.UI", "Netflix", "kodi", "plex", "stremio",
    };

    private readonly Func<IntPtr> _ownWindow;
    private readonly WinEventDelegate _foregroundProc;
    private readonly WinEventDelegate _locationProc;
    private readonly DispatcherTimer _debounce;
    private readonly Dictionary<uint, string> _processNames = new();
    private IntPtr _foregroundHook;
    private IntPtr _minimizeHook;
    private IntPtr _locationHook;
    private uint _locationPid;
    private IntPtr _foreground;

    public ForegroundTracker(Func<IntPtr> ownWindow)
    {
        _ownWindow = ownWindow;
        _foregroundProc = OnForegroundEvent;
        _locationProc = OnLocationEvent;
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Changed?.Invoke();
        };
    }

    /// <summary>Raised on the UI thread (debounced) after the foreground window changed, moved or resized.</summary>
    public event Action? Changed;

    public IntPtr Foreground => _foreground;

    public void Start()
    {
        if (_foregroundHook != IntPtr.Zero) return;
        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        _minimizeHook = SetWinEventHook(EVENT_SYSTEM_MINIMIZEEND, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, _foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (_foregroundHook == IntPtr.Zero) Log.Warn("SetWinEventHook(foreground) failed; active-monitor mode and fullscreen detection are limited.");
        UpdateForeground(GetForegroundWindow());
    }

    private void OnForegroundEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW) return;
        UpdateForeground(hwnd);
    }

    private void OnLocationEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW || idChild != 0 || hwnd != _foreground) return;
        Schedule();
    }

    private void UpdateForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || hwnd == _ownWindow()) return;
        hwnd = GetAncestor(hwnd, GA_ROOT) is var root && root != IntPtr.Zero ? root : hwnd;
        _foreground = hwnd;

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != _locationPid)
        {
            if (_locationHook != IntPtr.Zero) UnhookWinEvent(_locationHook);
            _locationHook = pid == 0 ? IntPtr.Zero
                : SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _locationProc, pid, 0, WINEVENT_OUTOFCONTEXT);
            _locationPid = pid;
        }
        Schedule();
    }

    private void Schedule()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Classifies whether a fullscreen app currently covers <paramref name="monitor"/>.</summary>
    public FullscreenKind DetectFullscreen(MonitorInfo monitor)
    {
        try
        {
            SHQueryUserNotificationState(out var quns);
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == _ownWindow() || hwnd == GetShellWindow() || hwnd == GetDesktopWindow()) return FullscreenKind.None;
            var cls = Win32MonitorProvider.ClassName(hwnd);
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return FullscreenKind.None;
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || IsZoomed(hwnd)) return quns == QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE ? FullscreenKind.Presentation : FullscreenKind.None;
            if (!GetWindowRect(hwnd, out var r)) return FullscreenKind.None;

            var b = monitor.Bounds;
            var covers = r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
            if (!covers) return FullscreenKind.None;

            if (quns == QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE || cls == "screenClass" /* PowerPoint slide show */)
                return FullscreenKind.Presentation;
            if (quns == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN) return FullscreenKind.Game;

            GetWindowThreadProcessId(hwnd, out var pid);
            var name = ProcessName(pid);
            if (name.Equals("POWERPNT", StringComparison.OrdinalIgnoreCase)) return FullscreenKind.Presentation;
            return VideoProcesses.Contains(name) ? FullscreenKind.Video : FullscreenKind.Game;
        }
        catch (Exception ex)
        {
            Log.Warn("Fullscreen detection failed", ex);
            return FullscreenKind.None;
        }
    }

    /// <summary>
    /// True when the foreground window's top edge touches the top of <paramref name="monitor"/> under
    /// the notch (maximized, snapped to the top, or just dragged up there), so its tab strip or title
    /// bar sits right where the notch is. NOVA's own window never counts.
    /// </summary>
    public bool HasWindowAtTop(MonitorInfo monitor)
    {
        var hwnd = _foreground;
        if (hwnd == IntPtr.Zero || hwnd == _ownWindow() || hwnd == GetShellWindow() || hwnd == GetDesktopWindow()) return false;
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || !GetWindowRect(hwnd, out var r)) return false;
        var cls = Win32MonitorProvider.ClassName(hwnd);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        var b = monitor.Bounds;
        var center = b.Left + b.Width / 2;
        // Maximized windows hang a few invisible border pixels above the monitor; allow a small margin.
        var slack = Math.Max(8, (int)Math.Round(12 * monitor.Dpi / 96.0));
        return r.Top <= b.Top + slack && r.Bottom > b.Top + slack && r.Left < center && r.Right > center;
    }

    private string ProcessName(uint pid)
    {
        if (pid == 0) return "";
        if (_processNames.TryGetValue(pid, out var cached)) return cached;
        string name;
        try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; }
        catch { name = ""; }
        if (_processNames.Count > 256) _processNames.Clear();
        _processNames[pid] = name;
        return name;
    }

    public void Dispose()
    {
        _debounce.Stop();
        if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
        if (_minimizeHook != IntPtr.Zero) UnhookWinEvent(_minimizeHook);
        if (_locationHook != IntPtr.Zero) UnhookWinEvent(_locationHook);
        _foregroundHook = _minimizeHook = _locationHook = IntPtr.Zero;
    }
}
