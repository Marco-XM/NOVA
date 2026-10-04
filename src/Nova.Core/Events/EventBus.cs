using Nova.Core.Logging;

namespace Nova.Core.Events;

/// <summary>
/// Minimal typed publish/subscribe hub. Handlers run synchronously on the publishing thread;
/// UI consumers are responsible for marshalling to the dispatcher. A throwing handler is logged
/// and never prevents other handlers from running.
/// </summary>
public sealed class EventBus
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public IDisposable Subscribe<T>(Action<T> handler) where T : NovaEvent
    {
        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
                _handlers[typeof(T)] = list = new List<Delegate>();
            list.Add(handler);
        }
        return new Subscription(() =>
        {
            lock (_gate)
            {
                if (_handlers.TryGetValue(typeof(T), out var list)) list.Remove(handler);
            }
        });
    }

    /// <summary>Subscribes to every event (used by the notification router and debug log).</summary>
    public IDisposable SubscribeAll(Action<NovaEvent> handler) => Subscribe(handler);

    public void Publish<T>(T evt) where T : NovaEvent
    {
        Delegate[] snapshot;
        lock (_gate)
        {
            var collected = new List<Delegate>();
            for (var type = evt.GetType(); type != null && typeof(NovaEvent).IsAssignableFrom(type); type = type.BaseType)
            {
                if (_handlers.TryGetValue(type, out var list)) collected.AddRange(list);
            }
            snapshot = collected.ToArray();
        }

        foreach (var handler in snapshot)
        {
            try { handler.DynamicInvoke(evt); }
            catch (Exception ex)
            {
                Log.Error($"Event handler for {evt.GetType().Name} failed", ex.InnerException ?? ex);
            }
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
