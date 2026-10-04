using Nova.Core.Settings;

namespace Nova.Core.Animation;

[Flags]
public enum ContentTransition
{
    Fade = 0,
    Slide = 1,
    Scale = 2,
    Blur = 4,
}

/// <summary>
/// Describes how a theme moves. Themes are data, not code paths: the animator and the renderer read
/// these values, which keeps every theme consistent and makes new themes cheap to add.
/// </summary>
public sealed record AnimationThemeDefinition
{
    public required AnimationTheme Theme { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }

    public Spring Expand { get; init; } = Spring.Default;
    public Spring Collapse { get; init; } = new(0.38, 0.9);
    public Spring Radius { get; init; } = Spring.Default;
    public Spring Content { get; init; } = new(0.3, 1.0);

    /// <summary>Start delays (seconds) for the expand direction; collapse mirrors them.</summary>
    public double WidthDelay { get; init; }
    public double HeightDelay { get; init; }
    public double RadiusDelay { get; init; }
    public double ContentDelay { get; init; } = 0.06;

    /// <summary>Velocity driven squash &amp; stretch (0 = none).</summary>
    public double Deformation { get; init; }
    /// <summary>Liquid meniscus: the bottom edge bulges while the height moves.</summary>
    public double Bulge { get; init; }
    /// <summary>How much the concave shoulders widen while the notch is moving.</summary>
    public double ShoulderFlow { get; init; }
    /// <summary>Accent glow flash on transitions (0..1).</summary>
    public double GlowPulse { get; init; }
    /// <summary>Glass highlight sweeping across the surface on transitions.</summary>
    public bool SpecularSweep { get; init; }
    /// <summary>Slow ambient light inside the surface (Aurora).</summary>
    public bool AmbientLight { get; init; }
    /// <summary>Extra translucency/reflection layers in the material.</summary>
    public bool GlassLayers { get; init; }
    public ContentTransition ContentStyle { get; init; } = ContentTransition.Fade;
}

public static class AnimationThemes
{
    public static readonly AnimationThemeDefinition Liquid = new()
    {
        Theme = AnimationTheme.Liquid,
        Name = "Liquid",
        Description = "Flows like a drop of liquid glass — elastic, fluid and alive.",
        Expand = new(0.52, 0.64),
        Collapse = new(0.44, 0.78),
        Radius = new(0.56, 0.7),
        Content = new(0.34, 0.92),
        HeightDelay = 0.02,
        ContentDelay = 0.09,
        Deformation = 1.0,
        Bulge = 1.0,
        ShoulderFlow = 1.0,
        GlowPulse = 0.35,
        GlassLayers = true,
        ContentStyle = ContentTransition.Blur | ContentTransition.Scale,
    };

    public static readonly AnimationThemeDefinition Glass = new()
    {
        Theme = AnimationTheme.Glass,
        Name = "Glass",
        Description = "Translucent glass with soft reflections and a light sweep.",
        Expand = new(0.46, 0.84),
        Collapse = new(0.38, 0.92),
        Radius = new(0.46, 0.86),
        Content = new(0.3, 1.0),
        ContentDelay = 0.07,
        Deformation = 0.12,
        ShoulderFlow = 0.35,
        GlowPulse = 0.55,
        SpecularSweep = true,
        GlassLayers = true,
        ContentStyle = ContentTransition.Blur | ContentTransition.Scale,
    };

    public static readonly AnimationThemeDefinition Elastic = new()
    {
        Theme = AnimationTheme.Elastic,
        Name = "Elastic",
        Description = "A physical object: overshoots, settles and compresses on the way back.",
        Expand = new(0.5, 0.5),
        Collapse = new(0.42, 0.6),
        Radius = new(0.5, 0.55),
        Content = new(0.32, 0.72),
        ContentDelay = 0.08,
        Deformation = 0.75,
        Bulge = 0.15,
        ShoulderFlow = 0.5,
        GlowPulse = 0.2,
        ContentStyle = ContentTransition.Scale,
    };

    public static readonly AnimationThemeDefinition Minimal = new()
    {
        Theme = AnimationTheme.Minimal,
        Name = "Minimal",
        Description = "Quiet, precise transitions with no extra effects.",
        Expand = new(0.34, 1.0),
        Collapse = new(0.28, 1.0),
        Radius = new(0.34, 1.0),
        Content = new(0.22, 1.0),
        ContentDelay = 0.04,
        ContentStyle = ContentTransition.Fade,
    };

    public static readonly AnimationThemeDefinition Morph = new()
    {
        Theme = AnimationTheme.Morph,
        Name = "Morph",
        Description = "Changes shape step by step: pill, wide pill, rounded card.",
        Expand = new(0.42, 0.8),
        Collapse = new(0.36, 0.88),
        Radius = new(0.5, 0.78),
        Content = new(0.3, 0.95),
        WidthDelay = 0,
        HeightDelay = 0.13,
        RadiusDelay = 0.09,
        ContentDelay = 0.2,
        Deformation = 0.2,
        ShoulderFlow = 0.6,
        GlowPulse = 0.25,
        ContentStyle = ContentTransition.Slide,
    };

