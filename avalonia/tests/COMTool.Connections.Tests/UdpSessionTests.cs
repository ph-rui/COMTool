using System.Net;
using System.Net.Sockets;
using System.Text;
using COMTool.Connections;

namespace COMTool.Connections.Tests;

public sealed class UdpSessionTests
{
    [Fact]
    public async Task NET02_UdpLoopbackSendsAndReceives()
    {
        var localPort = FreeUdpPort();
        using var target = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var targetPort = ((IPEndPoint)target.Client.LocalEndPoint!).Port;
        using var session = await UdpSession.CreateAsync(
            new UdpSessionOptions("127.0.0.1", targetPort, localPort));

        Assert.True(session.IsConnected);
        Assert.Equal(localPort, session.Port);

        var outbound = Encoding.UTF8.GetBytes("ping");
        await session.Pty.WriterStream.WriteAsync(outbound);
        await session.Pty.WriterStream.FlushAsync();
        var received = await target.ReceiveAsync(CancellationToken.None)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("ping", Encoding.UTF8.GetString(received.Buffer));

        var inbound = Encoding.UTF8.GetBytes("pong");
        await target.SendAsync(inbound, new IPEndPoint(IPAddress.Loopback, session.Port));
        var terminal = new byte[inbound.Length];
        await session.Pty.ReaderStream.ReadExactlyAsync(terminal)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("pong", Encoding.UTF8.GetString(terminal));
    }

    [Fact]
    public async Task NET02_UdpRejectsInvalidOptions()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UdpSession.CreateAsync(new UdpSessionOptions("", 9000, 2345)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UdpSession.CreateAsync(new UdpSessionOptions("127.0.0.1", 0, 2345)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UdpSession.CreateAsync(new UdpSessionOptions("127.0.0.1", 9000, 0)));
    }

    private static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }
}
