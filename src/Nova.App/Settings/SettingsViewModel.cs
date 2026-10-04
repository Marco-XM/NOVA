using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Nova.App.Controls;
using Nova.App.Notch.Rendering;
using Nova.Core.Animation;
using Nova.Core.Events;
using Nova.Core.Hotkeys;
using Nova.Core.Logging;
using Nova.Core.Media;
using Nova.Core.Monitors;
using Nova.Core.QuickApps;
using Nova.Core.Settings;
using Nova.Core.State;
using Nova.Platform.QuickApps;
using Nova.Platform.Services;

namespace Nova.App.Settings;

public sealed record Choice<T>(T Value, string Label, string? Description = null);

public sealed partial class MonitorCard : ObservableObject
{
    public required MonitorInfo Monitor { get; init; }
    public required int Number { get; init; }
    public string Label => Monitor.DisplayLabel;
    public string Description => Monitor.Description;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isCurrent;
    // Layout in the monitor map (filled by the view model).
    public double MapX { get; set; }
    public double MapY { get; set; }
    public double MapWidth { get; set; }
    public double MapHeight { get; set; }
}

public sealed partial class QuickAppRow : ObservableObject
{
    public QuickAppRow(QuickApp app, LaunchValidation validation, string? hotkeyError)
    {
        App = app;
        Validation = validation;
        HotkeyError = hotkeyError;
        Icon = ShellIcons.ForQuickApp(app, 48);
    }

    public QuickApp App { get; }
    public string Name => App.Name;
    public string Target => App.Kind == QuickAppKind.AppsFolder ? "Installed app" : App.Target;
    public string? Hotkey => App.Hotkey;
    public bool HasHotkey => !string.IsNullOrWhiteSpace(App.Hotkey);
    public LaunchValidation Validation { get; }
    public bool IsBroken => Validation != LaunchValidation.Ok;
    public string? HotkeyError { get; }
    public ImageSource? Icon { get; }
}

public sealed partial class ProviderRow : ObservableObject
{
    public required IMediaProvider Provider { get; init; }
    public string Name => Provider.DisplayName;
    [ObservableProperty] private string _status = "";
}

