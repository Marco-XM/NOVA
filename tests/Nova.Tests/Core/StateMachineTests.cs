using Nova.Core.Events;
using Nova.Core.Notifications;
using Nova.Core.Settings;
using Nova.Core.State;

namespace Nova.Tests.Core;

public class StateMachineTests
{
    private readonly FakeTime _time = new();
    private readonly NotchStateMachine _sm;
    private readonly List<NotchTransition> _transitions = new();

    public StateMachineTests()
    {
        _sm = new NotchStateMachine(_time) { HoverDelay = TimeSpan.FromMilliseconds(100), CollapseDelay = TimeSpan.FromMilliseconds(500), InteractiveCollapseDelay = TimeSpan.FromMilliseconds(1000) };
        _sm.Changed += t => _transitions.Add(t);
    }

    private void Advance(double ms)
    {
        _time.AdvanceMs(ms);
        _sm.Tick();
    }

    private static NotchNotification Note(string title, NotificationPriority p = NotificationPriority.Normal, string? key = null, bool queueable = true, double seconds = 2) =>
        new() { Title = title, Priority = p, CoalesceKey = key, Queueable = queueable, Duration = TimeSpan.FromSeconds(seconds) };

    [Fact]
    public void StartsIdle() => Assert.Equal(NotchState.Idle, _sm.State);

    [Fact]
    public void Hover_RespectsDelay_AndCancelsWhenPointerLeavesEarly()
    {
        _sm.PointerEntered();
        Assert.Equal(NotchState.Idle, _sm.State);
        _sm.PointerExited();
        Advance(200);
        Assert.Equal(NotchState.Idle, _sm.State);

        _sm.PointerEntered();
        Advance(120);
        Assert.Equal(NotchState.Hover, _sm.State);
    }

    [Fact]
    public void Hover_ReturnsToIdle_AfterPointerLeaves()
    {
        _sm.PointerEntered();
        Advance(120);
        _sm.PointerExited();
        Advance(300);
        Assert.Equal(NotchState.Idle, _sm.State);
    }

    [Fact]
    public void ExpandOnHoverOff_StaysIdle()
    {
        _sm.ExpandOnHover = false;
        _sm.PointerEntered();
        Advance(500);
        Assert.Equal(NotchState.Idle, _sm.State);
    }

    [Fact]
    public void Click_ExpandsHome_WithoutMedia_AndMedia_WithMedia()
    {
        _sm.Click();
        Assert.Equal(NotchState.Expanded, _sm.State);
        _sm.Collapse();

        _sm.SetLiveActivity(hasMedia: true, hasTimer: false);
        Assert.Equal(NotchState.Compact, _sm.State);
        _sm.Click();
        Assert.Equal(NotchState.Media, _sm.State);
    }

    [Fact]
    public void Click_WithOnlyTimer_OpensTimer()
    {
        _sm.SetLiveActivity(false, true);
        _sm.Click();
        Assert.Equal(NotchState.Tool, _sm.State);
        Assert.Equal(NotchTool.Timer, _sm.Snapshot.Tool);
    }

    [Fact]
    public void Interactive_CollapsesAfterPointerLeaves_UnlessPointerReturns()
    {
        _sm.Navigate(NotchState.Media);
        _sm.PointerEntered();
        _sm.PointerExited();
        Advance(600);
        _sm.PointerEntered(); // came back in time
        Advance(2000);
        Assert.Equal(NotchState.Media, _sm.State);

        _sm.PointerExited();
        Advance(1100);
        Assert.Equal(NotchState.Idle, _sm.State);
    }

    [Fact]
    public void Pinned_PreventsAutoCollapse()
    {
        _sm.Navigate(NotchState.Tool, NotchTool.Calculator);
        _sm.SetPinned(true);
        _sm.PointerExited();
        Advance(5000);
        Assert.Equal(NotchState.Tool, _sm.State);
        _sm.SetPinned(false);
        Advance(1100);
        Assert.Equal(NotchState.Idle, _sm.State);
    }

    [Fact]
    public void Hotkey_TogglesExpanded()
    {
        _sm.HotkeyPressed(HotkeyAction.Toggle);
        Assert.Equal(NotchState.Expanded, _sm.State);
        _sm.HotkeyPressed(HotkeyAction.Toggle);
        Assert.Equal(NotchState.Idle, _sm.State);
    }

