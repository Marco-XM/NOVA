using Nova.Core.Events;

namespace Nova.Core.Notifications;

/// <summary>One notification another app posted, as kept in the history.</summary>
public sealed record NotificationHistoryEntry(AppNotificationEvent Source, DateTimeOffset At, bool IsRead)
{
    public uint Id => Source.Id;

    /// <summary>Short age for the list: "now", "5m", "3h", "Yesterday" or a date.</summary>
    public string AgeText(DateTimeOffset now)
    {
        var age = now - At;
        if (age < TimeSpan.FromMinutes(1)) return "now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes}m";
        if (At.Date == now.Date) return $"{(int)age.TotalHours}h";
        if (At.Date == now.Date.AddDays(-1)) return "Yesterday";
        return At.ToString("MMM d");
    }
}

/// <summary>
/// Notifications from other apps (messages, calls), newest first, kept in memory only so they can
/// be read later and dismissed. Not thread-safe: use it from the UI thread.
/// </summary>
public sealed class NotificationHistory
{
    private readonly List<NotificationHistoryEntry> _items = new();

    public int Capacity { get; set; } = 50;
    public event Action? Changed;

    public IReadOnlyList<NotificationHistoryEntry> Items => _items;
    public int UnreadCount => _items.Count(i => !i.IsRead);

    /// <summary>Adds a notification as unread. Windows can re-post the same id; that replaces the old entry.</summary>
    public void Add(AppNotificationEvent notification, DateTimeOffset at)
    {
        _items.RemoveAll(i => i.Id == notification.Id);
        _items.Insert(0, new NotificationHistoryEntry(notification, at, IsRead: false));
        if (_items.Count > Capacity) _items.RemoveRange(Capacity, _items.Count - Capacity);
        Changed?.Invoke();
    }

    public bool Remove(uint id)
    {
        if (_items.RemoveAll(i => i.Id == id) == 0) return false;
        Changed?.Invoke();
        return true;
    }

    public void MarkRead(uint id)
    {
        var index = _items.FindIndex(i => i.Id == id);
        if (index < 0 || _items[index].IsRead) return;
        _items[index] = _items[index] with { IsRead = true };
        Changed?.Invoke();
    }

    public void MarkAllRead()
    {
        if (_items.All(i => i.IsRead)) return;
        for (var i = 0; i < _items.Count; i++) _items[i] = _items[i] with { IsRead = true };
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Changed?.Invoke();
    }
}
