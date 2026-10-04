using Nova.Core.Events;
using Nova.Core.Logging;

namespace Nova.Core.Media;

/// <summary>
/// Aggregates every <see cref="IMediaProvider"/>, decides which session is "current" and turns raw
/// session updates into high level <see cref="MediaEvent"/>s (started / paused / track changed).
/// </summary>
public sealed class MediaManager : IDisposable
{
    private readonly List<IMediaProvider> _providers;
    private readonly EventBus _bus;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private string? _announcedTrack;
    private string? _announcedKey;
    private bool _wasPlaying;
    private DateTimeOffset _lastPlayingAt;

    public MediaManager(IEnumerable<IMediaProvider> providers, EventBus bus, TimeProvider? time = null)
    {
        _providers = providers.ToList();
        _bus = bus;
        _time = time ?? TimeProvider.System;
        foreach (var p in _providers) p.SessionsChanged += OnProviderChanged;
    }

    public IReadOnlyList<IMediaProvider> Providers => _providers;
    public MediaSessionInfo? Current { get; private set; }
    public bool Enabled { get; set; } = true;
    /// <summary>How long a paused session still counts as "active" (keeps the compact indicator visible).</summary>
    public TimeSpan PausedLinger { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Raised whenever <see cref="Current"/> or any of its properties changed.</summary>
    public event Action<MediaSessionInfo?>? CurrentChanged;

    public bool HasActiveSession
    {
        get
        {
            var current = Current;
            if (current is null) return false;
            if (current.IsPlaying) return true;
            return current.State == PlaybackState.Paused && _time.GetUtcNow() - _lastPlayingAt < PausedLinger;
        }
    }

    public async Task StartAsync()
    {
        foreach (var provider in _providers)
        {
            try { await provider.StartAsync().ConfigureAwait(false); }
            catch (Exception ex) { Log.Error($"Media provider {provider.DisplayName} failed to start", ex); }
        }
        Recompute();
    }

    public IMediaProvider? ProviderFor(MediaSessionInfo session) => _providers.FirstOrDefault(p => p.Kind == session.Provider);

    public Task<bool> SendAsync(MediaCommand command)
    {
        var current = Current;
        var provider = current is null ? null : ProviderFor(current);
        return provider is null ? Task.FromResult(false) : SafeAsync(() => provider.SendCommandAsync(current!.Key, command));
    }

    public Task<bool> SeekAsync(TimeSpan position)
    {
        var current = Current;
        var provider = current is null ? null : ProviderFor(current);
        return provider is null ? Task.FromResult(false) : SafeAsync(() => provider.SeekAsync(current!.Key, position));
    }

    public double? GetVolume()
    {
        var current = Current;
        if (current is null) return null;
        try { return ProviderFor(current)?.GetVolume(current.Key); }
        catch (Exception ex) { Log.Warn("Reading session volume failed", ex); return null; }
    }

    public bool SetVolume(double value)
    {
        var current = Current;
        if (current is null) return false;
        try { return ProviderFor(current)?.SetVolume(current.Key, Math.Clamp(value, 0, 1)) ?? false; }
        catch (Exception ex) { Log.Warn("Setting session volume failed", ex); return false; }
    }

    private static async Task<bool> SafeAsync(Func<Task<bool>> action)
    {
        try { return await action().ConfigureAwait(false); }
        catch (Exception ex) { Log.Warn("Media command failed", ex); return false; }
    }

    private void OnProviderChanged(IMediaProvider _) => Recompute();

    /// <summary>Re-evaluates the current session. Public so tests and the controller can force it.</summary>
    public void Recompute()
    {
        MediaSessionInfo? next;
        var events = new List<MediaEvent>();
        lock (_gate)
        {
            var sessions = Enabled
                ? _providers.Where(p => p.IsEnabled).SelectMany(p => SafeSessions(p)).ToList()
                : new List<MediaSessionInfo>();
            next = Choose(sessions, Current);

            var now = _time.GetUtcNow();
            if (next is { IsPlaying: true }) _lastPlayingAt = now;

            var isPlaying = next?.IsPlaying ?? false;
            var trackKey = next?.TrackKey;
            var hasTitle = !string.IsNullOrWhiteSpace(next?.Title);

            if (next != null && isPlaying && hasTitle)
            {
                if (!_wasPlaying)
                    events.Add(new MediaEvent(MediaEventKind.Started, next));
                else if (_announcedTrack != trackKey || _announcedKey != next.Key)
                    events.Add(new MediaEvent(MediaEventKind.TrackChanged, next));
                _announcedTrack = trackKey;
                _announcedKey = next.Key;
            }
            else if (_wasPlaying && !isPlaying)
            {
                if (next != null && next.State == PlaybackState.Paused) events.Add(new MediaEvent(MediaEventKind.Paused, next));
                else if (Current != null) events.Add(new MediaEvent(MediaEventKind.Stopped, Current));
            }

            // Only flip the "playing" flag once a real title is known, so the start notification
            // isn't fired for the brief metadata-less state some players report first.
            if (!isPlaying || hasTitle) _wasPlaying = isPlaying;
            Current = next;
        }

        CurrentChanged?.Invoke(next);
        foreach (var e in events) _bus.Publish(e);
    }

    private static IReadOnlyList<MediaSessionInfo> SafeSessions(IMediaProvider provider)
    {
        try { return provider.Sessions; }
        catch (Exception ex) { Log.Warn($"{provider.DisplayName} sessions unavailable", ex); return Array.Empty<MediaSessionInfo>(); }
    }

    /// <summary>
    /// Picks the session to show: a playing session wins (sticking with the current one if it is still
    /// playing, to avoid flicker between two players), then the OS "current" session, then the most
    /// recently played one.
    /// </summary>
    internal static MediaSessionInfo? Choose(IReadOnlyList<MediaSessionInfo> sessions, MediaSessionInfo? previous)
    {
        if (sessions.Count == 0) return null;
        var playing = sessions.Where(s => s.IsPlaying).ToList();
        if (playing.Count > 0)
        {
            var sticky = previous is null ? null : playing.FirstOrDefault(s => s.Key == previous.Key);
            if (sticky != null) return sticky;
            return playing.OrderByDescending(s => s.IsSystemCurrent).ThenByDescending(s => s.LastPlayedAt).First();
        }

        var candidates = sessions.Where(s => s.State is PlaybackState.Paused or PlaybackState.Changing or PlaybackState.Opened).ToList();
        if (candidates.Count == 0) return null;
        var same = previous is null ? null : candidates.FirstOrDefault(s => s.Key == previous.Key);
        return same ?? candidates.OrderByDescending(s => s.IsSystemCurrent).ThenByDescending(s => s.LastPlayedAt).First();
    }

    public void Dispose()
    {
        foreach (var p in _providers)
        {
            p.SessionsChanged -= OnProviderChanged;
            try { p.Dispose(); } catch (Exception ex) { Log.Warn($"Disposing {p.DisplayName} failed", ex); }
        }
    }
}
