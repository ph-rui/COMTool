using System.Net;
using System.Net.Sockets;
using System.Text;

namespace COMTool.Connections.Servers;

/// <summary>
/// Minimal TFTP (RFC 1350) server: RRQ downloads and WRQ uploads in octet
/// mode, 512-byte blocks, one-second ACK timeout with three retransmissions,
/// and a zero-length terminal block. Every file stays under the configured
/// root directory.
/// </summary>
public sealed class TftpServer : IServerHost
{
    private const int BlockSize = 512;
    private const int AckTimeoutMs = 1000;
    private const int MaxRetransmits = 3;

    private readonly string _root;
    private readonly int _requestedPort;
    private readonly bool _showDownloadInfo;
    private readonly bool _showUploadInfo;

    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;

    public TftpServer(string rootDirectory, int port, bool showDownloadInfo,
        bool showUploadInfo)
    {
        _root = Path.GetFullPath(rootDirectory);
        _requestedPort = port;
        _showDownloadInfo = showDownloadInfo;
        _showUploadInfo = showUploadInfo;
    }

    public string Name => "TFTP server";

    public bool IsRunning => _listener is not null;

    public int Port { get; private set; }

    public event Action<string>? LogMessage;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_listener is not null)
        {
            return Task.CompletedTask;
        }

        LogMessage?.Invoke("Starting TFTP server, please wait...");
        var listener = new UdpClient(new IPEndPoint(IPAddress.Any, _requestedPort));
        _listener = listener;
        Port = ((IPEndPoint)listener.Client.LocalEndPoint!).Port;
        _cts = new CancellationTokenSource();
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(listener, _cts.Token), CancellationToken.None);
        LogMessage?.Invoke("TFTP server started");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_listener is null)
        {
            return;
        }

        _cts?.Cancel();
        _listener.Dispose();
        _listener = null;

        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
        }

        _receiveLoop = null;
        _cts?.Dispose();
        _cts = null;
        LogMessage?.Invoke("TFTP server stopped");
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task ReceiveLoopAsync(UdpClient listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await listener.ReceiveAsync(token).ConfigureAwait(false);
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
                if (token.IsCancellationRequested)
                {
                    break;
                }
                continue;
            }

            _ = Task.Run(
                () => HandleRequestAsync(result.Buffer, result.RemoteEndPoint, token),
                CancellationToken.None);
        }
    }

    private async Task HandleRequestAsync(byte[] packet, IPEndPoint remote,
        CancellationToken token)
    {
        if (packet.Length < 4)
        {
            return;
        }

        var opcode = ReadUInt16(packet, 0);
        if (opcode is not (1 or 2))
        {
            using var bad = NewTransferSocket(remote);
            await SendErrorAsync(bad, 4, "Illegal TFTP operation").ConfigureAwait(false);
            return;
        }

        var (fileName, mode) = ParseRequest(packet);
        if (fileName.Length == 0)
        {
            using var bad = NewTransferSocket(remote);
            await SendErrorAsync(bad, 4, "Illegal TFTP operation").ConfigureAwait(false);
            return;
        }

        // Only octet (binary) transfers; netascii is served as octet.
        _ = mode;

        var path = ResolvePath(fileName);
        if (path is null)
        {
            using var denied = NewTransferSocket(remote);
            await SendErrorAsync(denied, 2, "Access violation").ConfigureAwait(false);
            return;
        }

        using var transfer = NewTransferSocket(remote);
        try
        {
            if (opcode == 1)
            {
                await HandleReadAsync(transfer, path, fileName, token).ConfigureAwait(false);
            }
            else
            {
                await HandleWriteAsync(transfer, path, fileName, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // server stopping
        }
        catch (IOException ex)
        {
            await SendErrorAsync(transfer, 2, ex.Message).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            // client disappeared
        }
    }

    private async Task HandleReadAsync(UdpClient transfer, string path, string fileName,
        CancellationToken token)
    {
        if (!File.Exists(path))
        {
            await SendErrorAsync(transfer, 1, "File not found").ConfigureAwait(false);
            return;
        }

        byte[] content;
        try
        {
            content = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        }
        catch (IOException)
        {
            await SendErrorAsync(transfer, 2, "Access violation").ConfigureAwait(false);
            return;
        }

        ushort block = 1;
        var offset = 0;
        while (true)
        {
            var count = Math.Min(BlockSize, content.Length - offset);
            var chunk = new byte[count];
            Array.Copy(content, offset, chunk, 0, count);

            var sent = await SendDataWithRetryAsync(transfer, block, chunk, token)
                .ConfigureAwait(false);
            if (!sent)
            {
                return;
            }

            offset += count;
            if (count < BlockSize)
            {
                break;
            }
            block = unchecked((ushort)(block + 1));
        }

        if (_showDownloadInfo)
        {
            LogMessage?.Invoke($"Download: {fileName} ({content.Length} bytes)");
        }
    }

    private async Task HandleWriteAsync(UdpClient transfer, string path, string fileName,
        CancellationToken token)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is not null && !Directory.Exists(directory))
        {
            await SendErrorAsync(transfer, 2, "Access violation").ConfigureAwait(false);
            return;
        }

        await SendAckAsync(transfer, 0).ConfigureAwait(false);

        ushort expected = 1;
        long total = 0;
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write,
            FileShare.None);
        while (true)
        {
            var (data, ok) = await ReceiveDataAsync(transfer, expected, token)
                .ConfigureAwait(false);
            if (!ok)
            {
                return;
            }

            if (data.Length > 0)
            {
                await stream.WriteAsync(data, token).ConfigureAwait(false);
                total += data.Length;
            }

            await SendAckAsync(transfer, expected).ConfigureAwait(false);
            if (data.Length < BlockSize)
            {
                break;
            }
            expected = unchecked((ushort)(expected + 1));
        }

        await stream.FlushAsync(token).ConfigureAwait(false);
        if (_showUploadInfo)
        {
            LogMessage?.Invoke($"Upload: {fileName} ({total} bytes)");
        }
    }

    private static async Task<bool> SendDataWithRetryAsync(UdpClient transfer, ushort block,
        byte[] payload, CancellationToken token)
    {
        var packet = new byte[4 + payload.Length];
        packet[0] = 0;
        packet[1] = 3;
        WriteUInt16(packet, 2, block);
        payload.CopyTo(packet, 4);

        for (var attempt = 0; attempt <= MaxRetransmits; attempt++)
        {
            await transfer.SendAsync(packet.AsMemory(), token).ConfigureAwait(false);
            if (await WaitForAckAsync(transfer, block, token).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> WaitForAckAsync(UdpClient transfer, ushort block,
        CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(AckTimeoutMs);
        try
        {
            var result = await transfer.ReceiveAsync(timeout.Token).ConfigureAwait(false);
            if (result.Buffer.Length < 4)
            {
                return false;
            }
            if (ReadUInt16(result.Buffer, 0) == 5)
            {
                return false;
            }
            return ReadUInt16(result.Buffer, 0) == 4 && ReadUInt16(result.Buffer, 2) == block;
        }
        catch (OperationCanceledException)
        {
            token.ThrowIfCancellationRequested();
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static async Task<(byte[] Data, bool Ok)> ReceiveDataAsync(UdpClient transfer,
        ushort expected, CancellationToken token)
    {
        while (true)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            UdpReceiveResult result;
            try
            {
                result = await transfer.ReceiveAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return (Array.Empty<byte>(), false);
            }
            catch (SocketException)
            {
                return (Array.Empty<byte>(), false);
            }

            if (result.Buffer.Length < 4)
            {
                continue;
            }

            var opcode = ReadUInt16(result.Buffer, 0);
            if (opcode == 5)
            {
                return (Array.Empty<byte>(), false);
            }
            if (opcode != 3)
            {
                continue;
            }

            var block = ReadUInt16(result.Buffer, 2);
            if (block == unchecked((ushort)(expected - 1)))
            {
                // Duplicate of the previous block: re-ACK and keep waiting.
                await SendAckAsync(transfer, block).ConfigureAwait(false);
                continue;
            }
            if (block != expected)
            {
                return (Array.Empty<byte>(), false);
            }

            var data = result.Buffer.Length > 4
                ? result.Buffer[4..]
                : Array.Empty<byte>();
            return (data, true);
        }
    }

    private static async Task SendAckAsync(UdpClient transfer, ushort block)
    {
        var packet = new byte[4];
        packet[0] = 0;
        packet[1] = 4;
        WriteUInt16(packet, 2, block);
        await transfer.SendAsync(packet, packet.Length).ConfigureAwait(false);
    }

    private static async Task SendErrorAsync(UdpClient transfer, ushort code, string message)
    {
        var text = Encoding.ASCII.GetBytes(message + "\0");
        var packet = new byte[4 + text.Length];
        packet[0] = 0;
        packet[1] = 5;
        WriteUInt16(packet, 2, code);
        text.CopyTo(packet, 4);
        await transfer.SendAsync(packet, packet.Length).ConfigureAwait(false);
    }

    private static UdpClient NewTransferSocket(IPEndPoint remote)
    {
        var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        socket.Connect(remote);
        return socket;
    }

    private static (string FileName, string Mode) ParseRequest(byte[] packet)
    {
        var index = 2;
        var name = ReadNullTerminated(packet, ref index);
        var mode = ReadNullTerminated(packet, ref index);
        return (name, mode);
    }

    private static string ReadNullTerminated(byte[] packet, ref int index)
    {
        var start = index;
        while (index < packet.Length && packet[index] != 0)
        {
            index++;
        }
        var text = Encoding.ASCII.GetString(packet, start, index - start);
        if (index < packet.Length)
        {
            index++;
        }
        return text;
    }

    private string? ResolvePath(string fileName)
    {
        if (fileName.Length == 0 || fileName.IndexOf('\0') >= 0)
        {
            return null;
        }

        var normalized = fileName.Replace('\\', '/').TrimStart('/');
        if (normalized.Split('/').Any(part => part == ".."))
        {
            return null;
        }

        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(_root,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(full, _root, comparison))
        {
            return null;
        }

        var prefix = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, comparison) ? full : null;
    }

    private static ushort ReadUInt16(byte[] buffer, int offset) =>
        (ushort)((buffer[offset] << 8) | buffer[offset + 1]);

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)(value & 0xFF);
    }
}
