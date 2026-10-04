using System.Windows;
using System.Windows.Media;
using Nova.Core.Animation;
using Nova.Core.Settings;

namespace Nova.App.Notch.Rendering;

/// <summary>
/// Draws the notch: the shape (a pill hanging from the screen edge with concave "shoulders" — NOVA's
/// signature silhouette), its material, light effects and the theme's decorations. It renders one
/// immutable <see cref="NotchFrame"/> produced by the animator; it holds no animation state itself
/// except the ambient (Aurora) clock.
/// </summary>
public sealed class NotchSurface : FrameworkElement
{
    private NotchFrame _frame = new() { Width = 156, Height = 30, Radius = 15, StretchX = 1, StretchY = 1, Opacity = 1, ContentOpacity = 1, ContentScale = 1, Specular = -1 };
    private NotchPalette _palette = NotchPalette.For(NotchMaterial.DarkGlass);
    private Color _accent = Color.FromRgb(139, 156, 255);
    private double _surfaceOpacity = 0.9;
    private double _glowAmount = 0.35;
    private NotchShape _shape = NotchShape.Attached;
    private bool _glassLayers = true;

    private Brush _fill = Brushes.Black;
    private Brush _highlight = Brushes.Transparent;
    private Pen _rimPen = new(Brushes.Transparent, 1);
    private Brush _glowBrush = Brushes.Transparent;
    private readonly Brush[] _auroraBrushes = new Brush[3];
    private readonly GradientStop[][] _auroraStops = new GradientStop[3][];
    private readonly Color[] _auroraCurrent = new Color[3];
    private readonly Color[] _auroraTarget = new Color[3];
    private IReadOnlyList<Color>? _auroraOverride;
    private double _auroraClock = double.NaN;
    private readonly LinearGradientBrush _specularBrush;
    private readonly GradientStop[] _specularStops;
    private static readonly Brush HitZoneBrush = Freeze(new SolidColorBrush(Color.FromArgb(2, 0, 0, 0)));
    private Geometry? _geometry;
    private Rect _hitZone = Rect.Empty;

