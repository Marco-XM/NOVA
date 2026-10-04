using Nova.Core.State;

namespace Nova.Core.Animation;

/// <summary>Everything the renderer needs to draw one frame.</summary>
public readonly record struct NotchFrame
{
    public double Width { get; init; }
    public double Height { get; init; }
    public double Radius { get; init; }
    /// <summary>Gap between the screen edge and the top of the shape.</summary>
    public double Top { get; init; }
    /// <summary>Horizontal / vertical scale around the top-center anchor (squash &amp; stretch).</summary>
    public double StretchX { get; init; }
    public double StretchY { get; init; }
    /// <summary>Downward bulge of the bottom edge center, in DIPs (liquid meniscus).</summary>
    public double Bulge { get; init; }
    /// <summary>Extra width of the concave shoulders, in DIPs.</summary>
    public double ShoulderBoost { get; init; }
    public double ContentOpacity { get; init; }
    public double ContentScale { get; init; }
    public double ContentOffsetY { get; init; }
    public double ContentBlur { get; init; }
    /// <summary>Transient accent glow 0..1.</summary>
    public double Glow { get; init; }
    /// <summary>Specular sweep progress 0..1, or &lt;0 when inactive.</summary>
    public double Specular { get; init; }
    public double Opacity { get; init; }
}

/// <summary>
/// The animation engine's math: a set of spring channels choreographed by the active
/// <see cref="AnimationProfile"/>. It's UI-framework agnostic; a frame clock calls <see cref="Step"/>
/// and a renderer reads <see cref="Frame"/>.
/// </summary>
public sealed class NotchAnimator
{
    private readonly AnimatedValue _width;
    private readonly AnimatedValue _height;
    private readonly AnimatedValue _radius;
    private readonly AnimatedValue _top;
    private readonly AnimatedValue _opacity;
    private readonly AnimatedValue _contentOpacity;
    private readonly AnimatedValue _contentScale;
    private readonly AnimatedValue _contentOffset;
    private readonly AnimatedValue _glow;
    private readonly AnimatedValue _bulge;
    private readonly AnimatedValue _stretch;
    private readonly AnimatedValue _shoulder;
    private double _specular = -1;
    private bool _contentVisible = true;

    public NotchAnimator(AnimationProfile profile, NotchGeometry initial)
    {
        Profile = profile;
        _width = new AnimatedValue(initial.Width, profile.Expand, 0.05);
        _height = new AnimatedValue(initial.Height, profile.Expand, 0.05);
        _radius = new AnimatedValue(initial.CornerRadius, profile.Radius, 0.05);
        _top = new AnimatedValue(initial.Top, profile.Expand, 0.05);
        _opacity = new AnimatedValue(1, new Spring(0.25, 1), 0.002);
        _contentOpacity = new AnimatedValue(1, profile.Content, 0.002);
        _contentScale = new AnimatedValue(1, profile.Content, 0.0005);
        _contentOffset = new AnimatedValue(0, profile.Content, 0.02);
        _glow = new AnimatedValue(0, new Spring(0.9, 1), 0.002);
        _bulge = new AnimatedValue(0, new Spring(0.14, 0.9), 0.02);
        _stretch = new AnimatedValue(0, new Spring(0.16, 0.8), 0.0005);
        _shoulder = new AnimatedValue(0, new Spring(0.22, 0.95), 0.02);
        Frame = BuildFrame();
    }

    public AnimationProfile Profile { get; private set; }
    public NotchFrame Frame { get; private set; }
    public NotchGeometry Target => new(_width.Target, _height.Target, _radius.Target, _top.Target);
    public bool ContentVisible => _contentVisible;

    public bool IsAnimating =>
        !(_width.IsSettled && _height.IsSettled && _radius.IsSettled && _top.IsSettled && _opacity.IsSettled
          && _contentOpacity.IsSettled && _contentScale.IsSettled && _contentOffset.IsSettled
          && _glow.IsSettled && _bulge.IsSettled && _stretch.IsSettled && _shoulder.IsSettled)
        || _specular >= 0;

    public void SetProfile(AnimationProfile profile)
    {
        Profile = profile;
        _radius.Spring = profile.Radius;
        _contentOpacity.Spring = profile.Content;
        _contentScale.Spring = profile.Content;
        _contentOffset.Spring = profile.Content;
    }

    public void Snap(NotchGeometry geometry, bool visible = true)
    {
        _width.Snap(geometry.Width);
        _height.Snap(geometry.Height);
        _radius.Snap(geometry.CornerRadius);
        _top.Snap(geometry.Top);
        _opacity.Snap(visible ? 1 : 0);
        _bulge.Snap(0);
        _stretch.Snap(0);
        _shoulder.Snap(0);
        _specular = -1;
        Frame = BuildFrame();
    }

    /// <summary>Animates the shape to a new geometry using the profile's choreography.</summary>
    public void AnimateTo(NotchGeometry target, bool visible = true)
    {
        var current = new NotchGeometry(_width.Value, _height.Value, _radius.Value);
        var expanding = target.Area >= current.Area;
        var spring = expanding ? Profile.Expand : Profile.Collapse;
        _width.Spring = spring;
        _height.Spring = spring;

        double wDelay, hDelay;
        if (expanding) { wDelay = Profile.WidthDelay; hDelay = Profile.HeightDelay; }
        else { wDelay = Profile.HeightDelay; hDelay = Profile.WidthDelay; } // collapse mirrors expand

        _width.SetTarget(target.Width, wDelay);
        _height.SetTarget(target.Height, hDelay);
        _radius.SetTarget(target.CornerRadius, Profile.RadiusDelay);
        _top.Spring = spring;
        _top.SetTarget(target.Top, hDelay);
        _opacity.SetTarget(visible ? 1 : 0);

        if (Math.Abs(target.Area - current.Area) > 400)
        {
            if (Profile.GlowPulse > 0 && expanding) _glow.Kick(Math.Max(_glow.Value, Profile.GlowPulse));
            if (Profile.SpecularSweep) _specular = 0;
        }
    }

