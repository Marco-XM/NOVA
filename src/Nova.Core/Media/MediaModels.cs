namespace Nova.Core.Media;

public enum MediaProviderKind { Spotify, Chrome, Windows }

public enum PlaybackState { Unknown, Closed, Opened, Changing, Stopped, Playing, Paused }

/// <summary>Immutable snapshot of one media session as reported by a provider.</summary>
public sealed record MediaSessionInfo
{
    /// <summary>Unique key of the session (the source app id).</summary>
    public required string Key { get; init; }
    public required MediaProviderKind Provider { get; init; }
    /// <summary>Friendly app name, e.g. "Spotify", "Google Chrome".</summary>
    public required string SourceName { get; init; }
    public string SourceAppId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public PlaybackState State { get; init; }
    public TimeSpan Position { get; init; }
    public TimeSpan Duration { get; init; }
    /// <summary>When <see cref="Position"/> was sampled; used to interpolate progress without polling.</summary>
    public DateTimeOffset PositionTimestamp { get; init; }
    public double PlaybackRate { get; init; } = 1.0;
    /// <summary>Encoded artwork (PNG/JPEG bytes) when available.</summary>
    public byte[]? Artwork { get; init; }
    /// <summary>Changes whenever the artwork changes; lets the UI cache decoded bitmaps.</summary>
    public string? ArtworkKey { get; init; }
    public bool CanPlayPause { get; init; }
    public bool CanGoNext { get; init; }
    public bool CanGoPrevious { get; init; }
    public bool CanSeek { get; init; }
    public bool CanShuffle { get; init; }
    public bool IsShuffleActive { get; init; }
    public bool CanRepeat { get; init; }
    public MediaRepeatMode RepeatMode { get; init; }
    /// <summary>True if the OS marks this as the "current" session (what media keys control).</summary>
    public bool IsSystemCurrent { get; init; }
    public DateTimeOffset LastPlayedAt { get; init; }

    public bool IsPlaying => State == PlaybackState.Playing;
    public string TrackKey => $"{Title}\u001f{Artist}";

    public TimeSpan EstimatedPosition(DateTimeOffset now)
    {
        if (!IsPlaying || PositionTimestamp == default) return Clamp(Position);
        var elapsed = TimeSpan.FromTicks((long)((now - PositionTimestamp).Ticks * PlaybackRate));
        return Clamp(Position + elapsed);
    }

    private TimeSpan Clamp(TimeSpan value)
    {
        if (value < TimeSpan.Zero) return TimeSpan.Zero;
        return Duration > TimeSpan.Zero && value > Duration ? Duration : value;
    }
}

public enum MediaRepeatMode { None, Track, List }

public enum MediaCommand { PlayPause, Play, Pause, Next, Previous, ToggleShuffle, CycleRepeat }

/// <summary>
/// A source of media sessions. Implementations must never throw from <see cref="StartAsync"/> because
/// a missing app (Spotify not installed, Chrome not running) is a normal state, not an error.
/// </summary>
public interface IMediaProvider : IDisposable
{
    MediaProviderKind Kind { get; }
    string DisplayName { get; }
    bool IsEnabled { get; set; }
    /// <summary>False if the underlying system API is unavailable; see <see cref="StatusText"/>.</summary>
    bool IsAvailable { get; }
    string StatusText { get; }
    IReadOnlyList<MediaSessionInfo> Sessions { get; }

    /// <summary>Raised (on any thread) whenever the provider's session list or any session changed.</summary>
    event Action<IMediaProvider>? SessionsChanged;

    Task StartAsync();
    Task<bool> SendCommandAsync(string sessionKey, MediaCommand command);
    Task<bool> SeekAsync(string sessionKey, TimeSpan position);

    /// <summary>Per-app volume 0..1, or null when the app's audio session can't be found.</summary>
    double? GetVolume(string sessionKey);
    bool SetVolume(string sessionKey, double volume);
}
