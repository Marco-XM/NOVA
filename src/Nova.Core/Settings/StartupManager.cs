using Nova.Core.Logging;

namespace Nova.Core.Settings;

/// <summary>Minimal registry surface used by <see cref="StartupManager"/> (HKCU only).</summary>
public interface IStartupRegistry
{
    string? GetRunValue(string name);
    void SetRunValue(string name, string command);
    void DeleteRunValue(string name);
    /// <summary>Raw StartupApproved\Run entry (Task Manager enable/disable state), or null.</summary>
    byte[]? GetStartupApproved(string name);
    void DeleteStartupApproved(string name);
}

public enum StartupStatus
{
    Disabled,
    Enabled,
    /// <summary>Registered, but the user disabled it in Task Manager / Settings → Startup apps.</summary>
    DisabledByUser,
    /// <summary>Registered with an outdated executable path (e.g. after moving the app).</summary>
    PathMismatch,
}

/// <summary>Registers NOVA in HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
public sealed class StartupManager
{
    public const string ValueName = "NOVA";
    public const string BackgroundArgument = "--background";
    private readonly IStartupRegistry _registry;
    private readonly string _executablePath;

    public StartupManager(IStartupRegistry registry, string executablePath)
    {
        _registry = registry;
        _executablePath = executablePath;
    }

    public string ExpectedCommand => $"\"{_executablePath}\" {BackgroundArgument}";

    public StartupStatus GetStatus()
    {
        var value = _registry.GetRunValue(ValueName);
        if (string.IsNullOrWhiteSpace(value)) return StartupStatus.Disabled;
        var approved = _registry.GetStartupApproved(ValueName);
        // First byte: 0x02/0x06 = enabled, 0x03/0x07 = disabled (odd values mean disabled).
        if (approved is { Length: > 0 } && (approved[0] & 0x1) == 1) return StartupStatus.DisabledByUser;
        if (!string.Equals(value.Trim(), ExpectedCommand, StringComparison.OrdinalIgnoreCase)) return StartupStatus.PathMismatch;
        return StartupStatus.Enabled;
    }

    public bool IsEnabled => GetStatus() is StartupStatus.Enabled or StartupStatus.PathMismatch;

    public bool SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                _registry.SetRunValue(ValueName, ExpectedCommand);
                // The user explicitly asked for it, so clear a stale "disabled in Task Manager" flag.
                if (_registry.GetStartupApproved(ValueName) is { Length: > 0 } a && (a[0] & 0x1) == 1)
                    _registry.DeleteStartupApproved(ValueName);
            }
            else
            {
                _registry.DeleteRunValue(ValueName);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not {(enabled ? "enable" : "disable")} launch at startup", ex);
            return false;
        }
    }

    /// <summary>Fixes an outdated path if startup is on (the app was updated or moved).</summary>
    public void RepairIfNeeded()
    {
        if (GetStatus() == StartupStatus.PathMismatch) SetEnabled(true);
    }
}
