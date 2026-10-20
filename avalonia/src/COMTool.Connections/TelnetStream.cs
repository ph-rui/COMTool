using System.Text;

namespace COMTool.Connections;

/// <summary>
/// Minimal Telnet codec. It negotiates terminal type, window size and the
/// common SGA/ECHO/BINARY options, removes IAC control sequences from the
/// incoming terminal stream, and escapes IAC in outgoing terminal data.
/// </summary>
internal sealed class TelnetStream : Stream
{
    private const byte Iac = 255;
    private const byte Se = 240;
    private const byte Sb = 250;
    private const byte Will = 251;
    private const byte Wont = 252;
    private const byte Do = 253;
    private const byte Dont = 254;

    private const byte OptionBinary = 0;
    private const byte OptionEcho = 1;
    private const byte OptionSga = 3;
    private const byte OptionTtype = 24;
    private const byte OptionNaws = 31;

    private const byte TtypeIs = 0;
    private const byte TtypeSend = 1;

    private readonly Stream _inner;
    private readonly string _terminalType;
    private readonly object _writeGate = new();
    private readonly List<byte> _data = new();
    private readonly List<byte> _subnegotiation = new();
    private readonly byte[] _readBuffer = new byte[4096];

    private ParseState _state;
    private byte _verb;
    private bool _started;
    private bool _skipLeadingLf;
    private int _columns;
    private int _rows;

    private bool _localTtype;
    private bool _localNaws;
    private bool _localSga;
    private bool _localBinary;
    private bool _remoteEcho;
    private bool _remoteSga;
    private bool _remoteBinary;

    public TelnetStream(Stream inner, string terminalType, int columns, int rows)
    {
        _inner = inner;
        _terminalType = string.IsNullOrWhiteSpace(terminalType)
            ? "XTERM-256COLOR"
            : terminalType;
        _columns = columns > 0 ? columns : 100;
        _rows = rows > 0 ? rows : 30;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        SendRaw(new byte[]
        {
            Iac, Will, OptionTtype,
            Iac, Will, OptionNaws,
            Iac, Will, OptionSga,
            Iac, Do, OptionSga,
        });
    }

    public void Resize(int columns, int rows)
    {
        if (columns <= 0 || rows <= 0)
        {
            return;
        }

        _columns = columns;
        _rows = rows;
        if (!_localNaws)
        {
            return;
        }

        try
        {
            SendNaws();
        }
        catch (IOException)
        {
            // The terminal can arrange once more while the peer is closing.
        }
        catch (ObjectDisposedException)
        {
            // The transport is already gone.
        }
    }

    public void WriteData(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return;
        }

        var encoded = new List<byte>(data.Length + 8);
        foreach (var value in data)
        {
            if (value == Iac)
            {
                encoded.Add(Iac);
                encoded.Add(Iac);
                _skipLeadingLf = false;
                continue;
            }

            if (_localBinary)
            {
                encoded.Add(value);
                _skipLeadingLf = false;
                continue;
            }

            if (value == (byte)'\r')
            {
                encoded.Add((byte)'\r');
                encoded.Add((byte)'\n');
                _skipLeadingLf = true;
            }
            else if (value == (byte)'\n')
            {
                if (_skipLeadingLf)
                {
                    _skipLeadingLf = false;
                }
                else
                {
                    encoded.Add((byte)'\r');
                    encoded.Add((byte)'\n');
                }
            }
            else
            {
                _skipLeadingLf = false;
                encoded.Add(value);
            }
        }

