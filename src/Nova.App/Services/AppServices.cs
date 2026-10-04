using Nova.Core.Events;
using Nova.Core.Media;
using Nova.Core.QuickApps;
using Nova.Core.Settings;
using Nova.Core.State;
using Nova.Core.Tools;
using Nova.Platform.Media;
using Nova.Platform.Services;

namespace Nova.App.Services;

/// <summary>The long-lived services shared by the notch, tray and settings UI (composition root output).</summary>
public sealed class AppServices
{
    public required SettingsService Settings { get; init; }
    public required EventBus Bus { get; init; }
    public required MediaSessionHub MediaHub { get; init; }
    public required MediaManager Media { get; init; }
    public required AppLauncher Launcher { get; init; }
    public required NotchStateMachine StateMachine { get; init; }
    public required CountdownTimer Timer { get; init; }
    public required ClipboardHistory Clipboard { get; init; }
    public required ClipboardWatcher ClipboardWatcher { get; init; }
    public required VolumeWatcher Volume { get; init; }
    public required BrightnessWatcher Brightness { get; init; }
    public required PowerWatcher Power { get; init; }
    public required NetworkWatcher Network { get; init; }
    public required DownloadWatcher Downloads { get; init; }
    public required ForegroundTracker Foreground { get; init; }
    public required StartupManager Startup { get; init; }
    public required AppNotificationWatcher AppNotifications { get; init; }
    public required LyricsService Lyrics { get; init; }
    /// <summary>Opens the settings window on the given page id (null = overview).</summary>
    public required Action<string?> OpenSettings { get; init; }
}
