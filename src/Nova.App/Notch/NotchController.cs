using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Nova.App.Controls;
using Nova.App.Notch.Rendering;
using Nova.App.Notch.Views;
using Nova.App.Services;
using Nova.Core.Animation;
using Nova.Core.Events;
using Nova.Core.Logging;
using Nova.Core.Media;
using Nova.Core.Monitors;
using Nova.Core.Notifications;
using Nova.Core.Settings;
using Nova.Core.State;
using Nova.Platform.Services;

namespace Nova.App.Notch;

/// <summary>
/// Glue between the state machine (what), the animator (how it moves), the views (what's inside) and
/// the window (where). All notch behaviour flows through here; nothing else touches the notch window.
/// </summary>
public sealed class NotchController : IDisposable
{
    private const double WindowMargin = 64;
    /// <summary>Old content is swapped for new once it's nearly invisible (blur hides the rest).</summary>
    private const double SwapThreshold = 0.12;

    private readonly NotchWindow _window;
    private readonly NotchViewModel _vm;
    private readonly AppServices _s;
    private readonly MonitorService _monitors;
    private readonly NotchStateMachine _sm;
    private readonly NotchAnimator _animator;
    private readonly DispatcherTimer _deadline;
    private readonly Dictionary<string, FrameworkElement> _views = new();
    private readonly Func<double, bool> _onFrame;
    private readonly RectangleGeometry _clip = new();
    private readonly ScaleTransform _contentScale = new(1, 1);
    private readonly TranslateTransform _contentOffset = new();
    private readonly ScaleTransform _contentLayoutScale = new(1, 1);
    private readonly BlurEffect _contentBlur = new() { RenderingBias = RenderingBias.Performance };

    private string? _currentViewKey;
    private string? _pendingViewKey;
    private NotchGeometry _contentBaseSize;
    private NotchGeometry _pendingBaseSize;
    private AnimationProfile _profile;
    private AnimationThemeDefinition? _previewTheme;
    private double _sizeScale = 1;
    private double _windowTopOffset;
    private NotchPosition _position;
    private IReadOnlyList<Color>? _auroraColors;
    private double _windowWidthDip;
    private double _windowHeightDip;
    private bool _frameRunning;
    private double _ambientAccumulator;
    private double _discElapsed;
    private bool _keyboardRequested;
    private bool _started;
    private bool _windowCompact;
    private bool _windowAtTop;

    public NotchController(NotchWindow window, NotchViewModel vm, AppServices services, MonitorService monitors)
    {
        _window = window;
        _vm = vm;
        _s = services;
        _monitors = monitors;
        _sm = services.StateMachine;
        _profile = BuildProfile(services.Settings.Current);
        _vm.ReduceMotion = _profile.ReducedMotion;
        _animator = new NotchAnimator(_profile, NotchLayout.For(_sm.Snapshot, LayoutOptions()));
        _onFrame = OnFrame;

        _deadline = new DispatcherTimer(DispatcherPriority.Normal);
        _deadline.Tick += (_, _) =>
        {
            _deadline.Stop();
            _sm.Tick();
            ScheduleDeadline();
        };

        _window.DataContext = vm;
        _window.ContentClipHost.Clip = _clip;
        _window.ContentHost.LayoutTransform = _contentLayoutScale;
        _window.ContentHost.RenderTransform = new TransformGroup { Children = { _contentScale, _contentOffset } };
        _window.Surface.GeometryChanged += g => _window.Shadow.SetGeometry(g);
        _window.IsInteractiveAt = p => _window.Surface.IsInteractiveAt(p);
        _window.Root.SizeChanged += (_, _) => ApplyFrame(_animator.Frame);

        _window.Root.MouseEnter += (_, _) => { _sm.PointerEntered(); ScheduleDeadline(); };
        _window.Root.MouseLeave += (_, _) => { _sm.PointerExited(); ScheduleDeadline(); };
        _window.Root.MouseLeftButtonUp += OnBackgroundClick;
        _window.Root.MouseWheel += OnMouseWheel;
        _window.PreviewKeyDown += OnKeyDown;
        _window.Deactivated += (_, _) =>
        {
            if (_window.IsKeyboardActive && _sm.Snapshot.IsInteractive) _sm.Collapse();
        };
        _window.DisplayChanged += () => _monitors.ScheduleRefresh();
        _window.SystemSettingChanged += () => ApplyAnimationSettings();

        _sm.Changed += OnStateChanged;
        _monitors.SelectionChanged += OnMonitorChanged;
        SpinningDisc.SpinningChanged += () => { if (SpinningDisc.AnySpinning) RunFrames(); };
    }

