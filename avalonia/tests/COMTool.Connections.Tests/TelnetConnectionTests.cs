using System.Net;
using System.Net.Sockets;
using System.Text;
using COMTool.Connections;

namespace COMTool.Connections.Tests;

public sealed class TelnetConnectionTests
{
    private const byte Iac = 255;
    private const byte Se = 240;
    private const byte Sb = 250;
    private const byte Will = 251;
    private const byte Wont = 252;
    private const byte Do = 253;
    private const byte Dont = 254;
    private const byte Ttype = 24;
    private const byte Naws = 31;
    private const byte Echo = 1;
    private const byte Sga = 3;

    [Fact]
    public async Task TELNET01_HandshakeRelaysDataAndTerminalType()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            using var session = await TelnetConnection.ConnectAsync(
                new TelnetConnectionOptions(
                    "127.0.0.1",
                    port,
                    Timeout: TimeSpan.FromSeconds(5)));
            using var peer = await accept.WaitAsync(TimeSpan.FromSeconds(5));
            var stream = peer.GetStream();

            var initial = await ReadUntilAsync(stream, new[] { Iac, Do, Sga }, 5);
            AssertContains(initial, Iac, Will, Ttype);
            AssertContains(initial, Iac, Will, Naws);
            AssertContains(initial, Iac, Will, Sga);
            AssertContains(initial, Iac, Do, Sga);

            await stream.WriteAsync(new byte[]
            {
                Iac, Do, Ttype,
                Iac, Do, Naws,
                Iac, Will, Echo,
                Iac, Sb, Ttype, 1, Iac, Se,
            });
            var responses = await ReadUntilAsync(
                stream,
                Encoding.ASCII.GetBytes("XTERM-256COLOR"),
                5);
            AssertContains(responses, Iac, Will, Ttype);
            AssertContains(responses, Iac, Will, Naws);
            AssertContains(responses, Iac, Do, Echo);
            Assert.Contains("XTERM-256COLOR", Encoding.ASCII.GetString(responses));

            var inbound = Encoding.UTF8.GetBytes("hello");
            await stream.WriteAsync(inbound);
            var terminal = await ReadExactlyAsync(
                session.Pty.ReaderStream,
                inbound.Length,
                TimeSpan.FromSeconds(5));
            Assert.Equal("hello", Encoding.UTF8.GetString(terminal));

            // A literal 0xFF is IAC IAC on the wire but one data byte above
            // the terminal.
            await stream.WriteAsync(new byte[] { Iac, Iac, (byte)'!' });
            var escaped = await ReadExactlyAsync(
                session.Pty.ReaderStream,
                2,
                TimeSpan.FromSeconds(5));
            Assert.Equal(new byte[] { Iac, (byte)'!' }, escaped);

            await session.Pty.WriterStream.WriteAsync(Encoding.ASCII.GetBytes("ping\r"));
            await session.Pty.WriterStream.FlushAsync();
            var outbound = await ReadUntilAsync(stream, new byte[] { (byte)'\r', (byte)'\n' }, 5);
            Assert.EndsWith("ping\r\n", Encoding.ASCII.GetString(outbound));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task TELNET02_NawsTracksResize()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            using var session = await TelnetConnection.ConnectAsync(
                new TelnetConnectionOptions(
                    "127.0.0.1",
                    port,
                    Columns: 100,
                    Rows: 30,
                    Timeout: TimeSpan.FromSeconds(5)));
            using var peer = await accept.WaitAsync(TimeSpan.FromSeconds(5));
            var stream = peer.GetStream();
            _ = await ReadUntilAsync(stream, new[] { Iac, Do, Sga }, 5);

            await stream.WriteAsync(new byte[] { Iac, Do, Naws });
            var first = await ReadUntilAsync(stream, new byte[] { Iac, Se }, 5);
            AssertNaws(first, 100, 30);

