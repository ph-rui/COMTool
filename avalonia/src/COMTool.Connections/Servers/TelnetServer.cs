using System.Net;
using System.Net.Sockets;
using System.Text;

namespace COMTool.Connections.Servers;

/// <summary>
/// Minimal telnet server: accepts TCP connections and echoes whatever the
/// client sends, so a telnet client gets a live terminal-like session. Every
/// received line is reported through LogMessage. Telnet IAC negotiation
/// bytes (0xFF sequences) are echoed back but skipped in the line log so the
/// output stays readable.
/// </summary>
public sealed class TelnetServer : IServerHost
{
    private const int Iac = 0xFF;

    private readonly int _requestedPort;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public TelnetServer(int port)
    {
        _requestedPort = port;
    }

    public string Name => "Telnet server";

    public bool IsRunning => _listener is not null;

    public int Port { get; private set; }

    public event Action<string>? LogMessage;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_listener is not null)
        {
            return Task.CompletedTask;
        }

        var listener = new TcpListener(new IPEndPoint(IPAddress.Any, _requestedPort));
        listener.Start();
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, _cts.Token), CancellationToken.None);
        LogMessage?.Invoke($"Telnet server started on port {Port}");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_listener is null)
        {
            return;
        }

        _cts?.Cancel();
        _listener.Stop();
        _listener = null;

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
            _acceptLoop = null;
        }

        _cts?.Dispose();
        _cts = null;
        LogMessage?.Invoke("Telnet server stopped");
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            _ = Task.Run(
                () => HandleClientAsync(client, token),
                CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
        LogMessage?.Invoke($"Client {remote} connected");
        try
        {
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var line = new StringBuilder();
            int read;
            while (!token.IsCancellationRequested
                && (read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                // Echo everything back so the client sees a live session.
                await stream.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);

                for (var i = 0; i < read; i++)
                {
                    var b = buffer[i];
                    if (b == Iac)
                    {
                        // Skip the IAC command and its one option byte so
                        // negotiation garbage does not pollute the line log.
                        if (i + 1 < read)
                        {
                            i++;
                        }
                        continue;
                    }
                    if (b == '\n' || b == '\r')
                    {
                        // CRLF logs once at the \r; the following \n sees
                        // an empty line. Bare \r (old-Mac style) also works.
                        var text = line.ToString().TrimEnd(' ');
                        if (text.Length > 0)
                        {
                            LogMessage?.Invoke(text);
                        }
                        line.Clear();
                    }
                    else if (b >= 0x20 && b < 0x7F)
                    {
                        line.Append((char)b);
                        if (line.Length > 200)
                        {
                            line.Clear();
                        }
                    }
                    else
                    {
                        line.Clear();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // server stopping
        }
        catch (IOException)
        {
            // client dropped
        }
        finally
        {
            client.Dispose();
            LogMessage?.Invoke($"Client {remote} disconnected");
        }
    }
}