/// <summary>
/// View model for the settings window and the onboarding flow. Properties read and write the live
/// <see cref="AppSettings"/> through <see cref="SettingsService"/>, so every change applies instantly.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly AppHost _host;
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _liveTimer;
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _lastCpu;
    private DateTime _lastCpuSample;
    private readonly Action<MonitorSelection> _onSelection;

    public SettingsViewModel(AppHost host)
    {
        _host = host;
        _settings = host.Services.Settings;
        _settings.Changed += OnSettingsChanged;
        host.Monitors.MonitorsChanged += RefreshMonitors;
        _onSelection = _ => RefreshMonitors();
        host.Monitors.SelectionChanged += _onSelection;
        host.Services.Media.CurrentChanged += OnMediaChanged;
        host.Hotkeys.StatusChanged += OnHotkeyStatusChanged;

        Providers = new ObservableCollection<ProviderRow>(host.Services.Media.Providers.Select(p => new ProviderRow { Provider = p }));
        RefreshMonitors();
        RefreshQuickApps();
        RefreshProviders();
        RefreshMedia(host.Services.Media.Current);

        _liveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _liveTimer.Tick += (_, _) => RefreshLive();
        _liveTimer.Start();
        _lastCpu = _process.TotalProcessorTime;
        _lastCpuSample = DateTime.UtcNow;
        RefreshLive();
    }

    private AppSettings S => _settings.Current;

    private void Set(Action apply, SettingsSection section, [CallerMemberName] string? name = null)
    {
        apply();
        _settings.Update(section);
        OnPropertyChanged(name);
    }

    private void OnSettingsChanged(SettingsSection section)
    {
        // Something else (tray, notch) changed settings: refresh everything bound.
        OnPropertyChanged(string.Empty);
        if (section.HasFlag(SettingsSection.QuickApps)) RefreshQuickApps();
        if (section.HasFlag(SettingsSection.Monitor)) RefreshMonitors();
        if (section.HasFlag(SettingsSection.Media)) RefreshProviders();
    }

    // ───────────────────────── choices ─────────────────────────

    public IReadOnlyList<Choice<NotchMaterial>> Materials { get; } = new[]
    {
        new Choice<NotchMaterial>(NotchMaterial.DarkGlass, "Dark glass", "Translucent, softly lit (default)"),
        new Choice<NotchMaterial>(NotchMaterial.Midnight, "Midnight", "Pure black, blends with the screen edge"),
        new Choice<NotchMaterial>(NotchMaterial.Graphite, "Graphite", "Warm dark grey"),
        new Choice<NotchMaterial>(NotchMaterial.Frost, "Frost", "Light frosted surface"),
    };

    public IReadOnlyList<Choice<NotchShape>> Shapes { get; } = new[]
    {
        new Choice<NotchShape>(NotchShape.Attached, "Attached", "Hangs from the top edge with soft shoulders"),
        new Choice<NotchShape>(NotchShape.Floating, "Floating", "A detached pill just below the edge"),
    };

    public IReadOnlyList<Choice<IdleContent>> IdleContents { get; } = new[]
    {
        new Choice<IdleContent>(IdleContent.Wordmark, "NOVA wordmark"),
        new Choice<IdleContent>(IdleContent.Clock, "Clock"),
        new Choice<IdleContent>(IdleContent.Empty, "Nothing"),
    };

    public IReadOnlyList<Choice<NotchPosition>> Positions { get; } = new[]
    {
        new Choice<NotchPosition>(NotchPosition.Center, "Center"),
        new Choice<NotchPosition>(NotchPosition.Left, "Left"),
        new Choice<NotchPosition>(NotchPosition.Right, "Right"),
    };

    public IReadOnlyList<Choice<AuroraMode>> AuroraModes { get; } = new[]
    {
        new Choice<AuroraMode>(AuroraMode.ThemeDefault, "Only with the Aurora theme"),
        new Choice<AuroraMode>(AuroraMode.On, "On for every theme"),
        new Choice<AuroraMode>(AuroraMode.Off, "Off"),
    };

    public IReadOnlyList<Choice<AuroraColorSource>> AuroraColorSources { get; } = new[]
    {
        new Choice<AuroraColorSource>(AuroraColorSource.Accent, "From the accent color"),
        new Choice<AuroraColorSource>(AuroraColorSource.Custom, "Custom colors"),
    };

    public IReadOnlyList<Choice<AutoHideMode>> AutoHideModes { get; } = new[]
    {
        new Choice<AutoHideMode>(AutoHideMode.Off, "Off"),
        new Choice<AutoHideMode>(AutoHideMode.WhenWindowAtTop, "When a window reaches the top"),
        new Choice<AutoHideMode>(AutoHideMode.Always, "Always"),
    };

    public IReadOnlyList<Choice<HotkeyAction>> HotkeyActions { get; } = new[]
    {
        new Choice<HotkeyAction>(HotkeyAction.Toggle, "Toggle the notch"),
        new Choice<HotkeyAction>(HotkeyAction.Expand, "Always expand"),
    };

    public IReadOnlyList<Choice<SearchTarget>> SearchTargets { get; } = new[]
    {
        new Choice<SearchTarget>(SearchTarget.Web, "Web search"),
        new Choice<SearchTarget>(SearchTarget.WindowsSearch, "Windows Search"),
    };

    public IReadOnlyList<string> AccentPresets { get; } = new[] { "#8B9CFF", "#6EE7F9", "#A78BFA", "#F0ABFC", "#FDA4AF", "#FDBA74", "#86EFAC", "#E5E7EB" };

    public IReadOnlyList<AnimationThemeDefinition> Themes => AnimationThemes.All;

    // ───────────────────────── appearance ─────────────────────────

    public NotchMaterial Material { get => S.Appearance.Material; set => Set(() => S.Appearance.Material = value, SettingsSection.Appearance); }
    public NotchShape Shape { get => S.Appearance.Shape; set => Set(() => S.Appearance.Shape = value, SettingsSection.Appearance); }
    public IdleContent IdleContent { get => S.Appearance.IdleContent; set => Set(() => S.Appearance.IdleContent = value, SettingsSection.Appearance); }
    public double Transparency { get => Math.Round((1 - S.Appearance.Opacity) * 100); set => Set(() => S.Appearance.Opacity = 1 - value / 100.0, SettingsSection.Appearance); }
    public double GlowAmount { get => S.Appearance.GlowAmount * 100; set => Set(() => S.Appearance.GlowAmount = value / 100.0, SettingsSection.Appearance); }
    public double SizeScale { get => S.Appearance.SizeScale * 100; set => Set(() => S.Appearance.SizeScale = value / 100.0, SettingsSection.Appearance); }
    public double CornerRadius { get => S.Appearance.CornerRadiusScale * 100; set => Set(() => S.Appearance.CornerRadiusScale = value / 100.0, SettingsSection.Appearance); }
    public double TopOffset { get => S.Appearance.TopOffset; set => Set(() => S.Appearance.TopOffset = value, SettingsSection.Appearance); }
    public NotchPosition NotchPosition { get => S.Appearance.Position; set => Set(() => S.Appearance.Position = value, SettingsSection.Appearance); }

    public string AccentColor
    {
        get => S.Appearance.AccentColor;
        set
        {
            if (!ColorUtil.TryParseHex(value, out _)) return;
            Set(() => S.Appearance.AccentColor = value.StartsWith('#') ? value.ToUpperInvariant() : "#" + value.ToUpperInvariant(), SettingsSection.Appearance);
            OnPropertyChanged(nameof(AccentBrush));
        }
    }

    public Brush AccentBrush => new SolidColorBrush(ColorMath.Parse(S.Appearance.AccentColor, Color.FromRgb(139, 156, 255)));

    [RelayCommand] private void SetAccent(string? hex) { if (hex != null) AccentColor = hex; }

    // ───────────────────────── animation ─────────────────────────

    public AnimationTheme Theme
    {
        get => S.Animation.Theme;
        set
        {
            Set(() => S.Animation.Theme = value, SettingsSection.Animation);
            OnPropertyChanged(nameof(ThemeName));
            PreviewTheme = value;
        }
    }

    public string ThemeName => AnimationThemes.Get(S.Animation.Theme).Name;

    /// <summary>Theme shown in the large preview (not applied until "Apply").</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PreviewThemeDefinition), nameof(PreviewIsApplied))]
    private AnimationTheme _previewTheme;

    public AnimationThemeDefinition PreviewThemeDefinition => AnimationThemes.Get(PreviewTheme);
    public bool PreviewIsApplied => PreviewTheme == S.Animation.Theme;

    [RelayCommand] private void ChooseTheme(AnimationThemeDefinition? theme) { if (theme != null) Theme = theme.Theme; }
    [RelayCommand] private void SelectPreview(AnimationThemeDefinition? theme) { if (theme != null) PreviewTheme = theme.Theme; }
    [RelayCommand] private void ApplyPreview() { Theme = PreviewTheme; OnPropertyChanged(nameof(PreviewIsApplied)); }
    [RelayCommand] private void PreviewOnNotch() => _host.Notch.PreviewTheme(PreviewTheme);

    public double AnimationSpeed { get => S.Animation.Speed * 100; set => Set(() => S.Animation.Speed = value / 100.0, SettingsSection.Animation); }
    public double AnimationIntensity { get => S.Animation.Intensity * 100; set => Set(() => S.Animation.Intensity = value / 100.0, SettingsSection.Animation); }
    public double SpringStrength { get => S.Animation.SpringStrength * 100; set => Set(() => S.Animation.SpringStrength = value / 100.0, SettingsSection.Animation); }
    public double ExpansionDuration { get => S.Animation.ExpansionDurationMs; set => Set(() => S.Animation.ExpansionDurationMs = (int)value, SettingsSection.Animation); }
    public bool ReduceMotion { get => S.Animation.ReduceMotion; set => Set(() => S.Animation.ReduceMotion = value, SettingsSection.Animation); }
    public bool FollowSystemReduceMotion { get => S.Animation.FollowSystemReduceMotion; set => Set(() => S.Animation.FollowSystemReduceMotion = value, SettingsSection.Animation); }
    public bool AmbientWhenIdle { get => S.Animation.AmbientEffectsWhenIdle; set => Set(() => S.Animation.AmbientEffectsWhenIdle = value, SettingsSection.Animation); }
    public bool SystemReducesMotion => !SystemParameters.ClientAreaAnimation;

    public AuroraMode AuroraMode { get => S.Animation.Aurora; set => Set(() => S.Animation.Aurora = value, SettingsSection.Animation); }
    public AuroraColorSource AuroraColorSource
    {
        get => S.Animation.AuroraColors;
        set { Set(() => S.Animation.AuroraColors = value, SettingsSection.Animation); OnPropertyChanged(nameof(AuroraUsesCustomColors)); }
    }
    public bool AuroraUsesCustomColors => S.Animation.AuroraColors == AuroraColorSource.Custom;
    public bool AuroraFollowsArtwork { get => S.Animation.AuroraFollowsArtwork; set => Set(() => S.Animation.AuroraFollowsArtwork = value, SettingsSection.Animation); }
    public string AuroraColor1 { get => S.Animation.AuroraCustomColors[0]; set => SetAuroraColor(0, value); }
    public string AuroraColor2 { get => S.Animation.AuroraCustomColors[1]; set => SetAuroraColor(1, value); }
    public string AuroraColor3 { get => S.Animation.AuroraCustomColors[2]; set => SetAuroraColor(2, value); }
    public Brush AuroraBrush1 => SwatchBrush(S.Animation.AuroraCustomColors[0]);
    public Brush AuroraBrush2 => SwatchBrush(S.Animation.AuroraCustomColors[1]);
    public Brush AuroraBrush3 => SwatchBrush(S.Animation.AuroraCustomColors[2]);

    private void SetAuroraColor(int index, string value, [CallerMemberName] string? name = null)
    {
        if (!ColorUtil.TryParseHex(value, out _)) return;
        var hex = (value.StartsWith('#') ? value : "#" + value).ToUpperInvariant();
        Set(() => S.Animation.AuroraCustomColors[index] = hex, SettingsSection.Animation, name);
        OnPropertyChanged($"AuroraBrush{index + 1}");
    }

    /// <summary>Clicking a preset fills the next custom slot (1 → 2 → 3 → 1 …).</summary>
    [RelayCommand]
    private void AddAuroraPreset(string? hex)
    {
        if (hex is null) return;
        var slot = _nextAuroraSlot;
        _nextAuroraSlot = (_nextAuroraSlot + 1) % 3;
        if (S.Animation.AuroraColors != AuroraColorSource.Custom) AuroraColorSource = AuroraColorSource.Custom;
        SetAuroraColor(slot, hex, $"AuroraColor{slot + 1}");
    }
    private int _nextAuroraSlot;

    private static Brush SwatchBrush(string hex)
    {
        var brush = new SolidColorBrush(ColorMath.Parse(hex, Color.FromRgb(139, 156, 255)));
        brush.Freeze();
        return brush;
    }

    [RelayCommand]
    private void ResetAnimation()
    {
        var theme = S.Animation.Theme;
        S.Animation = new AnimationSettings { Theme = theme };
        _settings.Update(SettingsSection.Animation);
    }

    // ───────────────────────── monitors ─────────────────────────

    public ObservableCollection<MonitorCard> MonitorCards { get; } = new();
    [ObservableProperty] private string _currentMonitorText = "";
    [ObservableProperty] private string _currentMonitorReason = "";
    [ObservableProperty] private double _mapWidth = 420;
    [ObservableProperty] private double _mapHeight = 170;

    public MonitorMode MonitorMode
    {
        get => S.Monitor.Mode;
        set => Set(() => S.Monitor.Mode = value, SettingsSection.Monitor);
    }

    public bool ModePrimary { get => MonitorMode == MonitorMode.Primary; set { if (value) MonitorMode = MonitorMode.Primary; } }
    public bool ModeSelected
    {
        get => MonitorMode == MonitorMode.Selected;
        set
        {
            if (!value) return;
            if (MonitorSelector.FindSelected(S.Monitor, _host.Monitors.Monitors) is null && _host.Monitors.Current is { } current)
                MonitorSelector.StoreSelection(S.Monitor, current.Monitor);
            MonitorMode = MonitorMode.Selected;
        }
    }
    public bool ModeActive { get => MonitorMode == MonitorMode.Active; set { if (value) MonitorMode = MonitorMode.Active; } }
    public bool ModeMouse { get => MonitorMode == MonitorMode.Mouse; set { if (value) MonitorMode = MonitorMode.Mouse; } }

    [RelayCommand]
    private void SelectMonitor(MonitorCard? card)
    {
        if (card is null) return;
        _host.SelectMonitor(card.Monitor.DeviceName);
    }

    [RelayCommand] private void IdentifyMonitors() => MonitorIdentifier.Show(_host.Monitors.Monitors);

    private void RefreshMonitors()
    {
        var monitors = _host.Monitors.Monitors;
        var selected = MonitorSelector.FindSelected(S.Monitor, monitors);
        var current = _host.Monitors.Current;
        MonitorCards.Clear();
        if (monitors.Count > 0)
        {
            var minX = monitors.Min(m => m.Bounds.Left);
            var minY = monitors.Min(m => m.Bounds.Top);
            var maxX = monitors.Max(m => m.Bounds.Right);
            var maxY = monitors.Max(m => m.Bounds.Bottom);
            var scale = Math.Min(MapWidth / Math.Max(1, maxX - minX), MapHeight / Math.Max(1, maxY - minY));
            var offsetX = (MapWidth - (maxX - minX) * scale) / 2;
            var offsetY = (MapHeight - (maxY - minY) * scale) / 2;
            var n = 1;
            foreach (var m in monitors)
            {
                MonitorCards.Add(new MonitorCard
                {
                    Monitor = m,
                    Number = n++,
                    IsSelected = selected?.DeviceName == m.DeviceName && S.Monitor.Mode == MonitorMode.Selected,
                    IsCurrent = current?.Monitor.DeviceName == m.DeviceName,
                    MapX = offsetX + (m.Bounds.Left - minX) * scale + 3,
                    MapY = offsetY + (m.Bounds.Top - minY) * scale + 3,
                    MapWidth = Math.Max(20, m.Bounds.Width * scale - 6),
                    MapHeight = Math.Max(14, m.Bounds.Height * scale - 6),
                });
            }
        }
        if (current is { } c)
        {
            CurrentMonitorText = $"{c.Monitor.DisplayLabel} — {c.Monitor.Description}";
            CurrentMonitorReason = c.Reason;
        }
        OnPropertyChanged(nameof(ModePrimary));
        OnPropertyChanged(nameof(ModeSelected));
        OnPropertyChanged(nameof(ModeActive));
        OnPropertyChanged(nameof(ModeMouse));
        OnPropertyChanged(nameof(MonitorCount));
    }

    public int MonitorCount => _host.Monitors.Monitors.Count;

    // ───────────────────────── media ─────────────────────────

    public ObservableCollection<ProviderRow> Providers { get; }
    public bool MediaEnabled { get => S.Media.Enabled; set => Set(() => S.Media.Enabled = value, SettingsSection.Media); }
    public bool MediaSpotify { get => S.Media.Spotify; set => Set(() => S.Media.Spotify = value, SettingsSection.Media); }
    public bool MediaChrome { get => S.Media.Chrome; set => Set(() => S.Media.Chrome = value, SettingsSection.Media); }
    public bool MediaOther { get => S.Media.OtherSessions; set => Set(() => S.Media.OtherSessions = value, SettingsSection.Media); }
    public bool MediaAutoExpand { get => S.Media.AutoExpand; set => Set(() => S.Media.AutoExpand = value, SettingsSection.Media); }
    public double MediaAutoExpandSeconds { get => S.Media.AutoExpandDurationMs / 1000.0; set => Set(() => S.Media.AutoExpandDurationMs = (int)(value * 1000), SettingsSection.Media); }
    public double MediaAutoCollapseSeconds { get => S.Media.AutoCollapseDelayMs / 1000.0; set => Set(() => S.Media.AutoCollapseDelayMs = (int)(value * 1000), SettingsSection.Media); }
    public bool MediaCompact { get => S.Media.ShowCompactIndicator; set => Set(() => S.Media.ShowCompactIndicator = value, SettingsSection.Media); }
    public bool MediaEqualizer { get => S.Media.AnimatedEqualizer; set => Set(() => S.Media.AnimatedEqualizer = value, SettingsSection.Media | SettingsSection.Modules); }
    public double MediaLingerMinutes { get => S.Media.PausedLingerSeconds / 60.0; set => Set(() => S.Media.PausedLingerSeconds = (int)(value * 60), SettingsSection.Media); }
    public bool MediaLyrics { get => S.Media.ShowLyrics; set => Set(() => S.Media.ShowLyrics = value, SettingsSection.Media); }

    public string SpotifyStatus => Providers.FirstOrDefault(p => p.Provider.Kind == MediaProviderKind.Spotify)?.Status ?? "";
    public string ChromeStatus => Providers.FirstOrDefault(p => p.Provider.Kind == MediaProviderKind.Chrome)?.Status ?? "";
    public string OtherStatus => Providers.FirstOrDefault(p => p.Provider.Kind == MediaProviderKind.Windows)?.Status ?? "";

    [ObservableProperty] private bool _hasNowPlaying;
    [ObservableProperty] private string _nowPlayingTitle = "Nothing playing";
    [ObservableProperty] private string _nowPlayingSubtitle = "Start music or a video and it appears in the notch";
    [ObservableProperty] private bool _nowPlayingIsPlaying;
    public string NowPlayingGlyph => NowPlayingIsPlaying ? "" : "";
    partial void OnNowPlayingIsPlayingChanged(bool value) => OnPropertyChanged(nameof(NowPlayingGlyph));

    [RelayCommand] private Task NowPlayingToggle() => _host.Services.Media.SendAsync(MediaCommand.PlayPause);
    [RelayCommand] private Task NowPlayingNext() => _host.Services.Media.SendAsync(MediaCommand.Next);
    [RelayCommand] private Task NowPlayingPrevious() => _host.Services.Media.SendAsync(MediaCommand.Previous);

    private void OnMediaChanged(MediaSessionInfo? session) => _host.Ui(() => { RefreshMedia(session); RefreshProviders(); });

    private void RefreshMedia(MediaSessionInfo? s)
    {
        HasNowPlaying = s != null;
        NowPlayingTitle = s is null ? "Nothing playing" : string.IsNullOrWhiteSpace(s.Title) ? s.SourceName : s.Title;
        NowPlayingSubtitle = s is null ? "Start music or a video and it appears in the notch" : $"{(string.IsNullOrWhiteSpace(s.Artist) ? "" : s.Artist + " · ")}{s.SourceName}";
        NowPlayingIsPlaying = s?.IsPlaying ?? false;
    }

    private void RefreshProviders()
    {
        foreach (var row in Providers)
        {
            try { row.Status = row.Provider.StatusText; }
            catch (Exception ex) { row.Status = "Unavailable"; Log.Debug(ex.Message); }
        }
        OnPropertyChanged(nameof(SpotifyStatus));
        OnPropertyChanged(nameof(ChromeStatus));
        OnPropertyChanged(nameof(OtherStatus));
    }

    // ───────────────────────── quick apps ─────────────────────────

    public ObservableCollection<QuickAppRow> QuickApps { get; } = new();
    [ObservableProperty] private bool _hasQuickApps;

    private void RefreshQuickApps()
    {
        QuickApps.Clear();
        foreach (var app in S.QuickApps.Apps)
        {
            _host.Hotkeys.AppHotkeyErrors.TryGetValue(app.Id, out var error);
            QuickApps.Add(new QuickAppRow(app, _host.Services.Launcher.Validate(app), error));
        }
        HasQuickApps = QuickApps.Count > 0;
    }

    private void OnHotkeyStatusChanged()
    {
        OnPropertyChanged(nameof(HotkeyStatus));
        OnPropertyChanged(nameof(HotkeyHasError));
    }

    public void AddQuickApps(IEnumerable<QuickApp> apps)
    {
        foreach (var app in apps)
        {
            if (S.QuickApps.Apps.Any(a => a.Kind == app.Kind && string.Equals(a.Target, app.Target, StringComparison.OrdinalIgnoreCase))) continue;
            S.QuickApps.Apps.Add(app);
        }
        _settings.Update(SettingsSection.QuickApps);
    }

    [RelayCommand]
    private void AddExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an application",
            Filter = "Applications and shortcuts|*.exe;*.lnk;*.url;*.bat;*.cmd|All files|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dialog.ShowDialog() != true) return;
        var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        AddQuickApps(new[]
        {
            new QuickApp
            {
                Name = FileVersionName(dialog.FileName),
                Kind = ext is ".lnk" or ".url" ? QuickAppKind.Shortcut : QuickAppKind.Executable,
                Target = dialog.FileName,
            },
        });
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to open from the notch" };
        if (dialog.ShowDialog() != true) return;
        AddQuickApps(new[] { new QuickApp { Name = new DirectoryInfo(dialog.FolderName).Name, Kind = QuickAppKind.Folder, Target = dialog.FolderName } });
    }

    [ObservableProperty] private string _newWebsiteUrl = "";

    [RelayCommand]
    private void AddWebsite()
    {
        var url = NewWebsiteUrl.Trim();
        if (!url.Contains("://")) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        AddQuickApps(new[] { new QuickApp { Name = uri.Host.Replace("www.", ""), Kind = QuickAppKind.Uri, Target = uri.ToString() } });
        NewWebsiteUrl = "";
    }

    private static string FileVersionName(string path)
    {
        try
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(info.FileDescription)) return info.FileDescription!;
            }
        }
        catch { }
        return Path.GetFileNameWithoutExtension(path);
    }

    [RelayCommand]
    private void RemoveQuickApp(QuickAppRow? row)
    {
        if (row is null) return;
        S.QuickApps.Apps.RemoveAll(a => a.Id == row.App.Id);
        _settings.Update(SettingsSection.QuickApps);
    }

    [RelayCommand] private void MoveUp(QuickAppRow? row) => Move(row, -1);
    [RelayCommand] private void MoveDown(QuickAppRow? row) => Move(row, +1);

    public void Move(QuickAppRow? row, int delta)
    {
        if (row is null) return;
        var list = S.QuickApps.Apps;
        var index = list.FindIndex(a => a.Id == row.App.Id);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count) return;
        (list[index], list[target]) = (list[target], list[index]);
        _settings.Update(SettingsSection.QuickApps);
    }

    public void MoveTo(QuickAppRow row, int newIndex)
    {
        var list = S.QuickApps.Apps;
        var index = list.FindIndex(a => a.Id == row.App.Id);
        if (index < 0 || newIndex < 0 || newIndex >= list.Count || index == newIndex) return;
        var app = list[index];
        list.RemoveAt(index);
        list.Insert(newIndex, app);
        _settings.Update(SettingsSection.QuickApps);
    }

    [RelayCommand]
    private void ChooseIcon(QuickAppRow? row)
    {
        if (row is null) return;
        var dialog = new OpenFileDialog { Title = "Choose an icon", Filter = "Images and icons|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.exe;*.dll|All files|*.*" };
        if (dialog.ShowDialog() != true) return;
        UpdateApp(row, a => a.CustomIconPath = dialog.FileName);
    }

    [RelayCommand] private void ResetIcon(QuickAppRow? row) { if (row != null) UpdateApp(row, a => a.CustomIconPath = null); }

    public void RenameQuickApp(QuickAppRow row, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == row.App.Name) return;
        UpdateApp(row, a => a.Name = name.Trim());
    }

    public void SetQuickAppHotkey(QuickAppRow row, string? hotkey) => UpdateApp(row, a => a.Hotkey = string.IsNullOrWhiteSpace(hotkey) ? null : hotkey);

    private void UpdateApp(QuickAppRow row, Action<QuickApp> change)
    {
        var app = S.QuickApps.Apps.FirstOrDefault(a => a.Id == row.App.Id);
        if (app is null) return;
        change(app);
        _settings.Update(SettingsSection.QuickApps);
    }

    [RelayCommand]
    private void LaunchQuickApp(QuickAppRow? row)
    {
        if (row is null) return;
        var result = _host.Services.Launcher.Launch(row.App);
        if (!result.Success) MessageBox.Show(result.Error, "NOVA", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ───────────────────────── behavior ─────────────────────────

    public bool StartWithWindows { get => S.Behavior.StartWithWindows; set { Set(() => S.Behavior.StartWithWindows = value, SettingsSection.Behavior); OnPropertyChanged(nameof(StartupStatusText)); } }
    public string StartupStatusText => _host.Services.Startup.GetStatus() switch
    {
        StartupStatus.Enabled => "NOVA starts quietly in the background when you sign in",
        StartupStatus.DisabledByUser => "Disabled in Task Manager → Startup apps. Turning this on re-enables it.",
        StartupStatus.PathMismatch => "Registered for an older location; it will be updated",
        _ => "NOVA won't start automatically",
    };
    public bool RunInBackground { get => S.Behavior.RunInBackground; set => Set(() => S.Behavior.RunInBackground = value, SettingsSection.Behavior); }
    public bool ShowNotchOnStartup { get => S.Behavior.ShowNotchOnStartup; set => Set(() => S.Behavior.ShowNotchOnStartup = value, SettingsSection.Behavior); }
    public AutoHideMode AutoHideMode { get => S.Behavior.AutoHideMode; set => Set(() => S.Behavior.AutoHideMode = value, SettingsSection.Behavior); }
    public bool ExpandOnHover { get => S.Behavior.ExpandOnHover; set => Set(() => S.Behavior.ExpandOnHover = value, SettingsSection.Behavior); }
    public double HoverDelay { get => S.Behavior.HoverDelayMs; set => Set(() => S.Behavior.HoverDelayMs = (int)value, SettingsSection.Behavior); }
    public double CollapseDelay { get => S.Behavior.CollapseDelayMs; set => Set(() => S.Behavior.CollapseDelayMs = (int)value, SettingsSection.Behavior); }
    public HotkeyAction HotkeyAction { get => S.Behavior.HotkeyAction; set => Set(() => S.Behavior.HotkeyAction = value, SettingsSection.Behavior); }
    public bool HideInGames { get => S.Behavior.HideInFullscreenGames; set => Set(() => S.Behavior.HideInFullscreenGames = value, SettingsSection.Behavior); }
    public bool HideInVideo { get => S.Behavior.HideInFullscreenVideo; set => Set(() => S.Behavior.HideInFullscreenVideo = value, SettingsSection.Behavior); }
    public bool HideInPresentations { get => S.Behavior.HideDuringPresentations; set => Set(() => S.Behavior.HideDuringPresentations = value, SettingsSection.Behavior); }
    public bool ExcludeFromCapture { get => S.Behavior.ExcludeFromScreenCapture; set => Set(() => S.Behavior.ExcludeFromScreenCapture = value, SettingsSection.Behavior); }

    public string Hotkey
    {
        get => S.Behavior.Hotkey;
        set => Set(() => S.Behavior.Hotkey = value ?? "", SettingsSection.Behavior);
    }

    public string HotkeyStatus => _host.Hotkeys.MainHotkeyError ?? (string.IsNullOrWhiteSpace(S.Behavior.Hotkey) ? "No shortcut set" : "Active");
    public bool HotkeyHasError => _host.Hotkeys.MainHotkeyError != null;

    public void SuspendHotkeys() => _host.Hotkeys.Suspend();
    public void ResumeHotkeys() => _settings.Update(SettingsSection.Behavior);

    // ───────────────────────── notifications ─────────────────────────

    public bool NotificationsEnabled { get => S.Notifications.Enabled; set => Set(() => S.Notifications.Enabled = value, SettingsSection.Notifications); }
    public double NotificationSeconds { get => S.Notifications.DurationMs / 1000.0; set => Set(() => S.Notifications.DurationMs = (int)(value * 1000), SettingsSection.Notifications); }
    public bool NotifyMusicStarted { get => S.Notifications.MusicStarted; set => Set(() => S.Notifications.MusicStarted = value, SettingsSection.Notifications); }
    public bool NotifyMusicPaused { get => S.Notifications.MusicPaused; set => Set(() => S.Notifications.MusicPaused = value, SettingsSection.Notifications); }
    public bool NotifyMusicChanged { get => S.Notifications.MusicChanged; set => Set(() => S.Notifications.MusicChanged = value, SettingsSection.Notifications); }
    public bool NotifyAppLaunched { get => S.Notifications.AppLaunched; set => Set(() => S.Notifications.AppLaunched = value, SettingsSection.Notifications); }
    public bool NotifyDownloads { get => S.Notifications.DownloadCompleted; set => Set(() => S.Notifications.DownloadCompleted = value, SettingsSection.Notifications); }
    public bool NotifyTimer { get => S.Notifications.TimerCompleted; set => Set(() => S.Notifications.TimerCompleted = value, SettingsSection.Notifications); }
    public bool NotifyBattery { get => S.Notifications.BatteryChanged; set => Set(() => S.Notifications.BatteryChanged = value, SettingsSection.Notifications); }
    public bool NotifyClipboard { get => S.Notifications.ClipboardCopied; set => Set(() => S.Notifications.ClipboardCopied = value, SettingsSection.Notifications); }
    public bool NotifyVolume { get => S.Notifications.VolumeChanged; set => Set(() => S.Notifications.VolumeChanged = value, SettingsSection.Notifications); }
    public bool NotifyBrightness { get => S.Notifications.BrightnessChanged; set => Set(() => S.Notifications.BrightnessChanged = value, SettingsSection.Notifications); }
    public bool NotifyNetwork { get => S.Notifications.NetworkChanged; set => Set(() => S.Notifications.NetworkChanged = value, SettingsSection.Notifications); }
    public bool NotifyApps { get => S.Notifications.AppNotifications; set => Set(() => S.Notifications.AppNotifications = value, SettingsSection.Notifications); }
    public bool NotifyAppText { get => S.Notifications.AppNotificationText; set => Set(() => S.Notifications.AppNotificationText = value, SettingsSection.Notifications); }
    public string AppNotificationStatus => _host.Services.AppNotifications.Status;

    [RelayCommand]
    private void TestNotification() =>
        _host.Services.Bus.Publish(new CustomEvent("This is a test notification", "Events appear like this for a few seconds", ""));

    // ───────────────────────── modules ─────────────────────────

    public bool ModClipboard { get => S.Modules.Clipboard; set => Set(() => S.Modules.Clipboard = value, SettingsSection.Modules); }
    public double ClipboardSize { get => S.Modules.ClipboardHistorySize; set => Set(() => S.Modules.ClipboardHistorySize = (int)value, SettingsSection.Modules); }
    public bool ModTimer { get => S.Modules.Timer; set => Set(() => S.Modules.Timer = value, SettingsSection.Modules); }
    public bool ModCalculator { get => S.Modules.Calculator; set => Set(() => S.Modules.Calculator = value, SettingsSection.Modules); }
    public bool ModSearch { get => S.Modules.Search; set => Set(() => S.Modules.Search = value, SettingsSection.Modules); }
    public SearchTarget SearchTarget { get => S.Modules.SearchTarget; set => Set(() => S.Modules.SearchTarget = value, SettingsSection.Modules); }
    public string SearchUrl
    {
        get => S.Modules.SearchUrlTemplate;
        set { if (value?.Contains("{0}") == true) Set(() => S.Modules.SearchUrlTemplate = value, SettingsSection.Modules); }
    }
    public bool ModVolume { get => S.Modules.VolumeIndicator; set => Set(() => S.Modules.VolumeIndicator = value, SettingsSection.Modules); }
    public bool ModBrightness { get => S.Modules.BrightnessIndicator; set => Set(() => S.Modules.BrightnessIndicator = value, SettingsSection.Modules); }
    public bool ModBattery { get => S.Modules.Battery; set => Set(() => S.Modules.Battery = value, SettingsSection.Modules); }
    public bool ModNetwork { get => S.Modules.Network; set => Set(() => S.Modules.Network = value, SettingsSection.Modules); }
    public bool ModDownloads { get => S.Modules.DownloadWatcher; set => Set(() => S.Modules.DownloadWatcher = value, SettingsSection.Modules); }

    public string BrightnessStatus => _host.Services.Brightness.Status;
    public bool BrightnessSupported => _host.Services.Brightness.IsSupported;
    public string BatteryStatus => _host.Services.Power.HasBattery ? $"{_host.Services.Power.Percent}%{(_host.Services.Power.Charging ? " · charging" : "")}" : "No battery detected on this PC";
    public string VolumeStatus => _host.Services.Volume.IsAvailable ? "Shows when the system volume changes. Tip: scroll over the notch to change it." : "No audio output device found";
    public string DownloadsStatus => _host.Services.Downloads.Folder is { } f ? $"Watching {f}" : "Downloads folder not found";

    // ───────────────────────── advanced ─────────────────────────

    public bool DebugMode { get => S.Advanced.DebugMode; set => Set(() => S.Advanced.DebugMode = value, SettingsSection.Advanced); }
    public bool VerboseLogging { get => S.Advanced.VerboseLogging; set => Set(() => S.Advanced.VerboseLogging = value, SettingsSection.Advanced); }

    [ObservableProperty] private string _cpuText = "";
    [ObservableProperty] private string _memoryText = "";
    [ObservableProperty] private string _privateMemoryText = "";
    [ObservableProperty] private string _gcText = "";
    [ObservableProperty] private string _fpsText = "";
    [ObservableProperty] private string _uptimeText = "";
    [ObservableProperty] private string _threadsText = "";
    [ObservableProperty] private string _recentLog = "";

    public string VersionText => $"NOVA {AppHost.Version}";
    public string RuntimeText => $".NET {Environment.Version} · {(Environment.Is64BitProcess ? "x64" : "x86")}";
    public string OsText => Environment.OSVersion.VersionString;
    public string SettingsPath => _settings.FilePath;
    public string LogPath => Log.Directory ?? "";
    public string RefreshRateText => _host.Monitors.Current is { } c && c.Monitor.RefreshRate > 0 ? $"{c.Monitor.RefreshRate} Hz display" : "";

    private void RefreshLive()
    {
        OnPropertyChanged(nameof(AppNotificationStatus));
        try
        {
            _process.Refresh();
            var now = DateTime.UtcNow;
            var cpu = _process.TotalProcessorTime;
            var elapsed = (now - _lastCpuSample).TotalMilliseconds;
            if (elapsed > 0)
            {
                var percent = (cpu - _lastCpu).TotalMilliseconds / elapsed / Environment.ProcessorCount * 100;
                CpuText = $"{percent:0.0}%";
            }
            _lastCpu = cpu;
            _lastCpuSample = now;
            MemoryText = $"{_process.WorkingSet64 / 1048576.0:0} MB";
            PrivateMemoryText = $"{_process.PrivateMemorySize64 / 1048576.0:0} MB";
            GcText = $"{GC.GetTotalMemory(false) / 1048576.0:0.0} MB managed";
            FpsText = FrameClock.LastFps > 0 ? $"{FrameClock.LastFps:0} fps (last animation)" : "—";
            ThreadsText = $"{_process.Threads.Count} threads · {_process.HandleCount} handles";
            var up = DateTime.Now - _host.StartedAt;
            UptimeText = up.TotalHours >= 1 ? $"{(int)up.TotalHours} h {up.Minutes} min" : $"{up.Minutes} min {up.Seconds} s";
            RefreshProviders();
            OnPropertyChanged(nameof(BatteryStatus));
            OnPropertyChanged(nameof(BrightnessStatus));
            OnPropertyChanged(nameof(StartupStatusText));
        }
        catch (Exception ex)
        {
            Log.Debug($"Live stats failed: {ex.Message}");
        }
    }

    [RelayCommand] private void RefreshLog() => RecentLog = string.Join(Environment.NewLine, Log.GetRecentLines().TakeLast(200));
    [RelayCommand] private void OpenLogs() => SystemActions.OpenFolder(LogPath);
    [RelayCommand] private void OpenSettingsFolder() => SystemActions.OpenFolder(Path.GetDirectoryName(SettingsPath)!);
    [RelayCommand] private void RunOnboarding() => _host.ShowOnboarding();

    [RelayCommand]
    private void ResetSettings()
    {
        var answer = MessageBox.Show("Reset every NOVA setting to its default? Your quick apps are kept.", "Reset settings",
            MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;
        _settings.ResetToDefaults(keepQuickApps: true);
        Log.Info("Settings reset to defaults");
    }

    // ───────────────────────── overview ─────────────────────────

    public bool NotchVisible
    {
        get => !_host.Services.StateMachine.IsUserHidden;
        set
        {
            _host.Services.StateMachine.SetUserHidden(!value);
            OnPropertyChanged();
        }
    }

    [RelayCommand] private void ExpandNotch() => _host.Services.StateMachine.Navigate(NotchState.Expanded);

    public void Dispose()
    {
        _liveTimer.Stop();
        _settings.Changed -= OnSettingsChanged;
        _host.Monitors.MonitorsChanged -= RefreshMonitors;
        _host.Monitors.SelectionChanged -= _onSelection;
        _host.Services.Media.CurrentChanged -= OnMediaChanged;
        _host.Hotkeys.StatusChanged -= OnHotkeyStatusChanged;
    }
}
