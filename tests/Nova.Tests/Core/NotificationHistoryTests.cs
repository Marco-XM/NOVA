using Nova.Core.Events;
using Nova.Core.Notifications;
using Nova.Core.State;

namespace Nova.Tests.Core;

public class NotificationHistoryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static AppNotificationEvent Message(uint id, string title = "Sara", string body = "hi") =>
        new(id, "WhatsApp", "app.whatsapp", title, body, IsCall: false, Logo: null);

    [Fact]
    public void NewNotifications_AreUnread_NewestFirst()
    {
        var history = new NotificationHistory();
        history.Add(Message(1), Noon);
        history.Add(Message(2), Noon.AddMinutes(1));

        Assert.Equal(new uint[] { 2, 1 }, history.Items.Select(i => i.Id));
        Assert.Equal(2, history.UnreadCount);
    }

    [Fact]
    public void RepostedId_ReplacesTheOldEntry()
    {
        var history = new NotificationHistory();
        history.Add(Message(1, body: "first"), Noon);
        history.MarkAllRead();
        history.Add(Message(1, body: "edited"), Noon.AddMinutes(1));

        var entry = Assert.Single(history.Items);
        Assert.Equal("edited", entry.Source.Body);
        Assert.False(entry.IsRead);
    }

    [Fact]
    public void MarkRead_And_Dismiss()
    {
        var history = new NotificationHistory();
        history.Add(Message(1), Noon);
        history.Add(Message(2), Noon);

        history.MarkRead(1);
        Assert.Equal(1, history.UnreadCount);

        Assert.True(history.Remove(2));
        Assert.False(history.Remove(2));
        Assert.Equal(0, history.UnreadCount);
        Assert.Single(history.Items);

        history.Clear();
        Assert.Empty(history.Items);
    }

    [Fact]
    public void Changed_FiresOnlyWhenSomethingChanged()
    {
        var history = new NotificationHistory();
        var changes = 0;
        history.Changed += () => changes++;

        history.MarkAllRead();
        history.Clear();
        history.MarkRead(7);
        Assert.Equal(0, changes);

        history.Add(Message(1), Noon);
        history.MarkAllRead();
        history.MarkAllRead();
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Capacity_DropsTheOldest()
    {
        var history = new NotificationHistory { Capacity = 3 };
        for (uint i = 1; i <= 5; i++) history.Add(Message(i), Noon);
        Assert.Equal(new uint[] { 5, 4, 3 }, history.Items.Select(i => i.Id));
    }

    [Theory]
    [InlineData(0, "now")]
    [InlineData(5, "5m")]
    [InlineData(150, "2h")]
    [InlineData(13 * 60, "Yesterday")]
    [InlineData(4 * 24 * 60, "Sep 30")]
    public void AgeText(int minutesAgo, string expected)
    {
        var entry = new NotificationHistoryEntry(Message(1), Noon.AddMinutes(-minutesAgo), false);
        Assert.Equal(expected, entry.AgeText(Noon));
    }

    [Fact]
    public void Layout_HoverWithMedia_HasRoomForTheControls_AndHistoryFits()
    {
        var o = new LayoutOptions();
        var hover = NotchLayout.For(new NotchSnapshot(NotchState.Hover, NotchTool.None, null, true, false, HiddenReason.None), o);
        var history = NotchLayout.For(new NotchSnapshot(NotchState.Tool, NotchTool.Notifications, null, false, false, HiddenReason.None), o);
        Assert.True(hover.Width >= 440);
        Assert.True(history.Width <= NotchLayout.MaxWidth && history.Height <= NotchLayout.MaxHeight);
    }
}
