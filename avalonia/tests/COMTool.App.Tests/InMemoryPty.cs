using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Porta.Pty;

namespace COMTool.App.Tests;

/// <summary>
/// An <see cref="IPtyConnection"/> that keeps everything in memory so terminal
/// controls can be exercised without starting an external shell.
/// </summary>
internal sealed class InMemoryPty : IPtyConnection
{
    private readonly BlockingCollection<byte> _incoming = new();
    private readonly MemoryStream _written = new();
    private readonly ManualResetEventSlim _closedEvent = new(false);
    private readonly object _writeGate = new();
    private int _closed;

    public InMemoryPty()
    {
        ReaderStream = new IncomingStream(this);
        WriterStream = new OutgoingStream(this);
    }

    public Stream ReaderStream { get; }

    public Stream WriterStream { get; }

    public bool SupportsCancellableRead => false;

    public int Pid => 0;

    public int ExitCode => 0;

    /// <summary>
    /// Mirrors the production stream connection: input is still "open" while
    /// the terminal may read from it.
    /// </summary>
    public bool IsInputCompleted => IsClosed;

    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    public event EventHandler<PtyExitedEventArgs>? ProcessExited;

    /// <summary>
    /// Everything the terminal has written, decoded as UTF-8.
    /// </summary>
    public string WriterText
    {
        get
        {
            lock (_writeGate)
            {
                return Encoding.UTF8.GetString(_written.ToArray());
            }
        }
    }

    /// <summary>
    /// Delivers terminal output to the control's reader.
    /// </summary>
    public void Send(string text)
    {
        if (IsClosed)
        {
            return;
        }

        foreach (var value in Encoding.UTF8.GetBytes(text))
        {
            try
            {
                _incoming.Add(value);
            }
            catch (InvalidOperationException)
            {
                // The reader was closed between the IsClosed check and Add.
                return;
            }
        }
    }

    public bool WaitForExit(int milliseconds)
    {
        if (IsClosed)
        {
            return true;
        }

        return _closedEvent.Wait(milliseconds);
    }

    public void Resize(int columns, int rows)
    {
        // The in-memory terminal has no window to resize.
    }

    public void Kill() => Close();

    public void Dispose() => Close();

    /// <summary>
    /// Completes the reader so its pending read returns 0 and raises
    /// <see cref="ProcessExited"/> exactly once.
    /// </summary>
    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        try
        {
            _incoming.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by a test cleanup path.
        }

        _closedEvent.Set();
        var eventArgs =
            (PtyExitedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(PtyExitedEventArgs));
        eventArgs.ExitCode = 0;
        ProcessExited?.Invoke(this, eventArgs);
    }

    private void RecordWrite(ReadOnlySpan<byte> buffer)
    {
        lock (_writeGate)
        {
            _written.Write(buffer);
        }
    }

    private int ReadIncoming(Span<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        if (!TryTake(out var value))
        {
            return 0;
        }

        buffer[0] = value;
        var read = 1;
        while (read < buffer.Length && _incoming.TryTake(out value))
        {
            buffer[read++] = value;
        }

        return read;
    }

    private bool TryTake(out byte value)
    {
        try
        {
            return _incoming.TryTake(out value, Timeout.Infinite);
        }
        catch (InvalidOperationException)
        {
            // CompleteAdding was called and the queue is empty.
            value = 0;
            return false;
        }
    }

    private sealed class IncomingStream(InMemoryPty owner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            owner.ReadIncoming(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer) => owner.ReadIncoming(buffer);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            new(Task.Run(() => owner.ReadIncoming(buffer.Span), cancellationToken));

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            Task.Run(() => owner.ReadIncoming(buffer.AsSpan(offset, count)), cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class OutgoingStream(InMemoryPty owner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            owner.RecordWrite(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer) => owner.RecordWrite(buffer);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            owner.RecordWrite(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            owner.RecordWrite(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
