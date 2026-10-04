using Nova.Core.Animation;
using Nova.Core.Settings;
using Nova.Core.State;

namespace Nova.Tests.Core;

public class AnimationTests
{
    private static (double Peak, double Final, double SettleTime) Simulate(Spring spring, double from, double to, double dt = 1 / 60.0, double seconds = 4)
    {
        double x = from, v = 0, peak = from, settle = -1;
        var steps = (int)Math.Round(seconds / dt);
        for (var i = 0; i < steps; i++)
        {
            var t = i * dt;
            SpringSolver.Step(ref x, ref v, to, spring, dt);
            peak = Math.Max(peak, x);
            if (settle < 0 && Math.Abs(x - to) < 0.01 && Math.Abs(v) < 0.1) settle = t;
            if (settle >= 0 && Math.Abs(x - to) > 0.01) settle = -1;
        }
        return (peak, x, settle);
    }

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(0.4, 1.0)]
    [InlineData(0.3, 1.6)]
    public void Spring_ConvergesToTarget(double response, double damping)
    {
        var r = Simulate(new Spring(response, damping), 0, 100);
        Assert.Equal(100, r.Final, 2);
        Assert.True(r.SettleTime > 0);
    }

    [Fact]
    public void Underdamped_Overshoots_CriticallyDamped_DoesNot()
    {
        Assert.True(Simulate(new Spring(0.5, 0.5), 0, 100).Peak > 105);
        Assert.True(Simulate(new Spring(0.5, 1.0), 0, 100).Peak <= 100.0001);
        Assert.True(Simulate(new Spring(0.5, 1.4), 0, 100).Peak <= 100.0001);
    }

    [Fact]
    public void Spring_IsFrameRateIndependent()
    {
        var at60 = Simulate(new Spring(0.45, 0.7), 0, 100, 1 / 60.0, 0.25).Final;
        var at144 = Simulate(new Spring(0.45, 0.7), 0, 100, 1 / 144.0, 0.25).Final;
        var at240 = Simulate(new Spring(0.45, 0.7), 0, 100, 1 / 240.0, 0.25).Final;
        Assert.Equal(at60, at144, 0);
        Assert.Equal(at60, at240, 0);
    }

    [Fact]
    public void Spring_IsStable_WithHugeTimeSteps()
    {
        double x = 0, v = 0;
        for (var i = 0; i < 20; i++) SpringSolver.Step(ref x, ref v, 100, new Spring(0.3, 0.6), 0.5);
        Assert.False(double.IsNaN(x) || double.IsInfinity(x));
        Assert.Equal(100, x, 1);
    }

    [Fact]
    public void AnimatedValue_RespectsDelay()
    {
        var value = new AnimatedValue(0, new Spring(0.3, 1));
        value.SetTarget(10, delaySeconds: 0.2);
        value.Step(0.1);
        Assert.Equal(0, value.Value);
        value.Step(0.15);
        Assert.True(value.Value > 0);
        for (var i = 0; i < 200; i++) value.Step(1 / 60.0);
        Assert.True(value.IsSettled);
        Assert.Equal(10, value.Value, 3);
    }

    [Fact]
    public void EveryTheme_IsDistinct_AndValid()
    {
        Assert.Equal(6, AnimationThemes.All.Count);
        Assert.Equal(6, AnimationThemes.All.Select(t => t.Theme).Distinct().Count());
        foreach (var theme in AnimationThemes.All)
        {
            Assert.InRange(theme.Expand.Response, 0.1, 1.5);
            Assert.False(string.IsNullOrWhiteSpace(theme.Description));
        }
        Assert.True(AnimationThemes.Elastic.Expand.DampingRatio < AnimationThemes.Minimal.Expand.DampingRatio);
        Assert.True(AnimationThemes.Liquid.Bulge > 0);
        Assert.True(AnimationThemes.Glass.SpecularSweep);
        Assert.True(AnimationThemes.Aurora.AmbientLight);
        Assert.True(AnimationThemes.Morph.HeightDelay > AnimationThemes.Morph.WidthDelay);
    }

    [Fact]
    public void ReduceMotion_RemovesBounceAndDistortion()
    {
        var profile = AnimationProfile.Build(AnimationThemes.Liquid, new AnimationSettings { ReduceMotion = true });
        Assert.True(profile.ReducedMotion);
        Assert.Equal(1.0, profile.Expand.DampingRatio);
        Assert.Equal(0, profile.Deformation);
        Assert.Equal(0, profile.Bulge);
        Assert.False(profile.SpecularSweep);
        Assert.Equal(ContentTransition.Fade, profile.ContentStyle);
    }

    [Fact]
    public void FollowSystemReduceMotion_AppliesOnlyWhenEnabled()
    {
        Assert.True(AnimationProfile.Build(AnimationThemes.Glass, new AnimationSettings { FollowSystemReduceMotion = true }, systemReduceMotion: true).ReducedMotion);
        Assert.False(AnimationProfile.Build(AnimationThemes.Glass, new AnimationSettings { FollowSystemReduceMotion = false }, systemReduceMotion: true).ReducedMotion);
    }

    [Fact]
    public void Speed_AndDuration_ScaleResponse()
    {
        var normal = AnimationProfile.Build(AnimationThemes.Glass, new AnimationSettings());
        var fast = AnimationProfile.Build(AnimationThemes.Glass, new AnimationSettings { Speed = 2 });
        var slow = AnimationProfile.Build(AnimationThemes.Glass, new AnimationSettings { ExpansionDurationMs = 920 });
        Assert.Equal(normal.Expand.Response / 2, fast.Expand.Response, 5);
        Assert.Equal(normal.Expand.Response * 2, slow.Expand.Response, 5);
    }

    [Fact]
    public void SpringStrength_Zero_RemovesOvershoot()
    {
        var p = AnimationProfile.Build(AnimationThemes.Elastic, new AnimationSettings { SpringStrength = 0 });
        Assert.Equal(1.0, p.Expand.DampingRatio);
        var bouncy = AnimationProfile.Build(AnimationThemes.Elastic, new AnimationSettings { SpringStrength = 1.5 });
        Assert.True(bouncy.Expand.DampingRatio < AnimationThemes.Elastic.Expand.DampingRatio);
    }

    [Theory]
    [InlineData(AnimationTheme.Liquid)]
    [InlineData(AnimationTheme.Glass)]
    [InlineData(AnimationTheme.Elastic)]
    [InlineData(AnimationTheme.Minimal)]
    [InlineData(AnimationTheme.Morph)]
    [InlineData(AnimationTheme.Aurora)]
    public void Animator_ReachesTarget_AndStops(AnimationTheme theme)
    {
        var profile = AnimationProfile.Build(AnimationThemes.Get(theme), new AnimationSettings());
        var animator = new NotchAnimator(profile, new NotchGeometry(156, 30, 15));
        animator.AnimateTo(new NotchGeometry(600, 240, 32));
        animator.SetContentVisible(true);
        var frames = 0;
        while (animator.Step(1 / 120.0) && frames < 120 * 6) frames++;
        Assert.False(animator.IsAnimating);
        Assert.Equal(600, animator.Frame.Width, 0);
        Assert.Equal(240, animator.Frame.Height, 0);
        Assert.Equal(1, animator.Frame.ContentOpacity, 2);
        Assert.Equal(1, animator.Frame.StretchX, 3);
        Assert.Equal(0, animator.Frame.Bulge, 1);
    }

    [Fact]
    public void ElasticOvershoots_MinimalDoesNot()
    {
        double Peak(AnimationTheme theme)
        {
            var animator = new NotchAnimator(AnimationProfile.Build(AnimationThemes.Get(theme), new AnimationSettings()), new NotchGeometry(156, 30, 15));
            animator.AnimateTo(new NotchGeometry(600, 240, 32));
            var peak = 0.0;
            for (var i = 0; i < 600; i++) { animator.Step(1 / 120.0); peak = Math.Max(peak, animator.Frame.Width); }
            return peak;
        }
        Assert.True(Peak(AnimationTheme.Elastic) > 610);
        Assert.True(Peak(AnimationTheme.Minimal) <= 600.5);
    }

    [Fact]
    public void Morph_ExpandsWidthBeforeHeight()
    {
        var animator = new NotchAnimator(AnimationProfile.Build(AnimationThemes.Morph, new AnimationSettings()), new NotchGeometry(156, 30, 15));
        animator.AnimateTo(new NotchGeometry(600, 240, 32));
        for (var i = 0; i < 10; i++) animator.Step(1 / 120.0);
        var widthProgress = (animator.Frame.Width - 156) / (600 - 156);
        var heightProgress = (animator.Frame.Height - 30) / (240 - 30);
        Assert.True(widthProgress > heightProgress + 0.05);
    }

    [Fact]
    public void Liquid_BulgesWhileMoving()
    {
        var animator = new NotchAnimator(AnimationProfile.Build(AnimationThemes.Liquid, new AnimationSettings()), new NotchGeometry(156, 30, 15));
        animator.AnimateTo(new NotchGeometry(600, 240, 32));
        var maxBulge = 0.0;
        for (var i = 0; i < 60; i++) { animator.Step(1 / 120.0); maxBulge = Math.Max(maxBulge, Math.Abs(animator.Frame.Bulge)); }
        Assert.True(maxBulge > 1);
    }

    [Fact]
    public void Glass_RunsSpecularSweep_ThenStops()
    {
        var animator = new NotchAnimator(AnimationProfile.Build(AnimationThemes.Glass, new AnimationSettings()), new NotchGeometry(156, 30, 15));
        animator.AnimateTo(new NotchGeometry(600, 240, 32));
        animator.Step(1 / 60.0);
        Assert.True(animator.Frame.Specular >= 0);
        for (var i = 0; i < 400; i++) animator.Step(1 / 60.0);
        Assert.True(animator.Frame.Specular < 0);
    }

    [Fact]
    public void ContentFadesOut_BeforeSwap()
    {
        var animator = new NotchAnimator(AnimationProfile.Build(AnimationThemes.Glass, new AnimationSettings()), new NotchGeometry(156, 30, 15));
        animator.SetContentVisible(false);
        for (var i = 0; i < 30 && animator.Frame.ContentOpacity > 0.05; i++) animator.Step(1 / 60.0);
        Assert.True(animator.Frame.ContentOpacity <= 0.05);
    }
}
