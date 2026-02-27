using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace TerrariaModManager;

public class SingleInstance : IDisposable
{
    private const string MutexName = "TerrariaModManager_SingleInstance";
    private const string PipeName = "TerrariaModManager_Pipe";

    private Mutex? _mutex;
    private CancellationTokenSource? _cts;
    private bool _isFirstInstance;

    public event Action<string>? MessageReceived;

    public bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out _isFirstInstance);
        return _isFirstInstance;
    }

    public void StartListening()
    {
        if (!_isFirstInstance) return;

        _cts = new CancellationTokenSource();
        Task.Run(() => ListenLoop(_cts.Token));
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName,
                    PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(server);
                var message = await reader.ReadToEndAsync(ct);

                if (!string.IsNullOrWhiteSpace(message))
                    MessageReceived?.Invoke(message);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Brief delay before retrying on unexpected errors
                try { await Task.Delay(500, ct); } catch { break; }
            }
        }
    }

    public static bool SendToExisting(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000); // 2 second timeout

            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.Write(message);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        if (_isFirstInstance)
            _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
