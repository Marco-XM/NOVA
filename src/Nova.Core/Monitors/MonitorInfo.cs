namespace Nova.Core.Monitors;

/// <summary>Integer rectangle in physical (device) pixels of the virtual desktop.</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    public int CenterX => Left + Width / 2;
    public override string ToString() => $"{Width}x{Height} @ ({Left},{Top})";
}

/// <summary>Snapshot of one connected display.</summary>
public sealed record MonitorInfo
{
    /// <summary>GDI device name such as <c>\\.\DISPLAY1</c>. Can change between reboots.</summary>
    public required string DeviceName { get; init; }
    /// <summary>Device interface path of the physical monitor; the most stable identity available.</summary>
    public string DeviceId { get; init; } = "";
    /// <summary>EDID friendly name, e.g. "DELL U2720Q".</summary>
    public string FriendlyName { get; init; } = "";
    public required PixelRect Bounds { get; init; }
    public required PixelRect WorkArea { get; init; }
    /// <summary>Effective DPI (96 = 100% scaling).</summary>
    public int Dpi { get; init; } = 96;
    public bool IsPrimary { get; init; }
    public int RefreshRate { get; init; }

    public double Scale => Dpi / 96.0;
    public string DisplayLabel => string.IsNullOrWhiteSpace(FriendlyName) ? DeviceName.Replace(@"\\.\", "") : FriendlyName;
    public string Description => $"{Bounds.Width} × {Bounds.Height} · {Math.Round(Scale * 100)}%{(RefreshRate > 0 ? $" · {RefreshRate} Hz" : "")}";
}

/// <summary>Abstraction over the OS so monitor logic can be tested with fake layouts.</summary>
public interface IMonitorProvider
{
    IReadOnlyList<MonitorInfo> GetMonitors();
    /// <summary>Device name of the monitor containing the point (physical coordinates).</summary>
    string? GetMonitorAt(int x, int y);
    (int X, int Y)? GetCursorPosition();
    /// <summary>Device name of the monitor hosting the foreground window, if any meaningful one exists.</summary>
    string? GetForegroundMonitor();
}
