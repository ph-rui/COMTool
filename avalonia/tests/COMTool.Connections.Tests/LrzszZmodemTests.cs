using System.Text;
using COMTool.Connections;
using COMTool.Zmodem;
using Porta.Pty;

namespace COMTool.Connections.Tests;

/// <summary>
/// Exercises the real lrzsz binaries against the production ZmodemStream
/// interception path. These tests are opt-in because CI images do not ship
/// lrzsz; set LRZSZ_DIR to a directory containing sz/rz to enable them.
/// </summary>
public sealed class LrzszZmodemTests
{
    [Fact]
    public async Task RealSzFileIsReceived()
    {
        var tools = FindLrzsz();
        if (tools is null)
        {
            return;
        }

        var directory = CreateDirectory();
        try
        {
            var source = Path.Combine(directory, "source", "receive-source.bin");
            var payload = CreatePayload();
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            await File.WriteAllBytesAsync(source, payload);
            var result = await RunAsync(
                tools,
                Path.GetDirectoryName(source)!,
                isSend: false,
                source);

            Assert.False(result.IsSend);
            var received = Assert.Single(result.Files);
            Assert.Equal("receive-source.bin", received.Name);
            Assert.Equal(payload, await File.ReadAllBytesAsync(received.Path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task RealRzReceivesSentFile()
    {
        var tools = FindLrzsz();
        if (tools is null)
        {
            return;
        }

        var directory = CreateDirectory();
        try
        {
            var source = Path.Combine(directory, "source", "send-source.bin");
            var receiveDirectory = Path.Combine(directory, "receive");
            var payload = CreatePayload();
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            Directory.CreateDirectory(receiveDirectory);
            await File.WriteAllBytesAsync(source, payload);
            var result = await RunAsync(
                tools,
                receiveDirectory,
                isSend: true,
                source);

            Assert.True(result.IsSend);
            var received = Path.Combine(receiveDirectory, "send-source.bin");
            Assert.True(File.Exists(received));
            Assert.Equal(payload, await File.ReadAllBytesAsync(received));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static async Task<ZmodemTransferResult> RunAsync(
        string tools,
        string workingDirectory,
        bool isSend,
        string sourcePath)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException();
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";
        var options = new PtyOptions
        {
            Name = "comtool-lrzsz-test",
            Cols = 100,
            Rows = 40,
            Cwd = workingDirectory,
            App = "/bin/bash",
            CommandLine = new[] { "--noprofile", "--norc" },
            Environment = new Dictionary<string, string>
            {
                ["PATH"] = tools + ":" + path,
                ["TERM"] = "xterm-256color",
            },
        };

        using var pty = await PtyProvider.SpawnAsync(options, CancellationToken.None);
        using var connection = new PtyStreamConnection(
            pty.ReaderStream,
            pty.WriterStream,
            close: () => pty.Kill());
        var zmodem = connection.Zmodem;
        Assert.NotNull(zmodem);

        var completed = new TaskCompletionSource<ZmodemTransferResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        zmodem!.TransferCompleted += result => completed.TrySetResult(result);
        zmodem.TransferFailed += message =>
            completed.TrySetException(new InvalidOperationException(message));
        if (isSend)
        {
            zmodem.SendRequested += _ => zmodem.StartSend(new[] { sourcePath });
        }

        var command = isSend
            ? "rz -y\r"
            : $"sz {Path.GetFileName(sourcePath)}\r";
        await connection.WriterStream.WriteAsync(Encoding.UTF8.GetBytes(command));
        await connection.WriterStream.FlushAsync();

        return await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static string? FindLrzsz()
    {
        var configured = Environment.GetEnvironmentVariable("LRZSZ_DIR");
        if (!string.IsNullOrWhiteSpace(configured)
            && File.Exists(Path.Combine(configured, "sz"))
            && File.Exists(Path.Combine(configured, "rz")))
        {
            return configured;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (File.Exists(Path.Combine(directory, "sz"))
                && File.Exists(Path.Combine(directory, "rz")))
            {
                return directory;
            }
        }

        return null;
    }

    private static byte[] CreatePayload()
    {
        // 64 KiB exercises lrzsz's 1 KiB windows and CRC32 subpackets.
        var payload = new byte[64 * 1024];
        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)(index * 31 + 7);
        }

        return payload;
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "comtool-lrzsz-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Keep test cleanup best-effort; the OS temp cleaner handles this.
        }
    }
}
