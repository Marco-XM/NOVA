using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Nova.App.Notch.Rendering;
using Nova.Core.Animation;
using Nova.Core.Notifications;
using Nova.Core.Settings;
using Nova.Core.State;

namespace Nova.App.Controls;

/// <summary>
/// A live, real-time preview of an animation theme: the same renderer and animator as the real notch,
/// playing a short loop (idle → hover → live activity → expanded → notification) over the desktop
/// wallpaper. Content is drawn as neutral placeholder shapes; it only renders while visible.
/// </summary>
public sealed class NotchPreview : Border
{
    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(nameof(Theme), typeof(AnimationTheme), typeof(NotchPreview),
        new PropertyMetadata(AnimationTheme.Liquid, (d, _) => ((NotchPreview)d).OnThemeChanged()));
    public static readonly DependencyProperty ShowBackdropProperty = DependencyProperty.Register(nameof(ShowBackdrop), typeof(bool), typeof(NotchPreview), new PropertyMetadata(true));

    private static ImageSource? _wallpaper;
    private static bool _wallpaperLoaded;

    private readonly NotchSurface _surface = new();
    private readonly NotchShadow _shadow = new();
    private readonly SkeletonLayer _skeleton;
    private readonly Grid _stage;
    private double _layoutScale = 1;
    private readonly DispatcherTimer _script;
    private readonly Func<double, bool> _onFrame;
    private NotchAnimator _animator;
    private int _step;
    private bool _frameRunning;

    private static readonly (NotchSnapshot Snapshot, double Seconds)[] Script =
    {
        (Snap(NotchState.Idle), 1.1),
        (Snap(NotchState.Hover), 0.8),
        (Snap(NotchState.Compact, media: true), 1.3),
        (Snap(NotchState.Expanded, media: true), 1.9),
        (Snap(NotchState.Compact, media: true), 0.9),
        (Snap(NotchState.Notification, media: true, notification: true), 1.6),
        (Snap(NotchState.Idle), 0.6),
    };

    public NotchPreview()
    {
        ClipToBounds = true;
        CornerRadius = new CornerRadius(10);
        _skeleton = new SkeletonLayer(_surface);
        _stage = new Grid { Width = 640, Height = 280 };
        var stage = _stage;
        stage.Children.Add(_shadow);
        stage.Children.Add(_surface);
        stage.Children.Add(_skeleton);
        Child = new Viewbox { Child = stage, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top };
        _surface.GeometryChanged += g => _shadow.SetGeometry(g);
        _onFrame = OnFrame;

        _animator = new NotchAnimator(BuildProfile(), Geometry(Script[0].Snapshot));
        _script = new DispatcherTimer(DispatcherPriority.Background);
        _script.Tick += (_, _) => Advance();
        IsVisibleChanged += (_, _) => UpdateRunning();
        SizeChanged += (_, _) => FitStage();
        Loaded += (_, _) =>
        {
            ApplyLook();
            UpdateRunning();
            if (TryHost() is { } host) host.Services.Settings.Changed += OnSettingsChanged;
        };
        Unloaded += (_, _) =>
        {
            Stop();
            if (TryHost() is { } host) host.Services.Settings.Changed -= OnSettingsChanged;
        };
    }

    public AnimationTheme Theme { get => (AnimationTheme)GetValue(ThemeProperty); set => SetValue(ThemeProperty, value); }
    public bool ShowBackdrop { get => (bool)GetValue(ShowBackdropProperty); set => SetValue(ShowBackdropProperty, value); }

    private static NotchSnapshot Snap(NotchState state, bool media = false, bool notification = false) =>
        new(state, NotchTool.None, notification ? new NotchNotification { Title = "Preview", Style = NotificationStyle.Media } : null, media, false, HiddenReason.None);

    private static AppSettings? Settings => Application.Current is App ? TryHost()?.Services.Settings.Current : null;

    private static AppHost? TryHost()
    {
        try { return App.Host; } catch { return null; }
    }

    private AnimationProfile BuildProfile()
    {
        var animation = Settings?.Animation ?? new AnimationSettings();
        return AnimationProfile.Build(AnimationThemes.Get(Theme), animation, !SystemParameters.ClientAreaAnimation);
    }

