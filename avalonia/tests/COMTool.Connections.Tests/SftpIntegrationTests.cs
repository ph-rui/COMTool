using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using COMTool.Connections;
using COMTool.Sftp;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace COMTool.Connections.Tests;

/// <summary>
/// An ephemeral sshd with an sftp subsystem, the same way the shell
/// integration test builds one.
/// </summary>
public sealed class SshdFixture : IAsyncLifetime
{
    private Process? _sshd;

    public bool IsAvailable { get; private set; }

    public int Port { get; private set; }

    public string ClientKey { get; private set; } = string.Empty;

    public string WorkDirectory { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (!OperatingSystem.IsLinux()
            || !File.Exists("/usr/sbin/sshd")
            || !File.Exists("/usr/bin/ssh-keygen"))
        {
            return;
        }

        WorkDirectory = Path.Combine(
            Path.GetTempPath(),
            "comtool-sftp-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(WorkDirectory);

        var hostKey = Path.Combine(WorkDirectory, "host_key");
        ClientKey = Path.Combine(WorkDirectory, "client_key");
        var authorizedKeys = Path.Combine(WorkDirectory, "authorized_keys");
        Run("/usr/bin/ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", hostKey);
        Run("/usr/bin/ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", ClientKey);
        File.Copy(ClientKey + ".pub", authorizedKeys);
        RestrictKeyFile(authorizedKeys);
        RestrictKeyFile(ClientKey);

        Port = FreePort();
        var config = Path.Combine(WorkDirectory, "sshd_config");
        File.WriteAllText(config, BuildConfig(Port, hostKey, authorizedKeys, withSftpSubsystem: true));

        var check = RunCapture("/usr/sbin/sshd", "-t", "-f", config);
        if (check.ExitCode != 0)
        {
            return;
        }

        _sshd = Start(config);
        await WaitForPortAsync(Port, TimeSpan.FromSeconds(5));
        IsAvailable = true;
    }

    public Task DisposeAsync()
    {
        if (_sshd is { HasExited: false })
        {
            _sshd.Kill(entireProcessTree: true);
            _sshd.WaitForExit(2000);
        }

        if (Directory.Exists(WorkDirectory))
        {
            Directory.Delete(WorkDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    public static string BuildConfig(
        int port,
        string hostKey,
        string authorizedKeys,
        bool withSftpSubsystem) =>
        $"""
        Port {port}
        ListenAddress 127.0.0.1
        HostKey {hostKey}
        AuthorizedKeysFile {authorizedKeys}
        PubkeyAuthentication yes
        PasswordAuthentication no
        KbdInteractiveAuthentication no
        UsePAM no
        StrictModes no
        PidFile {Path.Combine(Path.GetTempPath(), "comtool-sftp-tests", port.ToString(), "sshd.pid")}
        LogLevel ERROR
        AllowUsers {Environment.UserName}
        {(withSftpSubsystem ? "Subsystem sftp internal-sftp" : string.Empty)}
        """;

    public static Process Start(string config)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "/usr/sbin/sshd",
            ArgumentList =
            {
                "-D",
                "-f",
                config,
                "-E",
                Path.Combine(Path.GetDirectoryName(config) ?? "/tmp", "sshd.log"),
            },
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        });

        return process ?? throw new InvalidOperationException("sshd did not start");
    }

    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task WaitForPortAsync(int port, TimeSpan timeout)
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

    public static void Run(string file, params string[] args)
    {
        var result = RunCapture(file, args);
        Assert.True(result.ExitCode == 0, $"{file} failed: {result.Error}");
    }

    /// <summary>
    /// The sshd refuses a key that other users may read.
    /// </summary>
    public static void RestrictKeyFile(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public static (int ExitCode, string Output, string Error) RunCapture(
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

public sealed class SftpIntegrationTests : IClassFixture<SshdFixture>
{
    private readonly SshdFixture _fixture;

    public SftpIntegrationTests(SshdFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ListsTheHomeDirectory()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        using var connection = SshConnection.Connect(Options());
        var sftp = connection.CreateSftpFileSystem();

        Assert.False(string.IsNullOrEmpty(sftp.HomeDirectory));
        Assert.StartsWith("/", sftp.HomeDirectory, StringComparison.Ordinal);

        // The home directory of the client is the home directory of the
        // server.
        var home = await RunShellAsync(connection, "printf '%s' \"$HOME\"");
        Assert.Equal(sftp.HomeDirectory, home);

        var entries = await sftp.ListAsync(sftp.HomeDirectory, CancellationToken.None);
        Assert.NotEmpty(entries);
        Assert.All(
            entries,
            entry => Assert.StartsWith(
                sftp.HomeDirectory + "/",
                entry.Path,
                StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Name.Length > 0);

        // The server sends the two entries of the directory itself, they
        // do not belong to the content of the directory.
        Assert.DoesNotContain(entries, entry => entry.Name is "." or "..");
    }

    [Fact]
    public async Task UploadsAndDownloadsTheSameBytes()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var payload = new byte[1024 * 1024];
        RandomNumberGenerator.Fill(payload);

        using var connection = SshConnection.Connect(Options());
        var sftp = connection.CreateSftpFileSystem();
        var work = await CreateWorkDirectoryAsync(sftp);

        try
        {
            var target = Path.Combine(work, "upload.bin");
            await using (var write = await sftp.OpenWriteAsync(target, CancellationToken.None))
            {
                await write.WriteAsync(payload);
            }

            var expected = Convert.ToHexString(SHA256.HashData(payload));
            var checksum = await RunShellAsync(connection, $"sha256sum '{target}'");
            Assert.Contains(expected.ToLowerInvariant(), checksum.ToLowerInvariant());

            var downloaded = Path.Combine(_fixture.WorkDirectory, "downloaded.bin");
            await using (var read = await sftp.OpenReadAsync(target, CancellationToken.None))
            await using (var file = File.Create(downloaded))
            {
                await read.CopyToAsync(file);
            }

            Assert.Equal(payload, await File.ReadAllBytesAsync(downloaded));
        }
        finally
        {
            await RemoveWorkDirectoryAsync(sftp, work);
        }
    }

    [Fact]
    public async Task CreatesRenamesAndDeletesOnTheServer()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        using var connection = SshConnection.Connect(Options());
        var sftp = connection.CreateSftpFileSystem();
        var work = await CreateWorkDirectoryAsync(sftp);

        try
        {
            var directory = Path.Combine(work, "dir");
            await sftp.CreateDirectoryAsync(directory, CancellationToken.None);

            var entries = await sftp.ListAsync(work, CancellationToken.None);
            var created = Assert.Single(entries, entry => entry.Name == "dir");
            Assert.True(created.IsDirectory);

            var renamed = Path.Combine(work, "dir-renamed");
            await sftp.RenameAsync(directory, renamed, CancellationToken.None);

            entries = await sftp.ListAsync(work, CancellationToken.None);
            Assert.DoesNotContain(entries, entry => entry.Name == "dir");
            Assert.Contains(entries, entry => entry.Name == "dir-renamed");

            await using (var write = await sftp.OpenWriteAsync(
                Path.Combine(renamed, "inner.txt"),
                CancellationToken.None))
            {
                await write.WriteAsync("inner"u8.ToArray());
            }

            // The tree is removed, the file inside it as well.
            await sftp.DeleteDirectoryAsync(renamed, recursive: true, CancellationToken.None);

            entries = await sftp.ListAsync(work, CancellationToken.None);
            Assert.DoesNotContain(entries, entry => entry.Name == "dir-renamed");
        }
        finally
        {
            await RemoveWorkDirectoryAsync(sftp, work);
        }
    }

    [Fact]
    public async Task SetsPermissionsThatTheServerAgreesWith()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        using var connection = SshConnection.Connect(Options());
        var sftp = connection.CreateSftpFileSystem();
        var work = await CreateWorkDirectoryAsync(sftp);

        try
        {
            var file = Path.Combine(work, "mode.txt");
            await using (var write = await sftp.OpenWriteAsync(file, CancellationToken.None))
            {
                await write.WriteAsync("x"u8.ToArray());
            }

            await sftp.SetPermissionsAsync(
                file,
                Convert.ToInt32("640", 8),
                CancellationToken.None);

            var mode = await RunShellAsync(connection, $"stat -c %a '{file}'");
            Assert.Equal("640", mode);
        }
        finally
        {
            await RemoveWorkDirectoryAsync(sftp, work);
        }
    }

    [Fact]
    public async Task ReportsASymbolicLinkAsASymbolicLink()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        using var connection = SshConnection.Connect(Options());
        var sftp = connection.CreateSftpFileSystem();
        var work = await CreateWorkDirectoryAsync(sftp);

        try
        {
            var real = Path.Combine(work, "real.txt");
            await using (var write = await sftp.OpenWriteAsync(real, CancellationToken.None))
            {
                await write.WriteAsync("real"u8.ToArray());
            }

            var link = Path.Combine(work, "link");
            await RunShellAsync(connection, $"ln -sf '{real}' '{link}'");

            var entries = await sftp.ListAsync(work, CancellationToken.None);
            var linkEntry = Assert.Single(entries, entry => entry.Name == "link");
            Assert.True(linkEntry.IsSymbolicLink);

            var target = Assert.Single(entries, entry => entry.Name == "real.txt");
            Assert.False(target.IsSymbolicLink);
        }
        finally
        {
            await RemoveWorkDirectoryAsync(sftp, work);
        }
    }

    [Fact]
    public async Task RemovesASymbolicLinkWithoutTouchingItsTarget()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        using var connection = SshConnection.Connect(Options());
        var sftp = connection.CreateSftpFileSystem();
        var work = await CreateWorkDirectoryAsync(sftp);

        try
        {
            var real = Path.Combine(work, "real.txt");
            await using (var write = await sftp.OpenWriteAsync(real, CancellationToken.None))
            {
                await write.WriteAsync("real"u8.ToArray());
            }

            var link = Path.Combine(work, "link");
            await RunShellAsync(connection, $"ln -sf '{real}' '{link}'");

            await sftp.DeleteFileAsync(link, CancellationToken.None);

            var entries = await sftp.ListAsync(work, CancellationToken.None);
            Assert.DoesNotContain(entries, entry => entry.Name == "link");

            // The file the link points at is still there.
            Assert.Contains(entries, entry => entry.Name == "real.txt");
        }
        finally
        {
            await RemoveWorkDirectoryAsync(sftp, work);
        }
    }