    [Fact]
    public void Hotkey_ExpandAction_NeverCollapses()
    {
        _sm.HotkeyPressed(HotkeyAction.Expand);
        _sm.HotkeyPressed(HotkeyAction.Expand);
        Assert.Equal(NotchState.Expanded, _sm.State);
    }

    [Fact]
    public void Notification_ShowsThenExpires()
    {
        Assert.True(_sm.Notify(Note("hello")));
        Assert.Equal(NotchState.Notification, _sm.State);
        Assert.Equal("hello", _sm.Snapshot.Notification!.Title);
        Advance(1900);
        Assert.Equal(NotchState.Notification, _sm.State);
        Advance(200);
        Assert.Equal(NotchState.Idle, _sm.State);
    }

    [Fact]
    public void Notification_ReturnsToCompact_WhenMediaIsLive()
    {
        _sm.SetLiveActivity(true, false);
        _sm.Notify(Note("n"));
        Advance(2100);
        Assert.Equal(NotchState.Compact, _sm.State);
    }

    [Fact]
    public void Notifications_Queue_InOrder()
    {
        _sm.Notify(Note("first"));
        _sm.Notify(Note("second"));
        Assert.Equal(1, _sm.QueueLength);
        Advance(2100);
        Assert.Equal("second", _sm.Snapshot.Notification!.Title);
        Advance(2100);
        Assert.Equal(NotchState.Idle, _sm.State);
    }

    [Fact]
    public void SameCoalesceKey_ReplacesAndExtends()
    {
        _sm.Notify(Note("vol 10", key: "volume", queueable: false, seconds: 1.5));
        Advance(1000);
        _sm.Notify(Note("vol 20", key: "volume", queueable: false, seconds: 1.5));
        Assert.Equal("vol 20", _sm.Snapshot.Notification!.Title);
        Assert.Equal(0, _sm.QueueLength);
        Advance(1000);
        Assert.Equal(NotchState.Notification, _sm.State); // extended
    }

    [Fact]
    public void HighPriority_Preempts_AndRequeuesCurrent()
    {
        _sm.Notify(Note("download", seconds: 4));
        _sm.Notify(Note("timer", NotificationPriority.High));
        Assert.Equal("timer", _sm.Snapshot.Notification!.Title);
        Assert.Equal(1, _sm.QueueLength);
        Advance(2100);
        Assert.Equal("download", _sm.Snapshot.Notification!.Title);
    }

    [Fact]
    public void WhileInteracting_IndicatorsAreDropped_ImportantEventsWait()
    {
        _sm.Navigate(NotchState.Media);
        Assert.False(_sm.Notify(Note("volume", NotificationPriority.Low, "volume", queueable: false)));
        Assert.True(_sm.Notify(Note("timer done", NotificationPriority.High)));
        Assert.Equal(NotchState.Media, _sm.State);
        _sm.Collapse();
        Assert.Equal(NotchState.Notification, _sm.State);
        Assert.Equal("timer done", _sm.Snapshot.Notification!.Title);
    }

    [Fact]
    public void Hovering_HoldsNotification_UntilPointerLeaves()
    {
        _sm.Notify(Note("hold me"));
        _sm.PointerEntered();
        Advance(5000);
        Assert.Equal(NotchState.Notification, _sm.State);
        _sm.PointerExited();
        Advance(1000);
        Assert.NotEqual(NotchState.Notification, _sm.State);
    }

    [Fact]
    public void ClickOnMediaNotification_OpensPlayer()
    {
        _sm.SetLiveActivity(true, false);
        _sm.Notify(new NotchNotification { Title = "Song", Style = NotificationStyle.Media });
        _sm.Click();
        Assert.Equal(NotchState.Media, _sm.State);
    }

    [Fact]
    public void ClickOnTimerNotification_OpensTimer()
    {
        _sm.Notify(new NotchNotification { Title = "Done", Source = new TimerCompletedEvent("t", TimeSpan.FromMinutes(1)) });
        _sm.Click();
        Assert.Equal(NotchTool.Timer, _sm.Snapshot.Tool);
    }

