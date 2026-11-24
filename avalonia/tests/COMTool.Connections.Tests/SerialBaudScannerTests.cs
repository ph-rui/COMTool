using System.Text;
using COMTool.Connections;
using COMTool.Core.Config;

namespace COMTool.Connections.Tests;

/// <summary>
/// r67.10: passive auto-baudrate detection. The scanner opens the port once per
/// candidate rate (SerialPort.BaudRate is fixed while open), collects a short
/// sample and scores how much it looks like text. These tests exercise the pure
/// scoring/picking logic plus the real open/read/close path over a PTY.
/// </summary>
public sealed class SerialBaudScannerTests
{
    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void BAUD01_ScoreAcceptsReadableConsoleText()
    {
        var sample = Utf8(
            "U-Boot 2023.04 (Jan 01 2026 - 10:00:00 +0800)\r\n" +
            "DRAM:  512 MiB\r\n" +
            "MMC:   mmc@ff0c0000: 0\r\n" +
            "Hit any key to stop autoboot:  0\r\n" +
            "=> ");

        var score = SerialBaudScanner.Score(sample);

        Assert.True(score >= 0.9, $"可读文本应接近满分，实际 {score:0.000}");
    }

    [Fact]
    public void BAUD02_ScoreRejectsFramingGarbage()
    {
        // Wrong baud on a UART typically yields 0x00/0xFF runs from framing errors.
        var sample = new byte[512];
        for (var i = 0; i < sample.Length; i++)
        {
            sample[i] = i % 3 == 0 ? (byte)0x00 : i % 3 == 1 ? (byte)0xFF : (byte)0xFE;
        }

        var score = SerialBaudScanner.Score(sample);

        Assert.True(score < 0.3, $"帧错误垃圾不应通过，实际 {score:0.000}");
    }

    [Fact]
    public void BAUD03_ScoreRejectsBinaryFrames()
    {
        // A binary protocol frame: header, length, payload with control bytes.
        var sample = new byte[256];
        for (var i = 0; i < sample.Length; i += 8)
        {
            sample[i] = 0xA5;
            sample[i + 1] = 0x5A;
            sample[i + 2] = 0x06;
            sample[i + 3] = 0x80;
            sample[i + 4] = 0x01;
            sample[i + 5] = 0x00;
            sample[i + 6] = 0x7F;
            sample[i + 7] = 0x10;
        }

        var score = SerialBaudScanner.Score(sample);

        Assert.True(score < 0.5, $"二进制帧不应判为文本，实际 {score:0.000}");
    }

    [Fact]
    public void BAUD04_ScoreAcceptsUtf8ChineseText()
    {
        var sample = Utf8("系统启动完成\r\n电压 3.30 V 电流 120 mA\r\n登录: ");

        var score = SerialBaudScanner.Score(sample);

        Assert.True(score >= 0.85, $"UTF-8 中文日志应通过，实际 {score:0.000}");
    }

    [Fact]
    public void BAUD05_ScoreRejectsTinyAndEmptySamples()
    {
        Assert.Equal(0, SerialBaudScanner.Score(ReadOnlySpan<byte>.Empty));
        Assert.True(SerialBaudScanner.Score(Utf8("ok")) < 0.5);
    }

    [Fact]
    public void BAUD06_ScanCorePicksRateWithReadableText()
    {
        var probes = new Dictionary<int, byte[]>
        {
            [115200] = new byte[] { 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF },
            [9600] = Utf8("Booting Linux...\r\n[ 1.20 ] usb 1-1: new device\r\nroot@board:~# "),
            [19200] = new byte[] { 0xFE, 0xFE, 0x00, 0x00, 0x80, 0xFF, 0xFE, 0x00 },
        };

        var outcome = SerialBaudScanner.ScanCore(
            new[] { 115200, 9600, 19200 },
            rate => probes[rate]);

        Assert.NotNull(outcome.Best);
        Assert.Equal(9600, outcome.Best!.Baudrate);
        Assert.True(outcome.Best.Score >= SerialBaudScanner.AcceptScore);
        Assert.Equal(probes[9600].Length, outcome.Best.SampleBytes);
    }

