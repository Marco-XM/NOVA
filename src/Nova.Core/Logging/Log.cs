using System.Collections.Concurrent;
using System.Text;

namespace Nova.Core.Logging;

public enum LogLevel { Debug, Info, Warning, Error }

/// <summary>
/// Small, allocation-light rolling file logger. Writes are queued and flushed on a
/// background thread so logging never blocks the UI thread.
/// </summary>
public static class Log
{
    private static readonly ConcurrentQueue<string> Pending = new();
    private static readonly string[] Recent = new string[400];
    private static int _recentIndex;
    private static readonly object RecentLock = new();
    private static readonly AutoResetEvent Signal = new(false);
    private static Thread? _writer;
    private static volatile bool _running;

    public static string? Directory { get; private set; }
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;
    public static event Action<string>? LineWritten;

    public static void Initialize(string directory, int retentionDays = 7)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in new DirectoryInfo(directory).GetFiles("nova-*.log"))
            {
                if (file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-retentionDays)) file.Delete();
            }
        }
        catch { /* best effort */ }

        if (_writer != null) return;
        _running = true;
        _writer = new Thread(WriterLoop) { IsBackground = true, Name = "NOVA log writer", Priority = ThreadPriority.BelowNormal };
        _writer.Start();
    }

    public static string CurrentFile => Path.Combine(Directory ?? Path.GetTempPath(), $"nova-{DateTime.Now:yyyyMMdd}.log");

    public static void Debug(string message) => Write(LogLevel.Debug, message, null);
    public static void Info(string message) => Write(LogLevel.Info, message, null);
    public static void Warn(string message, Exception? ex = null) => Write(LogLevel.Warning, message, ex);
    public static void Error(string message, Exception? ex = null) => Write(LogLevel.Error, message, ex);

    public static void Write(LogLevel level, string message, Exception? ex)
    {
        if (level < MinimumLevel) return;
        var sb = new StringBuilder(128);
        sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ')
          .Append(level switch { LogLevel.Debug => "DBG", LogLevel.Info => "INF", LogLevel.Warning => "WRN", _ => "ERR" })
          .Append(' ').Append(message);
        if (ex != null) sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
        var line = sb.ToString();

        lock (RecentLock)
        {
            Recent[_recentIndex % Recent.Length] = line;
            _recentIndex++;
        }

        if (ex != null && level == LogLevel.Error) line += Environment.NewLine + ex;
        Pending.Enqueue(line);
        Signal.Set();
        LineWritten?.Invoke(line);
    }

    public static IReadOnlyList<string> GetRecentLines()
    {
        lock (RecentLock)
        {
            var count = Math.Min(_recentIndex, Recent.Length);
            var list = new List<string>(count);
            for (var i = _recentIndex - count; i < _recentIndex; i++) list.Add(Recent[i % Recent.Length]);
            return list;
        }
    }

    public static void Flush()
    {
        Signal.Set();
        var spins = 0;
        while (!Pending.IsEmpty && spins++ < 50) Thread.Sleep(10);
    }

    public static void Shutdown()
    {
        Flush();
        _running = false;
        Signal.Set();
    }

    private static void WriterLoop()
    {
        while (_running)
        {
            Signal.WaitOne(2000);
            if (Pending.IsEmpty) continue;
            try
            {
                using var stream = new FileStream(CurrentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                while (Pending.TryDequeue(out var line)) writer.WriteLine(line);
            }
            catch
            {
                // Disk errors must never take the app down; drop the batch.
                while (Pending.TryDequeue(out _)) { }
            }
        }
    }
}
