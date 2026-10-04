using System.Text.Json.Serialization;
using Nova.Core.QuickApps;

namespace Nova.Core.Settings;

public enum MonitorMode { Primary, Selected, Active, Mouse }

public enum AnimationTheme { Liquid, Glass, Elastic, Minimal, Morph, Aurora }

public enum NotchMaterial { DarkGlass, Midnight, Graphite, Frost }

public enum NotchShape { Attached, Floating }

public enum IdleContent { Wordmark, Clock, Empty }

public enum HotkeyAction { Toggle, Expand }

/// <summary>Where along the top edge the notch sits.</summary>
public enum NotchPosition { Center, Left, Right }

/// <summary>Whether the drifting Aurora light is drawn inside the notch.</summary>
public enum AuroraMode { ThemeDefault, On, Off }

public enum AuroraColorSource { Accent, Custom }

/// <summary>When the idle notch shrinks to a thin line at the top edge.</summary>
public enum AutoHideMode { Off, WhenWindowAtTop, Always }

public enum SearchTarget { Web, WindowsSearch }

/// <summary>Root of the persisted configuration. Every section is a strongly typed POCO.</summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public bool FirstRunCompleted { get; set; }
    public MonitorSettings Monitor { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public AnimationSettings Animation { get; set; } = new();
    public MediaSettings Media { get; set; } = new();
    public QuickAppsSettings QuickApps { get; set; } = new();
    public BehaviorSettings Behavior { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
    public ModuleSettings Modules { get; set; } = new();
    public AdvancedSettings Advanced { get; set; } = new();

    /// <summary>Clamps every numeric value into its valid range and replaces null sections.</summary>
    public AppSettings Normalize()
    {
        Monitor ??= new();
        Appearance ??= new();
        Animation ??= new();
        Media ??= new();
        QuickApps ??= new();
        Behavior ??= new();
        Notifications ??= new();
        Modules ??= new();
        Advanced ??= new();

        Appearance.Opacity = Math.Clamp(Appearance.Opacity, 0.4, 1.0);
        Appearance.GlowAmount = Math.Clamp(Appearance.GlowAmount, 0, 1);
        Appearance.SizeScale = Math.Clamp(Appearance.SizeScale, 0.8, 1.3);
        Appearance.CornerRadiusScale = Math.Clamp(Appearance.CornerRadiusScale, 0.3, 1.5);
        Appearance.TopOffset = Math.Clamp(Appearance.TopOffset, 0, 40);
        if (!ColorUtil.TryParseHex(Appearance.AccentColor, out _)) Appearance.AccentColor = AppearanceSettings.DefaultAccent;

        Animation.Speed = Math.Clamp(Animation.Speed, 0.5, 2.0);
        Animation.Intensity = Math.Clamp(Animation.Intensity, 0, 1.5);
        Animation.SpringStrength = Math.Clamp(Animation.SpringStrength, 0, 1.5);
        Animation.ExpansionDurationMs = Math.Clamp(Animation.ExpansionDurationMs, 150, 1200);
        Animation.AuroraCustomColors ??= new();
        for (var i = 0; i < AnimationSettings.DefaultAuroraColors.Length; i++)
        {
            if (i >= Animation.AuroraCustomColors.Count) Animation.AuroraCustomColors.Add(AnimationSettings.DefaultAuroraColors[i]);
            else if (!ColorUtil.TryParseHex(Animation.AuroraCustomColors[i], out _)) Animation.AuroraCustomColors[i] = AnimationSettings.DefaultAuroraColors[i];
        }
        if (Animation.AuroraCustomColors.Count > AnimationSettings.DefaultAuroraColors.Length)
            Animation.AuroraCustomColors.RemoveRange(AnimationSettings.DefaultAuroraColors.Length, Animation.AuroraCustomColors.Count - AnimationSettings.DefaultAuroraColors.Length);

        Media.AutoExpandDurationMs = Math.Clamp(Media.AutoExpandDurationMs, 1000, 10000);
        Media.AutoCollapseDelayMs = Math.Clamp(Media.AutoCollapseDelayMs, 200, 10000);

        Behavior.HoverDelayMs = Math.Clamp(Behavior.HoverDelayMs, 0, 1500);
        Behavior.CollapseDelayMs = Math.Clamp(Behavior.CollapseDelayMs, 100, 5000);
        Behavior.Hotkey ??= BehaviorSettings.DefaultHotkey;

        Notifications.DurationMs = Math.Clamp(Notifications.DurationMs, 1000, 10000);
        Modules.ClipboardHistorySize = Math.Clamp(Modules.ClipboardHistorySize, 3, 30);
        if (string.IsNullOrWhiteSpace(Modules.SearchUrlTemplate) || !Modules.SearchUrlTemplate.Contains("{0}"))
            Modules.SearchUrlTemplate = ModuleSettings.DefaultSearchUrl;

        QuickApps.Apps ??= new();
        QuickApps.Apps.RemoveAll(a => a is null || string.IsNullOrWhiteSpace(a.Target));
        foreach (var app in QuickApps.Apps)
        {
            if (string.IsNullOrWhiteSpace(app.Id)) app.Id = Guid.NewGuid().ToString("N");
            app.Name = string.IsNullOrWhiteSpace(app.Name) ? Path.GetFileNameWithoutExtension(app.Target) : app.Name;
        }
        return this;
    }
}

public sealed class MonitorSettings
{
    public MonitorMode Mode { get; set; } = MonitorMode.Primary;
    /// <summary>Stable device interface path of the selected monitor (survives reboots and re-plugging).</summary>
    public string? SelectedDeviceId { get; set; }
    /// <summary>Human readable name, used as secondary match key if the device path changed.</summary>
    public string? SelectedFriendlyName { get; set; }
    /// <summary>GDI device name (\\.\DISPLAYn); least stable, used as final match key.</summary>
    public string? SelectedDeviceName { get; set; }
}

public sealed class AppearanceSettings
{
    public const string DefaultAccent = "#8B9CFF";

    public NotchMaterial Material { get; set; } = NotchMaterial.DarkGlass;
    public NotchShape Shape { get; set; } = NotchShape.Attached;
    public IdleContent IdleContent { get; set; } = IdleContent.Wordmark;
    /// <summary>Surface opacity. 1 = fully opaque, lower = more transparent.</summary>
    public double Opacity { get; set; } = 0.9;
    public string AccentColor { get; set; } = DefaultAccent;
    public double GlowAmount { get; set; } = 0.35;
    public double SizeScale { get; set; } = 1.0;
    public double CornerRadiusScale { get; set; } = 1.0;
    /// <summary>Distance from the top edge in DIPs (only used by the floating shape).</summary>
    public double TopOffset { get; set; } = 8;
    public NotchPosition Position { get; set; } = NotchPosition.Center;
}

public sealed class AnimationSettings
{
    public AnimationTheme Theme { get; set; } = AnimationTheme.Liquid;
    public double Speed { get; set; } = 1.0;
    public double Intensity { get; set; } = 1.0;
    /// <summary>0 = no overshoot at all, 1 = theme default, 1.5 = extra bouncy.</summary>
    public double SpringStrength { get; set; } = 1.0;
    public int ExpansionDurationMs { get; set; } = 460;
    public bool ReduceMotion { get; set; }
    public bool FollowSystemReduceMotion { get; set; } = true;
    public bool AmbientEffectsWhenIdle { get; set; }
    /// <summary>Aurora light on top of any theme (ThemeDefault = only the Aurora theme).</summary>
    public AuroraMode Aurora { get; set; } = AuroraMode.ThemeDefault;
    public AuroraColorSource AuroraColors { get; set; } = AuroraColorSource.Accent;
    /// <summary>Three colors (#RRGGBB) used when <see cref="AuroraColors"/> is Custom.</summary>
    public List<string> AuroraCustomColors { get; set; } = new(DefaultAuroraColors);
    /// <summary>While media with cover art plays, the Aurora takes its colors from the artwork.</summary>
    public bool AuroraFollowsArtwork { get; set; } = true;

    public static readonly string[] DefaultAuroraColors = { "#8B9CFF", "#6EE7F9", "#F0ABFC" };
}

public sealed class MediaSettings
{
    public bool Enabled { get; set; } = true;
    public bool Spotify { get; set; } = true;
    public bool Chrome { get; set; } = true;
    public bool OtherSessions { get; set; } = true;
    public bool AutoExpand { get; set; } = true;
    public int AutoExpandDurationMs { get; set; } = 3000;
    public int AutoCollapseDelayMs { get; set; } = 1200;
    public bool ShowCompactIndicator { get; set; } = true;
    public bool AnimatedEqualizer { get; set; } = true;
    /// <summary>Keep the compact media indicator for this long after playback pauses.</summary>
    public int PausedLingerSeconds { get; set; } = 120;
    /// <summary>Synced lyrics in the media panel, looked up on lrclib.net by title / artist.</summary>
    public bool ShowLyrics { get; set; } = true;
}

public sealed class QuickAppsSettings
{
    public List<QuickApp> Apps { get; set; } = new();
    public bool SeededDefaults { get; set; }
}

public sealed class BehaviorSettings
{
    public const string DefaultHotkey = "Ctrl+Alt+Space";

    public bool StartWithWindows { get; set; }
    public bool RunInBackground { get; set; } = true;
    public bool ShowNotchOnStartup { get; set; } = true;
    public AutoHideMode AutoHideMode { get; set; } = AutoHideMode.Off;
    /// <summary>Pre-<see cref="AutoHideMode"/> on/off switch; read once by the migration, then dropped.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AutoHide { get; set; }
    public bool ExpandOnHover { get; set; } = true;
    public int HoverDelayMs { get; set; } = 90;
    public int CollapseDelayMs { get; set; } = 500;
    public string Hotkey { get; set; } = DefaultHotkey;
    public HotkeyAction HotkeyAction { get; set; } = HotkeyAction.Toggle;
    public bool HideInFullscreenGames { get; set; } = true;
    public bool HideInFullscreenVideo { get; set; } = true;
    public bool HideDuringPresentations { get; set; } = true;
    public bool ExcludeFromScreenCapture { get; set; } = true;
}

public sealed class NotificationSettings
{
    public bool Enabled { get; set; } = true;
    public int DurationMs { get; set; } = 2800;
    public bool MusicStarted { get; set; } = true;
    public bool MusicPaused { get; set; }
    public bool MusicChanged { get; set; } = true;
    public bool AppLaunched { get; set; } = true;
    public bool DownloadCompleted { get; set; } = true;
    public bool TimerCompleted { get; set; } = true;
    public bool BatteryChanged { get; set; } = true;
    public bool ClipboardCopied { get; set; }
    public bool VolumeChanged { get; set; } = true;
    public bool BrightnessChanged { get; set; } = true;
    public bool NetworkChanged { get; set; } = true;
    /// <summary>Mirror other apps' Windows notifications (WhatsApp, Messenger, browsers, …) in the notch.</summary>
    public bool AppNotifications { get; set; } = true;
    /// <summary>Show the message text; off shows only the app and sender.</summary>
    public bool AppNotificationText { get; set; } = true;
}

public sealed class ModuleSettings
{
    public const string DefaultSearchUrl = "https://www.bing.com/search?q={0}";

    public bool Clipboard { get; set; } = true;
    public int ClipboardHistorySize { get; set; } = 10;
    public bool Timer { get; set; } = true;
    public bool Calculator { get; set; } = true;
    public bool Search { get; set; } = true;
    public SearchTarget SearchTarget { get; set; } = SearchTarget.Web;
    public string SearchUrlTemplate { get; set; } = DefaultSearchUrl;
    public bool VolumeIndicator { get; set; } = true;
    public bool BrightnessIndicator { get; set; } = true;
    public bool Battery { get; set; } = true;
    public bool Network { get; set; } = true;
    public bool DownloadWatcher { get; set; } = true;
}

public sealed class AdvancedSettings
{
    public bool DebugMode { get; set; }
    public bool ShowPerformanceOverlay { get; set; }
    public bool VerboseLogging { get; set; }
}

public static class ColorUtil
{
    public static bool TryParseHex(string? hex, out (byte A, byte R, byte G, byte B) color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim().TrimStart('#');
        try
        {
            if (s.Length == 6)
            {
                color = (255, Convert.ToByte(s[..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..6], 16));
                return true;
            }
            if (s.Length == 8)
            {
                color = (Convert.ToByte(s[..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..6], 16), Convert.ToByte(s[6..8], 16));
                return true;
            }
        }
        catch (FormatException) { }
        return false;
    }
}
