using Microsoft.Win32;
using Nova.Core.Settings;

namespace Nova.Platform.Services;

/// <summary>HKCU Run-key access (no admin rights needed).</summary>
public sealed class StartupRegistry : IStartupRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public string? GetRunValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(name) as string;
    }

    public void SetRunValue(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(name, command, RegistryValueKind.String);
    }

    public void DeleteRunValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public byte[]? GetStartupApproved(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return key?.GetValue(name) as byte[];
    }

    public void DeleteStartupApproved(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
