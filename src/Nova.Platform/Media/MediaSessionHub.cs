using Nova.Core.Logging;
using Nova.Core.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using GsmtcManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using GsmtcSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using GsmtcStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace Nova.Platform.Media;

/// <summary>
/// Single owner of the Windows media session manager (GSMTC). Every provider reads from this hub, so
/// we subscribe to each session exactly once. Fully event driven: SessionsChanged, PlaybackInfoChanged,
/// MediaPropertiesChanged and TimelinePropertiesChanged; bursts are coalesced per session.
/// </summary>
public sealed class MediaSessionHub : IDisposable
{
    private const int MaxArtworkBytes = 4 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly Dictionary<string, Tracked> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private GsmtcManager? _manager;
    private bool _disposed;
    private Task? _startTask;

    public bool IsAvailable { get; private set; }
    public string Status { get; private set; } = "Not started";

    /// <summary>Raised on a background thread after any session changed.</summary>
    public event Action? Changed;

    public Task StartAsync() => _startTask ??= StartCoreAsync();

    private async Task StartCoreAsync()
    {
        try
        {
            _manager = await GsmtcManager.RequestAsync();
            _manager.SessionsChanged += OnSessionsChanged;
            _manager.CurrentSessionChanged += OnSessionsChanged;
            IsAvailable = true;
            Status = "Connected to Windows media sessions";
            await SyncSessionsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            Status = "Windows media sessions are unavailable on this system";
            Log.Error("GlobalSystemMediaTransportControlsSessionManager.RequestAsync failed", ex);
        }
    }

    /// <summary>Re-reads everything (used after resume from sleep).</summary>
    public Task RefreshAllAsync() => _manager is null ? StartAsync() : SyncSessionsAsync(forceRefresh: true);

    public IReadOnlyList<MediaSessionInfo> Snapshot()
    {
        lock (_gate)
        {
            var currentId = SafeCurrentId();
            return _sessions.Values
                .Where(t => t.Info != null)
                .Select(t => t.Info! with { IsSystemCurrent = string.Equals(t.Info!.SourceAppId, currentId, StringComparison.OrdinalIgnoreCase) })
                .ToList();
        }
    }

    public GsmtcSession? Find(string key)
    {
        lock (_gate) return _sessions.TryGetValue(key, out var t) ? t.Session : null;
    }

    private string? SafeCurrentId()
    {
        try { return _manager?.GetCurrentSession()?.SourceAppUserModelId; }
        catch { return null; }
    }

    private void OnSessionsChanged(GsmtcManager sender, object args) => _ = SyncSessionsAsync();

