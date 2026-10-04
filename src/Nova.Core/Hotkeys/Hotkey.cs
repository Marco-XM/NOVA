using System.Text;

namespace Nova.Core.Hotkeys;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

public enum HotkeyConflictLevel { None, Warning, Blocked }

public readonly record struct HotkeyConflict(HotkeyConflictLevel Level, string? Message)
{
    public static HotkeyConflict Ok => new(HotkeyConflictLevel.None, null);
}

/// <summary>A key combination, using Win32 virtual-key codes (matches RegisterHotKey).</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    private static readonly Dictionary<string, int> NameToKey = BuildKeyMap();
    private static readonly Dictionary<int, string> KeyToName = NameToKey
        .GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);

    public bool IsEmpty => VirtualKey == 0;

    public override string ToString()
    {
        if (IsEmpty) return "";
        var sb = new StringBuilder();
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) sb.Append("Win+");
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) sb.Append("Ctrl+");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) sb.Append("Alt+");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) sb.Append("Shift+");
        sb.Append(KeyName(VirtualKey));
        return sb.ToString();
    }

    public static string KeyName(int vk) => KeyToName.TryGetValue(vk, out var name) ? name : $"0x{vk:X2}";

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        var mods = HotkeyModifiers.None;
        int key = 0;
        foreach (var raw in parts)
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= HotkeyModifiers.Ctrl; continue;
                case "alt": mods |= HotkeyModifiers.Alt; continue;
                case "shift": mods |= HotkeyModifiers.Shift; continue;
                case "win": case "windows": case "meta": mods |= HotkeyModifiers.Win; continue;
            }
            if (key != 0) return false; // two non-modifier keys
            var match = NameToKey.FirstOrDefault(kv => string.Equals(kv.Key, raw, StringComparison.OrdinalIgnoreCase));
            if (match.Key is null) return false;
            key = match.Value;
        }
        if (key == 0) return false;
        hotkey = new Hotkey(mods, key);
        return true;
    }

    public static bool IsModifierKey(int vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    /// <summary>Checks a combination against well known Windows / application shortcuts.</summary>
    public HotkeyConflict CheckConflicts()
    {
        if (IsEmpty) return new(HotkeyConflictLevel.Blocked, "Choose a key.");
        var m = Modifiers;
        var name = KeyName(VirtualKey);
        if (m == HotkeyModifiers.None)
            return new(HotkeyConflictLevel.Blocked, "Add at least one modifier (Ctrl, Alt, Shift or Win) so normal typing isn't intercepted.");
        if (m == HotkeyModifiers.Shift)
            return new(HotkeyConflictLevel.Blocked, "Shift + a key is used for typing capitals and symbols.");

        var combo = ToString();
        string[] blocked =
        {
            "Ctrl+Alt+Delete", "Ctrl+Shift+Escape", "Alt+Tab", "Alt+F4", "Alt+Escape", "Ctrl+Escape",
            "Win+L", "Win+D", "Win+E", "Win+R", "Win+Tab", "Win+I", "Win+A", "Win+S", "Win+X", "Win+V",
            "Win+Space", "Win+Shift+S", "Win+P", "Win+K", "Win+H", "Win+M", "Win+Up", "Win+Down", "Win+Left", "Win+Right",
            "Win+Period", "Win+N", "Win+W", "Win+G", "Win+Alt+R", "Win+Ctrl+D", "Win+Ctrl+Left", "Win+Ctrl+Right",
        };
        if (blocked.Contains(combo, StringComparer.OrdinalIgnoreCase))
            return new(HotkeyConflictLevel.Blocked, $"{combo} is reserved by Windows.");

        if (combo.Equals("Ctrl+Space", StringComparison.OrdinalIgnoreCase))
            return new(HotkeyConflictLevel.Warning, "Ctrl+Space toggles input methods (IME) and triggers autocomplete in many editors.");
        if (combo.Equals("Alt+Space", StringComparison.OrdinalIgnoreCase))
            return new(HotkeyConflictLevel.Warning, "Alt+Space opens the window menu (and PowerToys Run by default).");
        if (combo.Equals("Ctrl+Shift+Space", StringComparison.OrdinalIgnoreCase))
            return new(HotkeyConflictLevel.Warning, "Ctrl+Shift+Space shows parameter hints in code editors.");
        if (m == HotkeyModifiers.Ctrl && name.Length == 1)
            return new(HotkeyConflictLevel.Warning, $"Ctrl+{name} is a common application shortcut and will stop working in other apps.");
        if (m == HotkeyModifiers.Alt && name.Length == 1)
            return new(HotkeyConflictLevel.Warning, $"Alt+{name} opens menus in many applications.");
        if (m == HotkeyModifiers.Win)
            return new(HotkeyConflictLevel.Warning, $"Win+{name} may be used by Windows or a future Windows update.");
        return HotkeyConflict.Ok;
    }

    private static Dictionary<string, int> BuildKeyMap()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var c = 'A'; c <= 'Z'; c++) map[c.ToString()] = c;
        for (var d = '0'; d <= '9'; d++) map[d.ToString()] = d;
        for (var f = 1; f <= 24; f++) map["F" + f] = 0x6F + f;
        for (var n = 0; n <= 9; n++) map["Num" + n] = 0x60 + n;
        map["Space"] = 0x20; map["Enter"] = 0x0D; map["Tab"] = 0x09; map["Escape"] = 0x1B; map["Backspace"] = 0x08;
        map["Insert"] = 0x2D; map["Delete"] = 0x2E; map["Home"] = 0x24; map["End"] = 0x23;
        map["PageUp"] = 0x21; map["PageDown"] = 0x22; map["Left"] = 0x25; map["Up"] = 0x26; map["Right"] = 0x27; map["Down"] = 0x28;
        map["Pause"] = 0x13; map["PrintScreen"] = 0x2C; map["ScrollLock"] = 0x91;
        map["Semicolon"] = 0xBA; map["Plus"] = 0xBB; map["Comma"] = 0xBC; map["Minus"] = 0xBD; map["Period"] = 0xBE;
        map["Slash"] = 0xBF; map["Backtick"] = 0xC0; map["OpenBracket"] = 0xDB; map["Backslash"] = 0xDC; map["CloseBracket"] = 0xDD; map["Quote"] = 0xDE;
        map["NumMultiply"] = 0x6A; map["NumAdd"] = 0x6B; map["NumSubtract"] = 0x6D; map["NumDecimal"] = 0x6E; map["NumDivide"] = 0x6F;
        return map;
    }
}
