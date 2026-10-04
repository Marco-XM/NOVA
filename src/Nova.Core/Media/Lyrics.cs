using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Nova.Core.Logging;

namespace Nova.Core.Media;

public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>Time-synced lyrics of one track.</summary>
public sealed record Lyrics(IReadOnlyList<LyricLine> Lines)
{
    /// <summary>Index of the line being sung at <paramref name="position"/>, or -1 before the first line.</summary>
    public int IndexAt(TimeSpan position)
    {
        int lo = 0, hi = Lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (Lines[mid].Time <= position) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }
}

public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2}(?:[.:]\d{1,3})?)\]")]
    private static partial Regex Stamp();

    /// <summary>Parses LRC text ("[mm:ss.xx] line"); lines with several stamps repeat. Returns null when unsynced.</summary>
    public static Lyrics? Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc)) return null;
        var lines = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var matches = Stamp().Matches(raw);
            if (matches.Count == 0) continue;
            var text = Stamp().Replace(raw, "").Trim();
            foreach (Match m in matches)
            {
                var minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = double.Parse(m.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
                lines.Add(new LyricLine(TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds), text));
            }
        }
        if (lines.Count == 0) return null;
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return new Lyrics(lines);
    }
}

/// <summary>Normalizes what players report (YouTube titles especially) into a searchable title / artist.</summary>
public static partial class TrackNameCleaner
{
    [GeneratedRegex(@"\s*[\(\[][^\)\]]*(official|video|lyric|audio|visuali[sz]er|\bhd\b|\b4k\b|\bmv\b|remaster|explicit|\bfeat\b|\bft\.|\bwith\b)[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex Noise();

    /// <summary>A trailing " feat. X" / " ft. X" without brackets.</summary>
    [GeneratedRegex(@"\s+(feat\.?|ft\.)\s.*$", RegexOptions.IgnoreCase)]
    private static partial Regex Featuring();

    public static (string Title, string Artist) Clean(string title, string artist)
    {
        title = Featuring().Replace(Noise().Replace(title ?? "", ""), "").Trim();
        artist = (artist ?? "").Replace(" - Topic", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (artist.EndsWith("VEVO", StringComparison.OrdinalIgnoreCase)) artist = artist[..^4].Trim();

        // "Artist - Song" (typical for music videos): prefer the artist from the title.
        var dash = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            var left = title[..dash].Trim();
            var right = title[(dash + 3)..].Trim();
            if (artist.Length == 0 || left.Contains(artist, StringComparison.OrdinalIgnoreCase) || artist.Contains(left, StringComparison.OrdinalIgnoreCase))
            {
                artist = left;
                title = right;
            }
        }
        return (title, artist);
    }
}

/// <summary>
/// Looks up synced lyrics on lrclib.net (free, no account). Only the title, artist, album and duration
/// of the current track are sent. Results — including "not found" — are cached per track.
/// </summary>
public sealed class LyricsService : IDisposable
{
    /// <summary>After a network failure, the same track isn't retried sooner than this.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);
    private readonly HttpClient _http;
    private readonly Dictionary<string, Lyrics?> _cache = new();
    private readonly Dictionary<string, DateTimeOffset> _failedAt = new();
    private readonly object _gate = new();

    public LyricsService(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri("https://lrclib.net/");
        _http.Timeout = TimeSpan.FromSeconds(12);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NOVA/1.0 (Windows notch app)");
    }

    /// <summary>
    /// Synced lyrics, or null. <c>Failed</c> is true when the server couldn't be reached (worth trying
    /// again later) rather than the song simply having no synced lyrics.
    /// </summary>
    public async Task<(Lyrics? Lyrics, bool Failed)> GetAsync(string title, string artist, string album, TimeSpan duration, CancellationToken ct = default)
    {
        var (t, a) = TrackNameCleaner.Clean(title, artist);
        if (t.Length == 0) return (null, false);
        var key = $"{t}\u001f{a}".ToLowerInvariant();
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached)) return (cached, false);
            if (_failedAt.TryGetValue(key, out var failed) && DateTimeOffset.UtcNow - failed < RetryAfter) return (null, true);
        }

        Lyrics? result = null;
        try
        {
            if (a.Length > 0 && duration > TimeSpan.Zero)
            {
                var url = $"api/get?track_name={E(t)}&artist_name={E(a)}&album_name={E(album)}&duration={(int)Math.Round(duration.TotalSeconds)}";
                using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    result = LrcParser.Parse((await response.Content.ReadFromJsonAsync<LrcLibTrack>(ct).ConfigureAwait(false))?.SyncedLyrics);
                else if (response.StatusCode != HttpStatusCode.NotFound)
                    return Failed(key); // server trouble: don't cache, try again later
            }
            if (result is null)
            {
                var url = $"api/search?track_name={E(t)}" + (a.Length > 0 ? $"&artist_name={E(a)}" : "");
                var hits = await _http.GetFromJsonAsync<List<LrcLibTrack>>(url, ct).ConfigureAwait(false) ?? new();
                // Prefer a hit whose length matches the track, so a live / extended version doesn't win.
                var best = hits.Where(h => !string.IsNullOrWhiteSpace(h.SyncedLyrics))
                    .OrderBy(h => duration > TimeSpan.Zero ? Math.Abs(h.Duration - duration.TotalSeconds) : 0)
                    .FirstOrDefault();
                if (best != null && (duration <= TimeSpan.Zero || Math.Abs(best.Duration - duration.TotalSeconds) < 8))
                    result = LrcParser.Parse(best.SyncedLyrics);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return (null, false); }
        catch (Exception ex)
        {
            // Includes HttpClient's own timeout (a TaskCanceledException without our token).
            Log.Debug($"Lyrics lookup failed: {ex.Message}");
            return Failed(key);
        }

        lock (_gate)
        {
            if (_cache.Count > 64) _cache.Clear();
            _cache[key] = result;
            _failedAt.Remove(key);
        }
        return (result, false);
    }

    private (Lyrics?, bool) Failed(string key)
    {
        lock (_gate) _failedAt[key] = DateTimeOffset.UtcNow;
        return (null, true);
    }

    private static string E(string value) => Uri.EscapeDataString(value ?? "");

    public void Dispose() => _http.Dispose();

    private sealed class LrcLibTrack
    {
        [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; set; }
        [JsonPropertyName("duration")] public double Duration { get; set; }
    }
}
