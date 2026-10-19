namespace COMTool.Zmodem;

/// <summary>
/// Buffered ZMODEM header/subpacket reader. The buffer only contains bytes
/// not yet consumed by the protocol, which lets the caller hand any trailing
/// bytes back to the terminal after a transfer.
/// </summary>
public sealed class ZmodemReader
{
    private readonly List<byte> _buffer = new();

    public ZmodemReader(ZmodemIo io, TimeSpan? readTimeout = null)
    {
        Io = io;
        ReadTimeout = readTimeout ?? TimeSpan.FromSeconds(30);
    }

    public ZmodemIo Io { get; }

    public TimeSpan ReadTimeout { get; set; }

    public ZHeader ReadHeader(TimeSpan? timeout = null)
    {
        while (true)
        {
            if (_buffer.Count == 0)
            {
                ReadMore(timeout);
                continue;
            }

            CheckCancel();

            var star = _buffer.IndexOf(ZmodemCodec.Zpad);
            if (star < 0)
            {
                KeepTail(7);
                ReadMore(timeout);
                continue;
            }

            if (star > 0)
            {
                _buffer.RemoveRange(0, star);
            }

            var result = ZmodemCodec.ParseHeaderAt(
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer));
            if (result.Status == HeaderParseStatus.Ok && result.Header is not null)
            {
                _buffer.RemoveRange(0, result.Consumed);
                return result.Header;
            }

            if (result.Status == HeaderParseStatus.NeedMore)
            {
                ReadMore(timeout);
                continue;
            }

            _buffer.RemoveAt(0);
        }
    }

    public ZSubpacket ReadSubpacket(int crcLength)
    {
        while (true)
        {
            if (_buffer.Count == 0)
            {
                ReadMore();
                continue;
            }

            CheckCancel();
            var parsed = ZmodemCodec.ParseSubpacket(
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer),
                crcLength);
            if (parsed is not null)
            {
                _buffer.RemoveRange(0, parsed.Value.Consumed);
                return parsed.Value.Packet;
            }

            ReadMore();
        }
    }

    public byte[] ReadRaw(int count)
    {
        EnsureBuffered(count);
        var data = _buffer.GetRange(0, count).ToArray();
        _buffer.RemoveRange(0, count);
        return data;
    }

    public void Prepend(ReadOnlySpan<byte> data)
    {
        if (data.Length > 0)
        {
            _buffer.InsertRange(0, data.ToArray());
        }
    }

    public void ClearBuffer() => _buffer.Clear();

    public byte[] Drain()
    {
        var data = _buffer.ToArray();
        _buffer.Clear();
        return data.Concat(Io.Drain()).ToArray();
    }

    private void EnsureBuffered(int count)
    {
        while (_buffer.Count < count)
        {
            ReadMore();
        }
    }

    private void ReadMore(TimeSpan? timeout = null)
    {
        var data = Io.ReadSome(8192, timeout ?? ReadTimeout);
        if (data.Length == 0)
        {
            throw new EndOfStreamException("ZMODEM stream closed");
        }

        _buffer.AddRange(data);
    }

    private void KeepTail(int count)
    {
        if (_buffer.Count > count)
        {
            _buffer.RemoveRange(0, _buffer.Count - count);
        }
    }

    private void CheckCancel()
    {
        var run = 0;
        foreach (var value in _buffer)
        {
            if (value == ZmodemCodec.Zdle)
            {
                run++;
                if (run >= 8)
                {
                    throw new ZmodemAbortException("peer cancelled the transfer");
                }
            }
            else
            {
                run = 0;
            }
        }
    }
}
