using System.Net;
using System.Net.Sockets;
using System.Text;
using COMTool.Connections.Servers;

namespace COMTool.Connections.Tests;

/// <summary>
/// SRV-01: the built-in FTP server exercised by a raw-socket client: login,
/// passive data channel, LIST/RETR/STOR round-trip and path-traversal guard.
/// </summary>
public sealed class FtpServerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "comtool-ftp-tests",
        Guid.NewGuid().ToString("N"));

    public FtpServerTests() => Directory.CreateDirectory(_root);

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

    private async Task<(FtpServer Server, TcpClient Control)> ConnectAsync()
    {
        var server = new FtpServer(_root, 0);
        await server.StartAsync();
        var control = new TcpClient();
        await control.ConnectAsync(IPAddress.Loopback, server.Port);
        await ExpectAsync(control, "220");
        return (server, control);
    }

    [Fact(Timeout = 30000)]
    public async Task SRV01_LoginIsAnonymousAndPwdRoundTripWorks()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "up.bin"), new byte[] { 1, 2, 3, 4, 5 });
        Directory.CreateDirectory(Path.Combine(_root, "sub"));

        var (server, control) = await ConnectAsync();
        var stream = control.GetStream();

        await SendAsync(stream, "USER test");
        await ExpectAsync(control, "331");
        await SendAsync(stream, "PASS anything");
        await ExpectAsync(control, "230");

        // PWD starts at the root.
        await SendAsync(stream, "PWD");
        var pwd = await ExpectAsync(control, "257");
        Assert.Contains("\"/\"", pwd);

        // LIST must show the file and the subdirectory.
        var data = await OpenPassiveAsync(control);
        await SendAsync(stream, "LIST");
        await ExpectAsync(control, "150");
        var listingText = Encoding.ASCII.GetString(await ReadUntilClosedAsync(data));
        data.Close();
        await ExpectAsync(control, "226");
        Assert.Contains("up.bin", listingText);
        Assert.Contains("sub", listingText);

        // RETR downloads the exact bytes.
        data = await OpenPassiveAsync(control);
        await SendAsync(stream, "RETR up.bin");
        await ExpectAsync(control, "150");
        var downloaded = await ReadUntilClosedAsync(data);
        data.Close();
        await ExpectAsync(control, "226");
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, downloaded);

        // STOR uploads new content.
        data = await OpenPassiveAsync(control);
        await SendAsync(stream, "STOR sub/new.txt");
        await ExpectAsync(control, "150");
        await data.GetStream().WriteAsync(Encoding.ASCII.GetBytes("hello ftp"));
        data.Close();
        await ExpectAsync(control, "226");
        await Task.Delay(100); // give the server a moment to flush
        Assert.Equal("hello ftp",
            await File.ReadAllTextAsync(Path.Combine(_root, "sub", "new.txt")));

        // CWD into the subdirectory.
        await SendAsync(stream, "CWD sub");
        await ExpectAsync(control, "250");
        await SendAsync(stream, "PWD");
        pwd = await ExpectAsync(control, "257");
        Assert.Contains("sub", pwd);

        await SendAsync(stream, "QUIT");
        await ExpectAsync(control, "221");

        control.Dispose();
        await server.StopAsync();
    }

    [Fact(Timeout = 30000)]
    public async Task SRV01_FtpNonAnonymousRequiresConfiguredCredentials()
    {
        // 允许匿名连接=false 时，必须用配置的 用户/密码 登录（对应 FTP 面板复选框）。
        var server = new FtpServer(_root, 0, allowAnonymous: false, "alice", "s3cret");
        await server.StartAsync();
        var control = new TcpClient();
        try
        {
            await control.ConnectAsync(IPAddress.Loopback, server.Port);
            var stream = control.GetStream();
            await ExpectAsync(control, "220");

            await SendAsync(stream, "USER bob");
            await ExpectAsync(control, "331");
            await SendAsync(stream, "PASS s3cret");
            await ExpectAsync(control, "530");

            await SendAsync(stream, "USER alice");
            await ExpectAsync(control, "331");
            await SendAsync(stream, "PASS wrong");
            await ExpectAsync(control, "530");

            await SendAsync(stream, "USER alice");
            await ExpectAsync(control, "331");
            await SendAsync(stream, "PASS s3cret");
            await ExpectAsync(control, "230");

            await SendAsync(stream, "QUIT");
            await ExpectAsync(control, "221");
        }
        finally
        {
            control.Dispose();
            await server.StopAsync();
        }
    }

    [Fact(Timeout = 30000)]
    public async Task SRV01_PathTraversalIsRejected()
    {
        var (server, control) = await ConnectAsync();
        var stream = control.GetStream();

        await SendAsync(stream, "USER test");
        await ExpectAsync(control, "331");
        await SendAsync(stream, "PASS x");
        await ExpectAsync(control, "230");

        // RETR must not be able to escape the root.
        var data = await OpenPassiveAsync(control);
        await SendAsync(stream, "RETR ../../etc/passwd");
        await ExpectAsync(control, "550");
        data.Close();

        // CWD to the parent of the root is rejected too.
        await SendAsync(stream, "CWD ..");
        await ExpectAsync(control, "550");

        await SendAsync(stream, "QUIT");
        await ExpectAsync(control, "221");
        control.Dispose();
        await server.StopAsync();
    }

    /// <summary>Issues PASV and connects the returned data endpoint.</summary>
    private static async Task<TcpClient> OpenPassiveAsync(TcpClient control)
    {
        var stream = control.GetStream();
        await SendAsync(stream, "PASV");
        var reply = await ExpectAsync(control, "227");
        var open = reply.IndexOf('(');
        var close = reply.IndexOf(')', open);
        var parts = reply[(open + 1)..close].Split(',');
        var ip = IPAddress.Parse($"{parts[0]}.{parts[1]}.{parts[2]}.{parts[3]}");
        var port = int.Parse(parts[4]) * 256 + int.Parse(parts[5]);

        var data = new TcpClient();
        await data.ConnectAsync(ip, port);
        return data;
    }

    private static async Task<string> ExpectAsync(TcpClient control, string code)
    {
        var stream = control.GetStream();
        var buffer = new StringBuilder();
        var byteBuffer = new byte[1024];
        while (true)
        {
            var read = await stream.ReadAsync(byteBuffer);
            if (read <= 0)
            {
                throw new IOException("control connection closed");
            }

            buffer.Append(Encoding.ASCII.GetString(byteBuffer, 0, read));
            var text = buffer.ToString();
            if (text.Length >= 3 && text[3] == ' ' && text.StartsWith(code))
            {
                // single-line reply ends at the first CRLF after the code
                var end = text.IndexOf("\r\n", StringComparison.Ordinal);
                if (end >= 0)
                {
                    return text[..(end + 2)];
                }
            }
        }
    }

    private static async Task SendAsync(NetworkStream stream, string command)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(command + "\r\n"));
        await stream.FlushAsync();
    }

    private static async Task<byte[]> ReadUntilClosedAsync(TcpClient data)
    {
        using var memory = new MemoryStream();
        var stream = data.GetStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }
}
