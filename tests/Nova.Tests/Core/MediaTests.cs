using Nova.Core.Events;
using Nova.Core.Media;
using Nova.Core.Notifications;
using Nova.Core.Settings;
using static Nova.Tests.FakeMediaProvider;

namespace Nova.Tests.Core;

public class MediaTests
{
    private readonly FakeTime _time = new();
    private readonly EventBus _bus = new();
    private readonly List<MediaEvent> _events = new();
    private readonly FakeMediaProvider _spotify = new(MediaProviderKind.Spotify);
    private readonly FakeMediaProvider _chrome = new(MediaProviderKind.Chrome);
    private readonly MediaManager _manager;

    public MediaTests()
    {
        _bus.Subscribe<MediaEvent>(e => _events.Add(e));
        _manager = new MediaManager(new IMediaProvider[] { _spotify, _chrome }, _bus, _time);
    }

    [Fact]
    public void StartingPlayback_RaisesStarted_AndBecomesCurrent()
    {
        _spotify.Set(Session("Spotify.exe", "Blinding Lights", PlaybackState.Playing));
        Assert.Equal("Blinding Lights", _manager.Current!.Title);
        var e = Assert.Single(_events);
        Assert.Equal(MediaEventKind.Started, e.Kind);
        Assert.True(_manager.HasActiveSession);
    }

    [Fact]
    public void TrackChange_WhilePlaying_RaisesTrackChanged()
    {
        _spotify.Set(Session("Spotify.exe", "Song A", PlaybackState.Playing));
        _spotify.Set(Session("Spotify.exe", "Song B", PlaybackState.Playing));
        Assert.Equal(new[] { MediaEventKind.Started, MediaEventKind.TrackChanged }, _events.Select(e => e.Kind));
    }

    [Fact]
    public void MetadataOnlyUpdates_DoNotRaiseDuplicateEvents()
    {
        var s = Session("Spotify.exe", "Song A", PlaybackState.Playing);
        _spotify.Set(s);
        _spotify.Set(s with { Position = TimeSpan.FromSeconds(30) });
        _spotify.Set(s with { ArtworkKey = "new-art" });
        Assert.Single(_events);
    }

    [Fact]
    public void Pausing_RaisesPaused_AndLingersThenExpires()
    {
        _spotify.Set(Session("Spotify.exe", "Song A", PlaybackState.Playing));
        _spotify.Set(Session("Spotify.exe", "Song A", PlaybackState.Paused));
        Assert.Equal(MediaEventKind.Paused, _events.Last().Kind);
        Assert.True(_manager.HasActiveSession);
        _time.Advance(TimeSpan.FromMinutes(3));
        Assert.False(_manager.HasActiveSession);
        Assert.NotNull(_manager.Current); // still shown in the full player
    }

    [Fact]
    public void SessionWithoutTitle_DoesNotAnnounceUntilTitleArrives()
    {
        _chrome.Set(Session("Chrome", "", PlaybackState.Playing, MediaProviderKind.Chrome));
        Assert.Empty(_events);
        _chrome.Set(Session("Chrome", "Video title", PlaybackState.Playing, MediaProviderKind.Chrome));
        Assert.Equal(MediaEventKind.Started, Assert.Single(_events).Kind);
    }

    [Fact]
    public void PlayingSession_BeatsPausedOne_AndCurrentIsSticky()
    {
        _spotify.Set(Session("Spotify.exe", "Paused song", PlaybackState.Paused));
        _chrome.Set(Session("Chrome", "YouTube", PlaybackState.Playing, MediaProviderKind.Chrome));
        Assert.Equal("Chrome", _manager.Current!.Key);

        // Both playing: keep the one already shown instead of flickering.
        _spotify.Set(Session("Spotify.exe", "Now playing too", PlaybackState.Playing, played: DateTimeOffset.Now.AddSeconds(5)));
        Assert.Equal("Chrome", _manager.Current!.Key);
    }

    [Fact]
    public void DisabledProvider_IsIgnored()
    {
        _spotify.IsEnabled = false;
        _spotify.Set(Session("Spotify.exe", "Hidden", PlaybackState.Playing));
        Assert.Null(_manager.Current);
        Assert.Empty(_events);
    }

    [Fact]
    public void ManagerDisabled_ClearsCurrent()
    {
        _spotify.Set(Session("Spotify.exe", "Song", PlaybackState.Playing));
        _manager.Enabled = false;
        _manager.Recompute();
        Assert.Null(_manager.Current);
    }

    [Fact]
    public async Task FailingProvider_NeverBreaksTheManager()
    {
        _chrome.ThrowOnStart = true;
        _chrome.ThrowOnSessions = true;
        await _manager.StartAsync();
        _spotify.Set(Session("Spotify.exe", "Still works", PlaybackState.Playing));
        Assert.Equal("Still works", _manager.Current!.Title);
    }

