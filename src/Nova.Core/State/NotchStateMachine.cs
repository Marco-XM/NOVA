using Nova.Core.Notifications;
using Nova.Core.Settings;

namespace Nova.Core.State;

public enum NotchState
{
    /// <summary>Nothing visible (user hid it, or a fullscreen app suppresses it).</summary>
    Hidden,
    /// <summary>The small resting pill.</summary>
    Idle,
    /// <summary>Pointer is over the notch: slightly larger, hints at interactivity.</summary>
    Hover,
    /// <summary>Resting "live activity" (media playing / timer running).</summary>
    Compact,
    /// <summary>Home panel: media summary, quick apps, tools.</summary>
    Expanded,
    /// <summary>Full media controller.</summary>
    Media,
    /// <summary>Quick app launcher.</summary>
    QuickApps,
    /// <summary>One of the tools (timer, calculator, clipboard, search).</summary>
    Tool,
    /// <summary>Transient event display.</summary>
    Notification,
}

public enum NotchTool { None, Timer, Calculator, Clipboard, Search, Notifications }

public enum HiddenReason { None, User, Fullscreen }

public sealed record NotchSnapshot(
    NotchState State,
    NotchTool Tool,
    NotchNotification? Notification,
    bool HasMedia,
    bool HasTimer,
    HiddenReason HiddenReason)
{
    public bool IsInteractive => State is NotchState.Expanded or NotchState.Media or NotchState.QuickApps or NotchState.Tool;
}

public sealed record NotchTransition(NotchSnapshot Previous, NotchSnapshot Current)
{
    public bool StateChanged => Previous.State != Current.State || Previous.Tool != Current.Tool;
}

/// <summary>
/// The single authority over what the notch is doing. All inputs (pointer, clicks, hotkeys, media,
/// notifications, fullscreen) go through this class; the UI only renders <see cref="Snapshot"/>.
/// Time-based behaviour (hover intent, auto collapse, notification expiry) is evaluated in
/// <see cref="Tick"/> against <see cref="NextDeadline"/> so no timers live in here.
/// </summary>
public sealed class NotchStateMachine
{
    private const int MaxQueue = 4;
    private readonly TimeProvider _time;
    private readonly LinkedList<NotchNotification> _queue = new();

    private bool _pointerInside;
    private bool _pinned;
    private bool _userHidden;
    private bool _suppressed;
    private bool _forcedVisible;
    private bool _hasMedia;
    private bool _hasTimer;
    private DateTimeOffset? _hoverAt;
    private DateTimeOffset? _collapseAt;
    private DateTimeOffset? _notificationExpiresAt;

    public NotchStateMachine(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        Snapshot = new NotchSnapshot(NotchState.Idle, NotchTool.None, null, false, false, HiddenReason.None);
    }

    public NotchSnapshot Snapshot { get; private set; }
    public NotchState State => Snapshot.State;

    public bool ExpandOnHover { get; set; } = true;
    public TimeSpan HoverDelay { get; set; } = TimeSpan.FromMilliseconds(90);
    public TimeSpan CollapseDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan InteractiveCollapseDelay { get; set; } = TimeSpan.FromMilliseconds(1200);
    public int QueueLength => _queue.Count;
    public bool IsPointerInside => _pointerInside;

    public event Action<NotchTransition>? Changed;

    public DateTimeOffset? NextDeadline
    {
        get
        {
            DateTimeOffset? best = null;
            void Consider(DateTimeOffset? t) { if (t.HasValue && (best is null || t < best)) best = t; }
            if (_pointerInside) Consider(_hoverAt);
            if (!_pinned && !_pointerInside) Consider(_collapseAt);
            if (State == NotchState.Notification && !_pointerInside) Consider(_notificationExpiresAt);
            return best;
        }
    }

    // ───────────────────────── visibility ─────────────────────────

    public void SetUserHidden(bool hidden)
    {
        if (_userHidden == hidden) return;
        _userHidden = hidden;
        if (!hidden) _forcedVisible = false;
        GoToResting();
    }

    public void SetSuppressed(bool suppressed)
    {
        if (_suppressed == suppressed) return;
        _suppressed = suppressed;
        if (!suppressed) _forcedVisible = false;
        // Don't yank the panel away while the user is actively using it.
        if (suppressed && Snapshot.IsInteractive && _forcedVisible) return;
        if (suppressed || State == NotchState.Hidden) GoToResting();
    }