    public NotchSnapshot Snapshot => _sm.Snapshot;

    public string Diagnostics
    {
        get
        {
            var f = _animator.Frame;
            return $"animating={_animator.IsAnimating} frameRunning={_frameRunning} pending={_pendingViewKey} view={_currentViewKey} ambient={_window.Surface.AmbientLevel:0.00} discs={SpinningDisc.AnySpinning} compactWindow={_windowCompact} autoHide={AutoHideActive} windowAtTop={_windowAtTop} w={f.Width:0.0} h={f.Height:0.0} r={f.Radius:0.0} op={f.Opacity:0.00} content={f.ContentOpacity:0.000} scale={f.ContentScale:0.0000} off={f.ContentOffsetY:0.00} glow={f.Glow:0.000} bulge={f.Bulge:0.00} stretch={f.StretchX:0.0000} spec={f.Specular:0.00} target={_animator.Target}";
        }
    }
    public string CurrentThemeName => (_previewTheme ?? _profile.Theme).Name;

    // ───────────────────────── lifecycle ─────────────────────────

    public void Start(bool visible)
    {
        ApplyAllSettings();
        _window.Show();
        _started = true;
        if (_monitors.Current is { } selection) OnMonitorChanged(selection);
        _sm.SetUserHidden(!visible);
        var initial = NotchLayout.For(_sm.Snapshot, LayoutOptions());
        // Start collapsed into the top edge, then grow into place: a calm "boot" animation.
        _animator.Snap(initial with { Height = 0, Width = initial.Width * 0.6 }, visible);
        SwapTo(ViewKey(_sm.Snapshot), BaseSize(_sm.Snapshot));
        _animator.AnimateTo(initial, visible && _sm.State != NotchState.Hidden);
        RunFrames();
    }

    // ───────────────────────── settings ─────────────────────────

    public void ApplyAllSettings()
    {
        ApplyAppearance();
        ApplyAnimationSettings();
        ApplyBehavior();
    }