    [Fact]
    public void BAUD07_ScanCoreStopsEarlyOnHighConfidence()
    {
        var probed = new List<int>();
        var outcome = SerialBaudScanner.ScanCore(
            new[] { 115200, 9600, 57600 },
            rate =>
            {
                probed.Add(rate);
                return Utf8("[    0.000000] Linux version 6.1.0\r\n[    0.000000] Command line: console=ttyS0\r\n");
            });

        Assert.Equal(new[] { 115200 }, probed);
        Assert.Equal(115200, outcome.Best!.Baudrate);
    }

    [Fact]
    public void BAUD08_ScanCorePrefersHigherScoreOverCandidateOrder()
    {
        var weak = Utf8("x\r\ny\r\n");
        var strong = Utf8(
            "=== system monitor ===\r\n" +
            "uptime 00:12:33  cpu 12%  mem 48%\r\n" +
            "uart0: 115200 8N1\r\n" +
            "> ");
        var outcome = SerialBaudScanner.ScanCore(
            new[] { 115200, 9600 },
            rate => rate == 115200 ? weak : strong);

        Assert.Equal(9600, outcome.Best!.Baudrate);
        Assert.Equal(2, outcome.Tried.Count);
    }

    [Fact]
    public void BAUD09_ScanCoreReturnsNullForSilentDevice()
    {
        var probed = new List<int>();
        var outcome = SerialBaudScanner.ScanCore(
            new[] { 115200, 9600 },
            rate =>
            {
                probed.Add(rate);
                return Array.Empty<byte>();
            });

        Assert.Null(outcome.Best);
        Assert.Equal(new[] { 115200, 9600 }, probed);
        Assert.Equal(new[] { 115200, 9600 }, outcome.Tried);
    }

    [Fact]
    public void BAUD10_ScanCoreSkipsRatesThatFailToOpen()
    {
        var outcome = SerialBaudScanner.ScanCore(
            new[] { 115200, 9600 },
            rate => rate == 115200
                ? throw new UnauthorizedAccessException("port busy")
                : Utf8("console ready\r\nlogin: "));

        Assert.NotNull(outcome.Best);
        Assert.Equal(9600, outcome.Best!.Baudrate);
        Assert.Equal(new[] { 115200, 9600 }, outcome.Tried);
    }

    [Fact]
    public async Task BAUD11_ScanAsyncReadsTextThroughPty()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/socat"))
        {
            return;
        }

        var directory = Path.Combine(
            Path.GetTempPath(),
            "comtool-baud-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var device = Path.Combine(directory, "ttyB");
        var writer = Path.Combine(directory, "ttyA");
        var socat = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "/usr/bin/socat",
            ArgumentList =
            {
                "-d",
                "-d",
                $"pty,raw,echo=0,link={writer}",
                $"pty,raw,echo=0,link={device}",
            },
            RedirectStandardError = true,
            UseShellExecute = false,
        });

        try
        {
            Assert.NotNull(socat);
            WaitForLink(writer);
            WaitForLink(device);

            // PTYs ignore the baud rate, so the first candidate already reads the
            // text: this covers the real open/discard/read/close path.
            using var sender = SerialConnection.Open(new SerialConnectionConfig
            {
                Port = writer,
                Baudrate = 115200,
                Bytesize = 8,
                Parity = "None",
                Stopbits = "1",
                Flowcontrol = "None",
            });
            var payload = Utf8("hello from pty\r\nsecond line\r\n");
            var writerTask = Task.Run(async () =>
            {
                for (var i = 0; i < 20; i++)
                {
                    sender.Pty.WriterStream.Write(payload);
                    sender.Pty.WriterStream.Flush();
                    await Task.Delay(50);
                }
            });

            var outcome = await SerialBaudScanner.ScanAsync(new SerialConnectionConfig
                {
                    Port = device,
                    Bytesize = 8,
                    Parity = "None",
                    Stopbits = "1",
                    Flowcontrol = "None",
                });

            Assert.NotNull(outcome.Best);
            Assert.Equal(115200, outcome.Best!.Baudrate);
            Assert.True(outcome.Best.SampleBytes >= 16);
            await writerTask;
        }
        finally
        {
            try
            {
                socat?.Kill();
                socat?.WaitForExit(2000);
            }
            catch
            {
                // best effort
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

    private static void WaitForLink(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                return;
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"symlink/device {path} did not appear");
    }
}
