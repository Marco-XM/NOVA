using Nova.Core.Monitors;
using Nova.Core.Settings;
using M = Nova.Tests.Monitors;

namespace Nova.Tests.Core;

public class MonitorTests
{
    // Laptop (150%) on the left at negative coordinates, 4K (200%) primary in the middle, 1080p (100%) on the right.
    private static readonly MonitorInfo Laptop = M.Make(@"\\.\DISPLAY1", -1920, 360, 1920, 1200, dpi: 144, friendly: "Built-in display");
    private static readonly MonitorInfo FourK = M.Make(@"\\.\DISPLAY2", 0, 0, 3840, 2160, dpi: 192, primary: true, friendly: "LG 27UL850");
    private static readonly MonitorInfo Hd = M.Make(@"\\.\DISPLAY3", 3840, 0, 1920, 1080, dpi: 96, friendly: "DELL P2419H");
    private static readonly IReadOnlyList<MonitorInfo> Three = new[] { Laptop, FourK, Hd };

    private static MonitorSelectionContext Ctx(IReadOnlyList<MonitorInfo> monitors, string? fg = null, string? cursor = null, string? prev = null) =>
        new() { Monitors = monitors, ForegroundMonitor = fg, CursorMonitor = cursor, PreviousMonitor = prev };

    [Fact]
    public void SingleMonitor_AlwaysSelected_InEveryMode()
    {
        var one = new[] { M.Make(@"\\.\DISPLAY1", 0, 0, 2560, 1440, primary: true) };
        foreach (var mode in Enum.GetValues<MonitorMode>())
        {
            var selection = MonitorSelector.Select(new MonitorSettings { Mode = mode }, Ctx(one));
            Assert.Equal(@"\\.\DISPLAY1", selection!.Value.Monitor.DeviceName);
        }
    }

    [Fact]
    public void NoMonitors_ReturnsNull()
    {
        Assert.Null(MonitorSelector.Select(new MonitorSettings(), Ctx(Array.Empty<MonitorInfo>())));
    }

    [Fact]
    public void PrimaryMode_PicksPrimary()
    {
        var s = MonitorSelector.Select(new MonitorSettings { Mode = MonitorMode.Primary }, Ctx(Three, fg: Hd.DeviceName));
        Assert.Equal(FourK, s!.Value.Monitor);
        Assert.False(s.Value.IsFallback);
    }

    [Fact]
    public void SelectedMode_MatchesByDeviceId_EvenIfGdiNameChanged()
    {
        var settings = new MonitorSettings { Mode = MonitorMode.Selected, SelectedDeviceId = Hd.DeviceId, SelectedDeviceName = @"\\.\DISPLAY9" };
        var s = MonitorSelector.Select(settings, Ctx(Three));
        Assert.Equal(Hd, s!.Value.Monitor);
    }

    [Fact]
    public void SelectedMode_FallsBackToFriendlyName_ThenGdiName()
    {
        var byName = new MonitorSettings { Mode = MonitorMode.Selected, SelectedDeviceId = "gone", SelectedFriendlyName = "DELL P2419H" };
        Assert.Equal(Hd, MonitorSelector.Select(byName, Ctx(Three))!.Value.Monitor);

        var byGdi = new MonitorSettings { Mode = MonitorMode.Selected, SelectedDeviceId = "gone", SelectedDeviceName = Laptop.DeviceName };
        Assert.Equal(Laptop, MonitorSelector.Select(byGdi, Ctx(Three))!.Value.Monitor);
    }

    [Fact]
    public void SelectedMode_AmbiguousFriendlyName_UsesGdiName()
    {
        var a = M.Make(@"\\.\DISPLAY1", 0, 0, 1920, 1080, primary: true, friendly: "Same Model", id: "a");
        var b = M.Make(@"\\.\DISPLAY2", 1920, 0, 1920, 1080, friendly: "Same Model", id: "b");
        var settings = new MonitorSettings { Mode = MonitorMode.Selected, SelectedDeviceId = "x", SelectedFriendlyName = "Same Model", SelectedDeviceName = @"\\.\DISPLAY2" };
        Assert.Equal(b, MonitorSelector.Select(settings, Ctx(new[] { a, b }))!.Value.Monitor);
    }

