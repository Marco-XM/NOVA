using Nova.Core.Logging;
using static Nova.Platform.Interop.NativeMethods;

namespace Nova.Platform.Services;

/// <summary>
/// Detects finished browser downloads in the user's Downloads folder. Browsers write to a temporary
/// file (.crdownload, .part, …) and rename it when the download completes; that rename is the signal.
/// Windows has no system-wide "download completed" event, so this is the closest reliable mechanism.
/// </summary>
public sealed class DownloadWatcher : IDisposable
{
    private static readonly string[] TempExtensions = { ".crdownload", ".part", ".partial", ".download", ".opdownload", ".tmp" };
    private static readonly Guid FolderIdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");
    private readonly Dictionary<string, DateTime> _recent = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;

    public event Action<string, string>? DownloadCompleted;
    public string? Folder { get; private set; }

    public void Start()
    {
        try
        {
            Folder = SHGetKnownFolderPath(FolderIdDownloads, 0, IntPtr.Zero);
            if (!Directory.Exists(Folder)) return;
            _watcher = new FileSystemWatcher(Folder)
            {
                NotifyFilter = NotifyFilters.FileName,
                IncludeSubdirectories = false,
                InternalBufferSize = 16 * 1024,
            };
            _watcher.Renamed += OnRenamed;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            Log.Warn("Download watcher unavailable", ex);
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        var oldExt = Path.GetExtension(e.OldFullPath);
        var newExt = Path.GetExtension(e.FullPath);
        if (!TempExtensions.Contains(oldExt, StringComparer.OrdinalIgnoreCase)) return;
        if (TempExtensions.Contains(newExt, StringComparer.OrdinalIgnoreCase)) return;

        lock (_recent)
        {
            if (_recent.TryGetValue(e.FullPath, out var at) && DateTime.UtcNow - at < TimeSpan.FromSeconds(5)) return;
            _recent[e.FullPath] = DateTime.UtcNow;
            if (_recent.Count > 50) _recent.Clear();
        }
        DownloadCompleted?.Invoke(Path.GetFileName(e.FullPath), e.FullPath);
    }

    public void Dispose()
    {
        if (_watcher is null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Renamed -= OnRenamed;
        _watcher.Dispose();
        _watcher = null;
    }
}
