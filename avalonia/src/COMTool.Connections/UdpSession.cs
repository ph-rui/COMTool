using System.Net;
using System.Net.Sockets;

namespace COMTool.Connections;

public sealed record UdpSessionOptions(
    string Host,
    int Port,
    int LocalPort);

/// <summary>
/// A UDP endpoint matching the Python behavior: bind 0.0.0.0:localPort,
/// receive every datagram, and send each write as one datagram to the target.
/// UDP has no client/server distinction beyond the dialog labels, so both
/// modes use the same transport.
/// </summary>
public sealed class UdpSession : ITcpSession
{
    private readonly UdpClient _client;
    private readonly IPEndPoint _target;
    private readonly ByteQueueStream _incoming = new();
    private readonly CancellationTokenSource _cancellation = new();
    private int _closed;
    private int _disposed;

    private UdpSession(UdpClient client, IPEndPoint target)
    {
        _client = client;
        _target = target;
        Pty = new PtyStreamConnection(
            _incoming,
            new UdpWriteStream(this),
            close: CloseTransport,
            ownsStreams: false);
        _ = Task.Run(ReceiveLoopAsync);
    }

    public PtyStreamConnection Pty { get; }

    public bool IsConnected => Volatile.Read(ref _closed) == 0;

    public int Port
    {
        get
        {
            try
            {
                return ((IPEndPoint)_client.Client.LocalEndPoint!).Port;
            }
            catch
            {
                return 0;
            }
        }
    }

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

    public static async Task<UdpSession> CreateAsync(
        UdpSessionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var host = (options.Host ?? "").Trim();
        if (host.Length == 0)
        {
            throw new InvalidOperationException("UDP target host must not be empty.");
        }

        if (options.Port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("UDP target port must be between 1 and 65535.");
        }

        if (options.LocalPort is <= 0 or > 65535)
        {
            throw new InvalidOperationException("UDP local port must be between 1 and 65535.");
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken)
            .ConfigureAwait(false);
        var address = addresses.FirstOrDefault(candidate =>
                candidate.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault()
            ?? throw new InvalidOperationException($"Could not resolve UDP target '{host}'.");
        var client = new UdpClient(new IPEndPoint(IPAddress.Any, options.LocalPort));
        return new UdpSession(client, new IPEndPoint(address, options.Port));
    }

    public void SelectClient(string? endpoint)
    {
        // UDP has no per-peer connection selection.
    }

    public void DisconnectClient(string? endpoint) => CloseTransport();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Pty.Dispose();
        _cancellation.Dispose();
    }

    private async Task ReceiveLoopAsync()
    {
        var token = _cancellation.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await _client.ReceiveAsync(token).ConfigureAwait(false);
                if (result.Buffer.Length > 0)
                {
                    _incoming.Add(result.Buffer);
                }
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
                // A transient UDP receive error must not stop the endpoint.
            }
        }
    }

    private void Send(byte[] data)
    {
        if (Volatile.Read(ref _closed) != 0)
        {
            throw new ObjectDisposedException(nameof(UdpSession));
        }

        _client.Send(data, data.Length, _target);
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
            _client.Dispose();
        }
        catch
        {
            // The socket may already be closed.
        }

        _incoming.Complete();
    }

    private sealed class UdpWriteStream : Stream
    {
        private readonly UdpSession _session;

        public UdpWriteStream(UdpSession session) => _session = session;

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
