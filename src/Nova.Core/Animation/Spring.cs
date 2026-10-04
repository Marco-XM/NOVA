namespace Nova.Core.Animation;

/// <summary>
/// Spring parameters expressed perceptually: <see cref="Response"/> is the period of the undamped
/// oscillation in seconds (≈ how long the motion takes) and <see cref="DampingRatio"/> controls bounce
/// (1 = critically damped, &lt;1 = overshoot).
/// </summary>
public readonly record struct Spring(double Response, double DampingRatio)
{
    public static Spring Default => new(0.45, 0.85);
    public double AngularFrequency => 2 * Math.PI / Math.Max(Response, 0.01);
}

public static class SpringSolver
{
    /// <summary>
    /// Advances a damped harmonic oscillator analytically by <paramref name="dt"/> seconds. The closed form
    /// solution is unconditionally stable, so long frames (or a 240 Hz display) never explode or drift.
    /// </summary>
    public static void Step(ref double value, ref double velocity, double target, Spring spring, double dt)
    {
        if (dt <= 0) return;
        var w = spring.AngularFrequency;
        var z = Math.Max(0.0, spring.DampingRatio);
        var x0 = value - target;
        var v0 = velocity;
        double x, v;

        if (z < 0.9999)
        {
            var wd = w * Math.Sqrt(1 - z * z);
            var e = Math.Exp(-z * w * dt);
            var a = x0;
            var b = (v0 + z * w * x0) / wd;
            var cos = Math.Cos(wd * dt);
            var sin = Math.Sin(wd * dt);
            x = e * (a * cos + b * sin);
            v = e * ((b * wd - z * w * a) * cos + (-a * wd - z * w * b) * sin);
        }
        else if (z <= 1.0001)
        {
            var e = Math.Exp(-w * dt);
            var b = v0 + w * x0;
            x = (x0 + b * dt) * e;
            v = (b - w * (x0 + b * dt)) * e;
        }
        else
        {
            var s = Math.Sqrt(z * z - 1);
            var r1 = -w * (z - s);
            var r2 = -w * (z + s);
            var c2 = (v0 - r1 * x0) / (r2 - r1);
            var c1 = x0 - c2;
            var e1 = Math.Exp(r1 * dt);
            var e2 = Math.Exp(r2 * dt);
            x = c1 * e1 + c2 * e2;
            v = c1 * r1 * e1 + c2 * r2 * e2;
        }

        value = target + x;
        velocity = v;
    }
}

/// <summary>A single spring-driven channel with an optional start delay (used for choreography).</summary>
public sealed class AnimatedValue
{
    private double _delayRemaining;
    private double _pendingTarget;

    public AnimatedValue(double initial, Spring spring, double epsilon = 0.01)
    {
        Value = Target = _pendingTarget = initial;
        Spring = spring;
        Epsilon = epsilon;
    }

    public double Value { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; private set; }
    public Spring Spring { get; set; }
    public double Epsilon { get; set; }

    public bool IsSettled => _delayRemaining <= 0 && Math.Abs(Value - Target) < Epsilon && Math.Abs(Velocity) < Epsilon * 10;

    public void SetTarget(double target, double delaySeconds = 0)
    {
        _pendingTarget = target;
        if (delaySeconds > 0)
        {
            _delayRemaining = delaySeconds;
        }
        else
        {
            _delayRemaining = 0;
            Target = target;
        }
    }

    public void Snap(double value)
    {
        Value = Target = _pendingTarget = value;
        Velocity = 0;
        _delayRemaining = 0;
    }

    /// <summary>Sets the current value without changing the target (e.g. to kick off a pulse).</summary>
    public void Kick(double value, double velocity = 0)
    {
        Value = value;
        Velocity = velocity;
    }

    public void Step(double dt)
    {
        if (_delayRemaining > 0)
        {
            _delayRemaining -= dt;
            if (_delayRemaining > 0) return;
            Target = _pendingTarget;
            dt = -_delayRemaining;
            _delayRemaining = 0;
        }
        if (IsSettled)
        {
            Value = Target;
            Velocity = 0;
            return;
        }
        var value = Value;
        var velocity = Velocity;
        SpringSolver.Step(ref value, ref velocity, Target, Spring, dt);
        Value = value;
        Velocity = velocity;
    }
}
