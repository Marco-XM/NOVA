using System.Text.RegularExpressions;
using Nova.Core.Events;
using Nova.Core.Logging;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Nova.Platform.Services;

/// <summary>
/// Mirrors other apps' Windows notifications (WhatsApp, Messenger, browsers, …). Windows only raises
/// the "notification changed" event for packaged apps, so the list is polled (every 1.5 s, one
/// cross-process call) and diffed by id. Reading is all Windows allows: a notification's own buttons
/// (reply, answer, decline) can't be pressed by another app.
/// </summary>
public sealed partial class AppNotificationWatcher : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1500);
    private readonly Timer _timer;
    private readonly Dictionary<string, byte[]?> _logos = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<uint> _known = new();
    private bool _primed;
    private bool _enabled;
    private int _polling;
    private bool _accessRequested;

    public AppNotificationWatcher()
    {
        _timer = new Timer(_ => _ = PollAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Raised on a background thread for each newly posted notification.</summary>
    public event Action<AppNotificationEvent>? Posted;
    /// <summary>Raised on a background thread when a notification disappears from Windows.</summary>
    public event Action<uint>? Removed;

    public string Status { get; private set; } = "Off";

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            _primed = false; // don't replay what arrived while we weren't looking
            _timer.Change(value ? TimeSpan.Zero : Timeout.InfiniteTimeSpan, value ? Interval : Timeout.InfiniteTimeSpan);
            if (!value) Status = "Off";
        }
    }

    private async Task PollAsync()
    {
        if (!_enabled || Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            var listener = UserNotificationListener.Current;
            var access = listener.GetAccessStatus();
            if (access == UserNotificationListenerAccessStatus.Unspecified && !_accessRequested)
            {
                _accessRequested = true;
                access = await listener.RequestAccessAsync();
            }
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                Status = "Notification access is turned off in Windows Settings → Privacy & security → Notifications";
                return;
            }

            var list = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            var ids = new HashSet<uint>(list.Select(n => n.Id));
            Status = "Connected";
            if (!_primed)
            {
                _known = ids;
                _primed = true;
                return;
            }

            foreach (var n in list.Where(n => !_known.Contains(n.Id)).OrderBy(n => n.CreationTime))
            {
                var evt = await ToEventAsync(n);
                if (evt != null) Posted?.Invoke(evt);
            }
            foreach (var gone in _known.Where(id => !ids.Contains(id)))
                Removed?.Invoke(gone);
            _known = ids;
        }
        catch (Exception ex)
        {
            Status = "Unavailable on this system";
            Log.Debug($"Reading app notifications failed: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private async Task<AppNotificationEvent?> ToEventAsync(UserNotification n)
    {
        try
        {
            var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            var texts = binding?.GetTextElements().Select(t => t.Text?.Trim() ?? "").Where(t => t.Length > 0).ToList() ?? new List<string>();
            if (texts.Count == 0) return null;

            string appName = "", appId = "";
            try
            {
                appName = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
                appId = n.AppInfo?.AppUserModelId ?? "";
            }
            catch { /* some senders have no app info */ }

            var title = texts[0];
            var body = texts.Count > 1 ? string.Join(" · ", texts.Skip(1)) : null;
            var isCall = texts.Any(t => CallText().IsMatch(t));
            return new AppNotificationEvent(n.Id, appName, appId.Length > 0 ? appId : null, title, body, isCall, await LogoAsync(n, appId));
        }
        catch (Exception ex)
        {
            Log.Debug($"Couldn't read a notification: {ex.Message}");
            return null;
        }
    }

    private async Task<byte[]?> LogoAsync(UserNotification n, string appId)
    {
        if (appId.Length > 0 && _logos.TryGetValue(appId, out var cached)) return cached;
        byte[]? bytes = null;
        try
        {
            var reference = n.AppInfo?.DisplayInfo?.GetLogo(new Size(64, 64));
            if (reference != null)
            {
                using var stream = await reference.OpenReadAsync();
                if (stream.Size is > 0 and < 1024 * 1024)
                {
                    bytes = new byte[stream.Size];
                    using var reader = new DataReader(stream.GetInputStreamAt(0));
                    await reader.LoadAsync((uint)stream.Size);
                    reader.ReadBytes(bytes);
                }
            }
        }
        catch { bytes = null; }
        if (appId.Length > 0) _logos[appId] = bytes;
        return bytes;
    }

    [GeneratedRegex(@"\b(incoming (voice |video |audio )?call|is calling|calling you|voice call|video call|call from)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CallText();

    public void Dispose()
    {
        _enabled = false;
        _timer.Dispose();
    }
}