    public bool IsUserHidden => _userHidden;
    public bool IsSuppressed => _suppressed;

    // ───────────────────────── live activities ─────────────────────────

    public void SetLiveActivity(bool hasMedia, bool hasTimer)
    {
        if (_hasMedia == hasMedia && _hasTimer == hasTimer) return;
        _hasMedia = hasMedia;
        _hasTimer = hasTimer;
        if (State is NotchState.Idle or NotchState.Compact) GoToResting();
        else Go(State, Snapshot.Tool, Snapshot.Notification);
    }

    // ───────────────────────── pointer ─────────────────────────

    public void PointerEntered()
    {
        _pointerInside = true;
        _collapseAt = null;
        if (State is NotchState.Idle or NotchState.Compact && ExpandOnHover)
        {
            if (HoverDelay <= TimeSpan.Zero) Go(NotchState.Hover);
            else _hoverAt = Now + HoverDelay;
        }
    }

    public void PointerExited()
    {
        _pointerInside = false;
        _hoverAt = null;
        switch (State)
        {
            case NotchState.Hover:
                _collapseAt = Now + CollapseDelay * 0.4;
                break;
            case NotchState.Expanded or NotchState.Media or NotchState.QuickApps or NotchState.Tool:
                if (!_pinned) _collapseAt = Now + InteractiveCollapseDelay;
                break;
            case NotchState.Notification:
                var minimum = Now + TimeSpan.FromMilliseconds(900);
                if (_notificationExpiresAt is null || _notificationExpiresAt < minimum) _notificationExpiresAt = minimum;
                break;
        }
    }

    /// <summary>Keeps an interactive panel open (e.g. while a text box has keyboard focus).</summary>
    public void SetPinned(bool pinned)
    {
        _pinned = pinned;
        if (pinned) _collapseAt = null;
        else if (!_pointerInside && Snapshot.IsInteractive) _collapseAt = Now + InteractiveCollapseDelay;
    }

    // ───────────────────────── commands ─────────────────────────

    /// <summary>A click on the notch background (not on a control).</summary>
    public void Click()
    {
        switch (State)
        {
            case NotchState.Idle or NotchState.Hover or NotchState.Compact:
                if (_hasMedia) Go(NotchState.Media);
                else if (_hasTimer) Go(NotchState.Tool, NotchTool.Timer);
                else Go(NotchState.Expanded);
                break;
            case NotchState.Notification:
                var n = Snapshot.Notification;
                _notificationExpiresAt = null;
                if (n?.Style == NotificationStyle.Media && _hasMedia) Go(NotchState.Media);
                else if (n?.Source is Events.TimerCompletedEvent) Go(NotchState.Tool, NotchTool.Timer);
                else if (n?.Source is Events.AppNotificationEvent) { if (!TryShowQueued()) GoToResting(); } // the app opens; nothing to expand
                else Go(NotchState.Expanded);
                break;
        }
    }

    public void HotkeyPressed(HotkeyAction action)
    {
        if (_userHidden) { _userHidden = false; }
        if (_suppressed) _forcedVisible = true;

        if (Snapshot.IsInteractive && action == HotkeyAction.Toggle)
        {
            Collapse();
            return;
        }
        if (!Snapshot.IsInteractive) Go(NotchState.Expanded);
    }

    public void Navigate(NotchState target, NotchTool tool = NotchTool.None)
    {
        if (State == NotchState.Hidden && !_forcedVisible) return;
        if (target is NotchState.Idle or NotchState.Compact or NotchState.Hover or NotchState.Hidden)
        {
            Collapse();
            return;
        }
        if (target == NotchState.Notification) return; // notifications only arrive via Notify
        _collapseAt = null;
        Go(target, target == NotchState.Tool ? tool : NotchTool.None);
    }

    public void Collapse()
    {
        _pinned = false;
        _collapseAt = null;
        _forcedVisible = false;
        if (TryShowQueued()) return;
        GoToResting();
    }

    // ───────────────────────── notifications ─────────────────────────