    [Fact]
    public async Task RenamesASymbolicLinkWithoutTouchingItsTarget()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        using var connection = SshConnection.Connect(Options());
        var sftp = connection.CreateSftpFileSystem();
        var work = await CreateWorkDirectoryAsync(sftp);

        try
        {
            var real = Path.Combine(work, "real.txt");
            await using (var write = await sftp.OpenWriteAsync(real, CancellationToken.None))
            {
                await write.WriteAsync("real"u8.ToArray());
            }

            var link = Path.Combine(work, "link");
            await RunShellAsync(connection, $"ln -sf '{real}' '{link}'");

            await sftp.RenameAsync(link, Path.Combine(work, "renamed"), CancellationToken.None);

            var entries = await sftp.ListAsync(work, CancellationToken.None);
            Assert.DoesNotContain(entries, entry => entry.Name == "link");
            Assert.Contains(entries, entry => entry.Name == "renamed");

            // The file the link points at keeps its name.
            Assert.Contains(entries, entry => entry.Name == "real.txt");
        }
        finally
        {
            await RemoveWorkDirectoryAsync(sftp, work);
        }
    }

    [Fact]
    public async Task FailsWhenTheServerHasNoSftpSubsystem()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var directory = Path.Combine(
            Path.GetTempPath(),
            "comtool-sftp-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var hostKey = Path.Combine(directory, "host_key");
        var clientKey = Path.Combine(directory, "client_key");
        var authorizedKeys = Path.Combine(directory, "authorized_keys");
        SshdFixture.Run("/usr/bin/ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", hostKey);
        SshdFixture.Run("/usr/bin/ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", clientKey);
        File.Copy(clientKey + ".pub", authorizedKeys);
        SshdFixture.RestrictKeyFile(authorizedKeys);
        SshdFixture.RestrictKeyFile(clientKey);

        var port = SshdFixture.FreePort();
        var config = Path.Combine(directory, "sshd_config");
        File.WriteAllText(config, SshdFixture.BuildConfig(port, hostKey, authorizedKeys, withSftpSubsystem: false));
        var check = SshdFixture.RunCapture("/usr/sbin/sshd", "-t", "-f", config);
        Assert.True(check.ExitCode == 0, check.Error);

        Process? sshd = null;
        try
        {
            sshd = SshdFixture.Start(config);
            await SshdFixture.WaitForPortAsync(port, TimeSpan.FromSeconds(5));

            var options = new SshConnectionOptions(
                "127.0.0.1",
                port,
                Environment.UserName,
                KeyFile: clientKey,
                Timeout: TimeSpan.FromSeconds(10));

            using var connection = SshConnection.Connect(options);

            // The shell still works without the sftp subsystem.
            var marker = await RunShellAsync(connection, "printf 'alive'");
            Assert.Equal("alive", marker);

            var failure = await Assert.ThrowsAsync<SshException>(
                () => Task.FromResult(connection.CreateSftpFileSystem()));
            Assert.Contains("sftp", failure.Message.ToLowerInvariant());
        }
        finally
        {
            if (sshd is { HasExited: false })
            {
                sshd.Kill(entireProcessTree: true);
                sshd.WaitForExit(2000);
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DisposingTheConnectionReleasesTheSftpClient()
    {
        if (!_fixture.IsAvailable)
        {
            return;
        }

        var connection = SshConnection.Connect(Options());
        var sftp = connection.CreateSftpFileSystem();
        var home = sftp.HomeDirectory;
        connection.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => sftp.ListAsync(home, CancellationToken.None));
    }

    /// <summary>
    /// Every test works in a directory of its own, so that a rerun does not
    /// run into the leftovers of an earlier run.
    /// </summary>
    private static async Task<string> CreateWorkDirectoryAsync(ISftpFileSystem sftp)
    {
        var name = "comtool-sftp-" + Guid.NewGuid().ToString("N")[..8];
        var path = Path.Combine(sftp.HomeDirectory, name);
        await sftp.CreateDirectoryAsync(path, CancellationToken.None);
        return path;
    }

    private static async Task RemoveWorkDirectoryAsync(ISftpFileSystem sftp, string path)
    {
        try
        {
            await sftp.DeleteDirectoryAsync(path, recursive: true, CancellationToken.None);
        }
        catch (Exception)
        {
            // The test that failed reports the reason already. A leftover
            // in the home directory of the user does not fail the test.
        }
    }

    private SshConnectionOptions Options() =>
        new(
            "127.0.0.1",
            _fixture.Port,
            Environment.UserName,
            KeyFile: _fixture.ClientKey,
            Columns: 100,
            Rows: 40,
            Timeout: TimeSpan.FromSeconds(10));

    /// <summary>
    /// Runs a command in the shell of the connection and returns what it
    /// printed between the two markers.
    /// </summary>
    private static async Task<string> RunShellAsync(
        SshConnection connection,
        string command,
        TimeSpan? timeout = null)
    {
        var marker = Guid.NewGuid().ToString("N");
        var writer = connection.Pty.WriterStream;
        writer.Write(Encoding.UTF8.GetBytes(
            $"M={marker}; printf 'BEGIN%s\\n' \"$M\"; {command}; printf 'END%s\\n' \"$M\"\n"));
        writer.Flush();

        var reader = connection.Pty.ReaderStream;
        var buffer = new byte[4096];
        var text = new StringBuilder();
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
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
            var current = text.ToString().Replace("\r", string.Empty);
            var end = current.IndexOf("END" + marker, StringComparison.Ordinal);
            if (end < 0)
            {
                readTask = Task.Run(() => reader.Read(buffer, 0, buffer.Length));
                continue;
            }

            var begin = current.IndexOf("BEGIN" + marker, StringComparison.Ordinal);
            if (begin < 0)
            {
                return string.Empty;
            }

            return current[(begin + ("BEGIN" + marker).Length)..end].Trim();
        }

        return text.ToString();
    }
}
