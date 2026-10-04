using System.IO;
using System.Media;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Nova.App.Infrastructure;
using Nova.App.Notch;
using Nova.App.Services;
using Nova.App.Settings;
using Nova.App.Onboarding;
using Nova.App.Tray;
using Nova.Core.Animation;
using Nova.Core.Events;
using Nova.Core.Logging;
using Nova.Core.Media;
using Nova.Core.Monitors;
using Nova.Core.QuickApps;
using Nova.Core.Settings;
using Nova.Core.State;
using Nova.Core.Tools;
using Nova.Platform.Media;
using Nova.Platform.Monitors;
using Nova.Platform.QuickApps;
using Nova.Platform.Services;

namespace Nova.App;

/// <summary>
/// Composition root and lifecycle owner. Builds every service, wires events between them and
/// implements the tray actions. Everything created here is disposed on Quit.
/// </summary>
public sealed class AppHost : ITrayActions, IDisposable
{
    private readonly LaunchOptions _options;
    private readonly SingleInstance _instance;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly List<IDisposable> _subscriptions = new();

    private SettingsService _settings = null!;
    private NotchWindow _window = null!;
    private NotchViewModel _vm = null!;
    private MonitorService _monitors = null!;
    private TrayManager? _tray;
    private HotkeyManager _hotkeys = null!;
    private SettingsWindow? _settingsWindow;
    private OnboardingWindow? _onboarding;
    private bool _clipboardRunning;
    private bool _downloadsRunning;
    private bool _disposed;

    public AppHost(LaunchOptions options, SingleInstance instance)
    {
        _options = options;
        _instance = instance;
    }

    public AppServices Services { get; private set; } = null!;
    public NotchController Notch { get; private set; } = null!;
    public MonitorService Monitors => _monitors;
    public HotkeyManager Hotkeys => _hotkeys;
    public DateTime StartedAt { get; } = DateTime.Now;
    public static string Version => typeof(AppHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";
    public static string LocalDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NOVA");

    public void Start()
    {
        Log.Initialize(Path.Combine(LocalDataDirectory, "logs"));
        Log.Info($"NOVA {Version} starting · {Environment.OSVersion} · .NET {Environment.Version} · background={_options.Background}");

        _settings = new SettingsService(new SettingsStore(SettingsStore.DefaultDirectory));
        if (_options.ResetSettings) _settings.ResetToDefaults();
        Log.MinimumLevel = _settings.Current.Advanced.VerboseLogging ? LogLevel.Debug : LogLevel.Info;

        var bus = new EventBus();
        var stateMachine = new NotchStateMachine();
        var timer = new CountdownTimer();
        var clipboard = new ClipboardHistory { Capacity = _settings.Current.Modules.ClipboardHistorySize };
        var launcher = new AppLauncher(new ShellProcessStarter());
        var hub = new MediaSessionHub();
        var media = new MediaManager(new IMediaProvider[] { new SpotifyProvider(hub), new ChromeProvider(hub), new WindowsMediaProvider(hub) }, bus);
        _window = new NotchWindow();
        var foreground = new ForegroundTracker(() => _window.Handle);

        Services = new AppServices
        {
            Settings = _settings,
            Bus = bus,
            MediaHub = hub,
            Media = media,
            Launcher = launcher,
            StateMachine = stateMachine,
            Timer = timer,
            Clipboard = clipboard,
            ClipboardWatcher = new ClipboardWatcher(),
            Volume = new VolumeWatcher(),
            Brightness = new BrightnessWatcher(),
            Power = new PowerWatcher(),
            Network = new NetworkWatcher(),
            Downloads = new DownloadWatcher(),
            Foreground = foreground,
            Startup = new StartupManager(new StartupRegistry(), Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "NOVA.exe")),
            OpenSettings = OpenSettings,
            AppNotifications = new AppNotificationWatcher(),
            Lyrics = new LyricsService(),
        };

        _monitors = new MonitorService(new Win32MonitorProvider(() => _window.Handle), _settings, foreground);
        _vm = new NotchViewModel(Services);
        _vm.SetQuickApps(_settings.Current.QuickApps.Apps);
        Notch = new NotchController(_window, _vm, Services, _monitors);
        _hotkeys = new HotkeyManager();

        Wire();

        foreground.Start();
        _monitors.Start();
        StartWatchers();
        ApplyMediaSettings();
        _ = media.StartAsync();

        _tray = new TrayManager(this);
        Notch.Start(_settings.Current.Behavior.ShowNotchOnStartup || !_settings.Current.FirstRunCompleted);
        ApplyHotkeys();
        SyncStartup();
        _instance.StartServer();

        if (!_settings.Current.QuickApps.SeededDefaults) _ = SeedQuickAppsAsync();

        if (_settings.LoadOutcome is SettingsLoadOutcome.CorruptedReset or SettingsLoadOutcome.RecoveredFromBackup)
        {
            var text = _settings.LoadOutcome == SettingsLoadOutcome.RecoveredFromBackup ? "Settings restored from backup" : "Settings were reset";
            Ui(() => bus.Publish(new CustomEvent(text, "The settings file was damaged", "", Important: true)), DispatcherPriority.ApplicationIdle);
        }

        if (!_settings.Current.FirstRunCompleted || _options.ForceOnboarding)
        {
            ShowOnboarding();
        }
        else if (!_options.Background)
        {
            if (_options.OpenSettings) OpenSettings(null);
            else Ui(() => bus.Publish(new CustomEvent("NOVA is running", $"Hover the notch or press {_settings.Current.Behavior.Hotkey}", "")), DispatcherPriority.ApplicationIdle);
        }

        if (_options.DebugCommand != null) Ui(() => HandleCommand("debug " + _options.DebugCommand), DispatcherPriority.ApplicationIdle);
        MemoryTrimmer.Schedule(TimeSpan.FromSeconds(20));
        Log.Info("NOVA started");
    }