    public NotchSurface()
    {
        _specularStops = new[]
        {
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
            new GradientStop(Color.FromArgb(46, 255, 255, 255), 0.1),
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.2),
        };
        _specularBrush = new LinearGradientBrush(new GradientStopCollection(_specularStops), new Point(0, 0), new Point(1, 0.6));
        for (var i = 0; i < 3; i++)
        {
            _auroraStops[i] = new[] { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Transparent, 0.5), new GradientStop(Colors.Transparent, 1) };
            _auroraBrushes[i] = new RadialGradientBrush(new GradientStopCollection(_auroraStops[i]));
        }
        RebuildBrushes();
        SnapAurora();
        SnapsToDevicePixels = false;
    }

    /// <summary>Aurora intensity for the current state (0 = off).</summary>
    public double AmbientLevel { get; set; }
    /// <summary>Seconds on the ambient clock; advanced by the controller while Aurora is visible.</summary>
    public double AmbientTime { get; set; }
    /// <summary>Extends the hit area along the top edge so a nearly hidden notch is still easy to reach.</summary>
    public bool ExtendedHitZone { get; set; }
    /// <summary>Where the shape sits horizontally; Left/Right keep <see cref="SideInset"/> from that edge.</summary>
    public NotchPosition Anchor { get; set; } = NotchPosition.Center;
    public double SideInset { get; set; } = 16;

    /// <summary>
    /// Colors of the Aurora light (three), or null to derive them from the accent color. A change
    /// cross-fades over about a second while the light is drifting.
    /// </summary>
    public void SetAuroraColors(IReadOnlyList<Color>? colors)
    {
        _auroraOverride = colors is { Count: > 0 } ? colors : null;
        UpdateAuroraTarget();
        InvalidateVisual();
    }

    private void UpdateAuroraTarget()
    {
        var hues = new[] { 0.0, 48.0, -52.0 };
        for (var i = 0; i < 3; i++)
        {
            _auroraTarget[i] = _auroraOverride is { } custom
                ? custom[i % custom.Count]
                : ColorMath.Shift(_accent, hues[i], 0.85, _palette.IsLight ? 1.0 : 0.95);
        }
    }

    /// <summary>Jumps to the target colors (used when the light isn't drifting, so there's nothing to fade).</summary>
    private void SnapAurora()
    {
        Array.Copy(_auroraTarget, _auroraCurrent, 3);
        ApplyAuroraStops();
    }

    private void ApplyAuroraStops()
    {
        for (var i = 0; i < 3; i++)
        {
            var c = _auroraCurrent[i];
            _auroraStops[i][0].Color = ColorMath.WithAlpha(c, 0.5);
            _auroraStops[i][1].Color = ColorMath.WithAlpha(c, 0.16);
            _auroraStops[i][2].Color = ColorMath.WithAlpha(c, 0);
        }
    }

    private void StepAurora()
    {
        var dt = double.IsNaN(_auroraClock) ? 0 : AmbientTime - _auroraClock;
        _auroraClock = AmbientTime;
        if (dt <= 0 || dt > 0.5)
        {
            if (!_auroraCurrent.SequenceEqual(_auroraTarget)) SnapAurora();
            return;
        }
        if (_auroraCurrent.SequenceEqual(_auroraTarget)) return;
        var k = 1 - Math.Exp(-dt * 3.2);
        for (var i = 0; i < 3; i++)
        {
            var a = _auroraCurrent[i];
            var b = _auroraTarget[i];
            // Nudge by half a step so slow fades never stall on rounding, but never pass the target.
            byte L(byte x, byte y) => (byte)Math.Clamp(Math.Round(x + (y - x) * k + Math.Sign(y - x) * 0.5), Math.Min(x, y), Math.Max(x, y));
            _auroraCurrent[i] = Color.FromRgb(L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
        }
        ApplyAuroraStops();
    }

    public NotchFrame Frame => _frame;
    public double ShapeTop => _shape == NotchShape.Floating ? _frame.Top : 0;

    /// <summary>Raised whenever the outline changes (the shadow layer follows it).</summary>
    public event Action<Geometry?>? GeometryChanged;
    public Geometry? Geometry => _geometry;

    public void SetFrame(in NotchFrame frame)
    {
        _frame = frame;
        UpdateGeometry();
        InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateGeometry();
    }

    private void UpdateGeometry()
    {
        var f = _frame;
        _geometry = f.Opacity <= 0.005 || f.Height < 0.5 || f.Width < 1 || ActualWidth <= 0
            ? null
            : BuildGeometry(ShapeBounds, ShapeRadius, f);
        GeometryChanged?.Invoke(_geometry);
    }

    public void Configure(NotchPalette palette, Color accent, double opacity, double glow, NotchShape shape, bool glassLayers)
    {
        _palette = palette;
        _accent = accent;
        _surfaceOpacity = opacity;
        _glowAmount = glow;
        _shape = shape;
        _glassLayers = glassLayers;
        RebuildBrushes();
        UpdateGeometry();
        InvalidateVisual();
    }

    private void RebuildBrushes()
    {
        var alpha = _surfaceOpacity;
        _fill = Freeze(new LinearGradientBrush(ColorMath.WithAlpha(_palette.Top, alpha), ColorMath.WithAlpha(_palette.Bottom, Math.Min(1, alpha + 0.04)), 90));

        _highlight = Freeze(new LinearGradientBrush(new GradientStopCollection
        {
            new(_palette.Highlight, 0),
            new(Color.FromArgb(0, _palette.Highlight.R, _palette.Highlight.G, _palette.Highlight.B), 1),
        }, 90));

        var rim = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0, _palette.Rim.R, _palette.Rim.G, _palette.Rim.B), 0),
            new(_palette.Rim, 0.5),
            new(Color.FromArgb(0, _palette.Rim.R, _palette.Rim.G, _palette.Rim.B), 1),
        }, 0);
        _rimPen = new Pen(Freeze(rim), 1);
        _rimPen.Freeze();

        _glowBrush = Freeze(new RadialGradientBrush(new GradientStopCollection
        {
            new(ColorMath.WithAlpha(_accent, 0.55), 0),
            new(ColorMath.WithAlpha(_accent, 0.18), 0.55),
            new(ColorMath.WithAlpha(_accent, 0), 1),
        }));

        UpdateAuroraTarget();
    }

    /// <summary>Visible notch bounds (excluding shoulders and bulge) in this element's coordinates.</summary>
    public Rect ShapeBounds
    {
        get
        {
            var w = _frame.Width * _frame.StretchX;
            var h = _frame.Height * _frame.StretchY;
            var x = Anchor switch
            {
                NotchPosition.Left => SideInset,
                NotchPosition.Right => ActualWidth - SideInset - w,
                _ => ActualWidth / 2 - w / 2,
            };
            return new Rect(x, ShapeTop, Math.Max(0, w), Math.Max(0, h));
        }
    }

    public double ShapeRadius
    {
        get
        {
            var b = ShapeBounds;
            return Math.Min(_frame.Radius, Math.Min(b.Width / 2, b.Height / 2));
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        _hitZone = Rect.Empty;
        var f = _frame;
        var geometry = _geometry;
        if (geometry is null) return;

        var bounds = ShapeBounds;
        var cx = bounds.X + bounds.Width / 2;

        dc.PushOpacity(f.Opacity);

        // Soft accent light spilling below the notch.
        var glow = Math.Clamp(_glowAmount * (0.32 + f.Glow * 1.6) * Math.Min(1, bounds.Height / 30), 0, 1);
        if (glow > 0.02 && !_palette.IsLight)
        {
            dc.PushOpacity(glow);
            var rx = bounds.Width * 0.52 + 6;
            var ry = 9 + 10 * f.Glow + bounds.Height * 0.06;
            dc.DrawEllipse(_glowBrush, null, new Point(cx, bounds.Bottom - ry * 0.35 + f.Bulge * 0.5), rx, ry);
            dc.Pop();
        }

        dc.DrawGeometry(_fill, null, geometry);

        dc.PushClip(geometry);
        if (_glassLayers)
        {
            dc.DrawRectangle(_highlight, null, new Rect(bounds.X, bounds.Y, bounds.Width, Math.Max(1, bounds.Height * 0.55)));
        }

        if (AmbientLevel > 0.01 && bounds.Height > 8)
        {
            StepAurora();
            var t = AmbientTime;
            dc.PushOpacity(Math.Clamp(AmbientLevel, 0, 1));
            for (var i = 0; i < 3; i++)
            {
                var k = i * 2.1;
                var x = cx + bounds.Width * 0.36 * Math.Sin(t * (0.19 + i * 0.05) + k);
                var y = bounds.Y + bounds.Height * (0.55 + 0.4 * Math.Cos(t * (0.15 + i * 0.04) + k * 1.3));
                var rx = bounds.Width * (0.42 + 0.08 * Math.Sin(t * 0.11 + k));
                var ry = Math.Max(bounds.Height * 0.95, 26);
                dc.DrawEllipse(_auroraBrushes[i], null, new Point(x, y), rx, ry);
            }
            dc.Pop();
        }

        if (f.Specular >= 0)
        {
            var c = -0.25 + 1.5 * f.Specular;
            _specularStops[0].Offset = c - 0.16;
            _specularStops[1].Offset = c;
            _specularStops[2].Offset = c + 0.16;
            dc.DrawRectangle(_specularBrush, null, bounds);
        }

        // Rim light along the lower edge; clip away the top so no line shows at the screen edge.
        if (bounds.Height > 6)
        {
            var rimTop = _shape == NotchShape.Attached ? bounds.Y + Math.Max(2, bounds.Height * 0.45) : bounds.Y;
            dc.PushClip(new RectangleGeometry(new Rect(bounds.X - 20, rimTop, bounds.Width + 40, bounds.Height + 40)));
            dc.DrawGeometry(null, _rimPen, geometry);
            dc.Pop();
        }
        dc.Pop(); // clip

        if (ExtendedHitZone)
        {
            // A slim strip along the screen edge, a little wider than the sliver, so it's easy to hit
            // without stealing clicks from the tab strip / title bar below it.
            _hitZone = new Rect(cx - bounds.Width / 2 - 12, 0, bounds.Width + 24, Math.Max(6, bounds.Bottom + 2));
            dc.DrawRectangle(HitZoneBrush, null, _hitZone);
        }

        dc.Pop(); // opacity
    }

    /// <summary>Whether a point (in this element's coordinates) is on the notch or its hit zone.</summary>
    public bool IsInteractiveAt(Point p) =>
        (_geometry != null && _geometry.FillContains(p)) || (!_hitZone.IsEmpty && _hitZone.Contains(p));

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var p = hitTestParameters.HitPoint;
        return IsInteractiveAt(p) ? new PointHitTestResult(this, p) : null;
    }

    private Geometry BuildGeometry(Rect b, double r, in NotchFrame f)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var x0 = b.Left;
            var x1 = b.Right;
            var top = b.Top;
            var bottom = b.Bottom;
            var bulge = f.Bulge;

            if (_shape == NotchShape.Attached)
            {
                var s = Math.Min(Math.Min(7 + f.ShoulderBoost, b.Height * 0.45), 18);
                if (b.Height < 6) s = Math.Max(0, b.Height * 0.5);
                var rb = Math.Min(r, Math.Max(0, b.Height - s));

                ctx.BeginFigure(new Point(x0 - s, top), true, true);
                ctx.QuadraticBezierTo(new Point(x0, top), new Point(x0, top + s), true, true);
                ctx.LineTo(new Point(x0, bottom - rb), true, true);
                ctx.ArcTo(new Point(x0 + rb, bottom), new Size(rb, rb), 0, false, SweepDirection.Counterclockwise, true, true);
                if (Math.Abs(bulge) > 0.05)
                    ctx.QuadraticBezierTo(new Point((x0 + x1) / 2, bottom + bulge * 2), new Point(x1 - rb, bottom), true, true);
                else
                    ctx.LineTo(new Point(x1 - rb, bottom), true, true);
                ctx.ArcTo(new Point(x1, bottom - rb), new Size(rb, rb), 0, false, SweepDirection.Counterclockwise, true, true);
                ctx.LineTo(new Point(x1, top + s), true, true);
                ctx.QuadraticBezierTo(new Point(x1, top), new Point(x1 + s, top), true, true);
                // Slightly above the screen edge so the top never shows an anti-aliased seam.
                ctx.LineTo(new Point(x1 + s, top - 2), false, false);
                ctx.LineTo(new Point(x0 - s, top - 2), false, false);
            }
            else
            {
                ctx.BeginFigure(new Point(x0 + r, top), true, true);
                ctx.LineTo(new Point(x1 - r, top), true, true);
                ctx.ArcTo(new Point(x1, top + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                ctx.LineTo(new Point(x1, bottom - r), true, true);
                ctx.ArcTo(new Point(x1 - r, bottom), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                if (Math.Abs(bulge) > 0.05)
                    ctx.QuadraticBezierTo(new Point((x0 + x1) / 2, bottom + bulge * 2), new Point(x0 + r, bottom), true, true);
                else
                    ctx.LineTo(new Point(x0 + r, bottom), true, true);
                ctx.ArcTo(new Point(x0, bottom - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                ctx.LineTo(new Point(x0, top + r), true, true);
                ctx.ArcTo(new Point(x0 + r, top), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
