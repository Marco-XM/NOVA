using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.App.Services;
using Nova.Core.Events;
using Nova.Core.Media;
using Nova.Core.Notifications;
using Nova.Core.QuickApps;
using Nova.Core.Settings;
using Nova.Core.State;
using Nova.Core.Tools;
using Nova.Platform.QuickApps;
using Nova.Platform.Services;

namespace Nova.App.Notch;

public sealed partial class QuickAppItem : ObservableObject
{
    public QuickAppItem(QuickApp app) => App = app;
    public QuickApp App { get; }
    public string Name => App.Name;
    [ObservableProperty] private ImageSource? _icon;
}

/// <summary>One row of the notification history.</summary>
public sealed class NotificationHistoryItem
{
    public NotificationHistoryItem(NotificationHistoryEntry entry, ImageSource? icon, bool showText, DateTimeOffset now)
    {
        Entry = entry;
        var n = entry.Source;
        AppName = n.AppName;
        Title = n.Title.Length > 0 ? n.Title : n.AppName;
        Body = n.IsCall ? (n.Body ?? "Call") : showText ? n.Body : null;
        Icon = icon;
        AgeText = entry.AgeText(now);
    }

    public NotificationHistoryEntry Entry { get; }
    public string AppName { get; }
    public string Title { get; }
    public string? Body { get; }
    public bool HasBody => !string.IsNullOrWhiteSpace(Body);
    public bool IsCall => Entry.Source.IsCall;
    public bool IsUnread => !Entry.IsRead;
    public ImageSource? Icon { get; }
    public bool HasIcon => Icon != null;
    public string AgeText { get; }
}

/// <summary>Sentinel item rendered as the "+ Add" tile at the end of the quick app grid.</summary>
public sealed class AddAppTile
{
    public static readonly AddAppTile Instance = new();
}

