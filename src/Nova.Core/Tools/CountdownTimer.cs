namespace Nova.Core.Tools;

public enum CountdownState { Idle, Running, Paused, Completed }

/// <summary>
/// Countdown logic driven purely by wall-clock timestamps (no ticking), so it stays accurate across
/// sleep/wake and needs no timer while nothing displays it. The UI asks for <see cref="Remaining"/>.
/// </summary>
public sealed class CountdownTimer
{
    private readonly TimeProvider _time;
    private DateTimeOffset _endsAt;
    private TimeSpan _remainingWhenPaused;

    public CountdownTimer(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public CountdownState State { get; private set; }
    public TimeSpan Duration { get; private set; } = TimeSpan.FromMinutes(5);
    public string Label { get; set; } = "Timer";

    public event Action<CountdownTimer>? Completed;
    public event Action<CountdownTimer>? StateChanged;

    public bool IsActive => State is CountdownState.Running or CountdownState.Paused;
    public DateTimeOffset? EndsAt => State == CountdownState.Running ? _endsAt : null;

    public TimeSpan Remaining => State switch
    {
        CountdownState.Running => Max(_endsAt - _time.GetUtcNow()),
        CountdownState.Paused => _remainingWhenPaused,
        CountdownState.Completed => TimeSpan.Zero,
        _ => Duration,
    };

    public double Progress => Duration <= TimeSpan.Zero ? 0 : 1 - Remaining.TotalMilliseconds / Duration.TotalMilliseconds;

    public void SetDuration(TimeSpan duration)
    {
        if (IsActive) return;
        Duration = duration < TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : duration > TimeSpan.FromHours(24) ? TimeSpan.FromHours(24) : duration;
        if (State == CountdownState.Completed) State = CountdownState.Idle;
        StateChanged?.Invoke(this);
    }

    public void Start()
    {
        _endsAt = _time.GetUtcNow() + Duration;
        State = CountdownState.Running;
        StateChanged?.Invoke(this);
    }

    public void Pause()
    {
        if (State != CountdownState.Running) return;
        _remainingWhenPaused = Remaining;
        State = CountdownState.Paused;
        StateChanged?.Invoke(this);
    }

    public void Resume()
    {
        if (State != CountdownState.Paused) return;
        _endsAt = _time.GetUtcNow() + _remainingWhenPaused;
        State = CountdownState.Running;
        StateChanged?.Invoke(this);
    }

    public void AddTime(TimeSpan amount)
    {
        switch (State)
        {
            case CountdownState.Running: _endsAt += amount; Duration += amount; break;
            case CountdownState.Paused: _remainingWhenPaused += amount; Duration += amount; break;
            default: SetDuration(Duration + amount); return;
        }
        StateChanged?.Invoke(this);
    }

    public void Cancel()
    {
        State = CountdownState.Idle;
        StateChanged?.Invoke(this);
    }

    /// <summary>Call whenever convenient (UI tick, wake from sleep); fires <see cref="Completed"/> once.</summary>
    public bool CheckCompleted()
    {
        if (State != CountdownState.Running || _time.GetUtcNow() < _endsAt) return false;
        State = CountdownState.Completed;
        StateChanged?.Invoke(this);
        Completed?.Invoke(this);
        return true;
    }

    private static TimeSpan Max(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;
}