    [Fact]
    public async Task Commands_GoToTheCurrentSessionsProvider()
    {
        _chrome.Set(Session("Chrome", "Video", PlaybackState.Playing, MediaProviderKind.Chrome));
        await _manager.SendAsync(MediaCommand.PlayPause);
        await _manager.SendAsync(MediaCommand.Next);
        Assert.Equal(new[] { MediaCommand.PlayPause, MediaCommand.Next }, _chrome.Commands.Select(c => c.Command));
        Assert.Empty(_spotify.Commands);
    }

    [Fact]
    public async Task Commands_WithoutSession_ReturnFalse()
    {
        Assert.False(await _manager.SendAsync(MediaCommand.PlayPause));
    }

    [Fact]
    public void EstimatedPosition_InterpolatesWhilePlaying_AndClamps()
    {
        var stamp = DateTimeOffset.Now;
        var s = Session("x", "t", PlaybackState.Playing) with { Position = TimeSpan.FromSeconds(10), Duration = TimeSpan.FromSeconds(60), PositionTimestamp = stamp };
        Assert.Equal(15, s.EstimatedPosition(stamp.AddSeconds(5)).TotalSeconds, 1);
        Assert.Equal(60, s.EstimatedPosition(stamp.AddMinutes(5)).TotalSeconds, 1);
        var paused = s with { State = PlaybackState.Paused };
        Assert.Equal(10, paused.EstimatedPosition(stamp.AddSeconds(30)).TotalSeconds, 1);
    }

    [Fact]
    public void Router_MapsMediaEvents_ToMediaNotifications()
    {
        var settings = new AppSettings();
        var session = Session("Spotify.exe", "Blinding Lights", PlaybackState.Playing) with { Artist = "The Weeknd" };
        var n = NotificationRouter.Route(new MediaEvent(MediaEventKind.Started, session), settings);
        Assert.NotNull(n);
        Assert.Equal(NotificationStyle.Media, n!.Style);
        Assert.Equal("Blinding Lights", n.Title);
        Assert.Equal("The Weeknd", n.Subtitle);

        settings.Media.AutoExpand = false;
        Assert.Null(NotificationRouter.Route(new MediaEvent(MediaEventKind.Started, session), settings));
        Assert.Null(NotificationRouter.Route(new MediaEvent(MediaEventKind.Paused, session), new AppSettings())); // paused is off by default
    }

    [Fact]
    public void Router_RespectsPerEventToggles_AndMasterSwitch()
    {
        var settings = new AppSettings();
        Assert.NotNull(NotificationRouter.Route(new VolumeEvent(0.5, false), settings));
        Assert.Null(NotificationRouter.Route(new ClipboardEvent("x"), settings)); // off by default
        settings.Notifications.VolumeChanged = false;
        Assert.Null(NotificationRouter.Route(new VolumeEvent(0.5, false), settings));
        settings.Notifications.Enabled = false;
        Assert.Null(NotificationRouter.Route(new TimerCompletedEvent("t", TimeSpan.FromMinutes(1)), settings));
    }

    [Fact]
    public void Router_VolumeIndicator_CoalescesAndIsNotQueued()
    {
        var n = NotificationRouter.Route(new VolumeEvent(0.62, false), new AppSettings())!;
        Assert.Equal(NotificationStyle.Indicator, n.Style);
        Assert.Equal(0.62, n.Level);
        Assert.Equal("volume", n.CoalesceKey);
        Assert.False(n.Queueable);
        var muted = NotificationRouter.Route(new VolumeEvent(0.62, true), new AppSettings())!;
        Assert.Equal(0, muted.Level);
    }

    [Fact]
    public void Router_TimerCompletion_IsHighPriority()
    {
        var n = NotificationRouter.Route(new TimerCompletedEvent("Tea", TimeSpan.FromMinutes(3)), new AppSettings())!;
        Assert.Equal(NotificationPriority.High, n.Priority);
    }

    [Fact]
    public void EventBus_IsolatesFailingHandlers_AndSupportsUnsubscribe()
    {
        var bus = new EventBus();
        var received = 0;
        bus.Subscribe<CustomEvent>(_ => throw new Exception("bad handler"));
        var sub = bus.Subscribe<CustomEvent>(_ => received++);
        var all = 0;
        bus.SubscribeAll(_ => all++);
        bus.Publish(new CustomEvent("a", null));
        sub.Dispose();
        bus.Publish(new CustomEvent("b", null));
        Assert.Equal(1, received);
        Assert.Equal(2, all);
    }
}
