using System.Net.Sockets;
using System.Text;

namespace COMTool.Connections;

public sealed record TelnetConnectionOptions(
    string Host,
    int Port = 23,
    string? User = null,
    string? Password = null,
    int Columns = 100,
    int Rows = 30,
    TimeSpan? Timeout = null,
    string TerminalType = "XTERM-256COLOR");

/// <summary>
/// A Telnet client transport. It follows the same socket/receive-loop shape
/// as <see cref="TcpClientSession"/> and exposes the decoded byte stream as a
/// <see cref="PtyStreamConnection"/> for the terminal control.
/// </summary>
public sealed class TelnetConnection : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly TelnetStream _telnet;
    private readonly TelnetAutoLogin? _autoLogin;
    private readonly ByteQueueStream _incoming = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _gate = new();
    private int _closed;
    private int _disposed;

    private TelnetConnection(
        TcpClient client,
        NetworkStream stream,
        TelnetStream telnet,
        TelnetAutoLogin? autoLogin)
    {
        _client = client;
        _stream = stream;
        _telnet = telnet;
        _autoLogin = autoLogin;
        Pty = new PtyStreamConnection(
            _incoming,
            new TelnetWriteStream(this),
            resize: Resize,
            close: CloseTransport,
            ownsStreams: false);
    }

    public PtyStreamConnection Pty { get; }

    public bool IsConnected => Volatile.Read(ref _closed) == 0;

    public static async Task<TelnetConnection> ConnectAsync(
        TelnetConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var host = (options.Host ?? "").Trim();
        if (host.Length == 0)
        {
            throw new InvalidOperationException("Telnet host must not be empty.");
        }

        if (options.Port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("Telnet port must be between 1 and 65535.");
        }

        var timeout = options.Timeout is { } requested && requested > TimeSpan.Zero
            ? requested
            : TimeSpan.FromSeconds(10);
        var client = new TcpClient { NoDelay = true };
        NetworkStream? stream = null;
        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(host, options.Port, timeoutSource.Token)
                .ConfigureAwait(false);
            stream = client.GetStream();
            var telnet = new TelnetStream(
                stream,
                options.TerminalType,
                options.Columns,
                options.Rows);
            var user = (options.User ?? "").Trim();
            var autoLogin = user.Length == 0
                ? null
                : new TelnetAutoLogin(
                    user,
                    options.Password,
                    text => telnet.WriteData(Encoding.UTF8.GetBytes(text)));
            var connection = new TelnetConnection(client, stream, telnet, autoLogin);
            telnet.Start();
            _ = Task.Run(connection.ReceiveLoopAsync, CancellationToken.None);
            return connection;
        }
        catch
        {
            try
            {
                stream?.Dispose();
            }
            catch
            {
                // The peer may already have closed the socket.
            }

            client.Dispose();
            throw;
        }
    }

    public void Resize(int columns, int rows)
    {
        try
        {
            _telnet.Resize(columns, rows);
        }
        catch (IOException)
        {
            // The transport is already closing.
        }
        catch (ObjectDisposedException)
        {
            // The transport is already closing.
        }
        catch (SocketException)
        {
            // The transport is already closing.
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

    private async Task ReceiveLoopAsync()
    {
        var token = _cancellation.Token;
        var buffer = new byte[8192];
        try
        {
            while (!token.IsCancellationRequested)
            {
                var read = await _telnet.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                _autoLogin?.Observe(buffer.AsSpan(0, read));
                _incoming.Add(buffer.AsSpan(0, read).ToArray());
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (token.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
        finally
        {
            Pty.Close();
        }
    }

    private void Send(ReadOnlySpan<byte> data)
    {
        if (Volatile.Read(ref _closed) != 0)
        {
            throw new IOException("Telnet connection is not connected.");
        }

        _telnet.WriteData(data);
    }

    private void CloseTransport()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The connection is already being disposed.
        }

        lock (_gate)
        {
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
                _client.Close();
            }
            catch
            {
                // The peer may already have closed the socket.
            }
        }

        _incoming.Complete();
    }

    private sealed class TelnetWriteStream : Stream
    {
        private readonly TelnetConnection _connection;

        public TelnetWriteStream(TelnetConnection connection) => _connection = connection;

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
            _connection.Send(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer) =>
            _connection.Send(buffer);
    }
}
