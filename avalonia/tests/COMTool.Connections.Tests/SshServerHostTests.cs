using System.Net;
using System.Net.Sockets;
using COMTool.Connections.Servers;
using Renci.SshNet;

namespace COMTool.Connections.Tests;

/// <summary>
/// SRV-01: the built-in SSH/SFTP server verified end-to-end with an SSH.NET
/// SftpClient on loopback: log in with the current Windows username (any
/// password, MobaXterm-style), upload, list and download a file, all under
/// the configured root; other usernames are rejected.
/// </summary>
public sealed class SshServerHostTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "comtool-sshsrv-tests",
        Guid.NewGuid().ToString("N"));

    public SshServerHostTests() => Directory.CreateDirectory(_root);

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

    private SshServerHost? _server;

    [Fact(Timeout = 60000)]
    public async Task SRV01_SftpUploadListDownloadRoundTrip()
    {
        // Find a free port and start the server there.
        for (var port = 42100; port < 42200; port++)
        {
            var candidate = new SshServerHost(_root, port);
            try
            {
                await candidate.StartAsync();
                _server = candidate;
                break;
            }
            catch (SocketException)
            {
                // port taken, try the next one
            }
        }

        Assert.NotNull(_server);
        Assert.True(_server!.IsRunning);

        var content = "sftp round trip through the built-in server\n";
        // Local temp files stay outside the root so upload and download
        // cannot be confused with existing root entries.
        var localUp = Path.Combine(Path.GetTempPath(), $"comtool-ssh-up-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(localUp, content);
        var localDown = Path.Combine(Path.GetTempPath(), $"comtool-ssh-down-{Guid.NewGuid():N}.txt");

        try
        {
            // MobaXterm 同款限制：只能以当前 Windows 用户名登录（密码任意）。
            using var client = new SftpClient(
                IPAddress.Loopback.ToString(), _server.Port, Environment.UserName, "anypassword");
            client.Connect();

            using (var up = File.OpenRead(localUp))
            {
                client.UploadFile(up, "/up.txt");
            }
            Assert.True(client.Exists("/up.txt"));

            var names = client.ListDirectory("/");
            Assert.Contains(names, f => f.Name == "up.txt");

            using (var down = File.Create(localDown))
            {
                client.DownloadFile("/up.txt", down);
            }
            Assert.Equal(content, await File.ReadAllTextAsync(localDown));

            client.Disconnect();
        }
        finally
        {
            DeleteQuiet(localUp);
            DeleteQuiet(localDown);
            if (_server is not null)
            {
                await _server.StopAsync();
                _server = null;
            }
        }
    }

    [Fact(Timeout = 30000)]
    public async Task SRV01_SshRejectsOtherUser()
    {
        // 用户名不是当前 Windows 用户时应认证失败（对齐 MobaXterm 描述文字）。
        SshServerHost? candidate = null;
        for (var port = 42200; port < 42300; port++)
        {
            candidate = new SshServerHost(_root, port);
            try
            {
                await candidate.StartAsync();
                break;
            }
            catch (SocketException)
            {
                await candidate.DisposeAsync();
                candidate = null;
            }
        }

        Assert.NotNull(candidate);
        try
        {
            using var client = new SftpClient(
                IPAddress.Loopback.ToString(),
                candidate!.Port,
                Environment.UserName + "_other",
                "anypassword");
            // 认证失败应抛出 SSH.NET 异常（具体类型随版本而变，不固定断言）
            Assert.ThrowsAny<Exception>(() => client.Connect());
        }
        finally
        {
            if (candidate is not null)
            {
                await candidate.StopAsync();
                await candidate.DisposeAsync();
            }
        }
    }

    [Fact(Timeout = 30000)]
    public async Task SRV01_TakenPortFailsFast()
    {
        // Hold the port, then the server must refuse to start.
        using var blocker = new TcpListener(new IPEndPoint(IPAddress.Loopback, 0));
        blocker.Start();
        var port = ((IPEndPoint)blocker.LocalEndpoint).Port;

        var server = new SshServerHost(_root, port);
        var logs = new List<string>();
        server.LogMessage += m => logs.Add(m);

        await Assert.ThrowsAnyAsync<Exception>(() => server.StartAsync());
        Assert.False(server.IsRunning);
        await server.DisposeAsync();
    }

    private static void DeleteQuiet(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }
}
