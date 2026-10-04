using System.Windows;
using System.Windows.Media;
using Nova.Core.Settings;

namespace Nova.App.Notch.Rendering;

/// <summary>Colors for one material. Brushes are created once and frozen.</summary>
public sealed class NotchPalette
{
    public required Color Top { get; init; }
    public required Color Bottom { get; init; }
    public required Color Rim { get; init; }
    public required Color Highlight { get; init; }
    public required Color Shadow { get; init; }
    public required bool IsLight { get; init; }
    public double ShadowOpacity { get; init; } = 0.5;

    public static NotchPalette For(NotchMaterial material) => material switch
    {
        NotchMaterial.Midnight => new NotchPalette
        {
            Top = Color.FromRgb(0, 0, 0), Bottom = Color.FromRgb(0, 0, 0),
            Rim = Color.FromArgb(28, 255, 255, 255), Highlight = Color.FromArgb(10, 255, 255, 255),
            Shadow = Colors.Black, IsLight = false, ShadowOpacity = 0.55,
        },
        NotchMaterial.Graphite => new NotchPalette
        {
            Top = Color.FromRgb(38, 39, 46), Bottom = Color.FromRgb(24, 25, 30),
            Rim = Color.FromArgb(40, 255, 255, 255), Highlight = Color.FromArgb(18, 255, 255, 255),
            Shadow = Colors.Black, IsLight = false, ShadowOpacity = 0.5,
        },
        NotchMaterial.Frost => new NotchPalette
        {
            Top = Color.FromRgb(250, 251, 253), Bottom = Color.FromRgb(232, 235, 241),
            Rim = Color.FromArgb(120, 255, 255, 255), Highlight = Color.FromArgb(90, 255, 255, 255),
            Shadow = Color.FromRgb(20, 24, 40), IsLight = true, ShadowOpacity = 0.28,
        },
        _ => new NotchPalette
        {
            Top = Color.FromRgb(24, 25, 32), Bottom = Color.FromRgb(9, 10, 13),
            Rim = Color.FromArgb(36, 255, 255, 255), Highlight = Color.FromArgb(16, 255, 255, 255),
            Shadow = Colors.Black, IsLight = false, ShadowOpacity = 0.5,
        },
    };

    /// <summary>Pushes the foreground palette used by notch content into application resources.</summary>
    public void ApplyContentResources(ResourceDictionary resources, Color accent)
    {
        void Set(string key, Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            resources[key] = brush;
        }

        if (IsLight)
        {
            Set("Notch.Foreground", Color.FromRgb(18, 20, 26));
            Set("Notch.Secondary", Color.FromRgb(86, 92, 106));
            Set("Notch.Tertiary", Color.FromRgb(128, 134, 148));
            Set("Notch.Divider", Color.FromArgb(24, 0, 0, 0));
            Set("Notch.Hover", Color.FromArgb(20, 0, 0, 0));
            Set("Notch.Pressed", Color.FromArgb(36, 0, 0, 0));
            Set("Notch.Tile", Color.FromArgb(14, 0, 0, 0));
            Set("Notch.TileHover", Color.FromArgb(26, 0, 0, 0));
            Set("Notch.Track", Color.FromArgb(34, 0, 0, 0));
            Set("Notch.Fill", Color.FromRgb(24, 26, 32));
            Set("Notch.PrimaryButton", Color.FromRgb(20, 22, 28));
            Set("Notch.PrimaryButtonForeground", Color.FromRgb(250, 250, 252));
        }
        else
        {
            Set("Notch.Foreground", Color.FromRgb(245, 246, 249));
            Set("Notch.Secondary", Color.FromRgb(182, 187, 198));
            Set("Notch.Tertiary", Color.FromRgb(142, 147, 160));
            Set("Notch.Divider", Color.FromArgb(20, 255, 255, 255));
            Set("Notch.Hover", Color.FromArgb(26, 255, 255, 255));
            Set("Notch.Pressed", Color.FromArgb(43, 255, 255, 255));
            Set("Notch.Tile", Color.FromArgb(14, 255, 255, 255));
            Set("Notch.TileHover", Color.FromArgb(30, 255, 255, 255));
            Set("Notch.Track", Color.FromArgb(41, 255, 255, 255));
            Set("Notch.Fill", Color.FromRgb(245, 246, 249));
            Set("Notch.PrimaryButton", Color.FromRgb(245, 246, 249));
            Set("Notch.PrimaryButtonForeground", Color.FromRgb(10, 11, 15));
        }
        Set("Notch.Accent", accent);
        Set("Notch.AccentSoft", Color.FromArgb(51, accent.R, accent.G, accent.B));
        // Accent used for small text: nudged lighter (dark notch) or darker (Frost) until it reads clearly.
        Set("Notch.AccentText", ColorMath.Readable(accent, IsLight));
        // Unread dot and count follow the accent; the count text picks whichever of black/white reads on it.
        Set("Notch.Badge", accent);
        Set("Notch.BadgeForeground", ColorMath.Luminance(accent) > 0.18 ? Color.FromRgb(10, 11, 15) : Colors.White);
    }
}

public static class ColorMath
{
    public static Color Parse(string hex, Color fallback)
    {
        return ColorUtil.TryParseHex(hex, out var c) ? Color.FromArgb(c.A, c.R, c.G, c.B) : fallback;
    }

    public static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);

    /// <summary>WCAG relative luminance (0 = black, 1 = white).</summary>
    public static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>
    /// Blends <paramref name="c"/> toward white (on a dark background) or black (on a light one) just
    /// far enough for small text to stay legible, keeping its hue.
    /// </summary>
    public static Color Readable(Color c, bool onLight)
    {
        const double darkTarget = 0.30, lightTarget = 0.12;
        var target = onLight ? Colors.Black : Colors.White;
        for (var t = 0.0; t <= 1.0; t += 0.05)
        {
            var mixed = Color.FromRgb(
                (byte)Math.Round(c.R + (target.R - c.R) * t),
                (byte)Math.Round(c.G + (target.G - c.G) * t),
                (byte)Math.Round(c.B + (target.B - c.B) * t));
            var l = Luminance(mixed);
            if (onLight ? l <= lightTarget : l >= darkTarget) return mixed;
        }
        return target;
    }

    /// <summary>Rotates the hue of a color by <paramref name="degrees"/> and scales its saturation.</summary>
    public static Color Shift(Color c, double degrees, double saturation = 1, double lightness = 1)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double h = 0, s, l = (max + min) / 2;
        if (max == min) { s = 0; }
        else
        {
            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
        }
        h = (h + degrees) % 360; if (h < 0) h += 360;
        s = Math.Clamp(s * saturation, 0, 1);
        l = Math.Clamp(l * lightness, 0, 1);
        return FromHsl(h, s, l, c.A);
    }

    public static Color FromHsl(double h, double s, double l, byte a = 255)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromArgb(a, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
