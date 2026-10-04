using Nova.Platform.Monitors;
using Nova.Platform.QuickApps;
using Xunit.Abstractions;

namespace Nova.Tests.Integration;

/// <summary>
/// Runs against the real Windows APIs of the machine executing the tests. These verify that the
/// interop layer works end to end (no mocks) and are tagged so CI can filter them.
/// </summary>
[Trait("Category", "Integration")]
public class WindowsIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task InstalledAppScanner_FindsStartMenuApps()
    {
        var direct = RunSta(InstalledAppScanner.Scan);
        output.WriteLine($"direct: {direct.Count} {InstalledAppScanner.LastDiagnostics}");
        var apps = await InstalledAppScanner.ScanAsync();
        output.WriteLine($"{apps.Count} apps");
        foreach (var a in apps.Take(15)) output.WriteLine($"{a.Name} => {a.ParsingName}");
        Assert.NotEmpty(apps);
        var favorites = InstalledAppScanner.DetectDefaultFavorites(apps);
        output.WriteLine("Favorites: " + string.Join(", ", favorites.Select(f => f.Name)));
        Assert.Contains(favorites, f => f.Name == "Explorer" || f.Name == "Calculator");
    }

    private static T RunSta<T>(Func<T> f)
    {
        T result = default!;
        Exception? error = null;
        var t = new Thread(() => { try { result = f(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw new Exception("STA call failed", error);
        return result;
    }

    [Fact]
    public void Win32MonitorProvider_ReturnsAtLeastOnePrimaryMonitor()
    {
        var monitors = new Win32MonitorProvider().GetMonitors();
        foreach (var m in monitors) output.WriteLine($"{m.DeviceName} '{m.FriendlyName}' {m.Bounds} dpi={m.Dpi} id={m.DeviceId}");
        Assert.NotEmpty(monitors);
        Assert.Single(monitors, m => m.IsPrimary);
        Assert.All(monitors, m => Assert.True(m.Dpi >= 96 && m.Bounds.Width > 0));
    }
}