        if (encoded.Count > 0)
        {
            SendRaw(encoded.ToArray());
        }
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        while (true)
        {
            if (_data.Count > 0)
            {
                var count = Math.Min(buffer.Length, _data.Count);
                for (var index = 0; index < count; index++)
                {
                    buffer.Span[index] = _data[index];
                }

                _data.RemoveRange(0, count);
                return count;
            }

            var read = await _inner.ReadAsync(_readBuffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return 0;
            }

            Process(_readBuffer.AsSpan(0, read));
        }
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteData(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer) => WriteData(buffer);

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        WriteData(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override void Flush() => _inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    private void Process(ReadOnlySpan<byte> input)
    {
        _data.Clear();
        foreach (var value in input)
        {
            switch (_state)
            {
                case ParseState.Data:
                    if (value == Iac)
                    {
                        _state = ParseState.Iac;
                    }
                    else
                    {
                        _data.Add(value);
                    }

                    break;

                case ParseState.Iac:
                    if (value == Iac)
                    {
                        _data.Add(Iac);
                        _state = ParseState.Data;
                    }
                    else if (value is Will or Wont or Do or Dont)
                    {
                        _verb = value;
                        _state = ParseState.Negotiation;
                    }
                    else if (value == Sb)
                    {
                        _subnegotiation.Clear();
                        _state = ParseState.Subnegotiation;
                    }
                    else
                    {
                        // NOP, GA, and other two-byte commands have no
                        // terminal data.
                        _state = ParseState.Data;
                    }

                    break;

                case ParseState.Negotiation:
                    HandleNegotiation(_verb, value);
                    _state = ParseState.Data;
                    break;

                case ParseState.Subnegotiation:
                    if (value == Iac)
                    {
                        _state = ParseState.SubnegotiationIac;
                    }
                    else
                    {
                        _subnegotiation.Add(value);
                    }

                    break;

                case ParseState.SubnegotiationIac:
                    if (value == Se)
                    {
                        HandleSubnegotiation();
                        _state = ParseState.Data;
                    }
                    else if (value == Iac)
                    {
                        _subnegotiation.Add(Iac);
                        _state = ParseState.Subnegotiation;
                    }
                    else
                    {
                        // Malformed subnegotiation; drop it and resume data.
                        _state = ParseState.Data;
                    }

                    break;
            }
        }
    }

    private void HandleNegotiation(byte verb, byte option)
    {
        switch (verb)
        {
            case Will:
                HandleRemoteWill(option);
                break;
            case Do:
                HandleLocalDo(option);
                break;
            case Wont:
                ClearRemote(option);
                break;
            case Dont:
                ClearLocal(option);
                break;
        }
    }

    private void HandleRemoteWill(byte option)
    {
        switch (option)
        {
            case OptionEcho:
                if (!_remoteEcho)
                {
                    _remoteEcho = true;
                    SendCommand(Do, option);
                }

                break;
            case OptionSga:
                if (!_remoteSga)
                {
                    _remoteSga = true;
                    SendCommand(Do, option);
                }

                break;
            case OptionBinary:
                if (!_remoteBinary)
                {
                    _remoteBinary = true;
                    SendCommand(Do, option);
                }

                break;
            default:
                SendCommand(Dont, option);
                break;
        }
    }

    private void HandleLocalDo(byte option)
    {
        switch (option)
        {
            case OptionTtype:
                if (!_localTtype)
                {
                    _localTtype = true;
                    SendCommand(Will, option);
                }

                break;
            case OptionNaws:
                if (!_localNaws)
                {
                    _localNaws = true;
                    SendCommand(Will, option);
                    SendNaws();
                }
                else
                {
                    SendNaws();
                }

                break;
            case OptionSga:
                if (!_localSga)
                {
                    _localSga = true;
                    SendCommand(Will, option);
                }

                break;
            case OptionBinary:
                if (!_localBinary)
                {
                    _localBinary = true;
                    SendCommand(Will, option);
                }

                break;
            default:
                SendCommand(Wont, option);
                break;
        }
    }

    private void ClearRemote(byte option)
    {
        switch (option)
        {
            case OptionEcho:
                _remoteEcho = false;
                break;
            case OptionSga:
                _remoteSga = false;
                break;
            case OptionBinary:
                _remoteBinary = false;
                break;
        }
    }

    private void ClearLocal(byte option)
    {
        switch (option)
        {
            case OptionTtype:
                _localTtype = false;
                break;
            case OptionNaws:
                _localNaws = false;
                break;
            case OptionSga:
                _localSga = false;
                break;
            case OptionBinary:
                _localBinary = false;
                break;
        }
    }

    private void HandleSubnegotiation()
    {
        if (_subnegotiation.Count >= 2
            && _subnegotiation[0] == OptionTtype
            && _subnegotiation[1] == TtypeSend)
        {
            var payload = new List<byte> { OptionTtype, TtypeIs };
            payload.AddRange(Encoding.UTF8.GetBytes(_terminalType));
            SendSubnegotiation(payload);
        }
    }

    private void SendNaws()
    {
        var columns = (ushort)Math.Clamp(_columns, 1, ushort.MaxValue);
        var rows = (ushort)Math.Clamp(_rows, 1, ushort.MaxValue);
        SendSubnegotiation(new byte[]
        {
            OptionNaws,
            (byte)(columns >> 8),
            (byte)columns,
            (byte)(rows >> 8),
            (byte)rows,
        });
    }

    private void SendSubnegotiation(IReadOnlyList<byte> payload)
    {
        var encoded = new List<byte>(payload.Count + 5) { Iac, Sb };
        foreach (var value in payload)
        {
            encoded.Add(value);
            if (value == Iac)
            {
                encoded.Add(Iac);
            }
        }

        encoded.Add(Iac);
        encoded.Add(Se);
        SendRaw(encoded.ToArray());
    }

    private void SendCommand(byte verb, byte option) =>
        SendRaw(new[] { Iac, verb, option });

    private void SendRaw(byte[] data)
    {
        lock (_writeGate)
        {
            _inner.Write(data, 0, data.Length);
            _inner.Flush();
        }
    }

    private enum ParseState
    {
        Data,
        Iac,
        Negotiation,
        Subnegotiation,
        SubnegotiationIac,
    }
}
