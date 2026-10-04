using System.Diagnostics;
using Nova.Core.Logging;
using Nova.Core.Media;
using Nova.Platform.Services;

namespace Nova.Platform.Media;

/// <summary>
/// Base provider over the shared <see cref="MediaSessionHub"/>. Subclasses only decide which sessions
/// they own and how they report their status, which keeps adding a provider trivial.
/// </summary>
public abstract class HubMediaProvider : IMediaProvider
{
    private readonly MediaSessionHub _hub;
    private bool _enabled = true;

    protected HubMediaProvider(MediaSessionHub hub)
    {
        _hub = hub;
        _hub.Changed += OnHubChanged;
    }

    public abstract MediaProviderKind Kind { get; }
    public abstract string DisplayName { get; }

    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            SessionsChanged?.Invoke(this);
        }
    }

    public bool IsAvailable => _hub.IsAvailable;
    public virtual string StatusText => !_hub.IsAvailable ? _hub.Status : Sessions.Count == 0 ? "No active session" : DescribeSessions();

    public IReadOnlyList<MediaSessionInfo> Sessions => _hub.Snapshot().Where(s => s.Provider == Kind).ToList();

    public event Action<IMediaProvider>? SessionsChanged;

    public Task StartAsync() => _hub.StartAsync();

    private void OnHubChanged() => SessionsChanged?.Invoke(this);

    protected string DescribeSessions()
    {
        var playing = Sessions.FirstOrDefault(s => s.IsPlaying);
        return playing != null ? $"Playing: {playing.Title}" : "Connected — paused";
    }

    public async Task<bool> SendCommandAsync(string sessionKey, MediaCommand command)
    {
        var session = _hub.Find(sessionKey);
        if (session is null) return false;
        try
        {
            return command switch
            {
                MediaCommand.PlayPause => await session.TryTogglePlayPauseAsync(),
                MediaCommand.Play => await session.TryPlayAsync(),
                MediaCommand.Pause => await session.TryPauseAsync(),
                MediaCommand.Next => await session.TrySkipNextAsync(),
                MediaCommand.Previous => await session.TrySkipPreviousAsync(),
                MediaCommand.ToggleShuffle => await session.TryChangeShuffleActiveAsync(session.GetPlaybackInfo()?.IsShuffleActive != true),
                MediaCommand.CycleRepeat => await session.TryChangeAutoRepeatModeAsync(session.GetPlaybackInfo()?.AutoRepeatMode switch
                {
                    Windows.Media.MediaPlaybackAutoRepeatMode.None => Windows.Media.MediaPlaybackAutoRepeatMode.List,
                    Windows.Media.MediaPlaybackAutoRepeatMode.List => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
                    _ => Windows.Media.MediaPlaybackAutoRepeatMode.None,
                }),
                _ => false,
            };
        }
        catch (Exception ex)
        {
            Log.Warn($"{DisplayName}: {command} failed", ex);
            return false;
        }
    }

    public async Task<bool> SeekAsync(string sessionKey, TimeSpan position)
    {
        var session = _hub.Find(sessionKey);
        if (session is null) return false;
        try
        {
            var timeline = session.GetTimelineProperties();
            var target = timeline.StartTime + position;
            return await session.TryChangePlaybackPositionAsync(target.Ticks);
        }
        catch (Exception ex)
        {
            Log.Warn($"{DisplayName}: seek failed", ex);
            return false;
        }
    }

    public double? GetVolume(string sessionKey) => SessionVolume.Get(sessionKey);
    public bool SetVolume(string sessionKey, double volume) => SessionVolume.Set(sessionKey, volume);

    public virtual void Dispose() => _hub.Changed -= OnHubChanged;

    protected static bool AnyProcess(params string[] names)
    {
        foreach (var name in names)
        {
            var processes = Process.GetProcessesByName(name);
            var found = processes.Length > 0;
            foreach (var p in processes) p.Dispose();
            if (found) return true;
        }
        return false;
    }
}

/// <summary>
/// Spotify via its Windows media session (desktop and Microsoft Store versions). Spotify's Web API is
/// intentionally not used: it requires each user to register a developer OAuth app.
/// </summary>
public sealed class SpotifyProvider(MediaSessionHub hub) : HubMediaProvider(hub)
{
    public override MediaProviderKind Kind => MediaProviderKind.Spotify;
    public override string DisplayName => "Spotify";

    public static bool IsInstalled()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return File.Exists(Path.Combine(roaming, "Spotify", "Spotify.exe"))
               || File.Exists(Path.Combine(local, "Microsoft", "WindowsApps", "Spotify.exe"))
               || Directory.Exists(Path.Combine(local, "Packages")) && Directory.EnumerateDirectories(Path.Combine(local, "Packages"), "SpotifyAB.SpotifyMusic_*").Any();
    }

    public override string StatusText
    {
        get
        {
            if (!IsAvailable) return base.StatusText;
            if (Sessions.Count > 0) return DescribeSessions();
            if (AnyProcess("Spotify")) return "Running — nothing playing";
            return IsInstalled() ? "Not running" : "Not installed";
        }
    }
}

/// <summary>Google Chrome tabs that publish media (YouTube, YouTube Music, SoundCloud, …) via the Media Session API.</summary>
public sealed class ChromeProvider(MediaSessionHub hub) : HubMediaProvider(hub)
{
    public override MediaProviderKind Kind => MediaProviderKind.Chrome;
    public override string DisplayName => "Google Chrome";

    public static bool IsInstalled()
    {
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
        };
        return candidates.Any(File.Exists);
    }

    public override string StatusText
    {
        get
        {
            if (!IsAvailable) return base.StatusText;
            if (Sessions.Count > 0) return DescribeSessions();
            if (AnyProcess("chrome")) return "Running — no tab is playing media";
            return IsInstalled() ? "Not running" : "Not installed";
        }
    }
}

/// <summary>Every other app that publishes a Windows media session (Edge, Firefox, Media Player, VLC, …).</summary>
public sealed class WindowsMediaProvider(MediaSessionHub hub) : HubMediaProvider(hub)
{
    public override MediaProviderKind Kind => MediaProviderKind.Windows;
    public override string DisplayName => "Other media apps";

    public override string StatusText
    {
        get
        {
            if (!IsAvailable) return base.StatusText;
            var sessions = Sessions;
            return sessions.Count == 0 ? "No other app is playing" : string.Join(", ", sessions.Select(s => s.SourceName).Distinct());
        }
    }
}
