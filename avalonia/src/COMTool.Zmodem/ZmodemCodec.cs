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

    public static byte[] ZdleEncode(ReadOnlySpan<byte> data)
    {
        var output = new List<byte>(data.Length + 8);
        foreach (var value in data)
        {
            if (value is Zdle or Xon or Xoff or XonHigh or XoffHigh or 0x10 or 0x90)
            {
                output.Add(Zdle);
                output.Add((byte)(value ^ 0x40));
            }
            else
            {
                output.Add(value);
            }
        }

        return output.ToArray();
    }

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
