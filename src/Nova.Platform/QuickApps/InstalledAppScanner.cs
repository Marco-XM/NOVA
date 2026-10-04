using System.Runtime.InteropServices;
using Nova.Core.Logging;
using Nova.Core.QuickApps;
using static Nova.Platform.Interop.NativeMethods;
using static Nova.Platform.Interop.ShellInterop;

namespace Nova.Platform.QuickApps;

public sealed record InstalledApp(string Name, string ParsingName)
{
    public QuickApp ToQuickApp() => new() { Name = Name, Kind = QuickAppKind.AppsFolder, Target = ParsingName };
}

/// <summary>
/// Enumerates the Windows "AppsFolder" (everything the Start menu lists: Win32 apps, Store apps,
/// PWAs). Launching any of these goes through <c>shell:AppsFolder\&lt;parsing name&gt;</c>.
/// </summary>
public static class InstalledAppScanner
{
    private static readonly string[] NoiseWords = { "uninstall", "readme", "release notes", "help", "documentation", "website", "license", "manual" };

    private static readonly (string Match, string Display)[] Favorites =
    {
        ("Spotify", "Spotify"),
        ("Google Chrome", "Chrome"),
        ("Visual Studio Code", "VS Code"),
        ("Discord", "Discord"),
        ("Steam", "Steam"),
        ("File Explorer", "Explorer"),
        ("Calculator", "Calculator"),
    };

    /// <summary>Counters from the last scan, for the log and diagnostics.</summary>
    public static string LastDiagnostics { get; private set; } = "";

    public static Task<IReadOnlyList<InstalledApp>> ScanAsync()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<InstalledApp>>();
        // Shell enumeration is happiest on an STA thread.
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(Scan()); }
            catch (Exception ex)
            {
                Log.Warn("Scanning installed apps failed", ex);
                tcs.SetResult(Array.Empty<InstalledApp>());
            }
        }) { IsBackground = true, Name = "NOVA app scan" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    internal static IReadOnlyList<InstalledApp> Scan()
    {
        var result = new List<InstalledApp>();
        SHGetKnownFolderItem(FOLDERID_AppsFolder, 0, IntPtr.Zero, IID_IShellItem, out var folderObj);
        var folder = (IShellItem)folderObj;
        try
        {
            var hrBind = folder.BindToHandler(IntPtr.Zero, BHID_EnumItems, IID_IEnumShellItems, out var enumObj);
            if (hrBind != 0) throw new COMException("BindToHandler(BHID_EnumItems) failed", hrBind);
            var enumerator = (IEnumShellItems)enumObj;
            int raw = 0, unnamed = 0, hr = 0;
            try
            {
                while ((hr = enumerator.Next(1, out var item, out var fetched)) == 0 && fetched == 1)
                {
                    raw++;
                    try
                    {
                        var name = GetName(item, SIGDN_NORMALDISPLAY);
                        var parsing = GetName(item, SIGDN_PARENTRELATIVEPARSING);
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parsing)) { unnamed++; continue; }
                        if (parsing.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                        var lower = name.ToLowerInvariant();
                        if (NoiseWords.Any(lower.Contains)) continue;
                        result.Add(new InstalledApp(name, parsing));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(item);
                    }
                }
            }
            finally
            {
                LastDiagnostics = $"raw={raw} unnamed={unnamed} kept={result.Count} lastHr=0x{hr:X8}";
                Marshal.ReleaseComObject(enumerator);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(folder);
        }

        return result
            .GroupBy(a => a.ParsingName, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Picks the default favorites that are actually installed (no placeholder entries).</summary>
    public static IReadOnlyList<QuickApp> DetectDefaultFavorites(IReadOnlyList<InstalledApp> installed)
    {
        var list = new List<QuickApp>();
        foreach (var (match, display) in Favorites)
        {
            var app = installed.FirstOrDefault(a => string.Equals(a.Name, match, StringComparison.OrdinalIgnoreCase))
                      ?? installed.FirstOrDefault(a => a.Name.StartsWith(match, StringComparison.OrdinalIgnoreCase));
            if (app != null) list.Add(new QuickApp { Name = display, Kind = QuickAppKind.AppsFolder, Target = app.ParsingName });
        }
        return list;
    }
}
