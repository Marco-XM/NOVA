using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Nova.App.Notch.Views;

public partial class CompactView : UserControl, IFluidView
{
    private readonly Storyboard _equalizer;
    private NotchViewModel? _vm;
    private bool _visible;
    private bool _running;
    private bool _active = true;

    public CompactView()
    {
        InitializeComponent();
        _equalizer = BuildEqualizer();
        DataContextChanged += (_, e) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = e.NewValue as NotchViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmChanged;
            Refresh();
        };
        Loaded += (_, _) => { _visible = true; Refresh(); };
        Unloaded += (_, _) => { _visible = false; Refresh(); };
    }

    /// <summary>Only animate while the notch actually shows this view (not while hidden or fading out).</summary>
    public void SetActive(bool active)
    {
        _active = active;
        Refresh();
    }

    public void SetHover(bool hover)
    {
        TrackInfo.BeginAnimation(OpacityProperty, new DoubleAnimation(hover ? 1 : 0, TimeSpan.FromMilliseconds(hover ? 220 : 120)) { BeginTime = hover ? TimeSpan.FromMilliseconds(80) : TimeSpan.Zero });
        // Grow the artwork with the notch instead of snapping to the new size.
        var size = new DoubleAnimation(hover ? 28 : 22, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Art.BeginAnimation(WidthProperty, size);
        Art.BeginAnimation(HeightProperty, size);
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotchViewModel.IsPlaying) or nameof(NotchViewModel.HasMedia) or nameof(NotchViewModel.AnimatedEqualizer) or nameof(NotchViewModel.TimerActive))
            Refresh();
    }

    private void Refresh()
    {
        if (_vm is null) return;
        TimerPanel.Visibility = !_vm.HasMedia && _vm.TimerActive ? Visibility.Visible : Visibility.Collapsed;
        var run = _visible && _active && _vm.HasMedia && _vm.IsPlaying && _vm.AnimatedEqualizer;
        if (run == _running) return;
        _running = run;
        if (run)
        {
            _equalizer.Begin(this, true);
        }
        else
        {
            _equalizer.Stop(this);
            var paused = _vm.HasMedia && _vm.IsPlaying ? 0.55 : 0.25;
            foreach (var bar in new[] { Bar1, Bar2, Bar3, Bar4 })
                ((ScaleTransform)bar.RenderTransform).ScaleY = paused;
        }
    }

    private Storyboard BuildEqualizer()
    {
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(sb, 20);
        Rectangle[] bars = { Bar1, Bar2, Bar3, Bar4 };
        double[][] keys =
        {
            new[] { 0.35, 0.9, 0.5, 0.75, 0.35 },
            new[] { 0.7, 0.3, 1.0, 0.45, 0.7 },
            new[] { 0.5, 0.85, 0.35, 0.95, 0.5 },
            new[] { 0.8, 0.45, 0.7, 0.3, 0.8 },
        };
        double[] durations = { 1.1, 0.9, 1.25, 1.0 };
        for (var i = 0; i < bars.Length; i++)
        {
            var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(durations[i]), RepeatBehavior = RepeatBehavior.Forever };
            for (var k = 0; k < keys[i].Length; k++)
            {
                anim.KeyFrames.Add(new EasingDoubleKeyFrame(keys[i][k], KeyTime.FromPercent(k / (double)(keys[i].Length - 1)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            }
            Storyboard.SetTarget(anim, bars[i]);
            Storyboard.SetTargetProperty(anim, new PropertyPath("RenderTransform.ScaleY"));
            sb.Children.Add(anim);
        }
        return sb;
    }
}