    [Fact]
    public void UserHidden_HidesAndDropsNotifications()
    {
        _sm.SetUserHidden(true);
        Assert.Equal(NotchState.Hidden, _sm.State);
        Assert.Equal(HiddenReason.User, _sm.Snapshot.HiddenReason);
        Assert.False(_sm.Notify(Note("x")));
        _sm.SetUserHidden(false);
        Assert.Equal(NotchState.Idle, _sm.State);
    }

    [Fact]
    public void Fullscreen_SuppressesNotch_HotkeyCanStillOpenIt()
    {
        _sm.SetSuppressed(true);
        Assert.Equal(NotchState.Hidden, _sm.State);
        Assert.Equal(HiddenReason.Fullscreen, _sm.Snapshot.HiddenReason);

        _sm.HotkeyPressed(HotkeyAction.Toggle);
        Assert.Equal(NotchState.Expanded, _sm.State);
        _sm.Collapse();
        Assert.Equal(NotchState.Hidden, _sm.State);

        _sm.SetSuppressed(false);
        Assert.Equal(NotchState.Idle, _sm.State);
    }

    [Fact]
    public void LiveActivityChange_DuringHover_UpdatesSnapshotWithoutLeavingHover()
    {
        _sm.PointerEntered();
        Advance(150);
        _sm.SetLiveActivity(true, false);
        Assert.Equal(NotchState.Hover, _sm.State);
        Assert.True(_sm.Snapshot.HasMedia);
    }

    [Fact]
    public void NextDeadline_ReflectsPendingTimers()
    {
        Assert.Null(_sm.NextDeadline);
        _sm.Notify(Note("n", seconds: 3));
        Assert.Equal(_time.GetUtcNow() + TimeSpan.FromSeconds(3), _sm.NextDeadline);
    }

    [Fact]
    public void EveryTransitionIsReported()
    {
        _sm.Click();
        _sm.Navigate(NotchState.QuickApps);
        _sm.Collapse();
        Assert.Equal(new[] { NotchState.Expanded, NotchState.QuickApps, NotchState.Idle }, _transitions.Select(t => t.Current.State));
    }

    [Fact]
    public void Layout_GrowsFromIdleToExpanded_AndRespectsScale()
    {
        var options = new LayoutOptions { QuickAppCount = 6 };
        var idle = NotchLayout.For(new NotchSnapshot(NotchState.Idle, NotchTool.None, null, false, false, HiddenReason.None), options);
        var hover = NotchLayout.For(new NotchSnapshot(NotchState.Hover, NotchTool.None, null, false, false, HiddenReason.None), options);
        var home = NotchLayout.For(new NotchSnapshot(NotchState.Expanded, NotchTool.None, null, false, false, HiddenReason.None), options);
        Assert.True(hover.Area > idle.Area);
        Assert.True(home.Area > hover.Area);
        Assert.True(home.Width <= NotchLayout.MaxWidth && home.Height <= NotchLayout.MaxHeight);

        var big = NotchLayout.For(new NotchSnapshot(NotchState.Expanded, NotchTool.None, null, false, false, HiddenReason.None), options with { SizeScale = 1.2 });
        Assert.Equal(home.Width * 1.2, big.Width, 1);
        Assert.True(idle.CornerRadius <= idle.Height / 2);
    }

    [Fact]
    public void Layout_AutoHide_ShrinksIdleToAThinLine()
    {
        var idle = NotchLayout.For(new NotchSnapshot(NotchState.Idle, NotchTool.None, null, false, false, HiddenReason.None), new LayoutOptions { AutoHide = true });
        Assert.True(idle.Height < 8);
    }

    [Fact]
    public void Layout_Floating_KeepsTopOffset_ButAutoHideSliverTucksToTheEdge()
    {
        var floating = new LayoutOptions { Shape = NotchShape.Floating, TopOffset = 10 };
        var idle = new NotchSnapshot(NotchState.Idle, NotchTool.None, null, false, false, HiddenReason.None);
        var hover = new NotchSnapshot(NotchState.Hover, NotchTool.None, null, false, false, HiddenReason.None);
        Assert.Equal(10, NotchLayout.For(idle, floating).Top);
        Assert.Equal(0, NotchLayout.For(idle, floating with { AutoHide = true }).Top);
        Assert.Equal(10, NotchLayout.For(hover, floating with { AutoHide = true }).Top);
        Assert.Equal(0, NotchLayout.For(idle, new LayoutOptions { TopOffset = 10 }).Top);
    }
}
