using System.Runtime.InteropServices;

namespace Nova.Platform.Interop;

#pragma warning disable IDE1006, CA1712
internal static class NativeMethods
{
    // ───────────── window styles ─────────────
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOPMOST = 0x00000008;
    public const long WS_EX_TRANSPARENT = 0x00000020;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_LAYERED = 0x00080000;
    public const long WS_EX_APPWINDOW = 0x00040000;
    public const long WS_EX_NOACTIVATE = 0x08000000;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_MESSAGE = new(-3);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public const uint SWP_ASYNCWINDOWPOS = 0x4000;

    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_POWERBROADCAST = 0x0218;
    public const int PBT_APMRESUMEAUTOMATIC = 0x0012;

    public const uint WDA_NONE = 0x0;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, char[] className, int maxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    public const uint GA_ROOT = 2;

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    // ───────────── monitors ─────────────
    public const uint MONITOR_DEFAULTTONULL = 0;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint MONITORINFOF_PRIMARY = 1;

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);
    public const int ENUM_CURRENT_SETTINGS = -1;

    // ───────────── display config (friendly monitor names / stable ids) ─────────────
    public const uint QDC_ONLY_ACTIVE_PATHS = 0x2;

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    // ───────────── hotkeys ─────────────
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ───────────── win events ─────────────
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const int OBJID_WINDOW = 0;

    public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    // ───────────── clipboard ─────────────
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    // ───────────── shell ─────────────
    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE state);

    public enum QUERY_USER_NOTIFICATION_STATE
    {
        QUNS_NOT_PRESENT = 1,
        QUNS_BUSY = 2,
        QUNS_RUNNING_D3D_FULL_SCREEN = 3,
        QUNS_PRESENTATION_MODE = 4,
        QUNS_ACCEPTS_NOTIFICATIONS = 5,
        QUNS_QUIET_TIME = 6,
        QUNS_APP = 7,
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("shell32.dll", PreserveSig = false)]
    public static extern void SHGetKnownFolderItem([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, int flags, IntPtr hToken, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern string SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token);

    // ───────────── gdi ─────────────
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out DIBSECTION lpvObject);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

    [DllImport("user32.dll")]
    public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    // ───────────── input ─────────────
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    // ───────────── dwm / composition ─────────────
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    public const int DWMWA_CLOAKED = 14;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public int Attrib;
        public IntPtr pvData;
        public int cbData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ACCENT_POLICY
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    public const int WCA_ACCENT_POLICY = 19;
    public const int ACCENT_DISABLED = 0;
    public const int ACCENT_ENABLE_BLURBEHIND = 3;
    public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    // ───────────── structs ─────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
        public POINT(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY;
        public int dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitfields0, dsBitfields1, dsBitfields2;
        public IntPtr dshSection;
        public uint dsOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    /// <summary>The mode union is 64 bytes after the header; we never read it, only size it correctly.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct DISPLAYCONFIG_MODE_INFO
    {
        [FieldOffset(0)] public uint infoType;
        [FieldOffset(4)] public uint id;
        [FieldOffset(8)] public LUID adapterId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }
}
#pragma warning restore IDE1006, CA1712
