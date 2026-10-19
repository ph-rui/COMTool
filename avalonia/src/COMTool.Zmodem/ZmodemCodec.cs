namespace COMTool.Zmodem;

public enum HeaderParseStatus
{
    Ok,
    NeedMore,
    Invalid,
}

public sealed record ZHeader(
    int FrameType,
    byte[] Data,
    int CrcLength,
    byte[] Raw);

public readonly record struct HeaderParseResult(
    HeaderParseStatus Status,
    ZHeader? Header = null,
    int Consumed = 0);

public static class ZmodemCodec
{
    public const int FrameZRQInit = 0;
    public const int FrameZRInit = 1;
    public const int FrameZSInit = 2;
    public const int FrameZAck = 3;
    public const int FrameZFile = 4;
    public const int FrameZSkip = 5;
    public const int FrameZNak = 6;
    public const int FrameZAbort = 7;
    public const int FrameZFin = 8;
    public const int FrameZRPos = 9;
    public const int FrameZData = 10;
    public const int FrameZEof = 11;
    public const int FrameZFerr = 12;
    public const int FrameZCrc = 13;
    public const int FrameZChallenge = 14;
    public const int FrameZCompl = 15;
    public const int FrameZCan = 16;
    public const int FrameZFreeCnt = 17;
    public const int FrameZCommand = 18;
    public const int FrameZStdErr = 19;

    public const byte SubpacketZcrce = 0x68;
    public const byte SubpacketZcrcg = 0x69;
    public const byte SubpacketZcrcq = 0x6A;
    public const byte SubpacketZcrcw = 0x6B;

    public const byte ZrinitCanFdx = 0x01;
    public const byte ZrinitCanOvIo = 0x02;
    public const byte ZrinitCanBrk = 0x04;
    public const byte ZrinitCanCry = 0x08;
    public const byte ZrinitCanLzw = 0x10;
    public const byte ZrinitCanFc32 = 0x20;
    public const byte ZrinitEscCtl = 0x40;
    public const byte ZrinitEsc8 = 0x80;
    public const byte Zpad = 0x2A;
    public const byte Zdle = 0x18;
    public const byte Zbin = 0x41;
    public const byte Zhex = 0x42;
    public const byte Zbin32 = 0x43;
    public const byte Xon = 0x11;
    public const byte Xoff = 0x13;
    public const byte XonHigh = 0x91;
    public const byte XoffHigh = 0x93;

    private static readonly byte[] HexPrefix = { Zpad, Zpad, Zdle, Zhex };
    private static readonly byte[] Bin16Prefix = { Zpad, Zdle, Zbin };
    private static readonly byte[] Bin32Prefix = { Zpad, Zdle, Zbin32 };

    public static HeaderParseResult ParseHeaderAt(ReadOnlySpan<byte> data, int offset = 0)
    {
        if (offset < 0 || offset >= data.Length)
        {
            return new HeaderParseResult(HeaderParseStatus.NeedMore);
        }

        if (data[offset..].StartsWith(HexPrefix))
        {
            return ParseHexHeader(data, offset);
        }

        if (data[offset..].StartsWith(Bin32Prefix))
        {
            return ParseBinaryHeader(data, offset, 32);
        }

        if (data[offset..].StartsWith(Bin16Prefix))
        {
            return ParseBinaryHeader(data, offset, 16);
        }

        foreach (var prefix in new[] { HexPrefix, Bin16Prefix, Bin32Prefix })
        {
            if (IsPrefixOf(data[offset..], prefix))
            {
                return new HeaderParseResult(HeaderParseStatus.NeedMore);
            }
        }

        return new HeaderParseResult(HeaderParseStatus.Invalid);
    }

    public static byte[] EncodeHexHeader(int frameType, byte[]? data = null)
    {
        data ??= new byte[4];
        if (data.Length != 4)
        {
            throw new ArgumentException("header data must be 4 bytes", nameof(data));
        }

        var body = new byte[5];
        body[0] = (byte)frameType;
        data.CopyTo(body, 1);
        var crc = ZmodemCrc.PackCrc16(ZmodemCrc.Crc16(body));
        var digits = Convert.ToHexString(body.Concat(crc).ToArray()).ToLowerInvariant();
        return HexPrefix
            .Concat(System.Text.Encoding.ASCII.GetBytes(digits))
            .Concat(new byte[] { 0x0D, 0x0A, Xon })
            .ToArray();
    }

