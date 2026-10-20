using System.Net.Sockets;

namespace COMTool.Connections;

public sealed class TcpClientSession : ITcpSession
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private int _closed;

    private TcpClientSession(TcpClient client, NetworkStream stream)
    {
        _client = client;
        _stream = stream;
        Pty = new PtyStreamConnection(
            stream,
            stream,
            close: CloseTransport,
            ownsStreams: false);
    }

    public PtyStreamConnection Pty { get; }

    public bool IsConnected =>
        Volatile.Read(ref _closed) == 0
        && _client.Connected
        && !Pty.IsInputCompleted;

    public string LocalEndpoint
    {
        get
        {
            try
            {
                return _client.Client.LocalEndPoint?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }
    }

    public IReadOnlyList<TcpClientInfo> Clients => Array.Empty<TcpClientInfo>();

    public string? SelectedClient => null;

    public event EventHandler? ClientsChanged
    {
        add { }
        remove { }
    }

    public static async Task<TcpClientSession> ConnectAsync(
        TcpClientOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var host = (options.Host ?? "").Trim();
        if (host.Length == 0)
        {
            throw new InvalidOperationException("TCP target host must not be empty.");
        }

        if (options.Port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("TCP target port must be between 1 and 65535.");
        }

        var timeout = options.Timeout is { } requested && requested > TimeSpan.Zero
            ? requested
            : TimeSpan.FromSeconds(10);
        var client = new TcpClient { NoDelay = true };
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(host, options.Port, timeoutSource.Token)
                .ConfigureAwait(false);
            return new TcpClientSession(client, client.GetStream());
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public void SelectClient(string? endpoint)
    {
        // A client connection has exactly one peer; selection is a server
        // concept and intentionally does nothing here.
    }

    public void DisconnectClient(string? endpoint) => CloseTransport();

    public void Dispose() => Pty.Dispose();

    private void CloseTransport()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        try
        {
            _stream.Dispose();
        }
        catch
        {
            // The peer may already have closed the socket.
        }

        try
        {
            _client.Dispose();
        }
        catch
        {
            // The peer may already have closed the socket.
        }
    }
}