    // ───────────────────────── wiring ─────────────────────────

    private void Wire()
    {
        var s = Services;
        _subscriptions.Add(s.Bus.SubscribeAll(evt => Ui(() => Notch.HandleEvent(evt))));
        s.Media.CurrentChanged += session => Ui(() => Notch.OnMediaChanged(session));
        s.Volume.VolumeChanged += (level, muted) =>
        {
            s.Bus.Publish(new VolumeEvent(level, muted));
            Ui(UpdateStatus);
        };
        s.Brightness.BrightnessChanged += level => s.Bus.Publish(new BrightnessEvent(level));
        s.Power.BatteryEvent += (kind, percent, charging) => s.Bus.Publish(new BatteryEvent(kind, percent, charging));
        s.Power.StatusChanged += () => Ui(UpdateStatus);
        s.Network.ConnectionChanged += (connected, name, wireless) => s.Bus.Publish(new NetworkEvent(connected, name, wireless));
        s.Network.StatusChanged += () => Ui(UpdateStatus);
        s.Downloads.DownloadCompleted += (name, path) => s.Bus.Publish(new DownloadCompletedEvent(name, path));
        s.ClipboardWatcher.TextCopied += text =>
        {
            if (!_settings.Current.Modules.Clipboard) return;
            s.Clipboard.Add(text);
            var preview = text.ReplaceLineEndings(" ").Trim();
            s.Bus.Publish(new ClipboardEvent(preview.Length > 60 ? preview[..57] + "…" : preview));
        };
        s.Launcher.Launched += app => s.Bus.Publish(new AppLaunchedEvent(app.Name, app.Id));
        s.Timer.Completed += t =>
        {
            s.Bus.Publish(new TimerCompletedEvent(t.Label, t.Duration));
            PlayTimerSound();
        };
        s.Timer.StateChanged += _ => Ui(Notch.UpdateLiveActivity);
        s.Foreground.Changed += () => Notch.CheckForeground();
        s.AppNotifications.Posted += evt => s.Bus.Publish(evt);
        s.AppNotifications.Removed += id => s.Bus.Publish(new AppNotificationRemovedEvent(id));
        _settings.Changed += OnSettingsChanged;
        _instance.CommandReceived += cmd => Ui(() => HandleCommand(cmd));
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        _window.SystemSettingChanged += () => _tray?.UpdateIcon();
    }

