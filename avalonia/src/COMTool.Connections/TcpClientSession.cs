using System.Net.Sockets;

namespace COMTool.Connections;

public sealed class TcpClientSession : ITcpSession
{
    private readonly TcpClientOptions _options;
    private readonly ByteQueueStream _incoming = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _gate = new();
    private TcpClient? _client;
    private NetworkStream? _stream;
    private int _closed;
    private int _disposed;
    private int _reconnecting;

    private TcpClientSession(TcpClientOptions options)
    {
        _options = options;
        Pty = new PtyStreamConnection(
            _incoming,
            new ClientWriteStream(this),
            close: CloseTransport,
            ownsStreams: false);
    }

    public PtyStreamConnection Pty { get; }

    public bool IsConnected =>
        Volatile.Read(ref _closed) == 0
        && Volatile.Read(ref _reconnecting) == 0
        && CurrentStream() is not null;

    public string LocalEndpoint
    {
        get
        {
            try
            {
                TcpClient? client;
                lock (_gate)
                {
                    client = _client;
                }

                return client?.Client.LocalEndPoint?.ToString() ?? "";
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

    /// <summary>
    /// Raised from the transport receive loop when automatic reconnect changes
    /// the session state. The app marshals this onto the UI thread.
    /// </summary>
    public event EventHandler<TcpClientStatusEventArgs>? StatusChanged;

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

        var session = new TcpClientSession(options with { Host = host });
        try
        {
            await session.ConnectOnceAsync(cancellationToken).ConfigureAwait(false);
            _ = Task.Run(session.ReceiveLoopAsync, CancellationToken.None);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public void SelectClient(string? endpoint)
    {
        // A client connection has exactly one peer; selection is a server
        // concept and intentionally does nothing here.
    }

    public void DisconnectClient(string? endpoint) => Pty.Close();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Pty.Dispose();
        _cancellation.Dispose();
    }

    private async Task ConnectOnceAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _closed) != 0)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        var timeout = _options.Timeout is { } requested && requested > TimeSpan.Zero
            ? requested
            : TimeSpan.FromSeconds(10);
        var client = new TcpClient { NoDelay = true };
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(_options.Host, _options.Port, timeoutSource.Token)
                .ConfigureAwait(false);
            var stream = client.GetStream();
            lock (_gate)
            {
                if (Volatile.Read(ref _closed) != 0)
                {
                    stream.Dispose();
                    client.Dispose();
                    throw new OperationCanceledException(cancellationToken);
                }

                _client = client;
                _stream = stream;
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var token = _cancellation.Token;
        var buffer = new byte[8192];
        while (!token.IsCancellationRequested)
        {
            var stream = CurrentStream();
            if (stream is null)
            {
                if (!_options.AutoReconnect || !await TryReconnectAsync(token).ConfigureAwait(false))
                {
                    break;
                }

                continue;
            }

            try
            {
                var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read <= 0)
                {
                    if (!await HandleDisconnectAsync(token).ConfigureAwait(false))
                    {
                        break;
                    }

                    continue;
                }

                _incoming.Add(buffer.AsSpan(0, read).ToArray());
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                if (!await HandleDisconnectAsync(token).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (SocketException)
            {
                if (!await HandleDisconnectAsync(token).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
    }

    private async Task<bool> HandleDisconnectAsync(CancellationToken token)
    {
        CloseCurrent();
        if (Volatile.Read(ref _closed) != 0)
        {
            return false;
        }

        if (!_options.AutoReconnect)
        {
            Pty.Close();
            return false;
        }

        Interlocked.Exchange(ref _reconnecting, 1);
        RaiseStatus("Connection lose", false);
        return await TryReconnectAsync(token).ConfigureAwait(false);
    }

    private async Task<bool> TryReconnectAsync(CancellationToken token)
    {
        var interval = _options.ReconnectInterval is { } requested && requested > TimeSpan.Zero
            ? requested
            : TimeSpan.FromSeconds(1);

        while (!token.IsCancellationRequested && Volatile.Read(ref _closed) == 0)
        {
            try
            {
                await Task.Delay(interval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            try
            {
                await ConnectOnceAsync(token).ConfigureAwait(false);
                Interlocked.Exchange(ref _reconnecting, 0);
                RaiseStatus("Reconnected", true);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch
            {
                // Keep retrying at the configured interval.
            }
        }

        return false;
    }

    private NetworkStream? CurrentStream()
    {
        lock (_gate)
        {
            return _stream;
        }
    }

    private void CloseCurrent()
    {
        TcpClient? client;
        NetworkStream? stream;
        lock (_gate)
        {
            client = _client;
            stream = _stream;
            _client = null;
            _stream = null;
        }

        try
        {
            stream?.Dispose();
        }
        catch
        {
            // The peer may already have closed the socket.
        }

        try
        {
            client?.Close();
        }
        catch
        {
            // The peer may already have closed the socket.
        }
    }

    private void RaiseStatus(string message, bool isConnected)
    {
        try
        {
            StatusChanged?.Invoke(this, new TcpClientStatusEventArgs(message, isConnected));
        }
        catch
        {
            // A UI subscriber must not break the receive loop.
        }
    }

    private void Send(byte[] data)
    {
        var stream = CurrentStream();
        if (stream is null || Volatile.Read(ref _closed) != 0)
        {
            throw new IOException("TCP client is not connected.");
        }

        stream.Write(data, 0, data.Length);
        stream.Flush();
    }

    private void CloseTransport()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        _cancellation.Cancel();
        CloseCurrent();
        _incoming.Complete();
    }

    private sealed class ClientWriteStream : Stream
    {
        private readonly TcpClientSession _session;

        public ClientWriteStream(TcpClientSession session) => _session = session;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            _session.Send(buffer.AsSpan(offset, count).ToArray());

        public override void Write(ReadOnlySpan<byte> buffer) =>
            _session.Send(buffer.ToArray());
    }
}