    /// <summary>Fades the content out (before a view swap) or in (after it).</summary>
    public void SetContentVisible(bool visible, bool immediate = false)
    {
        _contentVisible = visible;
        var style = Profile.ContentStyle;
        double hiddenScale = style.HasFlag(ContentTransition.Scale) ? 0.94 : 1.0;
        double hiddenOffset = style.HasFlag(ContentTransition.Slide) ? -10 : style.HasFlag(ContentTransition.Scale) ? -2 : 0;

        if (immediate)
        {
            _contentOpacity.Snap(visible ? 1 : 0);
            _contentScale.Snap(visible ? 1 : hiddenScale);
            _contentOffset.Snap(visible ? 0 : hiddenOffset);
            Frame = BuildFrame();
            return;
        }

        if (visible)
        {
            // Start from the "hidden" pose so the content arrives with the theme's motion.
            if (_contentOpacity.Value < 0.05)
            {
                _contentScale.Snap(hiddenScale);
                _contentOffset.Snap(hiddenOffset);
            }
            // Arriving mid-morph: don't add the full entrance delay on top of the fade-out.
            var delay = _width.IsSettled && _height.IsSettled ? Profile.ContentDelay : Profile.ContentDelay * 0.4;
            _contentOpacity.SetTarget(1, delay);
            _contentScale.SetTarget(1, delay);
            _contentOffset.SetTarget(0, delay);
        }
        else
        {
            // Leaving content goes quickly and without delay.
            _contentOpacity.Spring = new Spring(Math.Max(0.07, Profile.Content.Response * 0.3), 1);
            _contentOpacity.SetTarget(0);
            _contentScale.SetTarget(style.HasFlag(ContentTransition.Scale) ? 0.97 : 1);
            _contentOffset.SetTarget(hiddenOffset * 0.4);
        }
    }

    /// <summary>Flash of glow (e.g. when a notification arrives).</summary>
    public void Pulse(double strength = 1)
    {
        if (Profile.ReducedMotion) return;
        _glow.Kick(Math.Max(_glow.Value, Profile.GlowPulse * strength));
        if (Profile.SpecularSweep) _specular = 0;
    }

    /// <summary>Advances all channels. Returns true while anything is still moving.</summary>
    public bool Step(double dt)
    {
        dt = Math.Clamp(dt, 0, 0.05); // never jump more than 50 ms (e.g. after a stall)

        _width.Step(dt);
        _height.Step(dt);
        _radius.Step(dt);
        _top.Step(dt);
        _opacity.Step(dt);
        if (_contentVisible && _contentOpacity.Spring != Profile.Content) _contentOpacity.Spring = Profile.Content;
        _contentOpacity.Step(dt);
        _contentScale.Step(dt);
        _contentOffset.Step(dt);
        _glow.SetTarget(0);
        _glow.Step(dt);

        // Velocity-driven secondary motion.
        var p = Profile;
        if (!p.ReducedMotion)
        {
            _bulge.SetTarget(Math.Clamp(_height.Velocity / 900.0, -1, 1) * 9 * p.Bulge);
            _stretch.SetTarget(Math.Clamp(_height.Velocity / 5000.0, -1, 1) * 0.09 * p.Deformation);
            var motion = Math.Min(1, (Math.Abs(_width.Velocity) + Math.Abs(_height.Velocity)) / 2200.0);
            _shoulder.SetTarget(motion * 9 * p.ShoulderFlow);
        }
        else
        {
            _bulge.SetTarget(0);
            _stretch.SetTarget(0);
            _shoulder.SetTarget(0);
        }
        _bulge.Step(dt);
        _stretch.Step(dt);
        _shoulder.Step(dt);

        if (_specular >= 0)
        {
            _specular += dt / (0.95 * Math.Max(0.3, p.TimeScale));
            if (_specular > 1) _specular = -1;
        }

        Frame = BuildFrame();
        return IsAnimating;
    }

    private NotchFrame BuildFrame()
    {
        var stretch = _stretch.Value;
        var blur = Profile.ContentStyle.HasFlag(ContentTransition.Blur) ? (1 - Math.Clamp(_contentOpacity.Value, 0, 1)) * 10 : 0;
        return new NotchFrame
        {
            Width = Math.Max(0, _width.Value),
            Height = Math.Max(0, _height.Value),
            Radius = Math.Max(0, _radius.Value),
            Top = Math.Max(0, _top.Value),
            StretchY = 1 + stretch,
            StretchX = 1 - stretch * 0.6,
            Bulge = _bulge.Value,
            ShoulderBoost = Math.Max(0, _shoulder.Value),
            ContentOpacity = Math.Clamp(_contentOpacity.Value, 0, 1),
            ContentScale = _contentScale.Value,
            ContentOffsetY = _contentOffset.Value,
            ContentBlur = blur,
            Glow = Math.Clamp(_glow.Value, 0, 1),
            Specular = _specular,
            Opacity = Math.Clamp(_opacity.Value, 0, 1),
        };
    }
}
