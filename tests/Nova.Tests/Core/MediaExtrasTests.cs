using System.Net.Http;
using Nova.Core.Animation;
using Nova.Core.Events;
using Nova.Core.Media;
using Nova.Core.Notifications;
using Nova.Core.Settings;
using Nova.Core.State;

namespace Nova.Tests.Core;

public class MediaExtrasTests
{
    private static byte[] Pixels(params (byte R, byte G, byte B, int Count)[] runs)
    {
        var list = new List<byte>();
        foreach (var (r, g, b, count) in runs)
            for (var i = 0; i < count; i++) list.AddRange(new byte[] { b, g, r, 255 });
        return list.ToArray();
    }

    [Fact]
    public void Palette_PicksDistinctHues_AndIgnoresGreyBackground()
    {
        var art = Pixels((128, 128, 128, 500), (230, 30, 30, 60), (30, 60, 230, 40), (240, 200, 20, 30));
        var colors = ArtworkPalette.Extract(art, 3);
        Assert.Equal(3, colors.Count);
        var hues = colors.Select(c => ArtworkPalette.ToHsv(c.R, c.G, c.B).H).ToList();
        Assert.Contains(hues, h => h < 15 || h > 345); // red
        Assert.Contains(hues, h => h is > 210 and < 250); // blue
        Assert.Contains(hues, h => h is > 40 and < 60); // yellow
    }

    [Fact]
    public void Palette_FillsUpSingleHueArt_AndHandlesGreyscale()
    {
        Assert.Equal(3, ArtworkPalette.Extract(Pixels((200, 40, 120, 100)), 3).Count);
        var grey = ArtworkPalette.Extract(Pixels((90, 90, 90, 50), (200, 200, 200, 50)), 3);
        Assert.Equal(3, grey.Count);
        Assert.Empty(ArtworkPalette.Extract(Array.Empty<byte>(), 3));
    }

