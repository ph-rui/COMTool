using System.Diagnostics;
using System.Text;
using COMTool.Connections;
using COMTool.Core.Config;

namespace COMTool.Connections.Tests;

public sealed class SerialOpenBufferTests
{
    [Fact]
    public async Task SER07_OpeningSerialSessionDiscardsQueuedStaleBytes()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/socat"))
        {
            return;
        }

        using var pair = await PtyPair.StartAsync();
        // Keep the slave open so bytes written by the peer before the session
        // starts stay queued in the tty input buffer.
        using var keepAlive = new FileStream(
            pair.Left,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite);
        using var peer = new FileStream(
            pair.Right,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite);

        var stale = Encoding.ASCII.GetBytes("STALE-SESSION-BYTES");
        await peer.WriteAsync(stale);
        await peer.FlushAsync();
        await Task.Delay(500);

        using var connection = SerialConnection.Open(new SerialConnectionConfig
        {
            Port = pair.Left,
            Baudrate = 115200,
            Bytesize = 8,
            Parity = "None",
            Stopbits = "1",
            Flowcontrol = "None",
        });

        // The marker lets the test read exactly one time: if the stale bytes
        // were not purged they arrive before the marker.
        var live = Encoding.ASCII.GetBytes("LIVE-OK");
        await peer.WriteAsync(live);
        await peer.FlushAsync();

        var text = await ReadUntilAsync(
            connection.Pty.ReaderStream,
            "LIVE-OK",
            TimeSpan.FromSeconds(3));

        Assert.DoesNotContain("STALE-SESSION-BYTES", text);
        Assert.Contains("LIVE-OK", text);
    }

    private static async Task<string> ReadUntilAsync(
        Stream stream,
        string marker,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var buffer = new byte[256];
        var text = new StringBuilder();
        while (DateTime.UtcNow < deadline)
        {
            var read = await Task.Run(() => stream.Read(buffer, 0, buffer.Length));
            if (read <= 0)
            {
                break;
            }

            text.Append(Encoding.ASCII.GetString(buffer, 0, read));
            if (text.ToString().Contains(marker, StringComparison.Ordinal))
            {
                return text.ToString();
            }
        }

        throw new TimeoutException(
            $"serial stream did not contain {marker}; received: {text}");
    }

    private sealed class PtyPair : IDisposable
    {
        private PtyPair(string directory, string left, string right, Process socat)
        {
            Directory = directory;
            Left = left;
            Right = right;
            Socat = socat;
        }

        public string Directory { get; }
        public string Left { get; }
        public string Right { get; }
        public Process Socat { get; }

        public static async Task<PtyPair> StartAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "comtool-serial-tests",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var left = Path.Combine(directory, "ttyA");
            var right = Path.Combine(directory, "ttyB");
            var socat = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/socat",
                ArgumentList =
                {
                    "-d",
                    "-d",
                    $"pty,raw,echo=0,link={left}",
                    $"pty,raw,echo=0,link={right}",
                },
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (socat is null)
            {
                throw new InvalidOperationException("socat did not start");
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(left) && File.Exists(right))
                {
                    return new PtyPair(directory, left, right, socat);
                }

                await Task.Delay(50);
            }

            socat.Kill(entireProcessTree: true);
            throw new TimeoutException("socat did not create the PTY pair");
        }

        public void Dispose()
        {
            if (Socat is { HasExited: false })
            {
                Socat.Kill(entireProcessTree: true);
                Socat.WaitForExit(2000);
            }

            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of the temporary PTY directory.
            }
        }
    }
}
