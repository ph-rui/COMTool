namespace COMTool.Zmodem;

public sealed record ZmodemDetection(
    string Direction,
    ZHeader Header,
    byte[] Remainder);

public sealed record ZmodemDetectionResult(
    byte[] TerminalData,
    ZmodemDetection? Detection = null);

/// <summary>
/// Scans terminal bytes for the start of a ZMODEM session. Bytes that cannot
/// be part of a header are passed to the terminal immediately; a potential
/// header at the end of a chunk is held until the next chunk arrives.
/// </summary>
public sealed class ZmodemDetector
{
    private byte[] _pending = Array.Empty<byte>();

    public void Reset() => _pending = Array.Empty<byte>();

    public ZmodemDetectionResult Feed(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return new ZmodemDetectionResult(Array.Empty<byte>());
        }

        var buffer = new byte[_pending.Length + data.Length];
        _pending.CopyTo(buffer, 0);
        data.CopyTo(buffer.AsSpan(_pending.Length));
        _pending = Array.Empty<byte>();

        var output = new List<byte>(buffer.Length);
        var index = 0;
        while (index < buffer.Length)
        {
            var star = Array.IndexOf(buffer, ZmodemCodec.Zpad, index);
            if (star < 0)
            {
                output.AddRange(buffer.AsSpan(index).ToArray());
                break;
            }

            output.AddRange(buffer.AsSpan(index, star - index).ToArray());
            var result = ZmodemCodec.ParseHeaderAt(buffer, star);
            if (result.Status == HeaderParseStatus.Ok && result.Header is not null)
            {
                if (result.Header.FrameType is ZmodemCodec.FrameZRQInit or ZmodemCodec.FrameZRInit)
                {
                    var direction = result.Header.FrameType == ZmodemCodec.FrameZRQInit
                        ? "receive"
                        : "send";
                    var remainderStart = star + result.Consumed;
                    var remainder = buffer[remainderStart..];
                    return new ZmodemDetectionResult(
                        output.ToArray(),
                        new ZmodemDetection(direction, result.Header, remainder));
                }

                output.AddRange(buffer.AsSpan(star, result.Consumed).ToArray());
                index = star + result.Consumed;
                continue;
            }

            if (result.Status == HeaderParseStatus.NeedMore)
            {
                _pending = buffer[star..];
                return new ZmodemDetectionResult(output.ToArray());
            }

            output.Add(buffer[star]);
            index = star + 1;
        }

        return new ZmodemDetectionResult(output.ToArray());
    }
}
