using System.IO;
using System.IO.Pipes;
using System.Text;
using Nova.Core.Logging;

namespace Nova.App.Infrastructure;

/// <summary>
/// Guarantees one NOVA per user session. A second launch forwards its intent (open settings, debug
/// command, quit) to the running instance over a current-user-only named pipe, then exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private CancellationTokenSource? _cts;

    public SingleInstance(string id)
    {
        var scope = $"{Environment.UserName}-{System.Diagnostics.Process.GetCurrentProcess().SessionId}";
        _mutex = new Mutex(initiallyOwned: true, $"Local\\{id}-{scope}", out var created);
        IsFirst = created;
        _pipeName = $"{id}-{scope}";
    }

    public bool IsFirst { get; }

    /// <summary>Raised on a background thread with each received command.</summary>
    public event Action<string>? CommandReceived;

    public void StartServer()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(line)) CommandReceived?.Invoke(line.Trim());
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Warn("Single-instance pipe error", ex);
                    await Task.Delay(500, token).ConfigureAwait(false);
                }
            }
        }, token);
    }

    public bool Send(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(3000);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(command);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        if (IsFirst)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }
        _mutex.Dispose();
    }
}
