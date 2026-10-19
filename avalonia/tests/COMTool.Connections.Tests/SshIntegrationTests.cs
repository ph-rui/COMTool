using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using COMTool.Connections;

namespace COMTool.Connections.Tests;

public sealed class SshIntegrationTests
{
    [Fact]
    public async Task KeyAuthShellOutputResizeAndReconnect()
    {
        if (!OperatingSystem.IsLinux()
            || !File.Exists("/usr/sbin/sshd")
            || !File.Exists("/usr/bin/ssh-keygen"))
        {
            return;
        }

        var directory = Path.Combine(
            Path.GetTempPath(),
            "comtool-sshd-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var port = FreePort();
        Process? sshd = null;
        try
        {
            var hostKey = Path.Combine(directory, "host_key");
            var clientKey = Path.Combine(directory, "client_key");
            var authorizedKeys = Path.Combine(directory, "authorized_keys");
            Run("/usr/bin/ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", hostKey);
            Run("/usr/bin/ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", clientKey);
            File.Copy(clientKey + ".pub", authorizedKeys);
            File.SetUnixFileMode(authorizedKeys, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(clientKey, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var config = Path.Combine(directory, "sshd_config");
            File.WriteAllText(config, $"""
                Port {port}
                ListenAddress 127.0.0.1
                HostKey {hostKey}
                AuthorizedKeysFile {authorizedKeys}
                PubkeyAuthentication yes
                PasswordAuthentication no
                KbdInteractiveAuthentication no
                UsePAM no
                StrictModes no
                PidFile {Path.Combine(directory, "sshd.pid")}
                LogLevel ERROR
                AllowUsers {Environment.UserName}
                """);

            var check = RunCapture("/usr/sbin/sshd", "-t", "-f", config);
            if (check.ExitCode != 0)
            {
                return;
            }

            sshd = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/sbin/sshd",
                ArgumentList =
                {
                    "-D",
                    "-f",
                    config,
                    "-E",
                    Path.Combine(directory, "sshd.log"),
                },
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            });
            Assert.NotNull(sshd);
            await WaitForPortAsync(port, TimeSpan.FromSeconds(5));

            var options = new SshConnectionOptions(
                "127.0.0.1",
                port,
                Environment.UserName,
                KeyFile: clientKey,
                Columns: 100,
                Rows: 40,
                Timeout: TimeSpan.FromSeconds(10));

            using (var connection = SshConnection.Connect(options))
            {
                var marker = $"SSH-{Guid.NewGuid():N}";
                var output = await ReadCommand(
                    connection,
                    $"printf '{marker}\\n'\n",
                    marker,
                    TimeSpan.FromSeconds(10));
                Assert.Contains(marker, output, StringComparison.Ordinal);

                connection.Pty.Resize(90, 30);
                var size = await ReadCommand(
                    connection,
                    "stty size\n",
                    "30 90",
                    TimeSpan.FromSeconds(10));
                Assert.Contains("30 90", size, StringComparison.Ordinal);
            }

            using (var reconnected = SshConnection.Connect(options))
            {
                var marker = $"SSH-{Guid.NewGuid():N}";
                var output = await ReadCommand(
                    reconnected,
                    $"printf '{marker}\\n'\n",
                    marker,
                    TimeSpan.FromSeconds(10));
                Assert.Contains(marker, output, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (sshd is { HasExited: false })
            {
                sshd.Kill(entireProcessTree: true);
                sshd.WaitForExit(2000);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitForPortAsync(int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(50);
            }
        }

        throw new TimeoutException($"sshd did not listen on port {port}");
    }

    private static async Task<string> ReadCommand(
        SshConnection connection,
        string command,
        string marker,
        TimeSpan timeout)
    {
        var writer = connection.Pty.WriterStream;
        writer.Write(Encoding.UTF8.GetBytes(command));
        writer.Flush();

        var reader = connection.Pty.ReaderStream;
        var buffer = new byte[4096];
        var text = new StringBuilder();
        var deadline = DateTime.UtcNow + timeout;
        var readTask = Task.Run(() => reader.Read(buffer, 0, buffer.Length));
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            if (await Task.WhenAny(readTask, Task.Delay(remaining)) != readTask)
            {
                break;
            }

            var count = await readTask;
            if (count <= 0)
            {
                break;
            }

            text.Append(Encoding.UTF8.GetString(buffer, 0, count));
            if (text.ToString().Contains(marker, StringComparison.Ordinal))
            {
                return text.ToString();
            }

            readTask = Task.Run(() => reader.Read(buffer, 0, buffer.Length));
        }

        return text.ToString();
    }

    private static void Run(string file, params string[] args)
    {
        var result = RunCapture(file, args);
        Assert.True(result.ExitCode == 0, $"{file} failed: {result.Error}");
    }

    private static (int ExitCode, string Output, string Error) RunCapture(
        string file,
        params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = file,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }
}