    private void StartWatchers()
    {
        var s = Services;
        s.Volume.Start();
        s.Power.Start();
        s.Network.Start();
        _ = s.Brightness.StartAsync();
        ApplyModules();
        UpdateStatus();
    }

    private void ApplyModules()
    {
        var m = _settings.Current.Modules;
        if (m.Clipboard && !_clipboardRunning) { Services.ClipboardWatcher.Start(); _clipboardRunning = true; }
        else if (!m.Clipboard && _clipboardRunning) { Services.ClipboardWatcher.Dispose(); Services.Clipboard.Clear(); _clipboardRunning = false; }

        if (m.DownloadWatcher && !_downloadsRunning) { Services.Downloads.Start(); _downloadsRunning = true; }
        else if (!m.DownloadWatcher && _downloadsRunning) { Services.Downloads.Dispose(); _downloadsRunning = false; }
        Services.Clipboard.Capacity = m.ClipboardHistorySize;

        var n = _settings.Current.Notifications;
        Services.AppNotifications.Enabled = n.Enabled && n.AppNotifications;
    }

    private void ApplyMediaSettings()
    {
        var m = _settings.Current.Media;
        var media = Services.Media;
        media.Enabled = m.Enabled;
        media.PausedLinger = TimeSpan.FromSeconds(Math.Max(5, m.PausedLingerSeconds));
        foreach (var p in media.Providers)
        {
            p.IsEnabled = p.Kind switch
            {
                MediaProviderKind.Spotify => m.Spotify,
                MediaProviderKind.Chrome => m.Chrome,
                _ => m.OtherSessions,
            };
        }
        media.Recompute();
    }

    private void ApplyHotkeys()
    {
        _hotkeys.Apply(_settings.Current,
            () => Ui(() =>
            {
                Services.StateMachine.HotkeyPressed(_settings.Current.Behavior.HotkeyAction);
                if (Services.StateMachine.State == NotchState.Expanded) _vm.UpdateStatus(Services.Power, Services.Network, Services.Volume, _settings.Current.Modules);
            }),
            app => Ui(() =>
            {
                var result = Services.Launcher.Launch(app);
                if (!result.Success) Services.Bus.Publish(new CustomEvent($"Couldn't open {app.Name}", result.Error, "", Important: true));
            }));
    }

    private void SyncStartup()
    {
        var startup = Services.Startup;
        startup.RepairIfNeeded();
        var actual = startup.IsEnabled;
        if (_settings.Current.Behavior.StartWithWindows != actual)
        {
            // The registry is the source of truth (the user may have changed it in Task Manager).
            _settings.Current.Behavior.StartWithWindows = actual;
            _settings.Update(SettingsSection.None);
        }
    }

    private void UpdateStatus() => _vm.UpdateStatus(Services.Power, Services.Network, Services.Volume, _settings.Current.Modules);

