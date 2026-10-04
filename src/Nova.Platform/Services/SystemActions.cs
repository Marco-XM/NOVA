using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Nova.Core.Logging;
using Nova.Core.QuickApps;
using static Nova.Platform.Interop.NativeMethods;

namespace Nova.Platform.Services;

/// <summary>Real process starter used by <see cref="AppLauncher"/>.</summary>
public sealed class ShellProcessStarter : IProcessStarter
{
    public bool TryStart(ProcessStartInfo info, out string? error)
    {
        try
        {
            using var _ = Process.Start(info);
            error = null;
            return true;
        }
        catch (Win32Exception ex)
        {
            error = ex.NativeErrorCode == 1223 ? "Launch was cancelled." : ex.Message;
            Log.Warn($"Starting {info.FileName} failed", ex);
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.Warn($"Starting {info.FileName} failed", ex);
            return false;
        }
    }
}

public static class SystemActions
{
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_S = 0x53;

    /// <summary>Opens the Windows Search flyout (equivalent to pressing Win+S).</summary>
    public static bool OpenWindowsSearch()
    {
        var inputs = new[]
        {
            Key(VK_LWIN, false), Key(VK_S, false), Key(VK_S, true), Key(VK_LWIN, true),
        };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == inputs.Length;
    }

    public static bool OpenUri(string uri)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Opening {uri} failed", ex);
            return false;
        }
    }

    public static void OpenFolder(string path) => OpenUri(path);

    public static void RevealFile(string path)
    {
        try { using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Reveal failed", ex); }
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } },
    };
}