    /// <summary>Requests a transient notification. Returns false if it was dropped.</summary>
    public bool Notify(NotchNotification notification)
    {
        if (State == NotchState.Hidden) return false;

        if (Snapshot.IsInteractive)
        {
            // Never interrupt the user; queue meaningful events for after they're done.
            if (!notification.Queueable || notification.Priority < NotificationPriority.Normal) return false;
            Enqueue(notification, front: notification.Priority == NotificationPriority.High);
            return true;
        }

        if (State == NotchState.Notification && Snapshot.Notification is { } active)
        {
            var sameKey = notification.CoalesceKey != null && notification.CoalesceKey == active.CoalesceKey;
            var preempt = sameKey || !notification.Queueable || notification.Priority > active.Priority;
            if (!preempt)
            {
                if (!notification.Queueable) return false;
                Enqueue(notification, front: false);
                return true;
            }
            if (!sameKey && active.Queueable && _notificationExpiresAt is { } exp && exp - Now > TimeSpan.FromMilliseconds(600))
                Enqueue(active with { Duration = exp - Now }, front: true);
        }

        ShowNotification(notification);
        return true;
    }

    /// <summary>Removes a notification (queued or showing) before it expires, e.g. a call that stopped ringing.</summary>
    public void Dismiss(string coalesceKey)
    {
        var queued = _queue.FirstOrDefault(q => q.CoalesceKey == coalesceKey);
        if (queued != null) _queue.Remove(queued);
        if (State == NotchState.Notification && Snapshot.Notification?.CoalesceKey == coalesceKey)
        {
            _notificationExpiresAt = null;
            if (!TryShowQueued()) GoToResting();
        }
    }

    // ───────────────────────── time ─────────────────────────

    public void Tick()
    {
        var now = Now;
        if (_hoverAt is { } hoverAt && now >= hoverAt)
        {
            _hoverAt = null;
            if (_pointerInside && State is NotchState.Idle or NotchState.Compact) Go(NotchState.Hover);
        }

        if (_collapseAt is { } collapseAt && now >= collapseAt && !_pinned && !_pointerInside)
        {
            _collapseAt = null;
            if (State == NotchState.Hover || Snapshot.IsInteractive) Collapse();
        }

        if (State == NotchState.Notification && !_pointerInside && _notificationExpiresAt is { } expires && now >= expires)
        {
            _notificationExpiresAt = null;
            if (!TryShowQueued()) GoToResting();
        }
    }

    // ───────────────────────── internals ─────────────────────────

    private DateTimeOffset Now => _time.GetUtcNow();

    private void ShowNotification(NotchNotification notification)
    {
        _notificationExpiresAt = Now + notification.Duration;
        _collapseAt = null;
        Go(NotchState.Notification, NotchTool.None, notification);
    }

    private void Enqueue(NotchNotification notification, bool front)
    {
        if (notification.CoalesceKey != null)
        {
            var existing = _queue.FirstOrDefault(q => q.CoalesceKey == notification.CoalesceKey);
            if (existing != null) _queue.Remove(existing);
        }
        if (front) _queue.AddFirst(notification); else _queue.AddLast(notification);
        while (_queue.Count > MaxQueue) _queue.RemoveLast();
    }

    private bool TryShowQueued()
    {
        if (_queue.First is null || _userHidden || (_suppressed && !_forcedVisible)) return false;
        var next = _queue.First.Value;
        _queue.RemoveFirst();
        ShowNotification(next);
        return true;
    }

    private void GoToResting()
    {
        if (_userHidden)
        {
            Go(NotchState.Hidden, hidden: HiddenReason.User);
            return;
        }
        if (_suppressed && !_forcedVisible)
        {
            Go(NotchState.Hidden, hidden: HiddenReason.Fullscreen);
            return;
        }
        if (_pointerInside && ExpandOnHover && State is NotchState.Hover or NotchState.Notification)
        {
            Go(NotchState.Hover);
            return;
        }
        Go(_hasMedia || _hasTimer ? NotchState.Compact : NotchState.Idle);
    }

    private void Go(NotchState state, NotchTool tool = NotchTool.None, NotchNotification? notification = null, HiddenReason hidden = HiddenReason.None)
    {
        if (state != NotchState.Notification) notification = null;
        if (state != NotchState.Tool) tool = NotchTool.None;
        var next = new NotchSnapshot(state, tool, notification, _hasMedia, _hasTimer, state == NotchState.Hidden ? hidden : HiddenReason.None);
        if (next == Snapshot) return;
        var previous = Snapshot;
        Snapshot = next;
        Changed?.Invoke(new NotchTransition(previous, next));
    }
}