    public void ApplyAppearance()
    {
        var a = _s.Settings.Current.Appearance;
        var palette = NotchPalette.For(a.Material);
        var accent = ColorMath.Parse(a.AccentColor, Color.FromRgb(139, 156, 255));
        _window.Surface.Configure(palette, accent, a.Opacity, a.GlowAmount, a.Shape, _profile.GlassLayers || a.Material == NotchMaterial.DarkGlass);
        _window.Shadow.Configure(palette.Shadow, palette.ShadowOpacity, 1);
        palette.ApplyContentResources(Application.Current.Resources, accent);
        _vm.ApplySettings(_s.Settings.Current);

        // Left / right: the shape keeps a small gap to the screen edge (the floating gap, if any).
        _window.Surface.Anchor = a.Position;
        _window.Surface.SideInset = a.Shape == NotchShape.Floating ? Math.Max(8, a.TopOffset) : 22;
        _window.ContentClipHost.HorizontalAlignment = a.Position switch
        {
            NotchPosition.Left => HorizontalAlignment.Left,
            NotchPosition.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
        _window.ContentClipHost.Margin = new Thickness(_window.Surface.SideInset, _window.ContentClipHost.Margin.Top, _window.Surface.SideInset, 0);
        UpdateAuroraColors();

        var topOffset = a.Shape == NotchShape.Floating ? a.TopOffset : 0;
        if (Math.Abs(_sizeScale - a.SizeScale) > 0.001 || Math.Abs(_windowTopOffset - topOffset) > 0.001 || _windowWidthDip == 0 || _position != a.Position)
        {
            _sizeScale = a.SizeScale;
            _windowTopOffset = topOffset;
            _position = a.Position;
            _contentLayoutScale.ScaleX = _contentLayoutScale.ScaleY = _sizeScale;
            UpdateWindowSize();
        }
        Relayout();
    }

    public void ApplyAnimationSettings()
    {
        _profile = BuildProfile(_s.Settings.Current);
        _vm.ReduceMotion = _profile.ReducedMotion;
        _animator.SetProfile(_previewTheme != null ? BuildProfile(_s.Settings.Current, _previewTheme) : _profile);
        ApplyAppearanceFlags();
        UpdateAuroraColors();
        UpdateAmbient();
        RunFrames();
    }

    /// <summary>
    /// Aurora colors: the cover art's palette while media plays (if enabled), else the user's custom
    /// colors, else shades of the accent color.
    /// </summary>
    private void UpdateAuroraColors()
    {
        var a = _s.Settings.Current.Animation;
        IReadOnlyList<Color>? colors = null;
        if (a.AuroraFollowsArtwork && _vm.HasMedia && _vm.ArtworkPalette is { Count: > 0 } art) colors = art;
        else if (a.AuroraColors == AuroraColorSource.Custom)
            colors = a.AuroraCustomColors.Select(hex => ColorMath.Parse(hex, Color.FromRgb(139, 156, 255))).ToList();

        if (colors is null ? _auroraColors is null : _auroraColors != null && colors.SequenceEqual(_auroraColors)) return;
        _auroraColors = colors;
        _window.Surface.SetAuroraColors(colors);
    }

    private void ApplyAppearanceFlags()
    {
        var a = _s.Settings.Current.Appearance;
        var palette = NotchPalette.For(a.Material);
        var accent = ColorMath.Parse(a.AccentColor, Color.FromRgb(139, 156, 255));
        _window.Surface.Configure(palette, accent, a.Opacity, a.GlowAmount, a.Shape, _animator.Profile.GlassLayers || a.Material == NotchMaterial.DarkGlass);
    }

    public void ApplyBehavior()
    {
        var b = _s.Settings.Current.Behavior;
        _sm.ExpandOnHover = b.ExpandOnHover;
        _sm.HoverDelay = TimeSpan.FromMilliseconds(b.HoverDelayMs);
        _sm.CollapseDelay = TimeSpan.FromMilliseconds(b.CollapseDelayMs);
        _sm.InteractiveCollapseDelay = TimeSpan.FromMilliseconds(Math.Max(b.CollapseDelayMs, _s.Settings.Current.Media.AutoCollapseDelayMs));
        _window.SetCaptureExcluded(b.ExcludeFromScreenCapture);
        _window.Surface.ExtendedHitZone = AutoHideActive;
        Relayout();
    }

    /// <summary>Whether the idle notch should currently be the thin auto-hide line.</summary>
    private bool AutoHideActive => _s.Settings.Current.Behavior.AutoHideMode switch
    {
        AutoHideMode.Always => true,
        AutoHideMode.WhenWindowAtTop => _windowAtTop,
        _ => false,
    };

    private static AnimationProfile BuildProfile(AppSettings settings, AnimationThemeDefinition? theme = null) =>
        AnimationProfile.Build(theme ?? AnimationThemes.Get(settings.Animation.Theme), settings.Animation, !SystemParameters.ClientAreaAnimation);

    /// <summary>Plays the given theme on the real notch (Settings → Animations → "Preview on notch").</summary>
    public void PreviewTheme(AnimationTheme theme)
    {
        _previewTheme = AnimationThemes.Get(theme);
        _animator.SetProfile(BuildProfile(_s.Settings.Current, _previewTheme));
        ApplyAppearanceFlags();
        UpdateAmbient();
        _sm.Navigate(NotchState.Expanded);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1700) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _sm.Collapse();
            var restore = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            restore.Tick += (_, _) =>
            {
                restore.Stop();
                _previewTheme = null;
                ApplyAnimationSettings();
            };
            restore.Start();
        };
        timer.Start();
    }

    private LayoutOptions LayoutOptions() => new()
    {
        SizeScale = _s.Settings.Current.Appearance.SizeScale,
        RadiusScale = _s.Settings.Current.Appearance.CornerRadiusScale,
        AutoHide = AutoHideActive,
        QuickAppCount = _vm.QuickApps.Count,
        HomeHasMedia = _vm.HasMedia,
        HomeHasTools = _vm.HasTools,
        Shape = _s.Settings.Current.Appearance.Shape,
        TopOffset = _s.Settings.Current.Appearance.TopOffset,
    };

    private NotchGeometry BaseSize(NotchSnapshot snapshot) =>
        NotchLayout.For(snapshot, LayoutOptions() with { SizeScale = 1, RadiusScale = 1 });

    /// <summary>Re-targets the current state (after a settings or content change).</summary>
    public void Relayout()
    {
        if (!_started) return;
        var target = NotchLayout.For(_sm.Snapshot, LayoutOptions());
        SyncView(_sm.Snapshot);
        _animator.AnimateTo(target, _sm.State != NotchState.Hidden);
        RunFrames();
    }

    private void UpdateWindowSize()
    {
        _windowWidthDip = NotchLayout.MaxWidth * _sizeScale + WindowMargin * 2;
        _windowHeightDip = NotchLayout.MaxHeight * _sizeScale + _windowTopOffset + WindowMargin;
        if (_monitors.Current is { } selection) Place(selection.Monitor);
    }

    // ───────────────────────── monitors ─────────────────────────

    private void OnMonitorChanged(MonitorSelection selection)
    {
        if (!_started) return;
        var moved = _window.Placement != default && !_window.Placement.Equals(DpiMath.PlaceTop(selection.Monitor, _windowWidthDip, _windowHeightDip, _position));
        Place(selection.Monitor);
        if (moved && _sm.State != NotchState.Hidden)
        {
            // Re-enter from the top edge on the new monitor instead of teleporting.
            var target = NotchLayout.For(_sm.Snapshot, LayoutOptions());
            _animator.Snap(target with { Height = 0 }, true);
            _animator.AnimateTo(target, true);
            RunFrames();
        }
        CheckForeground();
    }

    private void Place(MonitorInfo monitor)
    {
        _windowCompact = false;
        _window.Place(DpiMath.PlaceTop(monitor, _windowWidthDip, _windowHeightDip, _position));
    }

    /// <summary>
    /// Re-evaluates the foreground window: suppresses the notch while a fullscreen game / video /
    /// presentation covers its monitor, and drives "auto-hide when a window reaches the top".
    /// </summary>
    public void CheckForeground()
    {
        if (_monitors.Current is not { } selection) return;
        var b = _s.Settings.Current.Behavior;

        var atTop = _s.Foreground.HasWindowAtTop(selection.Monitor);
        if (atTop != _windowAtTop)
        {
            _windowAtTop = atTop;
            if (b.AutoHideMode == AutoHideMode.WhenWindowAtTop)
            {
                _window.Surface.ExtendedHitZone = AutoHideActive;
                Relayout();
            }
        }

        var kind = _s.Foreground.DetectFullscreen(selection.Monitor);
        var suppress = kind switch
        {
            Platform.Services.FullscreenKind.Game => b.HideInFullscreenGames,
            Platform.Services.FullscreenKind.Video => b.HideInFullscreenVideo,
            Platform.Services.FullscreenKind.Presentation => b.HideDuringPresentations,
            _ => false,
        };
        if (suppress != _sm.IsSuppressed) Log.Info(suppress ? $"Fullscreen {kind} detected — hiding notch" : "Fullscreen ended — showing notch");
        _sm.SetSuppressed(suppress);
        if (!suppress) _window.ReassertTopmost();
    }

    // ───────────────────────── live data ─────────────────────────

    public void OnMediaChanged(MediaSessionInfo? session)
    {
        var hadMedia = _vm.HasMedia;
        _vm.SetMedia(session);
        UpdateAuroraColors();
        UpdateLiveActivity();
        if (hadMedia != _vm.HasMedia && _sm.State == NotchState.Expanded) Relayout();
    }

    public void UpdateLiveActivity()
    {
        var showMedia = _s.Settings.Current.Media.Enabled && _s.Settings.Current.Media.ShowCompactIndicator && _s.Media.HasActiveSession;
        _sm.SetLiveActivity(showMedia, _s.Timer.IsActive);
    }

    public void OnQuickAppsChanged()
    {
        _vm.SetQuickApps(_s.Settings.Current.QuickApps.Apps);
        Relayout();
    }

    /// <summary>Routes a bus event to a notch notification (UI thread).</summary>
    public void HandleEvent(NovaEvent evt)
    {
        if (evt is AppNotificationRemovedEvent removed)
        {
            // The call stopped ringing / the message was dismissed in Windows: drop it here too.
            _sm.Dismiss($"app:{removed.Id}");
            ScheduleDeadline();
            return;
        }
        var notification = NotificationRouter.Route(evt, _s.Settings.Current);
        if (notification is null) return;
        if (evt is AppNotificationEvent app) _vm.AddToHistory(app);
        // While the media panel is open the user already sees the track; don't cover it.
        if (notification.Style == NotificationStyle.Media && _sm.State is NotchState.Media or NotchState.Expanded) return;
        if (_sm.Notify(notification) && _sm.State == NotchState.Notification) _animator.Pulse();
        ScheduleDeadline();
    }

    // ───────────────────────── state → visuals ─────────────────────────

    private void OnStateChanged(NotchTransition t)
    {
        var snap = t.Current;
        Log.Debug($"Notch {t.Previous.State} -> {snap.State} {snap.Tool} media={snap.HasMedia}");
        UpdateViewModel(snap);
        SyncView(snap);
        ApplyHover(snap);
        var target = NotchLayout.For(snap, LayoutOptions());
        _animator.AnimateTo(target, snap.State != NotchState.Hidden);
        if (snap.State == NotchState.Notification && t.Previous.Notification != snap.Notification)
            _animator.Pulse(snap.Notification?.Priority == NotificationPriority.High ? 1.3 : 0.8);

        // Keyboard tools activate the window; anything else hands focus back.
        _keyboardRequested = snap.State == NotchState.Tool && snap.Tool is NotchTool.Calculator or NotchTool.Search;
        if (_keyboardRequested && !_window.IsKeyboardActive)
        {
            _window.ActivateForKeyboard();
            _sm.SetPinned(true);
        }
        else if (!_keyboardRequested && _window.IsKeyboardActive)
        {
            _window.ReleaseKeyboard();
            _sm.SetPinned(false);
        }
        if (_keyboardRequested && _pendingViewKey is null) FocusKeyboardView();

        if (t.StateChanged) _window.ReassertTopmost();
        UpdateAmbient();
        RunFrames();
        ScheduleDeadline();
    }

    private void UpdateViewModel(NotchSnapshot snap)
    {
        if (snap.State == NotchState.Notification) _vm.SetNotification(snap.Notification);
        _vm.SetProgressVisible(snap.State is NotchState.Media or NotchState.Expanded);
        _vm.SetClockVisible(snap.State == NotchState.Expanded);
        _vm.SetTimerVisible((snap.State is NotchState.Compact or NotchState.Hover && snap.HasTimer && !snap.HasMedia) || snap.Tool == NotchTool.Timer);
        _vm.SetHistoryVisible(snap.Tool == NotchTool.Notifications);
        if (snap.State == NotchState.Expanded) _vm.UpdateStatus(_s.Power, _s.Network, _s.Volume, _s.Settings.Current.Modules);
    }

    /// <summary>Starts the cross-fade to the snapshot's view, or just resizes the current one.</summary>
    private void SyncView(NotchSnapshot snap)
    {
        var key = ViewKey(snap) ?? _currentViewKey;
        var baseSize = BaseSize(snap);
        if (key != _currentViewKey)
        {
            _pendingViewKey = key;
            _pendingBaseSize = baseSize;
            if (_animator.Frame.ContentOpacity < SwapThreshold || _currentViewKey is null) SwapTo(key, baseSize);
            else _animator.SetContentVisible(false);
        }
        else
        {
            // Back to the view that is still showing (e.g. a quick hover in and out): cancel the
            // pending swap and fade the content back in instead of swapping to a stale view.
            if (_pendingViewKey != null)
            {
                _pendingViewKey = null;
                _animator.SetContentVisible(true);
            }
            SetContentHostSize(baseSize);
        }
    }

    private string? ViewKey(NotchSnapshot snap) => snap.State switch
    {
        NotchState.Hidden => null,
        // The auto-hide sliver (idle or music pill) is only a few pixels tall: a grab handle with no content.
        NotchState.Idle or NotchState.Compact when NotchLayout.IsSliver(snap, LayoutOptions()) => "handle",
        NotchState.Idle => "idle",
        NotchState.Hover => snap.HasMedia || snap.HasTimer ? "compact" : "idle",
        NotchState.Compact => "compact",
        NotchState.Notification => "notification",
        NotchState.Expanded => "home",
        NotchState.Media => "media",
        NotchState.QuickApps => "quickapps",
        NotchState.Tool => "tool:" + snap.Tool,
        _ => "idle",
    };

    private FrameworkElement GetView(string key)
    {
        if (_views.TryGetValue(key, out var view)) return view;
        view = key switch
        {
            "idle" => new IdleView(),
            "handle" => new Grid(),
            "compact" => new CompactView(),
            "notification" => new NotificationView(),
            "home" => new HomeView(),
            "media" => new MediaView(),
            "quickapps" => new QuickAppsView(),
            "tool:Timer" => new TimerView(),
            "tool:Calculator" => new CalculatorView(),
            "tool:Clipboard" => new ClipboardView(),
            "tool:Search" => new SearchView(),
            "tool:Notifications" => new NotificationsView(),
            _ => new IdleView(),
        };
        _views[key] = view;
        return view;
    }

    private void SwapTo(string? key, NotchGeometry baseSize)
    {
        _pendingViewKey = null;
        if (key is null) return;
        _window.ContentHost.Content = GetView(key);
        _currentViewKey = key;
        SetContentHostSize(baseSize);
        ApplyHover(_sm.Snapshot);
        _animator.SetContentVisible(true);
        if (_keyboardRequested) FocusKeyboardView();
    }

    private void FocusKeyboardView()
    {
        if (_window.ContentHost.Content is IKeyboardView kv)
            _window.Dispatcher.BeginInvoke(kv.FocusInput, DispatcherPriority.Input);
    }

    private void SetContentHostSize(NotchGeometry baseSize)
    {
        _contentBaseSize = baseSize;
        _window.ContentHost.Width = baseSize.Width;
        _window.ContentHost.Height = baseSize.Height;
        _window.ContentClipHost.Width = baseSize.Width * _sizeScale;
        _window.ContentClipHost.Height = baseSize.Height * _sizeScale;
    }

    private void ApplyHover(NotchSnapshot snap)
    {
        var hover = snap.State == NotchState.Hover;
        if (_window.ContentHost.Content is IdleView idle) idle.SetHover(hover);
        if (_window.ContentHost.Content is CompactView compact)
        {
            compact.SetHover(hover && snap.HasMedia);
            compact.SetActive(snap.State is NotchState.Compact or NotchState.Hover);
        }
    }

    private void UpdateAmbient()
    {
        var p = _animator.Profile;
        var state = _sm.State;
        double level = 0;
        if (p.AmbientLight)
        {
            level = state switch
            {
                NotchState.Hidden => 0,
                NotchState.Idle => _s.Settings.Current.Animation.AmbientEffectsWhenIdle ? 0.22 : 0,
                NotchState.Hover or NotchState.Compact => 0.32,
                NotchState.Notification => 0.62,
                _ => 0.5,
            };
        }
        else if (_s.Settings.Current.Animation.AmbientEffectsWhenIdle && state is not NotchState.Hidden && !p.ReducedMotion && p.Theme.Theme == AnimationTheme.Aurora)
        {
            level = 0.2;
        }
        _window.Surface.AmbientLevel = level * Math.Max(0.35, _s.Settings.Current.Appearance.GlowAmount * 1.6);
    }

    // ───────────────────────── frames ─────────────────────────

    private void RunFrames()
    {
        if (_animator.IsAnimating) EnsureFullWindow();
        if (_frameRunning) return;
        _frameRunning = true;
        FrameClock.Run(_onFrame);
    }

    private bool OnFrame(double dt)
    {
        var animating = _animator.Step(dt);
        if (_pendingViewKey != null && _animator.Frame.ContentOpacity < SwapThreshold) SwapTo(_pendingViewKey, _pendingBaseSize);

        var ambient = _window.Surface.AmbientLevel > 0.01 && _animator.Profile.AmbientLight && !_animator.Profile.ReducedMotion;
        if (ambient) _window.Surface.AmbientTime += dt;
        var discs = SpinningDisc.AnySpinning;
        _discElapsed += dt;

        if (!animating && (ambient || discs))
        {
            // Ambient light and spinning artwork only need ~30 fps; skip the frames in between.
            _ambientAccumulator += dt;
            if (_ambientAccumulator < 1 / 30.0) return true;
            _ambientAccumulator = 0;
        }

        // Discs turn on the frames rendered here, so they never add frames of their own.
        if (discs) SpinningDisc.Advance(_discElapsed);
        _discElapsed = 0;
        if (animating || ambient || !discs) ApplyFrame(_animator.Frame);
        if (!animating && _pendingViewKey is null && !_windowCompact) ShrinkWindowToShape();
        var keepRunning = animating || ambient || discs || _pendingViewKey != null;
        if (!keepRunning)
        {
            _frameRunning = false;
        }
        return keepRunning;
    }

    private void ApplyFrame(in NotchFrame f)
    {
        _window.Surface.SetFrame(f);

        var bounds = _window.Surface.ShapeBounds;
        var host = _window.ContentClipHost;

        // Light views (the pills) are laid out at the animated size every frame, so their artwork and
        // equalizer ride along with the notch's edges instead of jumping to the final layout.
        if (_pendingViewKey is null && _window.ContentHost.Content is IFluidView && bounds.Width > 1)
        {
            _window.ContentHost.Width = bounds.Width / _sizeScale;
            _window.ContentHost.Height = Math.Max(1, bounds.Height / _sizeScale);
            host.Width = bounds.Width;
            host.Height = Math.Max(1, bounds.Height);
        }

        var hostWidth = host.Width;
        var inset = _window.Surface.SideInset;
        var hostX = _position switch
        {
            NotchPosition.Left => inset,
            NotchPosition.Right => _window.Root.ActualWidth - inset - hostWidth,
            _ => (_window.Root.ActualWidth - hostWidth) / 2,
        };
        var hostY = _window.Surface.ShapeTop;
        if (Math.Abs(host.Margin.Top - hostY) > 0.01) host.Margin = new Thickness(inset, hostY, inset, 0);
        var radius = _window.Surface.ShapeRadius;
        _clip.Rect = new Rect(bounds.X - hostX, bounds.Y - hostY, bounds.Width, bounds.Height);
        _clip.RadiusX = _clip.RadiusY = radius;

        var content = _window.ContentHost;
        content.Opacity = f.ContentOpacity * f.Opacity;
        _contentScale.ScaleX = _contentScale.ScaleY = f.ContentScale;
        _contentOffset.Y = f.ContentOffsetY;
        if (f.ContentBlur > 0.5)
        {
            _contentBlur.Radius = f.ContentBlur;
            if (!ReferenceEquals(content.Effect, _contentBlur)) content.Effect = _contentBlur;
        }
        else if (content.Effect != null)
        {
            content.Effect = null;
        }
        content.IsHitTestVisible = f.ContentOpacity > 0.6;
    }

    /// <summary>
    /// A layered window copies its whole surface to the CPU on every rendered frame, so at rest the
    /// window is shortened to the notch plus its shadow. Any equalizer/ambient animation then costs a
    /// fraction of what it would at full size. Only the height changes: the window keeps its left/top
    /// edge, so the bitmap Windows keeps showing until WPF re-renders at the new size still lines up.
    /// (Changing the width re-centers the window and makes the notch jump sideways for a frame.)
    /// </summary>
    private void ShrinkWindowToShape()
    {
        if (_monitors.Current is not { } selection) return;
        var b = _window.Surface.ShapeBounds;
        var width = _windowWidthDip;
        var height = Math.Min(_windowHeightDip, Math.Ceiling(b.Bottom + WindowMargin * 0.85));
        _windowCompact = true;
        _window.Place(DpiMath.PlaceTop(selection.Monitor, width, height, _position));
    }

    private void EnsureFullWindow()
    {
        if (!_windowCompact) return;
        _windowCompact = false;
        if (_monitors.Current is { } selection) Place(selection.Monitor);
    }

    private void ScheduleDeadline()
    {
        _deadline.Stop();
        if (_sm.NextDeadline is not { } next) return;
        var due = next - DateTimeOffset.UtcNow;
        _deadline.Interval = due < TimeSpan.FromMilliseconds(10) ? TimeSpan.FromMilliseconds(10) : due;
        _deadline.Start();
    }

    // ───────────────────────── input ─────────────────────────

    private void OnBackgroundClick(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        // Clicking another app's notification opens that app (where it can be answered / replied to).
        if (_sm.State == NotchState.Notification && _sm.Snapshot.Notification?.Source is AppNotificationEvent app) _vm.OpenNotificationApp(app);
        if (!_sm.Snapshot.IsInteractive) _sm.Click();
        ScheduleDeadline();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        // Scrolling over the notch adjusts the system volume (the indicator confirms it).
        if (_s.Volume.IsAvailable) _s.Volume.SetLevel(_s.Volume.Level + (e.Delta > 0 ? 0.02 : -0.02));
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _sm.Collapse();
            e.Handled = true;
        }
    }

    public void Dispose()
    {
        FrameClock.Stop(_onFrame);
        _deadline.Stop();
        _sm.Changed -= OnStateChanged;
        _monitors.SelectionChanged -= OnMonitorChanged;
    }
}
