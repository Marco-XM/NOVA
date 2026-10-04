using Nova.Core.Animation;
using Nova.Core.Monitors;
using Nova.Core.Settings;

namespace Nova.App.Tray;

/// <summary>What the tray menu can do. Implemented by the app host; faked in tests.</summary>
public interface ITrayActions
{
    bool IsNotchHidden { get; }
    AnimationTheme CurrentTheme { get; }
    MonitorMode MonitorMode { get; }
    string? SelectedMonitorDevice { get; }
    IReadOnlyList<MonitorInfo> Monitors { get; }
    bool StartWithWindows { get; }
    string VersionText { get; }

    void ShowNotch();
    void HideNotch();
    void OpenSettings();
    void SetTheme(AnimationTheme theme);
    void SetMonitorMode(MonitorMode mode);
    void SelectMonitor(string deviceName);
    void SetStartWithWindows(bool enabled);
    void Quit();
}

public enum TrayEntryKind { Header, Item, Separator, Submenu }

/// <summary>One menu row, independent of WPF so the menu structure and commands can be unit tested.</summary>
public sealed record TrayEntry(string Id, TrayEntryKind Kind, string Text, string? Glyph = null, bool IsChecked = false, bool IsEnabled = true, IReadOnlyList<TrayEntry>? Children = null);

public static class TrayMenuModel
{
    public static IReadOnlyList<TrayEntry> Build(ITrayActions a)
    {
        var themes = AnimationThemes.All
            .Select(t => new TrayEntry($"theme:{t.Theme}", TrayEntryKind.Item, t.Name, IsChecked: a.CurrentTheme == t.Theme))
            .ToList();

        var monitors = new List<TrayEntry>
        {
            new("monitor:mode:Primary", TrayEntryKind.Item, "Primary", IsChecked: a.MonitorMode == MonitorMode.Primary),
            new("monitor:mode:Active", TrayEntryKind.Item, "Active window", IsChecked: a.MonitorMode == MonitorMode.Active),
            new("monitor:mode:Mouse", TrayEntryKind.Item, "Follow mouse", IsChecked: a.MonitorMode == MonitorMode.Mouse),
        };
        if (a.Monitors.Count > 0)
        {
            monitors.Add(new TrayEntry("sep:monitors", TrayEntryKind.Separator, ""));
            var index = 1;
            foreach (var m in a.Monitors)
            {
                var label = $"{index++}. {m.DisplayLabel}{(m.IsPrimary ? " (primary)" : "")}";
                monitors.Add(new TrayEntry($"monitor:select:{m.DeviceName}", TrayEntryKind.Item, label,
                    IsChecked: a.MonitorMode == MonitorMode.Selected && string.Equals(a.SelectedMonitorDevice, m.DeviceName, StringComparison.OrdinalIgnoreCase)));
            }
        }

        return new List<TrayEntry>
        {
            new("header", TrayEntryKind.Header, a.VersionText),
            new("show", TrayEntryKind.Item, "Show Notch", "", IsEnabled: a.IsNotchHidden),
            new("hide", TrayEntryKind.Item, "Hide Notch", "", IsEnabled: !a.IsNotchHidden),
            new("sep:1", TrayEntryKind.Separator, ""),
            new("settings", TrayEntryKind.Item, "Settings", ""),
            new("sep:2", TrayEntryKind.Separator, ""),
            new("animation", TrayEntryKind.Submenu, "Animation", "", Children: themes),
            new("monitor", TrayEntryKind.Submenu, "Monitor", "", Children: monitors),
            new("startup", TrayEntryKind.Item, "Start with Windows", null, IsChecked: a.StartWithWindows),
            new("sep:3", TrayEntryKind.Separator, ""),
            new("quit", TrayEntryKind.Item, "Quit NOVA", ""),
        };
    }

    /// <summary>Executes the command behind a menu entry id. Returns false for unknown ids.</summary>
    public static bool Execute(string id, ITrayActions a)
    {
        switch (id)
        {
            case "show": a.ShowNotch(); return true;
            case "hide": a.HideNotch(); return true;
            case "settings": a.OpenSettings(); return true;
            case "startup": a.SetStartWithWindows(!a.StartWithWindows); return true;
            case "quit": a.Quit(); return true;
        }
        if (id.StartsWith("theme:", StringComparison.Ordinal) && Enum.TryParse<AnimationTheme>(id[6..], out var theme))
        {
            a.SetTheme(theme);
            return true;
        }
        if (id.StartsWith("monitor:mode:", StringComparison.Ordinal) && Enum.TryParse<MonitorMode>(id["monitor:mode:".Length..], out var mode))
        {
            a.SetMonitorMode(mode);
            return true;
        }
        if (id.StartsWith("monitor:select:", StringComparison.Ordinal))
        {
            a.SelectMonitor(id["monitor:select:".Length..]);
            return true;
        }
        return false;
    }
}