    public static byte[] EncodeBinaryHeader(int frameType, byte[]? data = null, int crcLength = 16)
    {
        data ??= new byte[4];
        if (data.Length != 4)
        {
            throw new ArgumentException("header data must be 4 bytes", nameof(data));
        }

        var body = new byte[5];
        body[0] = (byte)frameType;
        data.CopyTo(body, 1);
        var crc = crcLength == 32
            ? ZmodemCrc.PackCrc32(ZmodemCrc.Crc32(body))
            : ZmodemCrc.PackCrc16(ZmodemCrc.Crc16(body));
        return (crcLength == 32 ? Bin32Prefix : Bin16Prefix)
            .Concat(ZdleEncode(body.Concat(crc).ToArray()))
            .ToArray();
    }

    public static byte[] ZdleEncode(ReadOnlySpan<byte> data) =>
        EncodeZdle(data, escapeControl: false, escape8Bit: false);

    public static byte[] EncodeZdle(
        ReadOnlySpan<byte> data,
        bool escapeControl,
        bool escape8Bit)
    {
        var output = new List<byte>(data.Length + 8);
        byte previous = 0;
        foreach (var value in data)
        {
            if (ShouldEscape(value, escapeControl, escape8Bit, previous))
            {
                output.Add(Zdle);
                output.Add((byte)(value ^ 0x40));
            }
            else
            {
                output.Add(value);
            }

            previous = value;
        }

        return output.ToArray();
    }

    public static byte[] DecodeZdle(ReadOnlySpan<byte> data, bool ignoreXonOff = false)
    {
        var output = new List<byte>(data.Length);
        for (var index = 0; index < data.Length; index++)
        {
            var value = data[index];
            if (value == Zdle)
            {
                if (index + 1 >= data.Length)
                {
                    throw new ZmodemProtocolException("trailing ZDLE");
                }

                output.Add(DecodeZdleByte(data[++index]));
            }
            else if (ignoreXonOff && value is Xon or Xoff or XonHigh or XoffHigh)
            {
                continue;
            }
            else
            {
                output.Add(value);
            }
        }

        return output.ToArray();
    }

    public static byte[] EncodeSubpacket(
        ReadOnlySpan<byte> payload,
        byte frameEnd,
        int crcLength = 16)
    {
        if (frameEnd is not (SubpacketZcrce or SubpacketZcrcg or SubpacketZcrcq or SubpacketZcrcw))
        {
            throw new ArgumentOutOfRangeException(nameof(frameEnd));
        }

        if (crcLength is not (16 or 32))
        {
            throw new ArgumentOutOfRangeException(nameof(crcLength));
        }

        var body = new byte[payload.Length + 1];
        payload.CopyTo(body);
        body[^1] = frameEnd;
        var crc = crcLength == 32
            ? ZmodemCrc.PackCrc32(ZmodemCrc.Crc32(body))
            : ZmodemCrc.PackCrc16(ZmodemCrc.Crc16(body));
        return EncodeZdle(payload, escapeControl: false, escape8Bit: false)
            .Concat(new[] { Zdle, frameEnd })
            .Concat(EncodeZdle(crc, escapeControl: false, escape8Bit: false))
            .ToArray();
    }

