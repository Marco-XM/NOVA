using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Nova.Core.Logging;
using Nova.Core.Monitors;

namespace Nova.App.Notch;

/// <summary>
/// The overlay window. It is a per-pixel-alpha layered window, so every fully transparent pixel around
/// the notch passes mouse clicks straight through to the apps underneath. It never takes focus unless a
/// tool that needs the keyboard (calculator, search) is opened.
/// </summary>
public partial class NotchWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_APPWINDOW = 0x40000, WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20;
    private const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3, WM_DPICHANGED = 0x02E0, WM_DISPLAYCHANGE = 0x7E, WM_SETTINGCHANGE = 0x1A, WM_MOUSEMOVE = 0x200;
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200, SWP_SHOWWINDOW = 0x40;
    private const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private HwndSource? _source;
    private bool _activatable;
    private PixelRect _placement;
    private bool _captureExcluded;
    private bool _affinityApplied;
    private bool _clickThrough;
    private readonly DispatcherTimer _pointerPoll;

    public NotchWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        _pointerPoll = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
        _pointerPoll.Tick += (_, _) => PollPointer();
    }

    /// <summary>
    /// Whether a point (in <see cref="Root"/> coordinates) belongs to the notch. Everywhere else the
    /// window is click-through, including the soft shadow and glow, which are visible but must not
    /// block the tabs and title bars underneath.
    /// </summary>
    public Func<Point, bool>? IsInteractiveAt { get; set; }

    public IntPtr Handle { get; private set; }

    /// <summary>Raised after DPI or display configuration changed (UI thread).</summary>
    public event Action? DisplayChanged;
    public event Action? SystemSettingChanged;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(Handle);
        _source?.AddHook(WndProc);
        var style = GetWindowLongPtr(Handle, GWL_EXSTYLE).ToInt64();
        style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
        style &= ~WS_EX_APPWINDOW;
        SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(style));
        SetCaptureExcluded(_captureExcluded);
        _pointerPoll.Start();
    }

    /// <summary>
    /// A per-pixel-alpha window takes the mouse on every pixel that isn't fully transparent. Instead,
    /// it is made WS_EX_TRANSPARENT whenever the pointer isn't over the notch itself.
    /// </summary>
    private void PollPointer()
    {
        if (Handle == IntPtr.Zero || IsInteractiveAt is null || !GetCursorPos(out var pt)) return;
        bool over;
        if (Mouse.Captured != null) over = true; // keep drags and slider scrubs alive
        else if (!_placement.Contains(pt.X, pt.Y)) over = false;
        else
        {
            try { over = IsInteractiveAt(Root.PointFromScreen(new Point(pt.X, pt.Y))); }
            catch (InvalidOperationException) { return; } // not connected to a presentation source yet
        }
        if (over == !_clickThrough) return;

        _clickThrough = !over;
        var style = GetWindowLongPtr(Handle, GWL_EXSTYLE).ToInt64();
        style = _clickThrough ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(style));

        // Windows sends no mouse message for a style change; if the pointer came to rest on the
        // notch (e.g. flicked to the top edge), nudge WPF so hover still triggers.
        if (over)
        {
            var lParam = ((pt.Y - _placement.Top) << 16) | ((pt.X - _placement.Left) & 0xFFFF);
            PostMessage(Handle, WM_MOUSEMOVE, IntPtr.Zero, new IntPtr(lParam));
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE when !_activatable:
                handled = true;
                return new IntPtr(MA_NOACTIVATE);
            case WM_DPICHANGED:
            case WM_DISPLAYCHANGE:
                // Let WPF rescale first, then re-place the window ourselves in physical pixels.
                Dispatcher.BeginInvoke(() => DisplayChanged?.Invoke(), System.Windows.Threading.DispatcherPriority.Background);
                break;
            case WM_SETTINGCHANGE:
                Dispatcher.BeginInvoke(() => SystemSettingChanged?.Invoke(), System.Windows.Threading.DispatcherPriority.Background);
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>Places the window at an exact physical-pixel rectangle (DPI-safe).</summary>
    public void Place(PixelRect rect)
    {
        if (Handle == IntPtr.Zero) return;
        _placement = rect;
        SetWindowPos(Handle, HWND_TOPMOST, rect.Left, rect.Top, rect.Width, rect.Height, SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_SHOWWINDOW);
    }

    public PixelRect Placement => _placement;

    /// <summary>Other topmost windows (taskbar, overlays) can cover us; re-assert our z-order cheaply.</summary>
    public void ReassertTopmost()
    {
        if (Handle == IntPtr.Zero) return;
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    public void SetCaptureExcluded(bool excluded)
    {
        // Re-applying the same affinity still makes DWM redraw the window; only call it on a change.
        if (_affinityApplied && excluded == _captureExcluded) return;
        _captureExcluded = excluded;
        if (Handle == IntPtr.Zero) return;
        _affinityApplied = true;
        if (!SetWindowDisplayAffinity(Handle, excluded ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE))
            Log.Warn($"SetWindowDisplayAffinity failed ({Marshal.GetLastWin32Error()}); screenshot exclusion requires Windows 10 2004 or later.");
    }

    /// <summary>Temporarily allows keyboard focus (for text input tools) and activates the window.</summary>
    public void ActivateForKeyboard()
    {
        if (Handle == IntPtr.Zero) return;
        _activatable = true;
        var style = GetWindowLongPtr(Handle, GWL_EXSTYLE).ToInt64() & ~WS_EX_NOACTIVATE;
        SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(style));
        _previousForeground = GetForegroundWindow();
        Activate();
        SetForegroundWindow(Handle);
    }

    private IntPtr _previousForeground;

    /// <summary>Returns to the never-activate mode and hands focus back to the app the user was in.</summary>
    public void ReleaseKeyboard()
    {
        if (Handle == IntPtr.Zero || !_activatable) return;
        _activatable = false;
        var style = GetWindowLongPtr(Handle, GWL_EXSTYLE).ToInt64() | WS_EX_NOACTIVATE;
        SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(style));
        if (IsActive && _previousForeground != IntPtr.Zero && _previousForeground != Handle)
            SetForegroundWindow(_previousForeground);
        _previousForeground = IntPtr.Zero;
    }

    public bool IsKeyboardActive => _activatable;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
}
