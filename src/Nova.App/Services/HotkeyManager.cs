using Nova.Core.Hotkeys;
using Nova.Core.Logging;
using Nova.Core.QuickApps;
using Nova.Core.Settings;
using Nova.Platform.Services;

namespace Nova.App.Services;

/// <summary>Registers the notch hotkey and the per-app quick launch hotkeys from settings.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const string MainOwner = "notch";
    private readonly GlobalHotkeyService _service = new();
    private readonly Dictionary<string, string> _appErrors = new();

    public string? MainHotkeyError { get; private set; }
    public IReadOnlyDictionary<string, string> AppHotkeyErrors => _appErrors;

    public event Action? StatusChanged;

    public void Apply(AppSettings settings, Action onMainHotkey, Action<QuickApp> onAppHotkey)
    {
        _service.UnregisterOwner(MainOwner);
        _service.UnregisterWhere(o => o.StartsWith("app:", StringComparison.Ordinal));
        _appErrors.Clear();
        MainHotkeyError = null;

        if (!string.IsNullOrWhiteSpace(settings.Behavior.Hotkey))
        {
            if (Hotkey.TryParse(settings.Behavior.Hotkey, out var hotkey))
            {
                var result = _service.Register(MainOwner, hotkey, onMainHotkey);
                if (!result.Success) MainHotkeyError = result.Error;
            }
            else
            {
                MainHotkeyError = $"\"{settings.Behavior.Hotkey}\" isn't a valid shortcut.";
            }
        }

        foreach (var app in settings.QuickApps.Apps)
        {
            if (string.IsNullOrWhiteSpace(app.Hotkey)) continue;
            if (!Hotkey.TryParse(app.Hotkey, out var hk))
            {
                _appErrors[app.Id] = "Invalid shortcut";
                continue;
            }
            var captured = app;
            var result = _service.Register("app:" + app.Id, hk, () => onAppHotkey(captured));
            if (!result.Success) _appErrors[app.Id] = result.Error ?? "Unavailable";
        }

        if (MainHotkeyError != null) Log.Warn($"Main hotkey unavailable: {MainHotkeyError}");
        StatusChanged?.Invoke();
    }

    /// <summary>Temporarily releases all hotkeys (while the settings hotkey recorder listens).</summary>
    public void Suspend()
    {
        _service.UnregisterOwner(MainOwner);
        _service.UnregisterWhere(o => o.StartsWith("app:", StringComparison.Ordinal));
    }

    public void Dispose() => _service.Dispose();
}
