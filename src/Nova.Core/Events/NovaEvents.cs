using Nova.Core.Media;

namespace Nova.Core.Events;

/// <summary>Base type of everything that flows through the <see cref="EventBus"/>.</summary>
public abstract record NovaEvent
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}

public enum MediaEventKind { Started, Paused, TrackChanged, Stopped }

public sealed record MediaEvent(MediaEventKind Kind, MediaSessionInfo Session) : NovaEvent;

public sealed record AppLaunchedEvent(string AppName, string AppId) : NovaEvent;

public sealed record DownloadCompletedEvent(string FileName, string FullPath) : NovaEvent;

public sealed record TimerCompletedEvent(string Label, TimeSpan Duration) : NovaEvent;

public enum BatteryChangeKind { PluggedIn, Unplugged, Low, Critical, Full }

public sealed record BatteryEvent(BatteryChangeKind Kind, int Percent, bool Charging) : NovaEvent;

public sealed record VolumeEvent(double Level, bool Muted) : NovaEvent;

public sealed record BrightnessEvent(double Level) : NovaEvent;

public sealed record ClipboardEvent(string Preview) : NovaEvent;

public sealed record NetworkEvent(bool Connected, string? NetworkName, bool IsWireless) : NovaEvent;

/// <summary>A Windows notification posted by another app (WhatsApp, Messenger, a browser, …).</summary>
public sealed record AppNotificationEvent(uint Id, string AppName, string? AppId, string Title, string? Body, bool IsCall, byte[]? Logo) : NovaEvent
{
    public string CoalesceKey => $"app:{Id}";
}

/// <summary>The app's notification left Action Center (answered, declined, dismissed or expired).</summary>
public sealed record AppNotificationRemovedEvent(uint Id) : NovaEvent;

/// <summary>Extension point for future/third-party events.</summary>
public sealed record CustomEvent(string Title, string? Subtitle, string Glyph = "", bool Important = false) : NovaEvent;
