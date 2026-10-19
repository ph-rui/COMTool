namespace COMTool.Zmodem;

/// <summary>
/// Blocking byte queue used by the ZMODEM engines. A transport pump feeds
/// received bytes in and the engine writes through the callback. This mirrors
/// the Python implementation and keeps the protocol code independent of the
/// serial/SSH stream implementation.
/// </summary>
public sealed class ZmodemIo : IDisposable
{
    private readonly object _gate = new();
    private readonly List<byte> _buffer = new();
    private readonly Action<byte[]> _write;
    private int _offset;
    private bool _cancelled;
    private bool _disposed;

    public ZmodemIo(Action<byte[]> write)
    {
        _write = write ?? throw new ArgumentNullException(nameof(write));
    }

    public bool IsCancelled
    {
        get
        {
            lock (_gate)
            {
                return _cancelled;
            }
        }
    }

    public void Feed(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_cancelled || _disposed)
            {
                return;
            }

            _buffer.AddRange(data.ToArray());
            Monitor.PulseAll(_gate);
        }
    }

    public byte[] ReadSome(int size, TimeSpan? timeout = null)
    {
        if (size <= 0)
        {
            return Array.Empty<byte>();
        }

        lock (_gate)
        {
            WaitForData(size, timeout, exactly: false);
            var count = Math.Min(size, _buffer.Count - _offset);
            var data = new byte[count];
            _buffer.CopyTo(_offset, data, 0, count);
            _offset += count;
            Compact();
            return data;
        }
    }

    public byte[] ReadExactly(int size, TimeSpan? timeout = null)
    {
        if (size <= 0)
        {
            return Array.Empty<byte>();
        }

        lock (_gate)
        {
            WaitForData(size, timeout, exactly: true);
            var data = new byte[size];
            _buffer.CopyTo(_offset, data, 0, size);
            _offset += size;
            Compact();
            return data;
        }
    }

    public byte[] Drain()
    {
        lock (_gate)
        {
            var count = _buffer.Count - _offset;
            var data = new byte[count];
            if (count > 0)
            {
                _buffer.CopyTo(_offset, data, 0, count);
            }

            _buffer.Clear();
            _offset = 0;
            return data;
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            ThrowIfCancelled();
        }

        _write(data.ToArray());
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _cancelled = true;
            Monitor.PulseAll(_gate);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _cancelled = true;
            Monitor.PulseAll(_gate);
        }
    }

    private void WaitForData(int size, TimeSpan? timeout, bool exactly)
    {
        var deadline = timeout is null ? (DateTime?)null : DateTime.UtcNow + timeout.Value;
        while (true)
        {
            ThrowIfCancelled();
            var available = _buffer.Count - _offset;
            if (exactly ? available >= size : available > 0)
            {
                return;
            }

            if (deadline is null)
            {
                Monitor.Wait(_gate);
                continue;
            }

            var remaining = deadline.Value - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw new ZmodemTimeoutException("timed out waiting for ZMODEM data");
            }

            Monitor.Wait(_gate, ToMilliseconds(remaining));
        }
    }

    private void ThrowIfCancelled()
    {
        if (_cancelled)
        {
            throw new ZmodemAbortException("ZMODEM transfer cancelled");
        }
    }

    private void Compact()
    {
        if (_offset == 0)
        {
            return;
        }

        if (_offset >= _buffer.Count)
        {
            _buffer.Clear();
            _offset = 0;
            return;
        }

        if (_offset >= 8192)
        {
            _buffer.RemoveRange(0, _offset);
            _offset = 0;
        }
    }

    private static int ToMilliseconds(TimeSpan value) =>
        value.TotalMilliseconds >= int.MaxValue
            ? int.MaxValue
            : Math.Max(1, (int)Math.Ceiling(value.TotalMilliseconds));
}
