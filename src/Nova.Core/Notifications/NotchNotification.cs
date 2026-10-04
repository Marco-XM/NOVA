using Nova.Core.Events;
using Nova.Core.Media;
using Nova.Core.Settings;

namespace Nova.Core.Notifications;

public enum NotificationStyle
{
    /// <summary>Icon + title + subtitle.</summary>
    Standard,
    /// <summary>Icon + level bar (volume, brightness).</summary>
    Indicator,
    /// <summary>Album art + track info.</summary>
    Media,
    /// <summary>Another app's notification: app icon + sender + message, with Open / Dismiss.</summary>
    App,
    /// <summary>An incoming call from another app: caller + Open / Dismiss, shown while it rings.</summary>
    Call,
}

public enum NotificationPriority { Low = 0, Normal = 1, High = 2 }

/// <summary>What the notch shows while in the Notification state.</summary>
public sealed record NotchNotification
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    /// <summary>Segoe Fluent Icons glyph.</summary>
    public string Glyph { get; init; } = "";
    public NotificationStyle Style { get; init; } = NotificationStyle.Standard;
    public NotificationPriority Priority { get; init; } = NotificationPriority.Normal;
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(2.8);
    /// <summary>0..1 for indicator notifications.</summary>
    public double? Level { get; init; }
    /// <summary>Notifications with the same key replace each other instead of queueing (e.g. volume steps).</summary>
    public string? CoalesceKey { get; init; }
    /// <summary>Indicators are pointless once stale, so they are dropped instead of queued.</summary>
    public bool Queueable { get; init; } = true;
    public MediaSessionInfo? Media { get; init; }
    public string? AccentHex { get; init; }
    public NovaEvent? Source { get; init; }
}

