using System.Windows.Interop;
using static Nova.Platform.Interop.NativeMethods;

namespace Nova.Platform.Interop;

/// <summary>
/// Hidden message-only window used to receive WM_HOTKEY, WM_CLIPBOARDUPDATE and friends without any
/// visible UI. Must be created on a thread with a dispatcher (the UI thread).
/// </summary>
public sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;
    private readonly List<Func<int, IntPtr, IntPtr, bool>> _handlers = new();

    public MessageWindow(string name)
    {
        var parameters = new HwndSourceParameters(name)
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public IntPtr Handle => _source.Handle;

    /// <summary>Adds a handler; return true from it to mark the message handled.</summary>
    public void AddHandler(Func<int, IntPtr, IntPtr, bool> handler) => _handlers.Add(handler);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        foreach (var handler in _handlers.ToArray())
        {
            if (handler(msg, wParam, lParam))
            {
                handled = true;
                break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
