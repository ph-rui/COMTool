using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using COMTool.Connections;

namespace COMTool.Connections.Tests;

public sealed class TcpSessionTests
{
    [Fact]
    public async Task NET01_TcpClientLoopbackSendsAndReceives()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            using var session = await TcpClientSession.ConnectAsync(
                new TcpClientOptions("127.0.0.1", port, TimeSpan.FromSeconds(5)));
            using var peer = await accept.WaitAsync(TimeSpan.FromSeconds(5));
            var peerStream = peer.GetStream();

            var outbound = Encoding.UTF8.GetBytes("ping");
            await session.Pty.WriterStream.WriteAsync(outbound);
            await session.Pty.WriterStream.FlushAsync();
            var received = new byte[outbound.Length];
            await peerStream.ReadExactlyAsync(received).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("ping", Encoding.UTF8.GetString(received));

            var inbound = Encoding.UTF8.GetBytes("pong");
            await peerStream.WriteAsync(inbound);
            var terminal = new byte[inbound.Length];
            await session.Pty.ReaderStream.ReadExactlyAsync(terminal)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("pong", Encoding.UTF8.GetString(terminal));
            Assert.True(session.IsConnected);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task NET01_TcpServerRoutesClientsAndSupportsSelectionAndDisconnect()
    {
        using var server = new TcpServerSession(new TcpServerOptions(0));
        using var first = new TcpClient();
        using var second = new TcpClient();
        await first.ConnectAsync(IPAddress.Loopback, server.Port);
        await second.ConnectAsync(IPAddress.Loopback, server.Port);
        WaitUntil(() => server.Clients.Count == 2, TimeSpan.FromSeconds(5));

        var firstStream = first.GetStream();
        var secondStream = second.GetStream();
        await firstStream.WriteAsync(Encoding.UTF8.GetBytes("one"));
        await secondStream.WriteAsync(Encoding.UTF8.GetBytes("two"));

        var incoming = await ReadExactlyAsync(
            server.Pty.ReaderStream,
            6,
            TimeSpan.FromSeconds(5));
        var incomingText = Encoding.UTF8.GetString(incoming);
        Assert.Equal("enootw", new string(incomingText.OrderBy(character => character).ToArray()));

        await server.Pty.WriterStream.WriteAsync(Encoding.UTF8.GetBytes("all"));
        var firstAll = await ReadExactlyAsync(firstStream, 3, TimeSpan.FromSeconds(5));
        var secondAll = await ReadExactlyAsync(secondStream, 3, TimeSpan.FromSeconds(5));
        Assert.Equal("all", Encoding.UTF8.GetString(firstAll));
        Assert.Equal("all", Encoding.UTF8.GetString(secondAll));

        var firstPort = ((IPEndPoint)first.Client.LocalEndPoint!).Port;
        var selected = server.Clients
            .Single(client => client.Endpoint.EndsWith($":{firstPort}", StringComparison.Ordinal))
            .Endpoint;
        server.SelectClient(selected);
        await server.Pty.WriterStream.WriteAsync(Encoding.UTF8.GetBytes("one-only"));
        var selectedBytes = await ReadExactlyAsync(firstStream, 8, TimeSpan.FromSeconds(5));
        Assert.Equal("one-only", Encoding.UTF8.GetString(selectedBytes));

        server.DisconnectClient(selected);
        WaitUntil(() => server.Clients.Count == 1, TimeSpan.FromSeconds(5));
        Assert.Null(server.SelectedClient);

        server.SelectClient(null);
        await server.Pty.WriterStream.WriteAsync(Encoding.UTF8.GetBytes("rest"));
        var rest = await ReadExactlyAsync(secondStream, 4, TimeSpan.FromSeconds(5));
        Assert.Equal("rest", Encoding.UTF8.GetString(rest));
    }

    [Fact]
    public async Task NET01_TcpServerCloseEndsTerminalRead()
    {
        var server = new TcpServerSession(new TcpServerOptions(0));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes("bye"));
        var received = await ReadExactlyAsync(server.Pty.ReaderStream, 3, TimeSpan.FromSeconds(5));
        Assert.Equal("bye", Encoding.UTF8.GetString(received));

        server.Dispose();
        var result = await server.Pty.ReaderStream.ReadAsync(new byte[1])
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, result);
    }

    private static void WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (!condition())
        {
            if (Stopwatch.GetTimestamp() > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            Thread.Sleep(20);
        }
    }

    private static async Task<byte[]> ReadExactlyAsync(
        Stream stream,
        int count,
        TimeSpan timeout)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer).AsTask().WaitAsync(timeout);
        return buffer;
    }
}
