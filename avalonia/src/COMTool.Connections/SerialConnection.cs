using System.IO.Ports;
using COMTool.Core.Config;

namespace COMTool.Connections;

/// <summary>
/// A serial transport that can be replaced when a USB serial device is
/// unplugged and later reinserted under the same port name.
/// </summary>
public interface ISerialConnection : IDisposable
{
    PtyStreamConnection Pty { get; }
    string PortName { get; }
    bool IsOpen { get; }
}

/// <summary>
/// Opens a serial port with the same settings used by the Python serial
/// connection and exposes it as an IPtyConnection for the terminal control.
/// </summary>
public sealed class SerialConnection : ISerialConnection
{
    private readonly SerialPort _port;
    private int _disposed;

    private SerialConnection(SerialPort port, PtyStreamConnection pty)
    {
        _port = port;
        Pty = pty;
    }

    public PtyStreamConnection Pty { get; }

    public string PortName => _port.PortName;

    public bool IsOpen => _port.IsOpen;

    public static SerialConnection Open(SerialConnectionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var portName = (config.Port ?? "").Trim();
        if (portName.Length == 0)
        {
            throw new ArgumentException("serial port name is empty", nameof(config));
        }

        var port = new SerialPort(
            portName,
            config.Baudrate > 0 ? config.Baudrate : 115200,
            ParseParity(config.Parity),
            config.Bytesize is >= 5 and <= 8 ? config.Bytesize : 8,
            ParseStopBits(config.Stopbits));
        port.Handshake = ParseHandshake(config.Flowcontrol);
        port.ReadTimeout = 500;
        port.WriteTimeout = 5000;
        port.RtsEnable = config.Rts;
        port.DtrEnable = config.Dtr;

        try
        {
            port.Open();
            // Match Python: drop bytes a previous session or the driver left
            // queued. Otherwise stale ZMODEM payload can appear in the
            // terminal (or be sent back) when a saved session is reopened.
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
            var pty = new PtyStreamConnection(
                new TimeoutRetryStream(port.BaseStream),
                port.BaseStream,
                resize: null,
                close: () => port.Close(),
                ownsStreams: false);
            return new SerialConnection(port, pty);
        }
        catch
        {
            port.Dispose();
            throw;
        }
    }

    public static Parity ParseParity(string? value) =>
        (value ?? "").Trim().ToLowerInvariant() switch
        {
            "odd" => Parity.Odd,
            "even" => Parity.Even,
            "mark" => Parity.Mark,
            "space" => Parity.Space,
            _ => Parity.None,
        };

    public static StopBits ParseStopBits(string? value) =>
        (value ?? "").Trim() switch
        {
            "1.5" => StopBits.OnePointFive,
            "2" => StopBits.Two,
            _ => StopBits.One,
        };

    public static Handshake ParseHandshake(string? value) =>
        (value ?? "").Trim().ToUpperInvariant() switch
        {
            "XON/XOFF" or "XONXOFF" or "SOFTWARE" => Handshake.XOnXOff,
            "RTS/CTS" or "RTSCTS" or "HARDWARE" => Handshake.RequestToSend,
            // System.IO.Ports has no DSR/DTR handshake mode. Mapping it to
            // RequestToSendXOnXOff would silently enable unrelated RTS/CTS and
            // XON/XOFF flow control, so keep the line configuration neutral.
            "DSR/DTR" or "DSRDTR" => Handshake.None,
            _ => Handshake.None,
        };

    /// <summary>
    /// 发送硬件断流（MobaXterm「特殊命令 → Break」）：拉低数据线 durationMs 毫秒后
    /// 释放。后台线程执行（阻塞等待时长），端口已关/并发拔除时静默忽略。
    /// </summary>
    public Task SendBreakAsync(int durationMs = 250) => Task.Run(() =>
    {
        try
        {
            if (!_port.IsOpen)
            {
                return;
            }

            // .NET 10 SerialPort 用 BreakState 布尔属性（旧版 Break(bool)/BreakDuration 已移除）：
            // 拉高 break 保持 durationMs 后拉低，形成一次硬件断流脉冲。
            _port.BreakState = true;
            Thread.Sleep(durationMs);
            _port.BreakState = false;
        }
        catch
        {
            // 端口热拔/已关：视为本次 break 失败，不向 UI 抛异常。
        }
    });

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Pty.Close();
        _port.Dispose();
    }
}
