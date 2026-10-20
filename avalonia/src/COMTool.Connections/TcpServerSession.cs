using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace COMTool.Connections;

public sealed class TcpServerSession : ITcpSession
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ConcurrentDictionary<string, TcpClient> _clients =
        new(StringComparer.Ordinal);
    private readonly ByteQueueStream _incoming = new();
    private readonly object _selectionGate = new();
    private string? _selected;
    private int _closed;
    private int _disposed;

    public TcpServerSession(TcpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Port is < 0 or > 65535)
        {
            throw new InvalidOperationException("TCP server port must be between 1 and 65535.");
        }

        _listener = new TcpListener(options.BindAddress ?? IPAddress.Any, options.Port);
        _listener.Start(100);
        Pty = new PtyStreamConnection(
            _incoming,
            new BroadcastWriteStream(this),
            close: CloseTransport,
            ownsStreams: false);
        _ = Task.Run(AcceptLoopAsync);
    }

    public PtyStreamConnection Pty { get; }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public bool IsConnected
    {
        get
        {
            if (Volatile.Read(ref _closed) != 0)
            {
                return false;
            }

            try
            {
                return _listener.Server.IsBound;
            }
            catch
            {
                return false;
            }
        }
    }

    public string LocalEndpoint
    {
        get
        {
            try
            {
                return _listener.LocalEndpoint?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }
    }

    public IReadOnlyList<TcpClientInfo> Clients =>
        _clients.Keys
            .OrderBy(endpoint => endpoint, StringComparer.Ordinal)
            .Select(endpoint => new TcpClientInfo(endpoint))
            .ToList();

    public string? SelectedClient
    {
        get
        {
            lock (_selectionGate)
            {
                return _selected;
            }
        }
    }

    public event EventHandler? ClientsChanged;

    public void SelectClient(string? endpoint)
    {
        lock (_selectionGate)
        {
            if (endpoint is null)
            {
                _selected = null;
            }
            else if (!_clients.ContainsKey(endpoint))
            {
                throw new InvalidOperationException("The selected TCP client is no longer connected.");
            }
            else
            {
                _selected = endpoint;
            }
        }

        RaiseClientsChanged();
    }

    public void DisconnectClient(string? endpoint)
    {
        if (endpoint is null)
        {
            foreach (var client in _clients.Values)
            {
                CloseClient(client);
            }
        }
        else if (_clients.TryGetValue(endpoint, out var client))
        {
            CloseClient(client);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Pty.Dispose();
        _cancellation.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        var token = _cancellation.Token;
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException)
            {
                // A transient accept failure must not stop the listener.
                continue;
            }

            client.NoDelay = true;
            var endpoint = client.Client.RemoteEndPoint?.ToString()
                ?? $"client-{Guid.NewGuid():N}";
            _clients[endpoint] = client;
            RaiseClientsChanged();
            _ = Task.Run(() => ReceiveLoopAsync(endpoint, client, token), token);
        }
    }

    private async Task ReceiveLoopAsync(
        string endpoint,
        TcpClient client,
        CancellationToken token)
    {
        var buffer = new byte[8192];
        try
        {
            var stream = client.GetStream();
            while (!token.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                var selected = SelectedClient;
                if (selected is null || string.Equals(selected, endpoint, StringComparison.Ordinal))
                {
                    _incoming.Add(buffer.AsSpan(0, read).ToArray());
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (ObjectDisposedException)
        {
            // The client or server was closed.
        }
        catch (IOException)
        {
            // The peer disconnected.
        }
        catch (SocketException)
        {
            // The peer disconnected.
        }
        finally
        {
            if (_clients.TryRemove(endpoint, out var removed))
            {
                CloseClient(removed);
            }

            lock (_selectionGate)
            {
                if (string.Equals(_selected, endpoint, StringComparison.Ordinal))
                {
                    _selected = null;
                }
            }

            RaiseClientsChanged();
        }
    }

    private void Send(byte[] data)
    {
        TcpClient[] targets;
        string? selected;
        lock (_selectionGate)
        {
            selected = _selected;
            if (selected is not null)
            {
                if (!_clients.TryGetValue(selected, out var target))
                {
                    throw new IOException("The selected TCP client is no longer connected.");
                }

                targets = new[] { target };
            }
            else
            {
                targets = _clients.Values.ToArray();
            }
        }

        foreach (var target in targets)
        {
            try
            {
                var stream = target.GetStream();
                stream.Write(data, 0, data.Length);
                stream.Flush();
            }
            catch (Exception ex) when (
                ex is IOException or ObjectDisposedException or SocketException)
            {
                if (selected is not null)
                {
                    throw new IOException("Could not send to the selected TCP client.", ex);
                }
            }
        }
    }

    private void RaiseClientsChanged()
    {
        try
        {
            ClientsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // A UI subscriber must not break the receive loop.
        }
    }

    private void CloseTransport()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        _cancellation.Cancel();
        try
        {
            _listener.Stop();
        }
        catch
        {
            // The listener may already be stopped.
        }

        foreach (var client in _clients.Values)
        {
            CloseClient(client);
        }

        _clients.Clear();
        _incoming.Complete();
    }

    private static void CloseClient(TcpClient client)
    {
        try
        {
            client.Close();
        }
        catch
        {
            // The client may already be gone.
        }
    }

    private sealed class BroadcastWriteStream : Stream
    {
        private readonly TcpServerSession _session;

        public BroadcastWriteStream(TcpServerSession session) => _session = session;

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