/// <summary>
/// Converts bus events into notch notifications according to the user's notification and module
/// settings. Returns null when an event should not surface.
/// </summary>
public static class NotificationRouter
{
    public static NotchNotification? Route(NovaEvent evt, AppSettings settings)
    {
        var n = settings.Notifications;
        if (!n.Enabled) return null;
        var duration = TimeSpan.FromMilliseconds(n.DurationMs);

        switch (evt)
        {
            case MediaEvent media:
                if (!settings.Media.Enabled) return null;
                var enabled = media.Kind switch
                {
                    MediaEventKind.Started => n.MusicStarted && settings.Media.AutoExpand,
                    MediaEventKind.TrackChanged => n.MusicChanged && settings.Media.AutoExpand,
                    MediaEventKind.Paused => n.MusicPaused,
                    _ => false,
                };
                if (!enabled) return null;
                return new NotchNotification
                {
                    Title = string.IsNullOrWhiteSpace(media.Session.Title) ? media.Session.SourceName : media.Session.Title,
                    Subtitle = media.Kind == MediaEventKind.Paused ? "Paused" : media.Session.Artist,
                    Glyph = media.Kind == MediaEventKind.Paused ? "" : "",
                    Style = NotificationStyle.Media,
                    Media = media.Session,
                    Duration = TimeSpan.FromMilliseconds(settings.Media.AutoExpandDurationMs),
                    CoalesceKey = "media",
                    Source = evt,
                };

            case AppLaunchedEvent app when n.AppLaunched:
                return new NotchNotification { Title = app.AppName, Subtitle = "Launching", Glyph = "", Duration = TimeSpan.FromMilliseconds(Math.Min(n.DurationMs, 1800)), Priority = NotificationPriority.Low, Source = evt };

            case DownloadCompletedEvent dl when n.DownloadCompleted && settings.Modules.DownloadWatcher:
                return new NotchNotification { Title = "Download complete", Subtitle = dl.FileName, Glyph = "", Duration = duration, Source = evt };

            case TimerCompletedEvent timer when n.TimerCompleted:
                return new NotchNotification { Title = "Timer finished", Subtitle = timer.Label, Glyph = "", Priority = NotificationPriority.High, Duration = duration + TimeSpan.FromSeconds(2), Source = evt };

            case BatteryEvent battery when n.BatteryChanged && settings.Modules.Battery:
                var (title, glyph, priority) = battery.Kind switch
                {
                    BatteryChangeKind.PluggedIn => ("Charging", "", NotificationPriority.Normal),
                    BatteryChangeKind.Unplugged => ("On battery", "", NotificationPriority.Normal),
                    BatteryChangeKind.Low => ("Battery low", "", NotificationPriority.High),
                    BatteryChangeKind.Critical => ("Battery critical", "", NotificationPriority.High),
                    _ => ("Fully charged", "", NotificationPriority.Low),
                };
                return new NotchNotification { Title = title, Subtitle = $"{battery.Percent}%", Glyph = glyph, Priority = priority, Level = battery.Percent / 100.0, Duration = duration, CoalesceKey = "battery", Source = evt };

            case VolumeEvent volume when n.VolumeChanged && settings.Modules.VolumeIndicator:
                return new NotchNotification
                {
                    Title = volume.Muted ? "Muted" : "Volume",
                    Glyph = volume.Muted || volume.Level <= 0.001 ? "" : volume.Level < 0.34 ? "" : volume.Level < 0.67 ? "" : "",
                    Style = NotificationStyle.Indicator,
                    Level = volume.Muted ? 0 : volume.Level,
                    Duration = TimeSpan.FromMilliseconds(1600),
                    CoalesceKey = "volume",
                    Queueable = false,
                    Priority = NotificationPriority.Low,
                    Source = evt,
                };

            case BrightnessEvent brightness when n.BrightnessChanged && settings.Modules.BrightnessIndicator:
                return new NotchNotification
                {
                    Title = "Brightness",
                    Glyph = "",
                    Style = NotificationStyle.Indicator,
                    Level = brightness.Level,
                    Duration = TimeSpan.FromMilliseconds(1600),
                    CoalesceKey = "brightness",
                    Queueable = false,
                    Priority = NotificationPriority.Low,
                    Source = evt,
                };

            case ClipboardEvent clip when n.ClipboardCopied && settings.Modules.Clipboard:
                return new NotchNotification { Title = "Copied", Subtitle = clip.Preview, Glyph = "", Duration = TimeSpan.FromMilliseconds(1500), CoalesceKey = "clipboard", Queueable = false, Priority = NotificationPriority.Low, Source = evt };

            case NetworkEvent net when n.NetworkChanged && settings.Modules.Network:
                return new NotchNotification
                {
                    Title = net.Connected ? (net.NetworkName ?? "Connected") : "Offline",
                    Subtitle = net.Connected ? (net.IsWireless ? "Wi-Fi connected" : "Network connected") : "No internet connection",
                    Glyph = net.Connected ? (net.IsWireless ? "" : "") : "",
                    Duration = duration,
                    CoalesceKey = "network",
                    Source = evt,
                };

            case AppNotificationEvent app when n.AppNotifications:
                return new NotchNotification
                {
                    Title = app.Title.Length > 0 ? app.Title : app.AppName,
                    Subtitle = app.IsCall ? (app.Body ?? "Incoming call") : n.AppNotificationText ? app.Body : app.AppName,
                    Glyph = app.IsCall ? "" : "",
                    Style = app.IsCall ? NotificationStyle.Call : NotificationStyle.App,
                    // A call stays up while it rings; it's dismissed when its Windows notification goes away.
                    Duration = app.IsCall ? TimeSpan.FromSeconds(45) : duration + TimeSpan.FromSeconds(1.5),
                    Priority = app.IsCall ? NotificationPriority.High : NotificationPriority.Normal,
                    CoalesceKey = app.CoalesceKey,
                    Source = evt,
                };

            case CustomEvent custom:
                return new NotchNotification { Title = custom.Title, Subtitle = custom.Subtitle, Glyph = custom.Glyph, Priority = custom.Important ? NotificationPriority.High : NotificationPriority.Normal, Duration = duration, Source = evt };
        }
        return null;
    }
}
