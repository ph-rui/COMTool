using System.Buffers;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using COMTool.Core.Config;

namespace COMTool.Connections;

/// <summary>r67.10：自动波特率的扫描结果。</summary>
public sealed record BaudScanOutcome(
    BaudScanResult? Best,
    IReadOnlyList<int> Tried)
{
    public bool Found => Best is not null;
}

/// <summary>r67.10：识别到的波特率及其置信度。</summary>
public sealed record BaudScanResult(
    int Baudrate,
    double Score,
    int SampleBytes);

/// <summary>
/// r67.10：被动扫描串口波特率。设备持续输出文本时，依次用候选速率打开端口、
/// 采集一小段数据并评分（可打印字符占比、换行、非法 UTF-8 与 0x00/0xFF 扣分），
/// 取最高分；常见速率排在前面，首个高分结果提前结束。
/// </summary>
/// <remarks>
/// <see cref="System.IO.Ports.SerialPort"/> 的 BaudRate 只能在关闭状态修改，
/// 所以每个候选都是一次独立的开关端口；扫描只读、不写，不会改变设备状态。
/// 只适用于持续输出可读文本的设备；静默设备或纯二进制帧会返回 null。
/// </remarks>
public static class SerialBaudScanner
{
    /// <summary>候选速率，按常见程度排序。</summary>
    public static readonly IReadOnlyList<int> DefaultCandidates = new[]
    {
        115200, 9600, 57600, 38400, 19200, 230400, 460800, 921600, 4800, 2400, 1200,
    };

    internal const double AcceptScore = 0.80;

    internal const double EarlyScore = 0.97;

    internal static readonly TimeSpan DefaultDwell = TimeSpan.FromMilliseconds(500);

    internal const int MaxSampleBytes = 4096;

    // 太短的样本评分不可信（例如串口里只剩 2 个字节）；8 字节可容纳
    // "Password: " 这类不重复的短提示符。
    private const int MinSampleBytes = 8;

    /// <summary>评分：0~1，文本越规整越高。</summary>
    internal static double Score(ReadOnlySpan<byte> sample)
    {
        if (sample.Length < MinSampleBytes)
        {
            return 0;
        }

        var chars = 0;
        var printable = 0;
        var newlines = 0;
        var invalidUtf8 = 0;
        var remaining = sample;
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf8(remaining, out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                invalidUtf8++;
                remaining = remaining[1..];
                continue;
            }

            chars++;
            var value = rune.Value;
            if (value is >= 0x20 and <= 0x7E
                || value is '\r' or '\n' or '\t'
                // 串口控制台常见：ESC（ANSI 颜色）、退格、响铃。
                || value is 0x1B or 0x08 or 0x07
                // 合法 UTF-8 的非 ASCII 字符（中文日志、°C、µs 等）。
                || value >= 0xA0)
            {
                printable++;
            }

            if (value == '\n')
            {
                newlines++;
            }

            remaining = remaining[consumed..];
        }

        if (chars == 0)
        {
            return 0;
        }

        var printableRatio = (double)printable / chars;

        // 错误波特率下 UART 的帧错误通常表现为 0x00/0xFF 长串，二进制协议则
        // 大量非法 UTF-8 序列；两类都单独扣分，避免"恰好可打印"的垃圾胜出。
        var nulOrFf = 0;
        var longestRun = 0;
        var run = 0;
        byte previous = 0;
        for (var i = 0; i < sample.Length; i++)
        {
            var value = sample[i];
            if (value is 0x00 or 0xFF)
            {
                nulOrFf++;
            }

            if (i > 0 && value == previous)
            {
                run++;
            }
            else
            {
                run = 1;
                previous = value;
            }

            if (run > longestRun)
            {
                longestRun = run;
            }
        }

        var score = printableRatio;
        if (newlines >= 1)
        {
            score += 0.06;
        }

        if (newlines >= 2)
        {
            score += 0.04;
        }

        score -= 2.0 * nulOrFf / sample.Length;
        score -= 2.0 * invalidUtf8 / sample.Length;
        if (longestRun >= 8)
        {
            score -= 0.20;
        }

        return Math.Clamp(score, 0, 1);
    }

    /// <summary>
    /// 依次探测候选速率；probe 返回该速率下采集到的字节。首个 ≥ <see cref="EarlyScore"/>
    /// 的结果提前结束。
    /// </summary>
    internal static BaudScanOutcome ScanCore(
        IEnumerable<int> candidates,
        Func<int, byte[]> probe)
    {
        var tried = new List<int>();
        BaudScanResult? best = null;
        foreach (var rate in candidates)
        {
            tried.Add(rate);
            byte[] sample;
            try
            {
                sample = probe(rate) ?? Array.Empty<byte>();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 端口被占用/驱动异常：跳过该速率，继续试其他候选。
                continue;
            }

            var score = Score(sample);
            if (score < AcceptScore)
            {
                continue;
            }

            var result = new BaudScanResult(rate, score, sample.Length);
            if (best is null || result.Score > best.Score)
            {
                best = result;
            }

            if (result.Score >= EarlyScore)
            {
                break;
            }
        }

        return new BaudScanOutcome(best, tried);
    }

    /// <summary>扫描串口，返回识别结果（失败时 Best 为 null，Tried 为已试速率）。</summary>
    public static Task<BaudScanOutcome> ScanAsync(
        SerialConnectionConfig config,
        Action<int>? onProbing = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        var portName = (config.Port ?? "").Trim();
        if (portName.Length == 0)
        {
            throw new ArgumentException("serial port name is empty", nameof(config));
        }

        return Task.Run(
            () => ScanCore(
                DefaultCandidates,
                rate =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    onProbing?.Invoke(rate);
                    return Probe(portName, config, rate, DefaultDwell, cancellationToken);
                }),
            cancellationToken);
    }

    private static byte[] Probe(
        string portName,
        SerialConnectionConfig config,
        int baudrate,
        TimeSpan dwell,
        CancellationToken cancellationToken)
    {
        using var port = SerialConnection.CreatePort(portName, config, baudrate);
        // 每个速率只等一小段，超时异常是常态，靠循环累计到采集窗口结束。
        port.ReadTimeout = 100;
        port.Open();
        port.DiscardInBuffer();
        port.DiscardOutBuffer();

        var buffer = new byte[512];
        var sample = new List<byte>(MaxSampleBytes);
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < dwell
               && sample.Count < MaxSampleBytes
               && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var count = port.Read(buffer, 0, Math.Min(buffer.Length, MaxSampleBytes - sample.Count));
                if (count > 0)
                {
                    sample.AddRange(buffer.AsSpan(0, count).ToArray());
                }
            }
            catch (TimeoutException)
            {
                // 该速率下暂时没有数据，继续等。
            }
            catch (IOException)
            {
                // 帧错误/校验错误在错误波特率下很常见；保留已采集的字节。
            }
        }

        return sample.ToArray();
    }
}