    /// <summary>
    /// Parses one data subpacket at the start of <paramref name="data"/>.
    /// Returns null when more bytes are needed and throws when the CRC is bad.
    /// </summary>
    public static (ZSubpacket Packet, int Consumed)? ParseSubpacket(
        ReadOnlySpan<byte> data,
        int crcLength = 16)
    {
        if (crcLength is not (16 or 32))
        {
            throw new ArgumentOutOfRangeException(nameof(crcLength));
        }

        var delimiter = -1;
        for (var index = 0; index < data.Length;)
        {
            var value = data[index];
            if (value == Zdle)
            {
                if (index + 1 >= data.Length)
                {
                    return null;
                }

                if (IsSubpacketEnd(data[index + 1]))
                {
                    delimiter = index;
                    break;
                }

                index += 2;
            }
            else
            {
                index++;
            }
        }

        if (delimiter < 0)
        {
            return null;
        }

        var frameEnd = data[delimiter + 1];
        var payload = DecodeZdle(data[..delimiter], ignoreXonOff: true);
        var decodedCrc = DecodeExactly(data[(delimiter + 2)..], crcLength / 8);
        if (decodedCrc is null)
        {
            return null;
        }

        var (crcBytes, encodedCrcLength) = decodedCrc.Value;
        var body = new byte[payload.Length + 1];
        payload.CopyTo(body, 0);
        body[^1] = frameEnd;
        var expected = crcLength == 32
            ? ZmodemCrc.Crc32(body)
            : ZmodemCrc.Crc16(body);
        var got = crcLength == 32
            ? ZmodemCrc.UnpackCrc32(crcBytes)
            : ZmodemCrc.UnpackCrc16(crcBytes);
        if (expected != got)
        {
            throw new ZmodemProtocolException("bad subpacket CRC");
        }

        var consumed = delimiter + 2 + encodedCrcLength;
        return (new ZSubpacket(frameEnd, payload, crcLength, consumed), consumed);
    }