    [Fact]
    public void SelectedMonitorDisconnected_FallsBackToPrimary_AndReturnsWhenReconnected()
    {
        var settings = new MonitorSettings { Mode = MonitorMode.Selected };
        MonitorSelector.StoreSelection(settings, Hd);

        var unplugged = MonitorSelector.Select(settings, Ctx(new[] { Laptop, FourK }));
        Assert.Equal(FourK, unplugged!.Value.Monitor);
        Assert.True(unplugged.Value.IsFallback);

        var replugged = MonitorSelector.Select(settings, Ctx(Three));
        Assert.Equal(Hd, replugged!.Value.Monitor);
        Assert.False(replugged.Value.IsFallback);
    }

    [Fact]
    public void ActiveMode_FollowsForegroundWindow_ThenStaysOnPrevious()
    {
        var settings = new MonitorSettings { Mode = MonitorMode.Active };
        Assert.Equal(Laptop, MonitorSelector.Select(settings, Ctx(Three, fg: Laptop.DeviceName))!.Value.Monitor);
        // Desktop focused (no meaningful window): keep the previous monitor instead of jumping.
        Assert.Equal(Hd, MonitorSelector.Select(settings, Ctx(Three, fg: null, cursor: Laptop.DeviceName, prev: Hd.DeviceName))!.Value.Monitor);
    }

    [Fact]
    public void ActiveMode_PreviousMonitorUnplugged_UsesCursorMonitor()
    {
        var settings = new MonitorSettings { Mode = MonitorMode.Active };
        var s = MonitorSelector.Select(settings, Ctx(new[] { Laptop, FourK }, fg: null, cursor: Laptop.DeviceName, prev: Hd.DeviceName));
        Assert.Equal(Laptop, s!.Value.Monitor);
    }

    [Fact]
    public void MouseMode_FollowsCursor()
    {
        var settings = new MonitorSettings { Mode = MonitorMode.Mouse };
        Assert.Equal(Hd, MonitorSelector.Select(settings, Ctx(Three, cursor: Hd.DeviceName))!.Value.Monitor);
        Assert.Equal(Laptop, MonitorSelector.Select(settings, Ctx(Three, cursor: Laptop.DeviceName))!.Value.Monitor);
    }

    [Theory]
    [InlineData(96, 760, 420, 760, 420)]
    [InlineData(120, 760, 420, 950, 525)]
    [InlineData(144, 760, 420, 1140, 630)]
    [InlineData(192, 760, 420, 1520, 840)]
    public void DpiMath_ConvertsDipsToPhysicalPixels(int dpi, double wDip, double hDip, int expectedW, int expectedH)
    {
        var monitor = M.Make(@"\\.\DISPLAY1", 0, 0, 3840, 2160, dpi: dpi);
        var rect = DpiMath.PlaceTopCenter(monitor, wDip, hDip);
        Assert.Equal(expectedW, rect.Width);
        Assert.Equal(expectedH, rect.Height);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void PlaceTopCenter_IsCenteredAndAttachedToTop_OnEveryMonitor(int dpi)
    {
        foreach (var m in new[] { Laptop with { Dpi = dpi }, FourK with { Dpi = dpi }, Hd with { Dpi = dpi } })
        {
            var rect = DpiMath.PlaceTopCenter(m, 760, 420);
            Assert.Equal(m.Bounds.Top, rect.Top);
            var leftGap = rect.Left - m.Bounds.Left;
            var rightGap = m.Bounds.Right - rect.Right;
            Assert.InRange(Math.Abs(leftGap - rightGap), 0, 1);
            Assert.True(m.Bounds.Contains(rect.Left, rect.Top));
        }
    }

    [Fact]
    public void PlaceTopCenter_HandlesNegativeCoordinates_AndTopOffset()
    {
        var rect = DpiMath.PlaceTopCenter(Laptop, 760, 420, topOffsetDip: 8);
        Assert.Equal(-1920 + (1920 - 1140) / 2, rect.Left);
        Assert.Equal(360 + 12, rect.Top); // 8 DIP at 150% = 12 px
    }

    [Fact]
    public void DpiMath_RoundTrips()
    {
        Assert.Equal(150, DpiMath.ToPhysical(100, 144));
        Assert.Equal(100, DpiMath.ToDip(150, 144), 3);
    }
}
