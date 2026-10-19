using System.Diagnostics;
using System.Text;
using COMTool.Connections;
using COMTool.Core.Config;

namespace COMTool.Connections.Tests;

public sealed class SerialPtyIntegrationTests
{
    [Fact]
    public async Task SerialConnectionReadsAndWritesThroughPty()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/socat"))
        {
            return;
        }

        var directory = Path.Combine(
            Path.GetTempPath(),
            "comtool-serial-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
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

        try
        {
            Assert.NotNull(socat);
            WaitForLink(left);
            WaitForLink(right);

            using var sender = SerialConnection.Open(new SerialConnectionConfig
            {
                Port = left,
                Baudrate = 115200,
                Bytesize = 8,
                Parity = "None",
                Stopbits = "1",
                Flowcontrol = "None",
            });
            using var receiver = SerialConnection.Open(new SerialConnectionConfig
            {
                Port = right,
                Baudrate = 115200,
                Bytesize = 8,
                Parity = "None",
                Stopbits = "1",
                Flowcontrol = "None",
            });

            var payload = Encoding.UTF8.GetBytes("serial pty ok");
            sender.Pty.WriterStream.Write(payload);
            sender.Pty.WriterStream.Flush();

            var received = new byte[payload.Length];
            var read = Task.Run(() => receiver.Pty.ReaderStream.Read(received, 0, received.Length));
            var completed = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(read, completed);
            Assert.Equal(payload.Length, await read);
            Assert.Equal(payload, received);
        }
        finally
        {
            if (socat is { HasExited: false })
            {
                socat.Kill(entireProcessTree: true);
                socat.WaitForExit(2000);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void WaitForLink(string path)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                return;
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"socat did not create {path}");
    }
}
