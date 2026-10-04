using System.Diagnostics;
using NAudio.CoreAudioApi;
using Nova.Core.Logging;

namespace Nova.Platform.Services;

/// <summary>Per-application volume via CoreAudio audio sessions (what the Windows volume mixer shows).</summary>
public static class SessionVolume
{
    /// <summary>Maps a media session's source app id to the process names that own its audio.</summary>
    public static string[] ProcessNamesFor(string sourceAppId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppId)) return Array.Empty<string>();
        var id = sourceAppId.ToLowerInvariant();
        if (id.Contains("spotify")) return new[] { "Spotify" };
        if (id.StartsWith("chrome")) return new[] { "chrome" };
        if (id.Contains("msedge")) return new[] { "msedge" };
        if (id == "308046b0af4a39cb" || id.Contains("firefox")) return new[] { "firefox" };
        if (id.Contains("zunemusic")) return new[] { "Microsoft.Media.Player" };
        if (id.Contains("brave")) return new[] { "brave" };
        if (id.Contains("opera")) return new[] { "opera" };
        if (id.EndsWith(".exe")) return new[] { Path.GetFileNameWithoutExtension(sourceAppId) };
        var bang = sourceAppId.LastIndexOf('!');
        return new[] { bang >= 0 ? sourceAppId[(bang + 1)..] : sourceAppId };
    }

    public static double? Get(string sourceAppId)
    {
        double? result = null;
        Visit(sourceAppId, s => { result = s.SimpleAudioVolume.Volume; return false; });
        return result;
    }

    public static bool Set(string sourceAppId, double volume)
    {
        var any = false;
        Visit(sourceAppId, s =>
        {
            s.SimpleAudioVolume.Volume = (float)Math.Clamp(volume, 0, 1);
            any = true;
            return true; // a browser can own several sessions; set all of them
        });
        return any;
    }

    private static void Visit(string sourceAppId, Func<AudioSessionControl, bool> action)
    {
        var names = ProcessNamesFor(sourceAppId);
        if (names.Length == 0) return;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) return;
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.IsSystemSoundsSession) continue;
                string processName;
                try
                {
                    using var p = Process.GetProcessById((int)session.GetProcessID);
                    processName = p.ProcessName;
                }
                catch { continue; }
                if (!names.Any(n => string.Equals(n, processName, StringComparison.OrdinalIgnoreCase))) continue;
                if (!action(session)) return;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Session volume access failed: {ex.Message}");
        }
    }
}
