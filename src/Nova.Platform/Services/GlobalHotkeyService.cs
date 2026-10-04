using System.Runtime.InteropServices;
using Nova.Core.Hotkeys;
using Nova.Core.Logging;
using Nova.Platform.Interop;
using static Nova.Platform.Interop.NativeMethods;

namespace Nova.Platform.Services;

public readonly record struct HotkeyRegistration(bool Success, string? Error);

/// <summary>System-wide hotkeys via RegisterHotKey (no keyboard hook, zero cost while idle).</summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<int, (Hotkey Hotkey, Action Callback, string Owner)> _registered = new();
    private int _nextId = 0x4E00;

    public GlobalHotkeyService()
    {
        _window = new MessageWindow("NOVA.Hotkeys");
        _window.AddHandler((msg, wParam, _) =>
        {
            if (msg != WM_HOTKEY) return false;
            if (_registered.TryGetValue(wParam.ToInt32(), out var entry))
            {
                try { entry.Callback(); }
                catch (Exception ex) { Log.Error($"Hotkey handler for {entry.Owner} failed", ex); }
            }
            return true;
        });
    }

    public HotkeyRegistration Register(string owner, Hotkey hotkey, Action callback)
    {
        if (hotkey.IsEmpty) return new(false, "No key chosen");
        var mods = MOD_NOREPEAT;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt)) mods |= MOD_ALT;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Ctrl)) mods |= MOD_CONTROL;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift)) mods |= MOD_SHIFT;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Win)) mods |= MOD_WIN;

        var id = _nextId++;
        if (!RegisterHotKey(_window.Handle, id, mods, (uint)hotkey.VirtualKey))
        {
            var err = Marshal.GetLastWin32Error();
            var message = err == 1409 // ERROR_HOTKEY_ALREADY_REGISTERED
                ? $"{hotkey} is already used by another application."
                : $"Windows rejected {hotkey} (error {err}).";
            Log.Warn($"Hotkey registration failed for {owner}: {message}");
            return new(false, message);
        }
        _registered[id] = (hotkey, callback, owner);
        Log.Info($"Registered hotkey {hotkey} for {owner}");
        return new(true, null);
    }

    public void UnregisterOwner(string owner)
    {
        foreach (var id in _registered.Where(kv => kv.Value.Owner == owner).Select(kv => kv.Key).ToList())
        {
            UnregisterHotKey(_window.Handle, id);
            _registered.Remove(id);
        }
    }

    public void UnregisterWhere(Func<string, bool> ownerPredicate)
    {
        foreach (var owner in _registered.Values.Select(v => v.Owner).Where(ownerPredicate).Distinct().ToList())
            UnregisterOwner(owner);
    }

    public void Dispose()
    {
        foreach (var id in _registered.Keys) UnregisterHotKey(_window.Handle, id);
        _registered.Clear();
        _window.Dispose();
    }
}
