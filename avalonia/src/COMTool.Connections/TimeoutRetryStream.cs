namespace COMTool.Connections;

/// <summary>
/// SerialPort's BaseStream throws TimeoutException when ReadTimeout expires.
/// A terminal read loop treats that as a failed read, so idle serial ports can
/// stop updating. This wrapper retries timeouts until data arrives or the
/// underlying port is closed, which is the behavior of the Python serial
/// reader.
/// </summary>
internal sealed class TimeoutRetryStream : Stream
{
    private readonly Stream _inner;

    public TimeoutRetryStream(Stream inner)
    {
        _inner = inner;
    }

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => _inner.CanSeek;

    public override bool CanWrite => _inner.CanWrite;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        while (true)
        {
            try
            {
                return _inner.Read(buffer, offset, count);
            }
            catch (TimeoutException)
            {
                // Keep waiting; Close() will surface as IOException or
                // ObjectDisposedException on the next read.
            }
        }
    }

    public override int Read(Span<byte> buffer)
    {
        while (true)
        {
            try
            {
                return _inner.Read(buffer);
            }
            catch (TimeoutException)
            {
            }
        }
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            try
            {
                return await _inner.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        _inner.Seek(offset, origin);

    public override void SetLength(long value) => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) =>
        _inner.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) =>
        _inner.Write(buffer);

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        _inner.WriteAsync(buffer, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