    private void OnSettingsChanged(SettingsSection section)
    {
        if (section.HasFlag(SettingsSection.Appearance)) Notch.ApplyAppearance();
        if (section.HasFlag(SettingsSection.Animation)) Notch.ApplyAnimationSettings();
        if (section.HasFlag(SettingsSection.Behavior))
        {
            Notch.ApplyBehavior();
            ApplyHotkeys();
            var startup = Services.Startup;
            if (_settings.Current.Behavior.StartWithWindows != startup.IsEnabled) startup.SetEnabled(_settings.Current.Behavior.StartWithWindows);
            Notch.CheckForeground();
        }
        if (section.HasFlag(SettingsSection.Monitor)) _monitors.ApplyMode();
        if (section.HasFlag(SettingsSection.Media))
        {
            ApplyMediaSettings();
            _vm.ApplySettings(_settings.Current);
            Notch.UpdateLiveActivity();
            Notch.ApplyBehavior();
        }
        if (section.HasFlag(SettingsSection.QuickApps))
        {
            ShellIcons.Invalidate();
            Notch.OnQuickAppsChanged();
            ApplyHotkeys();
        }
        if (section.HasFlag(SettingsSection.Notifications)) ApplyModules();
        if (section.HasFlag(SettingsSection.Modules))
        {
            ApplyModules();
            _vm.ApplySettings(_settings.Current);
            UpdateStatus();
            Notch.Relayout();
        }
        if (section.HasFlag(SettingsSection.Advanced))
            Log.MinimumLevel = _settings.Current.Advanced.VerboseLogging ? LogLevel.Debug : LogLevel.Info;
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        Log.Info("Resumed from sleep");
        Ui(() =>
        {
            _ = Services.MediaHub.RefreshAllAsync();
            _monitors.ScheduleRefresh();
            Services.Timer.CheckCompleted();
            UpdateStatus();
            Notch.Relayout();
        });
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            Ui(() => { _monitors.ScheduleRefresh(); _ = Services.MediaHub.RefreshAllAsync(); });
    }

    private static void PlayTimerSound()
    {
        try
        {
            var alarm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");
            if (File.Exists(alarm)) new SoundPlayer(alarm).Play();
            else SystemSounds.Asterisk.Play();
        }
        catch (Exception ex) { Log.Debug($"Timer sound failed: {ex.Message}"); }
    }

    private async Task SeedQuickAppsAsync()
    {
        try
        {
            var installed = await InstalledAppScanner.ScanAsync();
            var favorites = InstalledAppScanner.DetectDefaultFavorites(installed);
            Log.Info($"App scan found {installed.Count} apps ({InstalledAppScanner.LastDiagnostics}); favorites: {string.Join(", ", favorites.Select(f => f.Name))}");
            Ui(() =>
            {
                if (_settings.Current.QuickApps.SeededDefaults || installed.Count == 0) return; // retry next launch if the scan found nothing
                if (_settings.Current.QuickApps.Apps.Count == 0) _settings.Current.QuickApps.Apps.AddRange(favorites);
                _settings.Current.QuickApps.SeededDefaults = true;
                _settings.Update(SettingsSection.QuickApps);
                Log.Info($"Seeded {favorites.Count} quick apps from installed applications");
            });
        }
        catch (Exception ex)
        {
            Log.Warn("Quick app seeding failed", ex);
        }
    }

    // ───────────────────────── windows ─────────────────────────

