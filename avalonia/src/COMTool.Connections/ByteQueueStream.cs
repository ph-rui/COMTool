using System.Collections.Concurrent;

namespace COMTool.Connections;

/// <summary>
/// A blocking read stream fed by a transport receive loop. Closing the
/// producer completes the stream so a terminal read returns EOF instead of
/// spinning.
/// </summary>
internal sealed class ByteQueueStream : Stream
{
    private readonly BlockingCollection<byte[]> _chunks = new();
    private byte[]? _current;
    private int _offset;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public void Add(byte[] data)
    {
        try
        {
            if (!_chunks.IsAddingCompleted)
            {
                _chunks.Add(data);
            }
        }
        catch (InvalidOperationException)
        {
            // The transport closed while a receive loop was still delivering.
        }
    }

    public void Complete()
    {
        try
        {
            _chunks.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
            // Already closed.
        }
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        while (true)
        {
            if (_current is not null && _offset < _current.Length)
            {
                var count = Math.Min(buffer.Length, _current.Length - _offset);
                _current.AsSpan(_offset, count).CopyTo(buffer);
                _offset += count;
                return count;
            }

            _current = null;
            _offset = 0;
            try
            {
                _current = _chunks.Take();
            }
            catch (ObjectDisposedException)
            {
                return 0;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        new(Read(buffer.Span));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Complete();
        }

        base.Dispose(disposing);
    }
}