    private async Task SyncSessionsAsync(bool forceRefresh = false)
    {
        if (_manager is null || _disposed) return;
        IReadOnlyList<GsmtcSession> sessions;
        try { sessions = _manager.GetSessions(); }
        catch (Exception ex) { Log.Warn("GetSessions failed", ex); return; }

        var added = new List<Tracked>();
        lock (_gate)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var session in sessions)
            {
                string id;
                try { id = session.SourceAppUserModelId; } catch { continue; }
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                if (_sessions.TryGetValue(id, out var existing) && ReferenceEquals(existing.Session, session))
                {
                    if (forceRefresh) added.Add(existing);
                    continue;
                }
                if (existing != null) existing.Detach();
                var tracked = new Tracked(this, id, session);
                _sessions[id] = tracked;
                added.Add(tracked);
            }
            foreach (var gone in _sessions.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _sessions[gone].Detach();
                _sessions.Remove(gone);
            }
        }

        foreach (var t in added) await t.RefreshAllAsync().ConfigureAwait(false);
        RaiseChanged();
    }

    internal void RaiseChanged()
    {
        if (_disposed) return;
        try { Changed?.Invoke(); }
        catch (Exception ex) { Log.Error("Media hub listener failed", ex); }
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_gate)
        {
            foreach (var t in _sessions.Values) t.Detach();
            _sessions.Clear();
        }
        if (_manager != null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager.CurrentSessionChanged -= OnSessionsChanged;
        }
    }

    /// <summary>One subscribed GSMTC session plus its last snapshot.</summary>
    private sealed class Tracked
    {
        private readonly MediaSessionHub _hub;
        private readonly string _id;
        private int _mediaVersion;
        private string? _artworkKey;
        private byte[]? _artwork;
        private string _title = "", _artist = "", _album = "";
        private DateTimeOffset _lastPlayed;

        public Tracked(MediaSessionHub hub, string id, GsmtcSession session)
        {
            _hub = hub;
            _id = id;
            Session = session;
            session.PlaybackInfoChanged += OnPlaybackChanged;
            session.TimelinePropertiesChanged += OnTimelineChanged;
            session.MediaPropertiesChanged += OnMediaChanged;
        }

        public GsmtcSession Session { get; }
        public MediaSessionInfo? Info { get; private set; }

        public void Detach()
        {
            try
            {
                Session.PlaybackInfoChanged -= OnPlaybackChanged;
                Session.TimelinePropertiesChanged -= OnTimelineChanged;
                Session.MediaPropertiesChanged -= OnMediaChanged;
            }
            catch { /* session already gone */ }
        }

        private void OnPlaybackChanged(GsmtcSession s, PlaybackInfoChangedEventArgs e) { Rebuild(); _hub.RaiseChanged(); }
        private void OnTimelineChanged(GsmtcSession s, TimelinePropertiesChangedEventArgs e) { Rebuild(); _hub.RaiseChanged(); }
        private void OnMediaChanged(GsmtcSession s, MediaPropertiesChangedEventArgs e) => _ = RefreshMediaAsync(debounce: true);

        public async Task RefreshAllAsync()
        {
            await RefreshMediaAsync(debounce: false).ConfigureAwait(false);
        }

        private async Task RefreshMediaAsync(bool debounce)
        {
            var version = Interlocked.Increment(ref _mediaVersion);
            if (debounce)
            {
                // Players typically fire several property changes per track change; coalesce them.
                await Task.Delay(120).ConfigureAwait(false);
                if (version != Volatile.Read(ref _mediaVersion)) return;
            }
            try
            {
                var props = await Session.TryGetMediaPropertiesAsync();
                if (version != Volatile.Read(ref _mediaVersion)) return;
                if (props != null)
                {
                    _title = props.Title ?? "";
                    _artist = string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumArtist ?? "" : props.Artist;
                    _album = props.AlbumTitle ?? "";
                    var (bytes, key) = await ReadArtworkAsync(props.Thumbnail, _title + _artist).ConfigureAwait(false);
                    if (version != Volatile.Read(ref _mediaVersion)) return;
                    _artwork = bytes;
                    _artworkKey = key;
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Media properties unavailable for {_id}: {ex.Message}");
            }
            Rebuild();
            _hub.RaiseChanged();
        }

        private static async Task<(byte[]?, string?)> ReadArtworkAsync(IRandomAccessStreamReference? reference, string salt)
        {
            if (reference is null) return (null, null);
            try
            {
                using var stream = await reference.OpenReadAsync();
                if (stream.Size == 0 || stream.Size > MaxArtworkBytes) return (null, null);
                var buffer = new byte[stream.Size];
                using var input = stream.GetInputStreamAt(0);
                using var reader = new DataReader(input);
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(buffer);
                return (buffer, $"{salt.GetHashCode():X8}-{Hash(buffer):X8}");
            }
            catch (Exception ex)
            {
                Log.Debug($"Artwork read failed: {ex.Message}");
                return (null, null);
            }
        }

        private static uint Hash(byte[] data)
        {
            // FNV-1a over a sample of the bytes; enough to detect artwork changes cheaply.
            uint h = 2166136261;
            var step = Math.Max(1, data.Length / 4096);
            for (var i = 0; i < data.Length; i += step) h = (h ^ data[i]) * 16777619;
            return h ^ (uint)data.Length;
        }

        private void Rebuild()
        {
            try
            {
                var playback = Session.GetPlaybackInfo();
                var timeline = Session.GetTimelineProperties();
                var controls = playback?.Controls;
                var state = playback?.PlaybackStatus switch
                {
                    GsmtcStatus.Playing => PlaybackState.Playing,
                    GsmtcStatus.Paused => PlaybackState.Paused,
                    GsmtcStatus.Stopped => PlaybackState.Stopped,
                    GsmtcStatus.Changing => PlaybackState.Changing,
                    GsmtcStatus.Opened => PlaybackState.Opened,
                    GsmtcStatus.Closed => PlaybackState.Closed,
                    _ => PlaybackState.Unknown,
                };
                var now = DateTimeOffset.Now;
                if (state == PlaybackState.Playing) _lastPlayed = now;

                var duration = TimeSpan.Zero;
                var position = TimeSpan.Zero;
                var stamp = now;
                if (timeline != null)
                {
                    duration = timeline.EndTime - timeline.StartTime;
                    position = timeline.Position - timeline.StartTime;
                    // Some apps never update LastUpdatedTime; trust "now" if it's obviously stale.
                    stamp = timeline.LastUpdatedTime;
                    if (stamp == default || stamp > now || (duration > TimeSpan.Zero && now - stamp > duration)) stamp = now;
                }

                var (provider, name) = Classify(_id);
                Info = new MediaSessionInfo
                {
                    Key = _id,
                    Provider = provider,
                    SourceName = name,
                    SourceAppId = _id,
                    Title = _title,
                    Artist = _artist,
                    Album = _album,
                    State = state,
                    Position = position < TimeSpan.Zero ? TimeSpan.Zero : position,
                    Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration,
                    PositionTimestamp = stamp,
                    PlaybackRate = playback?.PlaybackRate is double rate && rate > 0 ? rate : 1.0,
                    Artwork = _artwork,
                    ArtworkKey = _artworkKey,
                    CanPlayPause = controls?.IsPlayPauseToggleEnabled == true || controls?.IsPlayEnabled == true || controls?.IsPauseEnabled == true,
                    CanGoNext = controls?.IsNextEnabled == true,
                    CanGoPrevious = controls?.IsPreviousEnabled == true,
                    CanSeek = controls?.IsPlaybackPositionEnabled == true && duration > TimeSpan.Zero,
                    CanShuffle = controls?.IsShuffleEnabled == true,
                    IsShuffleActive = playback?.IsShuffleActive == true,
                    CanRepeat = controls?.IsRepeatEnabled == true,
                    RepeatMode = playback?.AutoRepeatMode switch
                    {
                        Windows.Media.MediaPlaybackAutoRepeatMode.Track => MediaRepeatMode.Track,
                        Windows.Media.MediaPlaybackAutoRepeatMode.List => MediaRepeatMode.List,
                        _ => MediaRepeatMode.None,
                    },
                    LastPlayedAt = _lastPlayed,
                };
            }
            catch (Exception ex)
            {
                Log.Debug($"Session {_id} rebuild failed: {ex.Message}");
            }
        }
    }

    /// <summary>Maps a source app id to provider kind and a friendly name.</summary>
    public static (MediaProviderKind Kind, string Name) Classify(string sourceAppId)
    {
        var id = sourceAppId.ToLowerInvariant();
        if (id.Contains("spotify")) return (MediaProviderKind.Spotify, "Spotify");
        if (id.StartsWith("chrome")) return (MediaProviderKind.Chrome, "Google Chrome");
        if (id.Contains("msedge")) return (MediaProviderKind.Windows, "Microsoft Edge");
        if (id == "308046b0af4a39cb" || id.Contains("firefox")) return (MediaProviderKind.Windows, "Firefox");
        if (id.Contains("zunemusic")) return (MediaProviderKind.Windows, "Media Player");
        if (id.Contains("brave")) return (MediaProviderKind.Windows, "Brave");
        if (id.Contains("vlc")) return (MediaProviderKind.Windows, "VLC");
        if (id.Contains("applemusic") || id.Contains("apple music")) return (MediaProviderKind.Windows, "Apple Music");
        var name = sourceAppId;
        var bang = name.LastIndexOf('!');
        if (bang >= 0) name = name[(bang + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return (MediaProviderKind.Windows, name);
    }
}