            session.Resize(132, 43);
            var second = await ReadUntilAsync(stream, new byte[] { Iac, Se }, 5);
            AssertNaws(second, 132, 43);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task TELNET03_AutoLoginSendsCredentialsOnce()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            using var session = await TelnetConnection.ConnectAsync(
                new TelnetConnectionOptions(
                    "127.0.0.1",
                    port,
                    "alice",
                    "secret",
                    Timeout: TimeSpan.FromSeconds(5)));
            using var peer = await accept.WaitAsync(TimeSpan.FromSeconds(5));
            var stream = peer.GetStream();
            _ = await ReadUntilAsync(stream, new[] { Iac, Do, Sga }, 5);

            // Split the user prompt across reads. The observer must keep a
            // tail instead of matching only complete socket packets.
            await stream.WriteAsync(Encoding.ASCII.GetBytes("Welcome\r\nlog"));
            await Task.Delay(50);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("in: "));
            var user = await ReadUntilAsync(stream, new byte[] { (byte)'\r', (byte)'\n' }, 5);
            Assert.Contains("alice", Encoding.ASCII.GetString(user));

            // ANSI CSI and OSC sequences must not prevent a password prompt
            // match. The OSC title follows the prompt text to prove the whole
            // sequence is removed rather than leaving its payload behind.
            await stream.WriteAsync(Encoding.ASCII.GetBytes(
                "\u001b[32mPassword:\u001b]0;telnet-e2e\u0007\u001b[0m "));
            var password = await ReadUntilAsync(stream, new byte[] { (byte)'\r', (byte)'\n' }, 5);
            Assert.Contains("secret", Encoding.ASCII.GetString(password));

            // A repeated prompt must not repeat the password.
            await stream.WriteAsync(Encoding.ASCII.GetBytes("Password: "));
            var duplicate = await ReadForAsync(stream, TimeSpan.FromMilliseconds(400));
            Assert.DoesNotContain("secret", Encoding.ASCII.GetString(duplicate));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task TELNET03_CommonLoginPromptVariantsTriggerAutoLogin()
    {
        var prompts = new[]
        {
            ("User name: ", "Password: "),
            ("SonnePower_070_Ax login: ", "SonnePower_070_Ax password: "),
            (">>User Name: ", "Password for admin: "),
            ("Login name: ", "Passwd: "),
            ("Login ID: ", "PIN: "),
            ("Account name: ", "Passcode: "),
            ("Please enter your user name: ", "Please enter your password: "),
            ("用户名：", "密码："),
            ("Login ID: \0", "PIN: \0"),
        };

        foreach (var (userPrompt, passwordPrompt) in prompts)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            try
            {
                var accept = listener.AcceptTcpClientAsync();
                using var session = await TelnetConnection.ConnectAsync(
                    new TelnetConnectionOptions(
                        "127.0.0.1",
                        port,
                        "alice",
                        "secret",
                        Timeout: TimeSpan.FromSeconds(5)));
                using var peer = await accept.WaitAsync(TimeSpan.FromSeconds(5));
                var stream = peer.GetStream();
                _ = await ReadUntilAsync(stream, new[] { Iac, Do, Sga }, 5);

                await stream.WriteAsync(Encoding.UTF8.GetBytes(userPrompt));
                var user = await ReadUntilAsync(
                    stream,
                    new byte[] { (byte)'\r', (byte)'\n' },
                    5);
                Assert.Contains("alice", Encoding.UTF8.GetString(user));

                await stream.WriteAsync(Encoding.UTF8.GetBytes(passwordPrompt));
                var password = await ReadUntilAsync(
                    stream,
                    new byte[] { (byte)'\r', (byte)'\n' },
                    5);
                Assert.Contains("secret", Encoding.UTF8.GetString(password));
            }
            finally
            {
                listener.Stop();
            }
        }
    }

    [Fact]
    public async Task TELNET04_NoPromptDoesNotSendCredentials()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            using var session = await TelnetConnection.ConnectAsync(
                new TelnetConnectionOptions(
                    "127.0.0.1",
                    port,
                    "alice",
                    "secret",
                    Timeout: TimeSpan.FromSeconds(5)));
            using var peer = await accept.WaitAsync(TimeSpan.FromSeconds(5));
            var stream = peer.GetStream();
            _ = await ReadUntilAsync(stream, new[] { Iac, Do, Sga }, 5);

            await stream.WriteAsync(Encoding.ASCII.GetBytes("Password: "));
            var sent = await ReadForAsync(stream, TimeSpan.FromMilliseconds(300));
            Assert.DoesNotContain("alice", Encoding.ASCII.GetString(sent));
            Assert.DoesNotContain("secret", Encoding.ASCII.GetString(sent));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task TELNET05_CloseEndsTerminalRead()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            using var session = await TelnetConnection.ConnectAsync(
                new TelnetConnectionOptions(
                    "127.0.0.1",
                    port,
                    Timeout: TimeSpan.FromSeconds(5)));
            using var peer = await accept.WaitAsync(TimeSpan.FromSeconds(5));
            var stream = peer.GetStream();
            _ = await ReadUntilAsync(stream, new[] { Iac, Do, Sga }, 5);

            await stream.WriteAsync(Encoding.ASCII.GetBytes("bye"));
            var received = await ReadExactlyAsync(
                session.Pty.ReaderStream,
                3,
                TimeSpan.FromSeconds(5));
            Assert.Equal("bye", Encoding.ASCII.GetString(received));

            peer.Close();
            var eof = await session.Pty.ReaderStream.ReadAsync(new byte[1])
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, eof);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void AssertNaws(byte[] response, int expectedColumns, int expectedRows)
    {
        var marker = new byte[] { Iac, Sb, Naws };
        var index = IndexOf(response, marker);
        Assert.True(index >= 0, "NAWS subnegotiation was not received.");
        var payload = response.AsSpan(index + marker.Length);
        Assert.True(payload.Length >= 4, "NAWS payload was truncated.");
        var columns = payload[0] * 256 + payload[1];
        var rows = payload[2] * 256 + payload[3];
        Assert.Equal(expectedColumns, columns);
        Assert.Equal(expectedRows, rows);
    }

    private static void AssertContains(byte[] actual, params byte[] expected)
    {
        Assert.True(
            IndexOf(actual, expected) >= 0,
            $"Expected bytes {Convert.ToHexString(expected)} in {Convert.ToHexString(actual)}.");
    }

    private static int IndexOf(IReadOnlyList<byte> haystack, IReadOnlyList<byte> needle)
    {
        if (needle.Count == 0)
        {
            return 0;
        }

        for (var index = 0; index <= haystack.Count - needle.Count; index++)
        {
            var match = true;
            for (var offset = 0; offset < needle.Count; offset++)
            {
                if (haystack[index + offset] != needle[offset])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return index;
            }
        }

        return -1;
    }

    private static async Task<byte[]> ReadUntilAsync(
        NetworkStream stream,
        byte[] marker,
        int timeoutSeconds)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var result = new List<byte>();
        var buffer = new byte[1024];
        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(), cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    $"Did not receive {Convert.ToHexString(marker)} within {timeoutSeconds}s.");
            }

            if (read == 0)
            {
                throw new IOException("The peer closed before the expected bytes arrived.");
            }

            result.AddRange(buffer.AsSpan(0, read).ToArray());
            if (IndexOf(result, marker) >= 0)
            {
                return result.ToArray();
            }
        }
    }

    private static async Task<byte[]> ReadForAsync(NetworkStream stream, TimeSpan duration)
    {
        using var cancellation = new CancellationTokenSource(duration);
        var result = new List<byte>();
        var buffer = new byte[1024];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(), cancellation.Token);
                if (read == 0)
                {
                    break;
                }

                result.AddRange(buffer.AsSpan(0, read).ToArray());
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the peer stays open with no further output.
        }

        return result.ToArray();
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
