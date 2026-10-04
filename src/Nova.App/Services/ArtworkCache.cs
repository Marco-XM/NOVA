using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nova.App.Notch.Rendering;
using Nova.Core.Logging;
using Nova.Core.Media;

namespace Nova.App.Services;

public sealed record Artwork(ImageSource Image, Color Dominant, IReadOnlyList<Color> Palette);

/// <summary>Decodes album art once per track (small LRU) and extracts a tasteful accent color from it.</summary>
public sealed class ArtworkCache
{
    private const int Capacity = 8;
    private readonly LinkedList<(string Key, Artwork? Value)> _lru = new();

    public Artwork? Get(string? key, byte[]? bytes)
    {
        if (string.IsNullOrEmpty(key) || bytes is null || bytes.Length == 0) return null;
        for (var node = _lru.First; node != null; node = node.Next)
        {
            if (node.Value.Key != key) continue;
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value.Value;
        }

        var artwork = Decode(bytes);
        _lru.AddFirst((key, artwork));
        while (_lru.Count > Capacity) _lru.RemoveLast();
        return artwork;
    }

    private static Artwork? Decode(byte[] bytes)
    {
        try
        {
            var image = new BitmapImage();
            using (var ms = new MemoryStream(bytes, writable: false))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 240;
                image.StreamSource = ms;
                image.EndInit();
            }
            image.Freeze();
            return new Artwork(image, DominantColor(bytes), AuroraPalette(bytes));
        }
        catch (Exception ex)
        {
            Log.Debug($"Artwork decode failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Three distinct colors from the art, made vivid and bright enough to glow on the dark notch.</summary>
    private static IReadOnlyList<Color> AuroraPalette(byte[] bytes)
    {
        try
        {
            var pixels = Thumbnail(bytes, 32, out _, out _);
            return ArtworkPalette.Extract(pixels, 3).Select(c =>
            {
                var (h, s, v) = ArtworkPalette.ToHsv(c.R, c.G, c.B);
                var rgb = ArtworkPalette.FromHsv(h, Math.Clamp(s * 1.15, 0.45, 0.95), Math.Clamp(v, 0.75, 1));
                return Color.FromRgb(rgb.R, rgb.G, rgb.B);
            }).ToList();
        }
        catch
        {
            return Array.Empty<Color>();
        }
    }

    private static byte[] Thumbnail(byte[] bytes, int width, out int w, out int h)
    {
        var small = new BitmapImage();
        using (var ms = new MemoryStream(bytes, writable: false))
        {
            small.BeginInit();
            small.CacheOption = BitmapCacheOption.OnLoad;
            small.DecodePixelWidth = width;
            small.StreamSource = ms;
            small.EndInit();
        }
        var converted = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
        w = converted.PixelWidth;
        h = converted.PixelHeight;
        var pixels = new byte[w * h * 4];
        converted.CopyPixels(pixels, w * 4, 0);
        return pixels;
    }

    /// <summary>Average of a 12px thumbnail, weighted towards saturated pixels, then normalized for a dark UI.</summary>
    private static Color DominantColor(byte[] bytes)
    {
        try
        {
            var pixels = Thumbnail(bytes, 12, out _, out _);

            double r = 0, g = 0, b = 0, total = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                double pb = pixels[i], pg = pixels[i + 1], pr = pixels[i + 2];
                var max = Math.Max(pr, Math.Max(pg, pb));
                var min = Math.Min(pr, Math.Min(pg, pb));
                var weight = 0.15 + (max - min) / 255.0 * 2 + (max > 40 ? 0.2 : 0);
                r += pr * weight; g += pg * weight; b += pb * weight; total += weight;
            }
            var avg = Color.FromRgb((byte)(r / total), (byte)(g / total), (byte)(b / total));
            // Keep it vivid but never neon, and bright enough to read on black.
            var c = ColorMath.Shift(avg, 0, 1.25, 1.0);
            var lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            if (lum < 0.45) c = ColorMath.Shift(c, 0, 1, 0.45 / Math.Max(0.05, lum) > 2.2 ? 2.2 : 0.45 / Math.Max(0.05, lum));
            return c;
        }
        catch
        {
            return Color.FromRgb(139, 156, 255);
        }
    }
}
