using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nova.Core.Logging;
using Nova.Core.QuickApps;
using Nova.Platform.Interop;
using static Nova.Platform.Interop.NativeMethods;
using static Nova.Platform.Interop.ShellInterop;

namespace Nova.Platform.QuickApps;

/// <summary>
/// Extracts high quality application icons through IShellItemImageFactory (works for .exe, .lnk,
/// folders and AppsFolder/Store apps) and caches the frozen bitmaps.
/// </summary>
public static class ShellIcons
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? ForQuickApp(QuickApp app, int size = 64)
    {
        if (!string.IsNullOrWhiteSpace(app.CustomIconPath) && File.Exists(app.CustomIconPath))
        {
            var custom = LoadImageFile(app.CustomIconPath!, size);
            if (custom != null) return custom;
        }
        var parsing = app.Kind switch
        {
            QuickAppKind.AppsFolder => "shell:AppsFolder\\" + app.Target,
            _ => Environment.ExpandEnvironmentVariables(app.Target),
        };
        return ForParsingName(parsing, size);
    }

    public static ImageSource? ForParsingName(string parsingName, int size = 64)
    {
        var key = parsingName + "|" + size;
        return Cache.GetOrAdd(key, _ => Extract(parsingName, size));
    }

    public static void Invalidate() => Cache.Clear();

    public static ImageSource? LoadImageFile(string path, int size)
    {
        try
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                return ForParsingName(path, size);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = size;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Warn($"Custom icon {path} could not be loaded", ex);
            return null;
        }
    }

    private static ImageSource? Extract(string parsingName, int size)
    {
        IntPtr hbitmap = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, IID_IShellItemImageFactory, out var obj);
            var factory = (IShellItemImageFactory)obj;
            try
            {
                var hr = factory.GetImage(new SIZE(size, size), SIIGBF.IconOnly | SIIGBF.BiggerSizeOk, out hbitmap);
                if (hr != 0 || hbitmap == IntPtr.Zero)
                    hr = factory.GetImage(new SIZE(size, size), SIIGBF.BiggerSizeOk, out hbitmap);
                if (hr != 0 || hbitmap == IntPtr.Zero) return null;
                return FromHBitmap(hbitmap);
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Icon extraction failed for {parsingName}: {ex.Message}");
            return null;
        }
        finally
        {
            if (hbitmap != IntPtr.Zero) DeleteObject(hbitmap);
        }
    }

    /// <summary>Converts a 32bpp DIB section preserving its alpha channel (Imaging.CreateBitmapSourceFromHBitmap drops it).</summary>
    private static BitmapSource? FromHBitmap(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<DIBSECTION>(), out var dib) == 0) return null;
        var bm = dib.dsBm;
        if (bm.bmBits == IntPtr.Zero || bm.bmBitsPixel != 32)
        {
            var fallback = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            fallback.Freeze();
            return fallback;
        }

        var width = bm.bmWidth;
        var height = Math.Abs(bm.bmHeight);
        var stride = bm.bmWidthBytes;
        var pixels = new byte[stride * height];
        Marshal.Copy(bm.bmBits, pixels, 0, pixels.Length);

        // Positive biHeight means bottom-up rows.
        if (dib.dsBmih.biHeight > 0)
        {
            var row = new byte[stride];
            for (var y = 0; y < height / 2; y++)
            {
                var top = y * stride;
                var bottom = (height - 1 - y) * stride;
                Buffer.BlockCopy(pixels, top, row, 0, stride);
                Buffer.BlockCopy(pixels, bottom, pixels, top, stride);
                Buffer.BlockCopy(row, 0, pixels, bottom, stride);
            }
        }

        // Icons without an alpha channel would render invisible; make them opaque.
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4) { if (pixels[i] != 0) { hasAlpha = true; break; } }
        if (!hasAlpha) for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        source.Freeze();
        return source;
    }
}
