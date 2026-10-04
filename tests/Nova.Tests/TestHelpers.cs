using System.Diagnostics;
using Nova.Core.Media;
using Nova.Core.Monitors;
using Nova.Core.QuickApps;
using Nova.Core.Settings;

namespace Nova.Tests;

/// <summary>Manually advanced clock.</summary>
public sealed class FakeTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
    public void AdvanceMs(double ms) => _now += TimeSpan.FromMilliseconds(ms);
}

public static class Monitors
{
    public static MonitorInfo Make(string name, int left, int top, int width, int height, int dpi = 96, bool primary = false, string? id = null, string? friendly = null) => new()
    {
        DeviceName = name,
        DeviceId = id ?? $"\\\\?\\DISPLAY#{name}",
        FriendlyName = friendly ?? name + " Monitor",
        Bounds = new PixelRect(left, top, width, height),
        WorkArea = new PixelRect(left, top, width, height - 48),
        Dpi = dpi,
        IsPrimary = primary,
    };
}

public sealed class FakeMediaProvider(MediaProviderKind kind) : IMediaProvider
{
    private List<MediaSessionInfo> _sessions = new();
    public MediaProviderKind Kind { get; } = kind;
    public string DisplayName => Kind.ToString();
    public bool IsEnabled { get; set; } = true;
    public bool IsAvailable => true;
    public string StatusText => "fake";
    public bool ThrowOnSessions { get; set; }
    public bool ThrowOnStart { get; set; }
    public List<(string Key, MediaCommand Command)> Commands { get; } = new();
    public IReadOnlyList<MediaSessionInfo> Sessions => ThrowOnSessions ? throw new InvalidOperationException("boom") : _sessions;
    public event Action<IMediaProvider>? SessionsChanged;

    public void Set(params MediaSessionInfo[] sessions)
    {
        _sessions = sessions.ToList();
        SessionsChanged?.Invoke(this);
    }

    public Task StartAsync() => ThrowOnStart ? throw new InvalidOperationException("not installed") : Task.CompletedTask;

    public Task<bool> SendCommandAsync(string sessionKey, MediaCommand command)
    {
        Commands.Add((sessionKey, command));
        return Task.FromResult(true);
    }

    public Task<bool> SeekAsync(string sessionKey, TimeSpan position) => Task.FromResult(true);
    public double? GetVolume(string sessionKey) => 0.5;
    public bool SetVolume(string sessionKey, double volume) => true;
    public void Dispose() { }

    public static MediaSessionInfo Session(string key, string title, PlaybackState state, MediaProviderKind kind = MediaProviderKind.Spotify, DateTimeOffset? played = null) => new()
    {
        Key = key,
        Provider = kind,
        SourceName = key,
        Title = title,
        Artist = "Artist",
        State = state,
        LastPlayedAt = played ?? DateTimeOffset.Now,
        CanPlayPause = true,
    };
}

public sealed class FakeStarter : IProcessStarter
{
    public List<ProcessStartInfo> Started { get; } = new();
    public string? FailWith { get; set; }

    public bool TryStart(ProcessStartInfo info, out string? error)
    {
        error = FailWith;
        if (FailWith != null) return false;
        Started.Add(info);
        return true;
    }
}

public sealed class FakeFileSystem : IFileSystemProbe
{
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool FileExists(string path) => Files.Contains(path);
    public bool DirectoryExists(string path) => Directories.Contains(path);
}

public sealed class FakeRegistry : IStartupRegistry
{
    public Dictionary<string, string> Run { get; } = new();
    public Dictionary<string, byte[]> Approved { get; } = new();
    public string? GetRunValue(string name) => Run.TryGetValue(name, out var v) ? v : null;
    public void SetRunValue(string name, string command) => Run[name] = command;
    public void DeleteRunValue(string name) => Run.Remove(name);
    public byte[]? GetStartupApproved(string name) => Approved.TryGetValue(name, out var v) ? v : null;
    public void DeleteStartupApproved(string name) => Approved.Remove(name);
}

public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nova-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }
    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { }
    }
}
