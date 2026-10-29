using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
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

                // Killing the remote shell ends the SSH channel without
                // necessarily closing the TCP connection. The shell channel's
                // Closed event must mark the connection dead even when no
                // terminal reader is consuming output.
                var pidText = await ReadCommand(
                    connection,
                    "printf 'SHELLPID-%d\\n' $$\n",
                    text => Regex.IsMatch(text, @"SHELLPID-(\d+)"),
                    TimeSpan.FromSeconds(10));
                var match = Regex.Match(pidText, @"SHELLPID-(\d+)");
                Assert.True(match.Success, pidText);
                var shellPid = int.Parse(match.Groups[1].Value);
                using (var shellProcess = Process.GetProcessById(shellPid))
                {
                    shellProcess.Kill();
                    shellProcess.WaitForExit(2000);
                }

                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                while (connection.IsConnected && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(50);
                }

                Assert.False(connection.IsConnected);
            }

            // A terminal layout pass can request a resize after the SSH tab
            // has been closed and its shell stream disposed. This used to throw
            // ObjectDisposedException from TerminalView.ArrangeOverride and
            // crash the whole app.
            using (var disposed = SshConnection.Connect(options))
            {
                disposed.Dispose();
                disposed.Pty.Resize(120, 40);
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

    private static Task<string> ReadCommand(
        SshConnection connection,
        string command,
        string marker,
        TimeSpan timeout) =>
        ReadCommand(
            connection,
            command,
            text => text.Contains(marker, StringComparison.Ordinal),
            timeout);

    private static async Task<string> ReadCommand(
        SshConnection connection,
        string command,
        Func<string, bool> isComplete,
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
            if (isComplete(text.ToString()))
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
    [Fact]
    public async Task RM02_ExecChannelRunsCommandWithoutPollutingShell()
    {
        if (!OperatingSystem.IsLinux()
            || !File.Exists("/usr/sbin/sshd")
            || !File.Exists("/usr/bin/ssh-keygen"))
        {
            return;
        }

        var directory = Path.Combine(
            Path.GetTempPath(),
            "comtool-sshd-exec-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var port = FreePort();
        Process? sshd = null;
        SshConnection? connection = null;
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
                ArgumentList = { "-D", "-f", config, "-E", Path.Combine(directory, "sshd.log") },
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
            connection = SshConnection.Connect(options);

            // The exec channel returns the command output untouched.
            var marker = $"RM02-{Guid.NewGuid():N}";
            var output = await connection.RunCommandAsync($"echo {marker}");
            Assert.Contains(marker, output, StringComparison.Ordinal);

            // /proc is readable through the exec channel: /proc/uptime starts with a float.
            var uptime = await connection.RunCommandAsync("head -c 32 /proc/uptime");
            Assert.Matches(@"^\d+\.\d", uptime.Trim());

            // The interactive shell channel and the exec channel are independent.
            var shellMarker = $"RM02SHELL-{Guid.NewGuid():N}";
            var shellTask = ReadCommand(
                connection,
                $"printf '{shellMarker}\n'\n",
                shellMarker,
                TimeSpan.FromSeconds(10));
            var secondExec = await connection.RunCommandAsync("echo exec-again");
            var shellOutput = await shellTask;
            Assert.Contains(shellMarker, shellOutput, StringComparison.Ordinal);
            Assert.Contains("exec-again", secondExec, StringComparison.Ordinal);

            connection.Dispose();
            connection = null!;
        }
        finally
        {
            if (sshd is not null)
            {
                sshd.Kill(entireProcessTree: true);
                sshd.WaitForExit(2000);
            }

            if (connection is not null)
            {
                try
                {
                    connection.Dispose();
                }
                catch
                {
                    // best effort
                }
            }

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public async Task RM02_RunCommandOnDisposedConnectionThrows()
    {
        if (!OperatingSystem.IsLinux()
            || !File.Exists("/usr/sbin/sshd")
            || !File.Exists("/usr/bin/ssh-keygen"))
        {
            return;
        }

        var directory = Path.Combine(
            Path.GetTempPath(),
            "comtool-sshd-exec-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var port = FreePort();
        Process? sshd = null;
        SshConnection? connection = null;
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
                ArgumentList = { "-D", "-f", config, "-E", Path.Combine(directory, "sshd.log") },
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
            connection = SshConnection.Connect(options);
            var ok = await connection.RunCommandAsync("echo alive");
            Assert.Contains("alive", ok, StringComparison.Ordinal);

            // After Dispose, RunCommandAsync must throw immediately, not hang.
            var dead = connection;
            dead.Dispose();
            connection = null!;
            await Assert.ThrowsAsync<ObjectDisposedException>(() => dead.RunCommandAsync("echo x"));
        }
        finally
        {
            if (sshd is not null)
            {
                sshd.Kill(entireProcessTree: true);
                sshd.WaitForExit(2000);
            }

            if (connection is not null)
            {
                try
                {
                    connection.Dispose();
                }
                catch
                {
                    // best effort
                }
            }

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

}