    public void OpenSettings(string? page)
    {
        if (_onboarding != null) { _onboarding.Activate(); return; }
        FluentResources.Ensure();
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                MemoryTrimmer.Schedule(TimeSpan.FromSeconds(3));
                if (!_settings.Current.Behavior.RunInBackground) Quit();
            };
        }
        _settingsWindow.Navigate(page);
        _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    public void ShowOnboarding()
    {
        FluentResources.Ensure();
        if (_onboarding != null) { _onboarding.Activate(); return; }
        _onboarding = new OnboardingWindow(this);
        _onboarding.Closed += (_, _) =>
        {
            _onboarding = null;
            MemoryTrimmer.Schedule(TimeSpan.FromSeconds(3));
            if (!_settings.Current.FirstRunCompleted)
            {
                // Closing the window counts as "skip": keep the defaults and start normally.
                _settings.Current.FirstRunCompleted = true;
                _settings.Update(SettingsSection.None);
            }
            Services.StateMachine.SetUserHidden(false);
            Services.Bus.Publish(new CustomEvent("You're all set", $"Hover the notch or press {_settings.Current.Behavior.Hotkey}", ""));
        };
        _onboarding.Show();
        _onboarding.Activate();
    }

    // ───────────────────────── pipe commands ─────────────────────────

    public void HandleCommand(string command)
    {
        Log.Info($"Command: {command}");
        var parts = command.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        switch (parts[0])
        {
            case "settings": OpenSettings(parts.Length > 1 ? parts[1] : null); return;
            case "onboarding": ShowOnboarding(); return;
            case "quit": Quit(); return;
            case "noop": return;
            case "debug" when parts.Length >= 2 && !_settings.Current.Advanced.DebugMode:
                Log.Warn("Ignored developer command: enable Settings → Advanced → Debug mode to allow them.");
                return;
            case "debug" when parts.Length >= 2:
                DebugCommands.Execute(this, parts[1], parts.Length > 2 ? parts[2] : "");
                return;
        }
    }

    // ───────────────────────── ITrayActions ─────────────────────────

    public bool IsNotchHidden => Services.StateMachine.IsUserHidden;
    public AnimationTheme CurrentTheme => _settings.Current.Animation.Theme;
    public MonitorMode MonitorMode => _settings.Current.Monitor.Mode;
    public string? SelectedMonitorDevice => MonitorSelector.FindSelected(_settings.Current.Monitor, _monitors.Monitors)?.DeviceName;
    IReadOnlyList<MonitorInfo> ITrayActions.Monitors => _monitors.Monitors;
    public bool StartWithWindows => Services.Startup.IsEnabled;
    public string VersionText => "v" + Version;

    public void ShowNotch() => Services.StateMachine.SetUserHidden(false);
    public void HideNotch() => Services.StateMachine.SetUserHidden(true);
    void ITrayActions.OpenSettings() => OpenSettings(null);
    public void SetTheme(AnimationTheme theme) => _settings.Update(s => s.Animation.Theme = theme, SettingsSection.Animation);
    public void SetMonitorMode(MonitorMode mode) => _settings.Update(s => s.Monitor.Mode = mode, SettingsSection.Monitor);

    public void SelectMonitor(string deviceName)
    {
        var monitor = _monitors.Monitors.FirstOrDefault(m => m.DeviceName == deviceName);
        if (monitor is null) return;
        _settings.Update(s =>
        {
            s.Monitor.Mode = MonitorMode.Selected;
            MonitorSelector.StoreSelection(s.Monitor, monitor);
        }, SettingsSection.Monitor);
    }

    public void SetStartWithWindows(bool enabled) => _settings.Update(s => s.Behavior.StartWithWindows = enabled, SettingsSection.Behavior);

    public void Quit()
    {
        Log.Info("Quit requested");
        Application.Current.Shutdown();
    }

    // ───────────────────────── helpers ─────────────────────────

    public void Ui(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (_disposed) return;
        if (_dispatcher.CheckAccess() && priority == DispatcherPriority.Normal) action();
        else _dispatcher.BeginInvoke(action, priority);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        foreach (var sub in _subscriptions) sub.Dispose();

        void Try(Action a, string what)
        {
            try { a(); } catch (Exception ex) { Log.Warn($"Disposing {what} failed", ex); }
        }
        Try(() => _settingsWindow?.Close(), "settings window");
        Try(() => _onboarding?.Close(), "onboarding");
        Try(() => _tray?.Dispose(), "tray");
        Try(() => _hotkeys?.Dispose(), "hotkeys");
        Try(() => Notch?.Dispose(), "notch");
        Try(() => _monitors?.Dispose(), "monitors");
        if (Services != null)
        {
            Try(Services.Foreground.Dispose, "foreground tracker");
            Try(Services.Media.Dispose, "media");
            Try(Services.MediaHub.Dispose, "media hub");
            Try(Services.Volume.Dispose, "volume");
            Try(Services.Brightness.Dispose, "brightness");
            Try(Services.Power.Dispose, "power");
            Try(Services.Network.Dispose, "network");
            Try(Services.Downloads.Dispose, "downloads");
            Try(Services.ClipboardWatcher.Dispose, "clipboard");
            Try(Services.AppNotifications.Dispose, "app notifications");
            Try(Services.Lyrics.Dispose, "lyrics");
        }
        Try(() => _window?.Close(), "notch window");
        Try(() => _settings?.Dispose(), "settings");
    }
}