/// <summary>
/// Everything the notch views display. The controller pushes state in; the views bind to it and call
/// its commands. Timers here only run while their data is actually visible.
/// </summary>
public sealed partial class NotchViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly ArtworkCache _artwork = new();
    private readonly DispatcherTimer _progressTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _timerTicker;
    private readonly DispatcherTimer _timerCompletion;
    private MediaSessionInfo? _session;
    private bool _updatingVolume;
    private bool _progressVisible;
    private bool _clockVisible;
    private bool _timerVisible;
    private DateTime _lastVolumeWrite;

    public NotchViewModel(AppServices services)
    {
        _s = services;
        _progressTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _progressTimer.Tick += (_, _) => UpdateProgress();
        _clockTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(15) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _timerTicker = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _timerTicker.Tick += (_, _) => UpdateTimer();
        _timerCompletion = new DispatcherTimer(DispatcherPriority.Normal);
        _timerCompletion.Tick += (_, _) =>
        {
            _timerCompletion.Stop();
            _s.Timer.CheckCompleted();
            ScheduleTimerCompletion();
        };
        _s.Timer.StateChanged += _ => { UpdateTimer(); ScheduleTimerCompletion(); RefreshTimerTicker(); };
        _s.Clipboard.Changed += () => Dispatcher.CurrentDispatcher.BeginInvoke(SyncClipboard);
        _history.Changed += SyncHistory;
        UpdateClock();
        UpdateTimer();
        ApplySettings(_s.Settings.Current);
    }

    // ───────────────────────── media ─────────────────────────
    [ObservableProperty] private bool _hasMedia;
    [ObservableProperty] private string _mediaTitle = "";
    [ObservableProperty] private string _mediaArtist = "";
    [ObservableProperty] private string _mediaSource = "";
    [ObservableProperty] private ImageSource? _artworkImage;
    [ObservableProperty] private bool _hasArtwork;
    [ObservableProperty] private Brush _mediaAccent = Brushes.White;
    [ObservableProperty] private Color _mediaAccentColor = Color.FromRgb(139, 156, 255);
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _canNext;
    [ObservableProperty] private bool _canPrevious;
    [ObservableProperty] private bool _canPlayPause;
    [ObservableProperty] private bool _canSeek;
    [ObservableProperty] private bool _hasTimeline;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";
    [ObservableProperty] private double _sessionVolume;
    [ObservableProperty] private bool _hasSessionVolume;
    [ObservableProperty] private bool _animatedEqualizer = true;
    [ObservableProperty] private bool _canShuffle;
    [ObservableProperty] private bool _isShuffleActive;
    [ObservableProperty] private bool _canRepeat;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RepeatGlyph), nameof(IsRepeatActive))] private MediaRepeatMode _repeatMode;
    [ObservableProperty] private bool _hasLyrics;
    [ObservableProperty] private string _lyricLine = "";
    [ObservableProperty] private string _nextLyricLine = "";

    /// <summary>Palette of the current cover art (for the Aurora light), or null without artwork.</summary>
    public IReadOnlyList<Color>? ArtworkPalette { get; private set; }

    public string RepeatGlyph => RepeatMode == MediaRepeatMode.Track ? "" : "";
    public bool IsRepeatActive => RepeatMode != MediaRepeatMode.None;

    public string PlayPauseGlyph => IsPlaying ? "" : "";
    public string MediaSourceGlyph => _session?.Provider switch
    {
        MediaProviderKind.Spotify => "",
        MediaProviderKind.Chrome => "",
        _ => "",
    };

    /// <summary>Follows the Reduce Motion setting (set by the controller).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DiscSpinning))] private bool _reduceMotion;

    /// <summary>The artwork disc turns while music plays, unless motion is reduced.</summary>
    public bool DiscSpinning => IsPlaying && !ReduceMotion;

    partial void OnIsPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayPauseGlyph));
        OnPropertyChanged(nameof(DiscSpinning));
        RefreshProgressTimer();
    }

    public void SetMedia(MediaSessionInfo? session)
    {
        _session = session;
        HasMedia = session != null;
        if (session is null)
        {
            IsPlaying = false;
            ArtworkPalette = null;
            ClearLyrics();
            _lyricsTrack = null;
            RefreshProgressTimer();
            return;
        }
        MediaTitle = string.IsNullOrWhiteSpace(session.Title) ? session.SourceName : session.Title;
        MediaArtist = string.IsNullOrWhiteSpace(session.Artist) ? session.SourceName : session.Artist;
        MediaSource = session.SourceName;
        OnPropertyChanged(nameof(MediaSourceGlyph));
        IsPlaying = session.IsPlaying;
        CanNext = session.CanGoNext;
        CanPrevious = session.CanGoPrevious;
        CanPlayPause = session.CanPlayPause;
        CanSeek = session.CanSeek;
        CanShuffle = session.CanShuffle;
        IsShuffleActive = session.IsShuffleActive;
        CanRepeat = session.CanRepeat;
        RepeatMode = session.RepeatMode;
        HasTimeline = session.Duration > TimeSpan.FromSeconds(1);

        var art = _artwork.Get(session.ArtworkKey, session.Artwork);
        ArtworkImage = art?.Image;
        HasArtwork = art != null;
        ArtworkPalette = art?.Palette is { Count: > 0 } palette ? palette : null;
        RefreshLyrics(session);
        var accent = art?.Dominant ?? Color.FromRgb(139, 156, 255);
        MediaAccentColor = accent;
        var brush = new SolidColorBrush(accent);
        brush.Freeze();
        MediaAccent = brush;
        UpdateProgress();
        RefreshSessionVolume();
    }

    /// <summary>Artwork for an arbitrary session (used by media notifications).</summary>
    public ImageSource? ArtworkFor(MediaSessionInfo? session) => session is null ? null : _artwork.Get(session.ArtworkKey, session.Artwork)?.Image;

    public void SetProgressVisible(bool visible)
    {
        _progressVisible = visible;
        if (visible) { UpdateProgress(); RefreshSessionVolume(); }
        RefreshProgressTimer();
    }

    // ───────────────────────── lyrics ─────────────────────────
    private string? _lyricsTrack;
    private Lyrics? _lyrics;
    private CancellationTokenSource? _lyricsCts;

    /// <summary>Looks up synced lyrics once per track (in the background) when lyrics are enabled.</summary>
    private void RefreshLyrics(MediaSessionInfo session)
    {
        if (!_s.Settings.Current.Media.ShowLyrics || string.IsNullOrWhiteSpace(session.Title))
        {
            ClearLyrics();
            _lyricsTrack = null;
            return;
        }
        var key = session.TrackKey;
        if (key == _lyricsTrack) return;
        _lyricsTrack = key;
        ClearLyrics();

        _lyricsCts?.Cancel();
        var cts = _lyricsCts = new CancellationTokenSource();
        var dispatcher = Dispatcher.CurrentDispatcher;
        var (title, artist, album, duration) = (session.Title, session.Artist, session.Album, session.Duration);
        _ = Task.Run(async () =>
        {
            var (lyrics, failed) = await _s.Lyrics.GetAsync(title, artist, album, duration, cts.Token).ConfigureAwait(false);
            if (cts.IsCancellationRequested) return;
            await dispatcher.InvokeAsync(() =>
            {
                if (_lyricsTrack != key) return;
                // Server unreachable: forget the track so a later update retries (the service throttles retries).
                if (failed) _lyricsTrack = null;
                _lyrics = lyrics;
                HasLyrics = lyrics != null;
                UpdateLyricLine(_session?.EstimatedPosition(DateTimeOffset.Now) ?? TimeSpan.Zero);
                RefreshProgressTimer();
            });
        });
    }

    private void ClearLyrics()
    {
        _lyricsCts?.Cancel();
        _lyrics = null;
        HasLyrics = false;
        LyricLine = "";
        NextLyricLine = "";
    }

    private void UpdateLyricLine(TimeSpan position)
    {
        if (_lyrics is not { } lyrics) return;
        var index = lyrics.IndexAt(position);
        static string Show(string text) => string.IsNullOrWhiteSpace(text) ? "♪" : text;
        LyricLine = index >= 0 ? Show(lyrics.Lines[index].Text) : "♪";
        NextLyricLine = index + 1 < lyrics.Lines.Count ? lyrics.Lines[index + 1].Text : "";
    }

    private void RefreshProgressTimer()
    {
        // Lyrics need a finer clock than the progress bar to switch lines on time.
        _progressTimer.Interval = TimeSpan.FromMilliseconds(HasLyrics ? 200 : 500);
        var run = _progressVisible && IsPlaying && HasTimeline;
        if (run && !_progressTimer.IsEnabled) _progressTimer.Start();
        else if (!run && _progressTimer.IsEnabled) _progressTimer.Stop();
    }

    private void UpdateProgress()
    {
        var s = _session;
        if (s is null || s.Duration <= TimeSpan.Zero)
        {
            Progress = 0;
            return;
        }
        var position = s.EstimatedPosition(DateTimeOffset.Now);
        UpdateLyricLine(position);
        Progress = Math.Clamp(position.TotalMilliseconds / s.Duration.TotalMilliseconds, 0, 1);
        PositionText = Format(position);
        DurationText = Format(s.Duration);
    }

    private static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    private void RefreshSessionVolume()
    {
        var volume = _s.Media.GetVolume();
        _updatingVolume = true;
        HasSessionVolume = volume.HasValue;
        if (volume.HasValue) SessionVolume = volume.Value;
        _updatingVolume = false;
    }

    partial void OnSessionVolumeChanged(double value)
    {
        if (_updatingVolume) return;
        // Throttle writes while dragging; the last value always lands.
        if ((DateTime.UtcNow - _lastVolumeWrite).TotalMilliseconds < 40) return;
        _lastVolumeWrite = DateTime.UtcNow;
        _s.Media.SetVolume(value);
    }

    public void CommitSessionVolume() => _s.Media.SetVolume(SessionVolume);

    [RelayCommand] private Task PlayPause() => _s.Media.SendAsync(MediaCommand.PlayPause);
    [RelayCommand] private Task Next() => _s.Media.SendAsync(MediaCommand.Next);
    [RelayCommand] private Task Previous() => _s.Media.SendAsync(MediaCommand.Previous);

    [RelayCommand]
    private Task ToggleShuffle()
    {
        IsShuffleActive = !IsShuffleActive; // optimistic; the session confirms a moment later
        return _s.Media.SendAsync(MediaCommand.ToggleShuffle);
    }

    [RelayCommand] private Task CycleRepeat() => _s.Media.SendAsync(MediaCommand.CycleRepeat);

    public async void SeekTo(double fraction)
    {
        var s = _session;
        if (s is null || !s.CanSeek) return;
        var target = TimeSpan.FromMilliseconds(s.Duration.TotalMilliseconds * Math.Clamp(fraction, 0, 1));
        Progress = fraction;
        await _s.Media.SeekAsync(target);
    }

    public void AdjustSessionVolume(double delta)
    {
        if (!HasSessionVolume) return;
        SessionVolume = Math.Clamp(SessionVolume + delta, 0, 1);
        CommitSessionVolume();
    }

    // ───────────────────────── quick apps ─────────────────────────
    public ObservableCollection<QuickAppItem> QuickApps { get; } = new();
    public ObservableCollection<QuickAppItem> HomeQuickApps { get; } = new();
    public ObservableCollection<object> QuickAppTiles { get; } = new();
    [ObservableProperty] private bool _hasQuickApps;
    [ObservableProperty] private string? _launchError;

    public void SetQuickApps(IReadOnlyList<QuickApp> apps)
    {
        QuickApps.Clear();
        HomeQuickApps.Clear();
        QuickAppTiles.Clear();
        foreach (var app in apps)
        {
            var item = new QuickAppItem(app) { Icon = ShellIcons.ForQuickApp(app, 64) };
            QuickApps.Add(item);
            QuickAppTiles.Add(item);
            if (HomeQuickApps.Count < NotchLayout.QuickAppsPerRow) HomeQuickApps.Add(item);
        }
        QuickAppTiles.Add(AddAppTile.Instance);
        HasQuickApps = QuickApps.Count > 0;
    }

    [RelayCommand]
    private void LaunchApp(QuickAppItem? item)
    {
        if (item is null) return;
        var result = _s.Launcher.Launch(item.App);
        if (!result.Success)
        {
            LaunchError = result.Error;
            _s.StateMachine.Notify(new NotchNotification { Title = "Couldn't open " + item.Name, Subtitle = result.Error, Glyph = "", Priority = NotificationPriority.High });
            return;
        }
        _s.StateMachine.Collapse();
    }

    [RelayCommand] private void AddApp() { _s.StateMachine.Collapse(); _s.OpenSettings("quickapps"); }

    // ───────────────────────── navigation ─────────────────────────
    [RelayCommand] private void OpenMedia() => _s.StateMachine.Navigate(NotchState.Media);
    [RelayCommand] private void OpenHome() => _s.StateMachine.Navigate(NotchState.Expanded);
    [RelayCommand] private void OpenQuickApps() => _s.StateMachine.Navigate(NotchState.QuickApps);
    [RelayCommand] private void OpenTool(string? tool)
    {
        if (Enum.TryParse<NotchTool>(tool, true, out var t)) _s.StateMachine.Navigate(NotchState.Tool, t);
    }
    [RelayCommand] private void OpenSettings() { _s.StateMachine.Collapse(); _s.OpenSettings(null); }
    [RelayCommand] private void Collapse() => _s.StateMachine.Collapse();

    // ───────────────────────── modules / status ─────────────────────────
    [ObservableProperty] private bool _showTimerTool = true;
    [ObservableProperty] private bool _showCalculatorTool = true;
    [ObservableProperty] private bool _showClipboardTool = true;
    [ObservableProperty] private bool _showSearchTool = true;
    [ObservableProperty] private bool _hasTools = true;
    [ObservableProperty] private bool _showWordmark = true;
    [ObservableProperty] private bool _showClockIdle;

    [ObservableProperty] private string _timeText = "";
    [ObservableProperty] private string _dateText = "";

    [ObservableProperty] private bool _hasBattery;
    [ObservableProperty] private string _batteryText = "";
    [ObservableProperty] private string _batteryGlyph = "";
    [ObservableProperty] private bool _showNetwork;
    [ObservableProperty] private string _networkGlyph = "";
    [ObservableProperty] private string _networkText = "";
    [ObservableProperty] private bool _showVolume;
    [ObservableProperty] private string _volumeGlyph = "";
    [ObservableProperty] private string _volumeText = "";

    public void ApplySettings(AppSettings settings)
    {
        var m = settings.Modules;
        ShowTimerTool = m.Timer;
        ShowCalculatorTool = m.Calculator;
        ShowClipboardTool = m.Clipboard;
        ShowSearchTool = m.Search;
        HasTools = m.Timer || m.Calculator || m.Clipboard || m.Search;
        ShowWordmark = settings.Appearance.IdleContent == IdleContent.Wordmark;
        ShowClockIdle = settings.Appearance.IdleContent == IdleContent.Clock;
        AnimatedEqualizer = settings.Media.AnimatedEqualizer;
        if (!settings.Media.ShowLyrics) { ClearLyrics(); _lyricsTrack = null; }
        else if (_session != null && _lyricsTrack is null) RefreshLyrics(_session);
        _s.Clipboard.Capacity = m.ClipboardHistorySize;
        RefreshClockTimer();
    }

    public void SetClockVisible(bool visible)
    {
        _clockVisible = visible;
        if (visible) UpdateClock();
        RefreshClockTimer();
    }

    private void RefreshClockTimer()
    {
        var run = _clockVisible || ShowClockIdle;
        if (run && !_clockTimer.IsEnabled) { UpdateClock(); _clockTimer.Start(); }
        else if (!run && _clockTimer.IsEnabled) _clockTimer.Stop();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        TimeText = now.ToString("t");
        DateText = now.ToString("dddd, MMMM d");
    }

    public void UpdateStatus(PowerWatcher power, NetworkWatcher network, VolumeWatcher volume, ModuleSettings modules)
    {
        HasBattery = modules.Battery && power.HasBattery;
        if (HasBattery)
        {
            BatteryText = $"{power.Percent}%";
            BatteryGlyph = power.Charging ? "" : power.Percent switch
            {
                <= 10 => "", <= 30 => "", <= 50 => "", <= 70 => "", <= 90 => "", _ => "",
            };
        }
        ShowNetwork = modules.Network;
        NetworkGlyph = !network.Connected ? "" : network.IsWireless ? network.SignalBars switch { >= 4 => "", 3 => "", 2 => "", _ => "" } : "";
        NetworkText = network.Connected ? (network.NetworkName ?? "Connected") : "Offline";
        ShowVolume = volume.IsAvailable;
        VolumeGlyph = volume.Muted || volume.Level < 0.005 ? "" : volume.Level < 0.34 ? "" : volume.Level < 0.67 ? "" : "";
        VolumeText = volume.Muted ? "Muted" : $"{Math.Round(volume.Level * 100)}%";
    }

    // ───────────────────────── notification ─────────────────────────
    [ObservableProperty] private string _notifTitle = "";
    [ObservableProperty] private string? _notifSubtitle;
    [ObservableProperty] private string _notifGlyph = "";
    [ObservableProperty] private double _notifLevel;
    [ObservableProperty] private string _notifLevelText = "";
    [ObservableProperty] private bool _notifIsStandard = true;
    [ObservableProperty] private bool _notifIsIndicator;
    [ObservableProperty] private bool _notifIsMedia;
    [ObservableProperty] private ImageSource? _notifArtwork;
    [ObservableProperty] private bool _notifHasArtwork;
    [ObservableProperty] private Brush _notifAccent = Brushes.White;
    [ObservableProperty] private bool _notifIsApp;
    [ObservableProperty] private bool _notifIsCall;
    [ObservableProperty] private string _notifAppName = "";
    [ObservableProperty] private ImageSource? _notifIcon;
    [ObservableProperty] private bool _notifHasIcon;
    private readonly ArtworkCache _appLogos = new();
    private AppNotificationEvent? _notifApp;

    public void SetNotification(NotchNotification? n)
    {
        if (n is null) return;
        _notifApp = n.Source as AppNotificationEvent;
        NotifIsApp = n.Style == NotificationStyle.App;
        NotifIsCall = n.Style == NotificationStyle.Call;
        if (_notifApp is { } app)
        {
            NotifAppName = app.AppName;
            NotifIcon = _appLogos.Get(app.AppId ?? app.AppName, app.Logo)?.Image;
            NotifHasIcon = NotifIcon != null;
        }
        NotifTitle = n.Title;
        NotifSubtitle = n.Subtitle;
        NotifGlyph = n.Glyph;
        NotifLevel = n.Level ?? 0;
        NotifLevelText = n.Level.HasValue ? $"{Math.Round(n.Level.Value * 100)}" : "";
        NotifIsIndicator = n.Style == NotificationStyle.Indicator;
        NotifIsMedia = n.Style == NotificationStyle.Media;
        NotifIsStandard = n.Style == NotificationStyle.Standard;
        NotifArtwork = n.Style == NotificationStyle.Media ? ArtworkFor(n.Media) : null;
        NotifHasArtwork = NotifArtwork != null;
        if (n.Media != null)
        {
            var art = _artwork.Get(n.Media.ArtworkKey, n.Media.Artwork);
            var brush = new SolidColorBrush(art?.Dominant ?? Color.FromRgb(139, 156, 255));
            brush.Freeze();
            NotifAccent = brush;
        }
    }

    /// <summary>Brings the app that posted a notification to the front (where it can be answered / replied to).</summary>
    public void OpenNotificationApp(AppNotificationEvent app)
    {
        if (string.IsNullOrEmpty(app.AppId)) return;
        try { using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{app.AppId}") { UseShellExecute = true }); }
        catch (Exception ex) { Nova.Core.Logging.Log.Warn($"Couldn't open {app.AppName}", ex); }
    }

    [RelayCommand]
    private void OpenNotification()
    {
        if (_notifApp is not { } app) return;
        OpenNotificationApp(app);
        _history.Remove(app.Id); // handled in the app
        _s.StateMachine.Dismiss(app.CoalesceKey);
    }

    [RelayCommand]
    private void DismissNotification()
    {
        if (_notifApp is not { } app) return;
        _history.MarkRead(app.Id); // seen; it stays in the history
        _s.StateMachine.Dismiss(app.CoalesceKey);
    }

    // ───────────────────────── notification history ─────────────────────────
    private readonly NotificationHistory _history = new();
    private bool _historyVisible;
    public ObservableCollection<NotificationHistoryItem> HistoryItems { get; } = new();
    [ObservableProperty] private bool _hasHistory;
    [ObservableProperty] private int _unreadCount;
    [ObservableProperty] private bool _hasUnread;
    [ObservableProperty] private string _unreadBadgeText = "";

    /// <summary>Records another app's notification (UI thread).</summary>
    public void AddToHistory(AppNotificationEvent notification) => _history.Add(notification, DateTimeOffset.Now);

    /// <summary>The history view opened or closed. Leaving it marks everything as read.</summary>
    public void SetHistoryVisible(bool visible)
    {
        if (visible == _historyVisible) return;
        _historyVisible = visible;
        if (visible) SyncHistory(); // refresh the ages
        else _history.MarkAllRead();
    }

    private void SyncHistory()
    {
        var now = DateTimeOffset.Now;
        var showText = _s.Settings.Current.Notifications.AppNotificationText;
        HistoryItems.Clear();
        foreach (var entry in _history.Items)
        {
            var n = entry.Source;
            HistoryItems.Add(new NotificationHistoryItem(entry, _appLogos.Get(n.AppId ?? n.AppName, n.Logo)?.Image, showText, now));
        }
        HasHistory = HistoryItems.Count > 0;
        UnreadCount = _history.UnreadCount;
        HasUnread = UnreadCount > 0;
        UnreadBadgeText = UnreadCount > 9 ? "9+" : UnreadCount.ToString();
    }

    [RelayCommand] private void OpenNotifications() => _s.StateMachine.Navigate(NotchState.Tool, NotchTool.Notifications);

    [RelayCommand]
    private void OpenHistoryItem(NotificationHistoryItem? item)
    {
        if (item is null) return;
        OpenNotificationApp(item.Entry.Source);
        _history.Remove(item.Entry.Id);
        _s.StateMachine.Collapse();
    }

    [RelayCommand] private void DismissHistoryItem(NotificationHistoryItem? item) { if (item != null) _history.Remove(item.Entry.Id); }
    [RelayCommand] private void ClearHistory() => _history.Clear();

    // ───────────────────────── timer ─────────────────────────
    [ObservableProperty] private string _timerText = "05:00";
    [ObservableProperty] private double _timerProgress;
    [ObservableProperty] private bool _timerRunning;
    [ObservableProperty] private bool _timerPaused;
    [ObservableProperty] private bool _timerActive;
    [ObservableProperty] private bool _timerIdle = true;
    public string TimerPauseGlyph => TimerRunning ? "" : "";

    public void SetTimerVisible(bool visible)
    {
        _timerVisible = visible;
        RefreshTimerTicker();
        UpdateTimer();
    }

    private void RefreshTimerTicker()
    {
        var run = _timerVisible && _s.Timer.State == CountdownState.Running;
        if (run && !_timerTicker.IsEnabled) _timerTicker.Start();
        else if (!run && _timerTicker.IsEnabled) _timerTicker.Stop();
    }

    private void ScheduleTimerCompletion()
    {
        _timerCompletion.Stop();
        if (_s.Timer.EndsAt is { } end)
        {
            var due = end - DateTimeOffset.UtcNow;
            _timerCompletion.Interval = due < TimeSpan.FromMilliseconds(20) ? TimeSpan.FromMilliseconds(20) : due;
            _timerCompletion.Start();
        }
    }

    private void UpdateTimer()
    {
        var t = _s.Timer;
        var remaining = t.Remaining;
        var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        TimerText = seconds >= 3600 ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}" : $"{seconds / 60:00}:{seconds % 60:00}";
        TimerProgress = t.State == CountdownState.Idle ? 0 : Math.Clamp(t.Progress, 0, 1);
        TimerRunning = t.State == CountdownState.Running;
        TimerPaused = t.State == CountdownState.Paused;
        TimerActive = t.IsActive;
        TimerIdle = !t.IsActive;
        OnPropertyChanged(nameof(TimerPauseGlyph));
    }

    [RelayCommand] private void StartTimer() => _s.Timer.Start();
    [RelayCommand]
    private void PauseResumeTimer()
    {
        if (_s.Timer.State == CountdownState.Running) _s.Timer.Pause();
        else if (_s.Timer.State == CountdownState.Paused) _s.Timer.Resume();
    }
    [RelayCommand] private void CancelTimer() => _s.Timer.Cancel();
    [RelayCommand] private void AddTimerMinutes(string? minutes) => _s.Timer.AddTime(TimeSpan.FromMinutes(double.TryParse(minutes, out var m) ? m : 1));
    [RelayCommand] private void SubtractTimerMinute()
    {
        if (!_s.Timer.IsActive) _s.Timer.SetDuration(_s.Timer.Duration - TimeSpan.FromMinutes(1));
    }
    [RelayCommand]
    private void TimerPreset(string? minutes)
    {
        if (!double.TryParse(minutes, out var m)) return;
        _s.Timer.Cancel();
        _s.Timer.SetDuration(TimeSpan.FromMinutes(m));
        _s.Timer.Label = $"{m:0} minute timer";
        _s.Timer.Start();
    }

    // ───────────────────────── calculator ─────────────────────────
    [ObservableProperty] private string _calcExpression = "";
    [ObservableProperty] private string _calcResult = "0";
    [ObservableProperty] private bool _calcHasError;
    private bool _calcJustEvaluated;

    [RelayCommand]
    private void CalcKey(string? key)
    {
        if (key is null) return;
        switch (key)
        {
            case "C":
                CalcExpression = "";
                CalcResult = "0";
                CalcHasError = false;
                return;
            case "back":
                if (CalcExpression.Length > 0) CalcExpression = CalcExpression[..^1];
                break;
            case "=":
                var result = ExpressionEvaluator.Evaluate(CalcExpression);
                CalcHasError = !result.Success;
                CalcResult = result.Format();
                if (result.Success)
                {
                    CalcExpression = Math.Round(result.Value, 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    _calcJustEvaluated = true;
                }
                return;
            case "copy":
                _s.ClipboardWatcher.SetText(CalcResult);
                return;
            default:
                if (_calcJustEvaluated && (char.IsDigit(key[0]) || key == "." || key == "("))
                    CalcExpression = "";
                if (CalcExpression.Length < 120) CalcExpression += key;
                break;
        }
        _calcJustEvaluated = false;
        var preview = ExpressionEvaluator.Evaluate(CalcExpression);
        CalcHasError = false;
        CalcResult = preview.Success ? preview.Format() : CalcExpression.Length == 0 ? "0" : CalcResult;
    }

    // ───────────────────────── clipboard ─────────────────────────
    public ObservableCollection<ClipboardItem> ClipboardItems { get; } = new();
    [ObservableProperty] private bool _hasClipboardItems;
    [ObservableProperty] private string? _clipboardFeedback;

    private void SyncClipboard()
    {
        ClipboardItems.Clear();
        foreach (var item in _s.Clipboard.Items) ClipboardItems.Add(item);
        HasClipboardItems = ClipboardItems.Count > 0;
    }

    [RelayCommand]
    private async Task CopyClip(ClipboardItem? item)
    {
        if (item is null) return;
        ClipboardFeedback = _s.ClipboardWatcher.SetText(item.Text) ? "Copied to clipboard" : "Clipboard is busy — try again";
        await Task.Delay(1400);
        ClipboardFeedback = null;
    }

    [RelayCommand] private void RemoveClip(ClipboardItem? item) { if (item != null) _s.Clipboard.Remove(item); }
    [RelayCommand] private void ClearClipboard() => _s.Clipboard.Clear();

    // ───────────────────────── search ─────────────────────────
    [ObservableProperty] private string _searchQuery = "";

    [RelayCommand]
    private void Search()
    {
        var settings = _s.Settings.Current.Modules;
        var query = SearchQuery.Trim();
        if (settings.SearchTarget == SearchTarget.WindowsSearch || query.Length == 0)
        {
            _s.StateMachine.Collapse();
            SystemActions.OpenWindowsSearch();
        }
        else
        {
            var url = settings.SearchUrlTemplate.Replace("{0}", Uri.EscapeDataString(query));
            SystemActions.OpenUri(url);
            _s.StateMachine.Collapse();
        }
        SearchQuery = "";
    }

    [RelayCommand]
    private void WindowsSearch()
    {
        _s.StateMachine.Collapse();
        SystemActions.OpenWindowsSearch();
    }
}
