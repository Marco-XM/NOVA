namespace Nova.Core.Media;

public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>
/// Picks a few distinct, vivid colors from cover art (used to tint the Aurora light). Pixels are
/// grouped by hue and weighted by how colorful and bright they are, so a small saturated detail
/// can win over a large grey background, and the chosen colors are at least 30° of hue apart.
/// </summary>
public static class ArtworkPalette
{
    private const int Bins = 24;
    private const int MinBinDistance = 2; // 2 × 15° = 30°

    /// <summary>Returns exactly <paramref name="count"/> colors, or none if there are no pixels.</summary>
    public static IReadOnlyList<Rgb> Extract(ReadOnlySpan<byte> bgra, int count = 3)
    {
        if (bgra.Length < 4 || count <= 0) return Array.Empty<Rgb>();

        var weight = new double[Bins];
        var sumR = new double[Bins];
        var sumG = new double[Bins];
        var sumB = new double[Bins];
        double allR = 0, allG = 0, allB = 0, allW = 0;

        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            double b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            var (h, s, v) = ToHsv(r, g, b);
            allR += r; allG += g; allB += b; allW++;
            if (s < 0.12 || v < 0.12) continue; // greys and near-black say nothing about the mood
            var w = s * s * v;
            var bin = (int)(h / 360.0 * Bins) % Bins;
            weight[bin] += w;
            sumR[bin] += r * w; sumG[bin] += g * w; sumB[bin] += b * w;
        }

        var picked = new List<int>();
        foreach (var bin in Enumerable.Range(0, Bins).Where(b => weight[b] > 0).OrderByDescending(b => weight[b]))
        {
            if (picked.Count == count) break;
            if (picked.Any(p => HueDistance(p, bin) < MinBinDistance)) continue;
            // Ignore hues that are only a sprinkle compared to the strongest one.
            if (picked.Count > 0 && weight[bin] < weight[picked[0]] * 0.04) break;
            picked.Add(bin);
        }

        var colors = picked.Select(b => new Rgb(Byte(sumR[b] / weight[b]), Byte(sumG[b] / weight[b]), Byte(sumB[b] / weight[b]))).ToList();
        if (colors.Count == 0)
        {
            // Black-and-white art: use its average tone.
            colors.Add(new Rgb(Byte(allR / allW), Byte(allG / allW), Byte(allB / allW)));
        }

        // Single-hue art: fill up with neighbouring hues of the main color so the light still moves.
        var baseColor = colors[0];
        var shifts = new[] { 32.0, -32.0, 64.0, -64.0 };
        for (var k = 0; colors.Count < count; k++)
            colors.Add(ShiftHue(baseColor, shifts[k % shifts.Length] * (1 + k / shifts.Length)));
        return colors;
    }

    public static (double H, double S, double V) ToHsv(double r, double g, double b)
    {
        r /= 255; g /= 255; b /= 255;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        double h = 0;
        if (d > 1e-9)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max <= 0 ? 0 : d / max, max);
    }

    public static Rgb FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return new Rgb(Byte((r + m) * 255), Byte((g + m) * 255), Byte((b + m) * 255));
    }

    private static Rgb ShiftHue(Rgb c, double degrees)
    {
        var (h, s, v) = ToHsv(c.R, c.G, c.B);
        return FromHsv(h + degrees, s, v);
    }

    private static int HueDistance(int a, int b)
    {
        var d = Math.Abs(a - b);
        return Math.Min(d, Bins - d);
    }

    private static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
