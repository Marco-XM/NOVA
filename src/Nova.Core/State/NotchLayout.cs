using Nova.Core.Notifications;
using Nova.Core.Settings;

namespace Nova.Core.State;

/// <summary>Target shape of the notch in DIPs. <paramref name="Top"/> is the gap to the screen edge.</summary>
public readonly record struct NotchGeometry(double Width, double Height, double CornerRadius, double Top = 0)
{
    public double Area => Width * Height;
}

public sealed record LayoutOptions
{
    public double SizeScale { get; init; } = 1;
    public double RadiusScale { get; init; } = 1;
    public bool AutoHide { get; init; }
    public int QuickAppCount { get; init; }
    public bool HomeHasMedia { get; init; }
    public bool HomeHasTools { get; init; } = true;
    public NotchShape Shape { get; init; } = NotchShape.Attached;
    /// <summary>Gap to the screen edge for the floating shape.</summary>
    public double TopOffset { get; init; }
}

/// <summary>
/// Maps a state snapshot to the notch size. Keeping this table in one place is what makes the UI
/// consistent and lets the animation system interpolate between any two states.
/// </summary>
public static class NotchLayout
{
    /// <summary>Largest size any state can request (before scaling). The notch window is sized from this.</summary>
    public const double MaxWidth = 640;
    public const double MaxHeight = 340;

    public const int QuickAppsPerRow = 6;

    public static NotchGeometry For(NotchSnapshot snapshot, LayoutOptions options)
    {
        var g = Base(snapshot, options);
        var s = options.SizeScale;
        var radius = g.CornerRadius * options.RadiusScale;
        var w = g.Width * s;
        var h = g.Height * s;
        radius = Math.Min(radius * s, Math.Min(h / 2, w / 2));
        return new NotchGeometry(Math.Round(w, 1), Math.Round(h, 1), Math.Max(0, radius), Top(snapshot, options));
    }

    /// <summary>
    /// A floating notch keeps its gap to the edge, except the auto-hide sliver: it tucks against the
    /// edge so it doesn't sit on top of the tab strip / title bar of a window below it.
    /// </summary>
    private static double Top(NotchSnapshot snapshot, LayoutOptions o) =>
        o.Shape == NotchShape.Floating && !IsSliver(snapshot, o) ? o.TopOffset : 0;

    /// <summary>
    /// With auto-hide active, the resting states (idle and the live-activity pill) shrink to a thin
    /// line so they don't cover the tab strip / title bar below. Hovering brings the full view back.
    /// </summary>
    public static bool IsSliver(NotchSnapshot snapshot, LayoutOptions o) =>
        o.AutoHide && snapshot.State is NotchState.Idle or NotchState.Compact;

    private static NotchGeometry Base(NotchSnapshot snapshot, LayoutOptions o)
    {
        if (IsSliver(snapshot, o)) return new(132, 5, 2.5);
        switch (snapshot.State)
        {
            case NotchState.Hidden:
                return new(120, 0, 0);
            case NotchState.Idle:
                return new(156, 30, 15);
            case NotchState.Hover:
                if (snapshot.HasMedia) return new(392, 40, 20);
                if (snapshot.HasTimer) return new(250, 40, 20);
                return new(196, 38, 19);
            case NotchState.Compact:
                return snapshot.HasMedia ? new(300, 34, 17) : new(220, 34, 17);
            case NotchState.Notification:
                return snapshot.Notification?.Style switch
                {
                    NotificationStyle.Indicator => new(300, 46, 23),
                    NotificationStyle.Media => new(412, 78, 30),
                    NotificationStyle.App => new(430, 74, 30),
                    NotificationStyle.Call => new(430, 84, 32),
                    _ => new(384, 66, 28),
                };
            case NotchState.Expanded:
            {
                var height = 96.0; // header (media summary or clock) + padding
                if (o.QuickAppCount > 0) height += 84;
                if (o.HomeHasTools) height += 50;
                return new(600, height, 32);
            }
            case NotchState.Media:
                return new(528, 212, 34);
            case NotchState.QuickApps:
            {
                var rows = Math.Clamp((int)Math.Ceiling((o.QuickAppCount + 1) / (double)QuickAppsPerRow), 1, 3);
                return new(560, 62 + rows * 92, 32);
            }
            case NotchState.Tool:
                return snapshot.Tool switch
                {
                    NotchTool.Timer => new(440, 200, 32),
                    NotchTool.Calculator => new(360, 336, 30),
                    NotchTool.Clipboard => new(460, 304, 30),
                    NotchTool.Search => new(560, 76, 30),
                    _ => new(440, 200, 32),
                };
        }
        return new(156, 30, 15);
    }
}
