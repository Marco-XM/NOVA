using System.Diagnostics;
using System.Windows.Media;

namespace Nova.App.Notch.Rendering;

/// <summary>
/// Shared per-frame callback built on CompositionTarget.Rendering (which follows the display's refresh
/// rate). It only hooks the render loop while at least one subscriber is animating, so an idle notch
/// costs zero CPU.
/// </summary>
public static class FrameClock
{
    private static readonly List<Func<double, bool>> Subscribers = new();
    private static bool _hooked;
    private static TimeSpan _lastRenderTime;
    private static readonly Stopwatch Stopwatch = new();
    private static int _frames;
    private static double _fpsWindow;

    /// <summary>Measured frames per second during the last animation.</summary>
    public static double LastFps { get; private set; }

    public static bool IsRunning => _hooked;
    public static int SubscriberCount => Subscribers.Count;

    /// <summary>Adds a callback receiving delta seconds; it is removed when it returns false.</summary>
    public static void Run(Func<double, bool> onFrame)
    {
        if (!Subscribers.Contains(onFrame)) Subscribers.Add(onFrame);
        if (_hooked) return;
        _hooked = true;
        _lastRenderTime = TimeSpan.Zero;
        Stopwatch.Restart();
        _frames = 0;
        _fpsWindow = 0;
        CompositionTarget.Rendering += OnRendering;
    }

    public static void Stop(Func<double, bool> onFrame) => Subscribers.Remove(onFrame);

    private static void OnRendering(object? sender, EventArgs e)
    {
        var renderTime = e is RenderingEventArgs r ? r.RenderingTime : Stopwatch.Elapsed;
        // Rendering can fire more than once per frame; only advance on a new frame time.
        if (renderTime == _lastRenderTime) return;
        var dt = _lastRenderTime == TimeSpan.Zero ? 1.0 / 60 : (renderTime - _lastRenderTime).TotalSeconds;
        _lastRenderTime = renderTime;

        _frames++;
        _fpsWindow += dt;
        if (_fpsWindow >= 0.5)
        {
            LastFps = _frames / _fpsWindow;
            _frames = 0;
            _fpsWindow = 0;
        }

        for (var i = Subscribers.Count - 1; i >= 0; i--)
        {
            bool keep;
            try { keep = Subscribers[i](dt); }
            catch (Exception ex)
            {
                Nova.Core.Logging.Log.Error("Frame callback failed", ex);
                keep = false;
            }
            if (!keep && i < Subscribers.Count) Subscribers.RemoveAt(i);
        }

        if (Subscribers.Count == 0)
        {
            CompositionTarget.Rendering -= OnRendering;
            _hooked = false;
        }
    }
}
