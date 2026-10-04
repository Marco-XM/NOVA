using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nova.App.Controls;

/// <summary>
/// Cover art as a spinning record: the artwork fills a disc with faint grooves and a center hole.
/// Only the disc turns; the light reflection and the hole stay put, like a real record. It turns
/// while <see cref="IsSpinning"/> is true and visible, and keeps its angle when paused.
/// <para>
/// Discs don't animate themselves: the notch's frame loop calls <see cref="Advance"/> on the frames
/// it renders anyway. The notch is a layered window (every rendered frame is a full copy), so a
/// separate animation clock would add its own, unsynchronized frames on top of the aurora's.
/// </para>
/// </summary>
public sealed class SpinningDisc : Viewbox
{
    private static readonly List<SpinningDisc> Active = new();

    /// <summary>True while any visible disc should be turning.</summary>
    public static bool AnySpinning => Active.Count > 0;

    /// <summary>Raised when the first disc starts or the last one stops turning.</summary>
    public static event Action? SpinningChanged;

    /// <summary>Turns every active disc by <paramref name="seconds"/> of playback.</summary>
    public static void Advance(double seconds)
    {
        var degrees = 360 * seconds / TurnDuration.TotalSeconds;
        foreach (var disc in Active) disc._rotate.Angle = (disc._rotate.Angle + degrees) % 360;
    }

    public static readonly DependencyProperty ArtworkProperty = DependencyProperty.Register(nameof(Artwork), typeof(ImageSource), typeof(SpinningDisc),
        new PropertyMetadata(null, (d, _) => ((SpinningDisc)d).UpdateArtwork()));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(SpinningDisc),
        new PropertyMetadata(Brushes.White, (d, _) => ((SpinningDisc)d).UpdateArtwork()));
    public static readonly DependencyProperty IsSpinningProperty = DependencyProperty.Register(nameof(IsSpinning), typeof(bool), typeof(SpinningDisc),
        new PropertyMetadata(false, (d, _) => ((SpinningDisc)d).UpdateSpin()));

    /// <summary>One full turn takes this long (a 33⅓ rpm record is much faster; this reads calmer).</summary>
    private static readonly TimeSpan TurnDuration = TimeSpan.FromSeconds(9);
    private const double Size = 100;

    private readonly RotateTransform _rotate = new();
    private readonly Ellipse _art = new() { Width = Size, Height = Size };
    private readonly Ellipse _label = new() { Width = 46, Height = 46 };

    public SpinningDisc()
    {
        Stretch = Stretch.Uniform;
        IsHitTestVisible = false;

        var disc = new Grid { Width = Size, Height = Size, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _rotate };
        disc.Children.Add(new Ellipse { Fill = Frozen(new RadialGradientBrush(Color.FromRgb(0x24, 0x26, 0x2E), Color.FromRgb(0x0A, 0x0B, 0x10))) });
        disc.Children.Add(_label);
        disc.Children.Add(_art);
        // Grooves.
        foreach (var d in new[] { 88.0, 74, 60 })
            disc.Children.Add(new Ellipse { Width = d, Height = d, Stroke = Frozen(new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF))), StrokeThickness = 0.8 });

        var root = new Grid { Width = Size, Height = Size };
        root.Children.Add(disc);
        // Fixed light reflection across the vinyl.
        root.Children.Add(new Ellipse
        {
            Fill = Frozen(new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.25),
                new(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF), 0.45),
                new(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.62),
            }, new Point(0, 0), new Point(1, 1))),
        });
        root.Children.Add(new Ellipse { Stroke = Frozen(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF))), StrokeThickness = 1 });
        // Spindle hole.
        root.Children.Add(new Ellipse
        {
            Width = 22, Height = 22,
            Fill = Frozen(new SolidColorBrush(Color.FromArgb(0xD9, 0x10, 0x11, 0x16))),
            Stroke = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF))), StrokeThickness = 1.2,
        });
        root.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = Frozen(new SolidColorBrush(Color.FromRgb(0x05, 0x06, 0x09))) });
        Child = root;

        IsVisibleChanged += (_, _) => UpdateSpin();
        UpdateArtwork();
    }

    public ImageSource? Artwork { get => (ImageSource?)GetValue(ArtworkProperty); set => SetValue(ArtworkProperty, value); }
    /// <summary>Color of the record label shown when there is no artwork.</summary>
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public bool IsSpinning { get => (bool)GetValue(IsSpinningProperty); set => SetValue(IsSpinningProperty, value); }

    private void UpdateArtwork()
    {
        var art = Artwork;
        _art.Fill = art is null ? null : Frozen(new ImageBrush(art) { Stretch = Stretch.UniformToFill });
        _art.Visibility = art is null ? Visibility.Collapsed : Visibility.Visible;
        _label.Fill = Accent;
        _label.Visibility = art is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSpin()
    {
        var run = IsSpinning && IsVisible;
        var wasAny = AnySpinning;
        if (run && !Active.Contains(this)) Active.Add(this);
        else if (!run) Active.Remove(this);
        if (wasAny != AnySpinning) SpinningChanged?.Invoke();
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }
}
