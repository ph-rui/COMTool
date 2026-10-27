using System.Net;
using System.Net.Sockets;
using System.Text;
using COMTool.Connections.Servers;

namespace COMTool.Connections.Tests;

/// <summary>
/// SRV-01: the built-in telnet server echoes client bytes and reports the
/// received lines (IAC negotiation bytes excluded from the line log).
/// </summary>
public sealed class TelnetServerTests
{
    [Fact(Timeout = 30000)]
    public async Task SRV01_EchoesBytesAndLogsLines()
    {
        var logs = new List<string>();
        var server = new TelnetServer(0);
        server.LogMessage += m => logs.Add(m);
        await server.StartAsync();
        Assert.True(server.IsRunning);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);
        var stream = client.GetStream();

        // Plain text: echoed back verbatim.
        var hello = Encoding.ASCII.GetBytes("hello\r\n");
        await stream.WriteAsync(hello);
        var echoed = await ReadExactlyAsync(stream, hello.Length);
        Assert.Equal(hello, echoed);

        // IAC WILL ECHO (0xFF 0xFB 01) is echoed but not logged.
        var iac = new byte[] { 0xFF, 0xFB, 01, (byte)'a', (byte)'b', (byte)'\n' };
        await stream.WriteAsync(iac);
        echoed = await ReadExactlyAsync(stream, iac.Length);
        Assert.Equal(iac, echoed);

        await server.StopAsync();
        Assert.False(server.IsRunning);

        Assert.Contains("hello", logs);
        Assert.Contains("ab", logs);
        Assert.True(logs.All(l => !l.Contains("\0")));
    }

    [Fact(Timeout = 30000)]
    public async Task SRV01_StopClosesTheListener()
    {
        var server = new TelnetServer(0);
        await server.StartAsync();
        var port = server.Port;
        await server.StopAsync();

        // The port must be free again: a connect attempt should be refused.
        using var probe = new TcpClient();
        Exception? error = null;
        try
        {
            await probe.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            error = ex;
        }
        Assert.NotNull(error);
    }

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
            if (read <= 0)
            {
                break;
            }
            offset += read;
        }

        return buffer;
    }
}

/// <summary>SRV-01: IperfServer reports a missing binary instead of starting.</summary>
public sealed class IperfServerTests
{
    [Fact(Timeout = 30000)]
    public async Task SRV01_MissingBinaryStaysUnstartedWithAMessage()
    {
        var logs = new List<string>();
        var server = new IperfServer(5201)
        {
            BinaryOverride = Path.Combine(Path.GetTempPath(), "definitely-not-installed-iperf-xyz"),
        };
        server.LogMessage += m => logs.Add(m);

        await server.StartAsync();
        Assert.False(server.IsRunning);
        Assert.Contains(logs, m => m.Contains("未找到"));

        await server.StopAsync(); // must be a safe no-op
    }
}

/// <summary>SRV-01: the NFS/VNC placeholder reports its status and never starts.</summary>
public sealed class StubServerTests
{
    [Fact]
    public async Task SRV01_StubReportsItsMessageAndStaysStopped()
    {
        var logs = new List<string>();
        var server = new StubServer("NFS 服务器", "NFS 服务器暂未实现：真实 NFS 服务需要系统级组件");
        server.LogMessage += m => logs.Add(m);

        await server.StartAsync();
        Assert.False(server.IsRunning);
        Assert.Single(logs);
        Assert.Contains("暂未实现", logs[0]);

        await server.StopAsync();
        await server.DisposeAsync();
    }
}