    /// <summary>
    /// Sizes the logical stage to the control's aspect ratio. Small tiles use a narrower stage and a
    /// scaled-down layout so the notch stays large enough to read.
    /// </summary>
    private void FitStage()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var small = ActualWidth < 360;
        var aspect = ActualWidth / ActualHeight;
        // Wide enough for the expanded panel and tall enough that it's never cropped.
        var width = small ? Math.Max(470, 205 * aspect) : Math.Max(650, 300 * aspect);
        _stage.Width = width;
        _stage.Height = width * ActualHeight / ActualWidth;
        var scale = small ? 0.74 : 1.0;
        if (Math.Abs(scale - _layoutScale) > 0.001)
        {
            _layoutScale = scale;
            _skeleton.Scale = scale;
            _animator.Snap(Geometry(Script[Math.Max(0, _step - 1) % Script.Length].Snapshot));
        }
    }

    private NotchGeometry Geometry(NotchSnapshot snapshot) =>
        NotchLayout.For(snapshot, new LayoutOptions
        {
            QuickAppCount = 6,
            HomeHasMedia = true,
            SizeScale = _layoutScale,
            RadiusScale = Settings?.Appearance.CornerRadiusScale ?? 1,
            Shape = Settings?.Appearance.Shape ?? NotchShape.Attached,
            TopOffset = Settings?.Appearance.TopOffset ?? 0,
        });

    private void OnSettingsChanged(SettingsSection section)
    {
        if ((section & (SettingsSection.Appearance | SettingsSection.Animation)) == 0) return;
        _animator.SetProfile(BuildProfile());
        ApplyLook();
        // Shape and top offset are part of the layout now; re-target the current step.
        _animator.AnimateTo(Geometry(Script[Math.Max(0, _step - 1) % Script.Length].Snapshot));
        if (IsVisible && !_frameRunning)
        {
            _frameRunning = true;
            FrameClock.Run(_onFrame);
        }
    }

    private void OnThemeChanged()
    {
        _animator.SetProfile(BuildProfile());
        ApplyLook();
        _step = 0;
        if (IsVisible) Advance();
    }

    /// <summary>Re-reads appearance settings (material, accent, shape).</summary>
    public void ApplyLook()
    {
        var a = Settings?.Appearance ?? new AppearanceSettings();
        var palette = NotchPalette.For(a.Material);
        var accent = ColorMath.Parse(a.AccentColor, Color.FromRgb(139, 156, 255));
        var profile = _animator.Profile;
        _surface.Configure(palette, accent, a.Opacity, a.GlowAmount, a.Shape, profile.GlassLayers || a.Material == NotchMaterial.DarkGlass);
        _shadow.Configure(palette.Shadow, palette.ShadowOpacity, 1);
        _skeleton.Light = palette.IsLight;
        _surface.AmbientLevel = profile.AmbientLight ? 0.5 * Math.Max(0.35, a.GlowAmount * 1.6) : 0;
        var animation = Settings?.Animation;
        _surface.SetAuroraColors(animation?.AuroraColors == AuroraColorSource.Custom
            ? animation.AuroraCustomColors.Select(hex => ColorMath.Parse(hex, Color.FromRgb(139, 156, 255))).ToList()
            : null);
        Background = ShowBackdrop ? Backdrop() : Brushes.Transparent;
    }

    private static Brush Backdrop()
    {
        if (!_wallpaperLoaded)
        {
            _wallpaperLoaded = true;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                var path = key?.GetValue("WallPaper") as string;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 900;
                    bmp.UriSource = new Uri(path);
                    bmp.EndInit();
                    bmp.Freeze();
                    _wallpaper = bmp;
                }
            }
            catch { _wallpaper = null; }
        }
        if (_wallpaper != null)
        {
            var brush = new ImageBrush(_wallpaper) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top, Opacity = 0.9 };
            brush.Freeze();
            return brush;
        }
        var gradient = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(36, 48, 84), 0), new(Color.FromRgb(70, 52, 104), 0.55), new(Color.FromRgb(18, 30, 50), 1),
        }, 35);
        gradient.Freeze();
        return gradient;
    }

    private void UpdateRunning()
    {
        if (IsVisible && IsLoaded) { if (!_script.IsEnabled) Advance(); }
        else Stop();
    }

    private void Stop()
    {
        _script.Stop();
        FrameClock.Stop(_onFrame);
        _frameRunning = false;
    }

    private void Advance()
    {
        var (snapshot, seconds) = Script[_step % Script.Length];
        _step++;
        var target = Geometry(snapshot);
        _animator.AnimateTo(target);
        _animator.SetContentVisible(false, immediate: false);
        _skeleton.Pending = snapshot;
        if (snapshot.State == NotchState.Notification) _animator.Pulse();
        _script.Interval = TimeSpan.FromSeconds(seconds * Math.Max(0.6, _animator.Profile.TimeScale));
        _script.Start();
        if (!_frameRunning)
        {
            _frameRunning = true;
            FrameClock.Run(_onFrame);
        }
    }

    private bool OnFrame(double dt)
    {
        if (!IsVisible) { _frameRunning = false; return false; }
        var animating = _animator.Step(dt);
        if (_skeleton.Pending != null && _animator.Frame.ContentOpacity < 0.05)
        {
            _skeleton.Snapshot = _skeleton.Pending;
            _skeleton.Pending = null;
            _animator.SetContentVisible(true);
        }
        if (_surface.AmbientLevel > 0) _surface.AmbientTime += dt;
        _surface.SetFrame(_animator.Frame);
        _skeleton.InvalidateVisual();
        var keep = animating || _surface.AmbientLevel > 0 || _skeleton.Pending != null;
        if (!keep) _frameRunning = false;
        return keep;
    }

    /// <summary>Neutral placeholder content (never real data) laid out like each notch view.</summary>
    private sealed class SkeletonLayer : FrameworkElement
    {
        private readonly NotchSurface _surface;
        public SkeletonLayer(NotchSurface surface) { _surface = surface; IsHitTestVisible = false; }
        public NotchSnapshot? Snapshot { get; set; }
        public NotchSnapshot? Pending { get; set; }
        public bool Light { get; set; }
        public double Scale { get; set; } = 1;

        protected override void OnRender(DrawingContext dc)
        {
            if (Snapshot is null || _surface.Geometry is null) return;
            var f = _surface.Frame;
            var sb = _surface.ShapeBounds;
            if (f.ContentOpacity < 0.02) return;
            // Lay out at 1x inside an unscaled rect, then scale around the shape's top-left.
            var b = new Rect(sb.X, sb.Y, sb.Width / Scale, sb.Height / Scale);
            var c = Light ? Color.FromArgb(60, 0, 0, 0) : Color.FromArgb(56, 255, 255, 255);
            var strong = new SolidColorBrush(Light ? Color.FromArgb(110, 0, 0, 0) : Color.FromArgb(120, 255, 255, 255));
            var soft = new SolidColorBrush(c);
            var accent = new SolidColorBrush(Color.FromArgb(200, 139, 156, 255));
            dc.PushClip(_surface.Geometry);
            dc.PushOpacity(f.ContentOpacity);
            dc.PushTransform(new TranslateTransform(0, f.ContentOffsetY));
            dc.PushTransform(new ScaleTransform(Scale, Scale, sb.X, sb.Y));
            var x = b.X;
            var y = b.Y;
            switch (Snapshot.State)
            {
                case NotchState.Idle:
                    dc.DrawRoundedRectangle(soft, null, new Rect(b.X + b.Width / 2 - 22, y + b.Height / 2 - 2.5, 44, 5), 2.5, 2.5);
                    break;
                case NotchState.Hover:
                    dc.DrawRoundedRectangle(strong, null, new Rect(b.X + b.Width / 2 - 26, y + b.Height / 2 - 3, 52, 6), 3, 3);
                    break;
                case NotchState.Compact:
                    dc.DrawRoundedRectangle(accent, null, new Rect(x + 12, y + b.Height / 2 - 11, 22, 22), 6, 6);
                    for (var i = 0; i < 4; i++)
                    {
                        var h = 6 + (i * 5 % 9);
                        dc.DrawRoundedRectangle(accent, null, new Rect(b.Right - 34 + i * 6, y + b.Height / 2 - h / 2.0, 3, h), 1.5, 1.5);
                    }
                    break;
                case NotchState.Notification:
                    dc.DrawRoundedRectangle(accent, null, new Rect(x + 14, y + b.Height / 2 - 24, 48, 48), 12, 12);
                    dc.DrawRoundedRectangle(strong, null, new Rect(x + 76, y + b.Height / 2 - 12, 150, 9), 4.5, 4.5);
                    dc.DrawRoundedRectangle(soft, null, new Rect(x + 76, y + b.Height / 2 + 4, 96, 8), 4, 4);
                    break;
                case NotchState.Expanded:
                    dc.DrawRoundedRectangle(accent, null, new Rect(x + 22, y + 22, 44, 44), 11, 11);
                    dc.DrawRoundedRectangle(strong, null, new Rect(x + 78, y + 30, 170, 10), 5, 5);
                    dc.DrawRoundedRectangle(soft, null, new Rect(x + 78, y + 48, 110, 8), 4, 4);
                    for (var i = 0; i < 6; i++) dc.DrawRoundedRectangle(soft, null, new Rect(x + 28 + i * 74, y + 92, 34, 34), 9, 9);
                    var w = (b.Width - 44 - 18) / 4;
                    for (var i = 0; i < 4; i++) dc.DrawRoundedRectangle(soft, null, new Rect(x + 22 + i * (w + 6), b.Bottom - 56, w, 36), 12, 12);
                    break;
            }
            dc.Pop();
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }
    }
}
