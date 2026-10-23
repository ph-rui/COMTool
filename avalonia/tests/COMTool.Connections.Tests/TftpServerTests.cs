using System.Net;
using System.Net.Sockets;
using System.Text;
using COMTool.Connections.Servers;

namespace COMTool.Connections.Tests;

public sealed class TftpServerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "comtool-tftp-tests",
        Guid.NewGuid().ToString("N"));

    public TftpServerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact(Timeout = 30000)]
    public async Task ReadRequestSendsFileInBlocks()
    {
        var content = CreatePayload(700);
        await File.WriteAllBytesAsync(Path.Combine(_root, "big.bin"), content);

        await using var server = new TftpServer(_root, 0, showDownloadInfo: true,
            showUploadInfo: true);
        await server.StartAsync();

        using var client = NewClient();
        var serverEndpoint = new IPEndPoint(IPAddress.Loopback, server.Port);
        await client.SendAsync(BuildRequest(1, "big.bin", "octet"), serverEndpoint);

        using var received = new MemoryStream();
        ushort expected = 1;
        while (true)
        {
            var packet = await ReceiveAsync(client);
            Assert.Equal(3, ReadUInt16(packet.Buffer, 0));
            Assert.Equal(expected, ReadUInt16(packet.Buffer, 2));
            var payload = packet.Buffer[4..];
            received.Write(payload);
            await client.SendAsync(BuildAck(expected), packet.RemoteEndPoint);
            if (payload.Length < 512)
            {
                break;
            }
            expected++;
        }

        Assert.Equal(content, received.ToArray());
    }

    [Fact(Timeout = 30000)]
    public async Task ReadRequestMissingFileReturnsError()
    {
        await using var server = new TftpServer(_root, 0, showDownloadInfo: true,
            showUploadInfo: true);
        await server.StartAsync();

        using var client = NewClient();
        var serverEndpoint = new IPEndPoint(IPAddress.Loopback, server.Port);
        await client.SendAsync(BuildRequest(1, "nope.bin", "octet"), serverEndpoint);

        var packet = await ReceiveAsync(client);
        Assert.Equal(5, ReadUInt16(packet.Buffer, 0));
        Assert.Equal(1, ReadUInt16(packet.Buffer, 2));
    }

    [Fact(Timeout = 30000)]
    public async Task WriteRequestStoresFile()
    {
        var content = CreatePayload(600);
        var target = Path.Combine(_root, "upload.bin");

        await using var server = new TftpServer(_root, 0, showDownloadInfo: true,
            showUploadInfo: true);
        await server.StartAsync();

        using var client = NewClient();
        var serverEndpoint = new IPEndPoint(IPAddress.Loopback, server.Port);
        await client.SendAsync(BuildRequest(2, "upload.bin", "octet"), serverEndpoint);

        var ack0 = await ReceiveAsync(client);
        Assert.Equal(4, ReadUInt16(ack0.Buffer, 0));
        Assert.Equal(0, ReadUInt16(ack0.Buffer, 2));
        var transferEndpoint = ack0.RemoteEndPoint;

        await client.SendAsync(BuildData(1, content[..512]), transferEndpoint);
        var ack1 = await ReceiveAsync(client);
        Assert.Equal(4, ReadUInt16(ack1.Buffer, 0));
        Assert.Equal(1, ReadUInt16(ack1.Buffer, 2));

        await client.SendAsync(BuildData(2, content[512..]), transferEndpoint);
        var ack2 = await ReceiveAsync(client);
        Assert.Equal(4, ReadUInt16(ack2.Buffer, 0));
        Assert.Equal(2, ReadUInt16(ack2.Buffer, 2));

        var written = await WaitForFileAsync(target, content.Length);
        Assert.Equal(content, written);
    }

    [Fact(Timeout = 30000)]
    public async Task TraversalFileNameIsRejected()
    {
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(_root)!, "evil.txt"),
            "outside");
        await using var server = new TftpServer(_root, 0, showDownloadInfo: true,
            showUploadInfo: true);
        await server.StartAsync();

        using var client = NewClient();
        var serverEndpoint = new IPEndPoint(IPAddress.Loopback, server.Port);
        await client.SendAsync(BuildRequest(1, "../evil.txt", "octet"), serverEndpoint);

        var packet = await ReceiveAsync(client);
        Assert.Equal(5, ReadUInt16(packet.Buffer, 0));
        Assert.Equal(2, ReadUInt16(packet.Buffer, 2));
    }

    private static UdpClient NewClient() =>
        new(new IPEndPoint(IPAddress.Loopback, 0));

    private static async Task<byte[]> WaitForFileAsync(string path, int length)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            if (File.Exists(path))
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(path);
                    if (bytes.Length == length)
                    {
                        return bytes;
                    }
                }
                catch (IOException)
                {
                    // still being written
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"file {path} did not reach {length} bytes");
            }

            await Task.Delay(20);
        }
    }

    private static byte[] CreatePayload(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)(i % 251);
        }
        return data;
    }

    private static async Task<UdpReceiveResult> ReceiveAsync(UdpClient client,
        int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            return await client.ReceiveAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("TFTP receive timed out");
        }
    }

    private static byte[] BuildRequest(ushort opcode, string fileName, string mode)
    {
        var name = Encoding.ASCII.GetBytes(fileName);
        var modeBytes = Encoding.ASCII.GetBytes(mode);
        var packet = new byte[2 + name.Length + 1 + modeBytes.Length + 1];
        WriteUInt16(packet, 0, opcode);
        name.CopyTo(packet, 2);
        packet[2 + name.Length] = 0;
        modeBytes.CopyTo(packet, 2 + name.Length + 1);
        packet[^1] = 0;
        return packet;
    }

    private static byte[] BuildAck(ushort block)
    {
        var packet = new byte[4];
        packet[1] = 4;
        WriteUInt16(packet, 2, block);
        return packet;
    }

    private static byte[] BuildData(ushort block, byte[] payload)
    {
        var packet = new byte[4 + payload.Length];
        packet[1] = 3;
        WriteUInt16(packet, 2, block);
        payload.CopyTo(packet, 4);
        return packet;
    }

    private static ushort ReadUInt16(byte[] buffer, int offset) =>
        (ushort)((buffer[offset] << 8) | buffer[offset + 1]);

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)(value & 0xFF);
    }
}