    public static byte[] PackOffset(long offset)
    {
        if (offset is < 0 or > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return new[]
        {
            (byte)offset,
            (byte)(offset >> 8),
            (byte)(offset >> 16),
            (byte)(offset >> 24),
        };
    }

    public static long UnpackOffset(ReadOnlySpan<byte> data)
    {
        if (data.Length != 4)
        {
            throw new ArgumentException("offset must be 4 bytes", nameof(data));
        }

        return (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
    }

    public static byte[] StripIgnoredBytes(ReadOnlySpan<byte> data) =>
        data.ToArray()
            .Where(value => value is not (Xon or Xoff or XonHigh or XoffHigh))
            .ToArray();

    private static bool ShouldEscape(
        byte value,
        bool escapeControl,
        bool escape8Bit,
        byte previous)
    {
        if (value is Zdle or Xon or Xoff or XonHigh or XoffHigh or 0x10 or 0x90)
        {
            return true;
        }

        if (escapeControl && (value & 0x7F) < 0x20)
        {
            return true;
        }

        if (escape8Bit && (value & 0x80) != 0)
        {
            return true;
        }

        return value is 0x0D or 0x8D && (previous & 0x7F) == 0x40;
    }

    private static byte DecodeZdleByte(byte value) => value switch
    {
        0x6C => 0x7F,
        0x6D => 0xFF,
        _ => (byte)(value ^ 0x40),
    };

    private static bool IsSubpacketEnd(byte value) =>
        value is SubpacketZcrce or SubpacketZcrcg or SubpacketZcrcq or SubpacketZcrcw;

    private static HeaderParseResult ParseHexHeader(ReadOnlySpan<byte> data, int offset)
    {
        var start = offset + HexPrefix.Length;
        var hexEnd = start + 14;
        if (data.Length < hexEnd)
        {
            return new HeaderParseResult(HeaderParseStatus.NeedMore);
        }

        byte[] rawBody;
        try
        {
            rawBody = Convert.FromHexString(
                System.Text.Encoding.ASCII.GetString(data[start..hexEnd]));
        }
        catch (FormatException)
        {
            return new HeaderParseResult(HeaderParseStatus.Invalid);
        }

        if (rawBody.Length != 7)
        {
            return new HeaderParseResult(HeaderParseStatus.Invalid);
        }

        var index = hexEnd;
        if (index >= data.Length)
        {
            return new HeaderParseResult(HeaderParseStatus.NeedMore);
        }

        if (data[index] == 0x0D)
        {
            if (index + 1 >= data.Length)
            {
                return new HeaderParseResult(HeaderParseStatus.NeedMore);
            }

            if (data[index + 1] is not (0x0A or 0x8A))
            {
                return new HeaderParseResult(HeaderParseStatus.Invalid);
            }

            index += 2;
        }
        else if (data[index] == 0x0A)
        {
            index++;
        }
        else
        {
            return new HeaderParseResult(HeaderParseStatus.Invalid);
        }

        if (index < data.Length && data[index] == Xon)
        {
            index++;
        }

        var body = rawBody.AsSpan(0, 5);
        if (ZmodemCrc.Crc16(body) != ZmodemCrc.UnpackCrc16(rawBody.AsSpan(5, 2)))
        {
            return new HeaderParseResult(HeaderParseStatus.Invalid);
        }

        return new HeaderParseResult(
            HeaderParseStatus.Ok,
            new ZHeader(rawBody[0], rawBody[1..5], 16, data[offset..index].ToArray()),
            index - offset);
    }

    private static HeaderParseResult ParseBinaryHeader(
        ReadOnlySpan<byte> data,
        int offset,
        int crcLength)
    {
        var prefix = crcLength == 32 ? Bin32Prefix : Bin16Prefix;
        var start = offset + prefix.Length;
        var decoded = DecodeExactly(data[start..], 5 + (crcLength / 8));
        if (decoded is null)
        {
            return new HeaderParseResult(HeaderParseStatus.NeedMore);
        }

        var (bodyAndCrc, encodedLength) = decoded.Value;
        var body = bodyAndCrc.AsSpan(0, 5);
        var expected = crcLength == 32
            ? ZmodemCrc.Crc32(body)
            : ZmodemCrc.Crc16(body);
        var got = crcLength == 32
            ? ZmodemCrc.UnpackCrc32(bodyAndCrc.AsSpan(5, 4))
            : ZmodemCrc.UnpackCrc16(bodyAndCrc.AsSpan(5, 2));
        if (expected != got)
        {
            return new HeaderParseResult(HeaderParseStatus.Invalid);
        }

        var consumed = prefix.Length + encodedLength;
        return new HeaderParseResult(
            HeaderParseStatus.Ok,
            new ZHeader(bodyAndCrc[0], bodyAndCrc[1..5], crcLength, data[offset..(offset + consumed)].ToArray()),
            consumed);
    }

    private static (byte[] Decoded, int EncodedLength)? DecodeExactly(
        ReadOnlySpan<byte> data,
        int count)
    {
        var output = new byte[count];
        var written = 0;
        var index = 0;
        while (written < count)
        {
            if (index >= data.Length)
            {
                return null;
            }

            var value = data[index++];
            if (value == Zdle)
            {
                if (index >= data.Length)
                {
                    return null;
                }

                var next = data[index++];
                output[written++] = next switch
                {
                    0x6C => 0x7F,
                    0x6D => 0xFF,
                    _ => (byte)(next ^ 0x40),
                };
            }
            else if (value is Xon or Xoff or XonHigh or XoffHigh)
            {
                continue;
            }
            else
            {
                output[written++] = value;
            }
        }

        return (output, index);
    }

    private static bool IsPrefixOf(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix)
    {
        if (data.Length > prefix.Length)
        {
            return false;
        }

        return prefix[..data.Length].SequenceEqual(data);
    }
}

public sealed record ZSubpacket(
    byte FrameEnd,
    byte[] Payload,
    int CrcLength,
    int RawLength)
{
    public bool FrameEndFrame =>
        FrameEnd is ZmodemCodec.SubpacketZcrce or ZmodemCodec.SubpacketZcrcw;

    public bool AckExpected =>
        FrameEnd is ZmodemCodec.SubpacketZcrcq or ZmodemCodec.SubpacketZcrcw;
}

public sealed class ZmodemProtocolException : Exception
{
    public ZmodemProtocolException(string message) : base(message)
    {
    }
}

public sealed class ZmodemTimeoutException : Exception
{
    public ZmodemTimeoutException(string message) : base(message)
    {
    }
}

public sealed class ZmodemAbortException : Exception
{
    public ZmodemAbortException(string message) : base(message)
    {
    }
}