    [Fact]
    public void Lrc_ParsesStamps_SortsLines_AndFindsTheCurrentLine()
    {
        var lyrics = LrcParser.Parse("[ar: someone]\n[00:12.50] second\n[00:01.00]first\n[01:02.3][01:30.00] chorus\n[00:20.00]")!;
        Assert.NotNull(lyrics);
        Assert.Equal(new[] { "first", "second", "", "chorus", "chorus" }, lyrics.Lines.Select(l => l.Text));
        Assert.Equal(-1, lyrics.IndexAt(TimeSpan.FromSeconds(0.5)));
        Assert.Equal(0, lyrics.IndexAt(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, lyrics.IndexAt(TimeSpan.FromSeconds(12.5)));
        Assert.Equal(3, lyrics.IndexAt(TimeSpan.FromSeconds(62.4)));
        Assert.Equal(4, lyrics.IndexAt(TimeSpan.FromMinutes(5)));
        Assert.Null(LrcParser.Parse("just plain text"));
    }

    [Theory]
    [InlineData("Song (Official Video)", "Artist", "Song", "Artist")]
    [InlineData("Artist - Song [Official Audio]", "ArtistVEVO", "Song", "Artist")]
    [InlineData("Artist - Song", "", "Song", "Artist")]
    [InlineData("Song", "Artist - Topic", "Song", "Artist")]
    [InlineData("Intro - Live", "Band", "Intro - Live", "Band")]
    [InlineData("Play with Fire (feat. Yacht Money)", "Sam Tinnesz", "Play with Fire", "Sam Tinnesz")]
    [InlineData("Song ft. Someone", "Artist", "Song", "Artist")]
    public void TrackNames_AreCleanedForLookup(string title, string artist, string expectedTitle, string expectedArtist)
    {
        var (t, a) = TrackNameCleaner.Clean(title, artist);
        Assert.Equal(expectedTitle, t);
        Assert.Equal(expectedArtist, a);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public async Task Lyrics_FoundViaSearch_AreCached()
    {
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.EndsWith("/get")
            ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""[{"duration":181,"syncedLyrics":"[00:01.00] hi"}]""") });
        using var service = new LyricsService(handler);
        var (lyrics, failed) = await service.GetAsync("Song (Official Video)", "Artist", "", TimeSpan.FromSeconds(180));
        Assert.False(failed);
        Assert.Equal("hi", lyrics!.Lines[0].Text);
        var calls = handler.Calls;
        await service.GetAsync("Song", "Artist", "", TimeSpan.FromSeconds(180));
        Assert.Equal(calls, handler.Calls); // cached under the cleaned name
    }

    [Fact]
    public async Task Lyrics_ServerTrouble_IsReportedAsFailed_AndNotHammered()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("down"));
        using var service = new LyricsService(handler);
        var first = await service.GetAsync("Song", "Artist", "", TimeSpan.FromSeconds(180));
        Assert.True(first.Failed);
        Assert.Null(first.Lyrics);
        var calls = handler.Calls;
        var second = await service.GetAsync("Song", "Artist", "", TimeSpan.FromSeconds(180));
        Assert.True(second.Failed);
        Assert.Equal(calls, handler.Calls); // throttled: no new request right away
    }

    [Fact]
    public void Layout_AutoHide_AlsoSlimsTheMusicPill_ButNotHover()
    {
        var o = new LayoutOptions { AutoHide = true };
        var compact = NotchLayout.For(new NotchSnapshot(NotchState.Compact, NotchTool.None, null, true, false, HiddenReason.None), o);
        var hover = NotchLayout.For(new NotchSnapshot(NotchState.Hover, NotchTool.None, null, true, false, HiddenReason.None), o);
        Assert.True(compact.Height < 8);
        Assert.True(hover.Height > 30);
    }

    [Theory]
    [InlineData(AuroraMode.ThemeDefault, AnimationTheme.Liquid, false)]
    [InlineData(AuroraMode.ThemeDefault, AnimationTheme.Aurora, true)]
    [InlineData(AuroraMode.On, AnimationTheme.Liquid, true)]
    [InlineData(AuroraMode.Off, AnimationTheme.Aurora, false)]
    public void AuroraSetting_OverridesTheTheme(AuroraMode mode, AnimationTheme theme, bool expected)
    {
        var profile = AnimationProfile.Build(AnimationThemes.Get(theme), new AnimationSettings { Aurora = mode });
        Assert.Equal(expected, profile.AmbientLight);
    }

    [Fact]
    public void AppNotifications_RouteToMessageOrCall_AndRespectSettings()
    {
        var settings = new AppSettings();
        var message = NotificationRouter.Route(new AppNotificationEvent(7, "WhatsApp", "id", "Sara", "See you at 8", false, null), settings)!;
        Assert.Equal(NotificationStyle.App, message.Style);
        Assert.Equal("See you at 8", message.Subtitle);
        Assert.Equal("app:7", message.CoalesceKey);

        var call = NotificationRouter.Route(new AppNotificationEvent(8, "WhatsApp", "id", "Sara", "Incoming voice call", true, null), settings)!;
        Assert.Equal(NotificationStyle.Call, call.Style);
        Assert.Equal(NotificationPriority.High, call.Priority);

        settings.Notifications.AppNotificationText = false;
        Assert.Equal("WhatsApp", NotificationRouter.Route(new AppNotificationEvent(9, "WhatsApp", "id", "Sara", "secret", false, null), settings)!.Subtitle);
        settings.Notifications.AppNotifications = false;
        Assert.Null(NotificationRouter.Route(new AppNotificationEvent(10, "WhatsApp", "id", "Sara", "x", false, null), settings));
    }

    [Fact]
    public void Dismiss_RemovesTheShowingNotification_AndQueuedOnes()
    {
        var sm = new NotchStateMachine(new FakeTime());
        sm.Notify(new NotchNotification { Title = "call", CoalesceKey = "app:1", Priority = NotificationPriority.High, Duration = TimeSpan.FromSeconds(45) });
        sm.Notify(new NotchNotification { Title = "msg", CoalesceKey = "app:2" });
        Assert.Equal("call", sm.Snapshot.Notification?.Title);

        sm.Dismiss("app:2");
        sm.Dismiss("app:1");
        Assert.Equal(NotchState.Idle, sm.State);
    }
}