    public static readonly AnimationThemeDefinition Aurora = new()
    {
        Theme = AnimationTheme.Aurora,
        Name = "Aurora",
        Description = "Soft, slowly drifting light inside a dark glass surface.",
        Expand = new(0.5, 0.8),
        Collapse = new(0.42, 0.9),
        Radius = new(0.5, 0.82),
        Content = new(0.34, 0.95),
        ContentDelay = 0.08,
        Deformation = 0.2,
        ShoulderFlow = 0.4,
        GlowPulse = 0.8,
        AmbientLight = true,
        GlassLayers = true,
        ContentStyle = ContentTransition.Blur,
    };

    public static IReadOnlyList<AnimationThemeDefinition> All { get; } = new[] { Liquid, Glass, Elastic, Minimal, Morph, Aurora };

    public static AnimationThemeDefinition Get(AnimationTheme theme) => All.FirstOrDefault(t => t.Theme == theme) ?? Liquid;
}

/// <summary>A theme with the user's animation settings applied. This is what the animator consumes.</summary>
public sealed record AnimationProfile
{
    public required AnimationThemeDefinition Theme { get; init; }
    public required Spring Expand { get; init; }
    public required Spring Collapse { get; init; }
    public required Spring Radius { get; init; }
    public required Spring Content { get; init; }
    public double TimeScale { get; init; } = 1;
    public double WidthDelay { get; init; }
    public double HeightDelay { get; init; }
    public double RadiusDelay { get; init; }
    public double ContentDelay { get; init; }
    public double Deformation { get; init; }
    public double Bulge { get; init; }
    public double ShoulderFlow { get; init; }
    public double GlowPulse { get; init; }
    public bool SpecularSweep { get; init; }
    public bool AmbientLight { get; init; }
    public bool GlassLayers { get; init; }
    public bool ReducedMotion { get; init; }
    public ContentTransition ContentStyle { get; init; }

    public static AnimationProfile Build(AnimationThemeDefinition theme, AnimationSettings settings, bool systemReduceMotion = false)
    {
        var reduce = settings.ReduceMotion || (settings.FollowSystemReduceMotion && systemReduceMotion);
        if (reduce)
        {
            // Accessibility: no bounce, no distortion, quick fades/scales only.
            var quick = new Spring(0.2, 1.0);
            return new AnimationProfile
            {
                Theme = theme,
                Expand = quick,
                Collapse = quick,
                Radius = quick,
                Content = new Spring(0.16, 1.0),
                ContentDelay = 0.02,
                ReducedMotion = true,
                GlassLayers = theme.GlassLayers,
                AmbientLight = false,
                ContentStyle = ContentTransition.Fade,
            };
        }

        var timeScale = settings.ExpansionDurationMs / 460.0 / settings.Speed;
        var intensity = settings.Intensity;
        Spring Adjust(Spring s) => new(s.Response * timeScale, AdjustDamping(s.DampingRatio, settings.SpringStrength));

        return new AnimationProfile
        {
            Theme = theme,
            Expand = Adjust(theme.Expand),
            Collapse = Adjust(theme.Collapse),
            Radius = Adjust(theme.Radius),
            Content = new Spring(theme.Content.Response * timeScale, AdjustDamping(theme.Content.DampingRatio, settings.SpringStrength)),
            TimeScale = timeScale,
            WidthDelay = theme.WidthDelay * timeScale,
            HeightDelay = theme.HeightDelay * timeScale,
            RadiusDelay = theme.RadiusDelay * timeScale,
            ContentDelay = theme.ContentDelay * timeScale,
            Deformation = theme.Deformation * intensity,
            Bulge = theme.Bulge * intensity,
            ShoulderFlow = theme.ShoulderFlow * intensity,
            GlowPulse = Math.Clamp(theme.GlowPulse * intensity, 0, 1),
            SpecularSweep = theme.SpecularSweep && intensity > 0.05,
            AmbientLight = settings.Aurora switch
            {
                AuroraMode.On => true,
                AuroraMode.Off => false,
                _ => theme.AmbientLight,
            },
            GlassLayers = theme.GlassLayers,
            ContentStyle = theme.ContentStyle,
        };
    }

    /// <summary>
    /// Spring strength 1 keeps the theme's damping, 0 removes all overshoot, &gt;1 adds more bounce.
    /// </summary>
    internal static double AdjustDamping(double zeta, double strength)
    {
        if (zeta >= 1) return zeta;
        var adjusted = 1 - (1 - zeta) * strength;
        return Math.Clamp(adjusted, 0.32, 1.0);
    }
}
