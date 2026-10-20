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
