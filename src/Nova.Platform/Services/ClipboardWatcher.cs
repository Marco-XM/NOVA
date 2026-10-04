using System.Windows;
using System.Windows.Threading;
using Nova.Core.Logging;
using Nova.Platform.Interop;
using static Nova.Platform.Interop.NativeMethods;

namespace Nova.Platform.Services;

/// <summary>
/// Clipboard change notifications via AddClipboardFormatListener. Respects the standard opt-out
/// formats used by password managers so secrets never enter the history.
/// </summary>
public sealed class ClipboardWatcher : IDisposable
{
    private MessageWindow? _window;
    private int _suppressCount;
    private Dispatcher? _dispatcher;

    public event Action<string>? TextCopied;

    public void Start()
    {
        if (_window != null) return;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _window = new MessageWindow("NOVA.Clipboard");
        _window.AddHandler((msg, _, _) =>
        {
            if (msg != WM_CLIPBOARDUPDATE) return false;
            // Read after the owner finished writing all formats.
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ReadClipboard));
            return true;
        });
        if (!AddClipboardFormatListener(_window.Handle)) Log.Warn("AddClipboardFormatListener failed");
    }

    /// <summary>Writes text to the clipboard without recording it in the history again.</summary>
    public bool SetText(string text)
    {
        Interlocked.Increment(ref _suppressCount);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try { Clipboard.SetText(text); return true; }
            catch (Exception) { Thread.Sleep(30); }
        }
        Interlocked.Exchange(ref _suppressCount, 0);
        return false;
    }

    private void ReadClipboard()
    {
        if (Interlocked.Exchange(ref _suppressCount, 0) > 0) return;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing") ||
                    Clipboard.ContainsData("Clipboard Viewer Ignore")) return;
                if (Clipboard.ContainsData("CanIncludeInClipboardHistory") && IsZeroDword(Clipboard.GetData("CanIncludeInClipboardHistory"))) return;
                if (!Clipboard.ContainsText()) return;
                var text = Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text)) TextCopied?.Invoke(text);
                return;
            }
            catch (Exception)
            {
                Thread.Sleep(40); // clipboard is locked by another process
            }
        }
    }

    private static bool IsZeroDword(object? data)
    {
        if (data is MemoryStream stream)
        {
            var buf = new byte[4];
            stream.Position = 0;
            return stream.Read(buf, 0, 4) == 4 && BitConverter.ToInt32(buf, 0) == 0;
        }
        return data is int i && i == 0;
    }

    public void Dispose()
    {
        if (_window is null) return;
        RemoveClipboardFormatListener(_window.Handle);
        _window.Dispose();
        _window = null;
    }
}
