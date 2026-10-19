namespace COMTool.Zmodem;

public sealed record ZmodemSendRequest(ZHeader Header);

public sealed record ZmodemTransferResult(
    bool IsSend,
    IReadOnlyList<ZmodemReceivedFile> Files,
    string? TempDirectory,
    byte[] Trailing);

public sealed record ZmodemProgressInfo(
    string Name,
    long Sent,
    long? Total,
    bool IsSend);

/// <summary>
/// Stream wrapper that watches the terminal byte stream for ZMODEM headers
/// and switches the connection into a transfer session. Terminal bytes are
/// queued normally; during a transfer incoming bytes are routed to the
/// protocol engine, and the shell prompt/trailing bytes are released to the
/// terminal when the transfer finishes.
/// </summary>
public sealed class ZmodemStream : Stream
{
    private readonly Stream _inner;
    private readonly Action<byte[]> _write;
    private readonly bool _ownsInner;
    private readonly object _gate = new();
    private readonly List<byte> _terminal = new();
    private readonly ZmodemDetector _detector = new();
    private readonly MobaKeywordHighlighter _highlighter = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _pump;
    private ZmodemIo? _sessionIo;
    private ZHeader? _pendingSendHeader;
    private byte[] _pendingSendRemainder = Array.Empty<byte>();
    private bool _sessionActive;
    private bool _completed;
    private bool _disposed;

    public ZmodemStream(Stream inner, Action<byte[]> write, bool ownsInner = false)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _ownsInner = ownsInner;
        _pump = Task.Factory.StartNew(
            Pump,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public event Action<ZmodemSendRequest>? SendRequested;

    public event Action<ZmodemTransferResult>? TransferCompleted;

    public event Action<string>? TransferFailed;

    public event Action<ZmodemProgressInfo>? Progress;

    public bool IsTransferActive
    {
        get
        {
            lock (_gate)
            {
                return _sessionActive;
            }
        }
    }

    public void StartSend(IEnumerable<string> files)
    {
        ZHeader? header;
        byte[] remainder;
        lock (_gate)
        {
            header = _pendingSendHeader;
            remainder = _pendingSendRemainder;
            _pendingSendHeader = null;
            _pendingSendRemainder = Array.Empty<byte>();
            if (header is null)
            {
                return;
            }

            _sessionActive = true;
            _sessionIo = new ZmodemIo(WriteProtocol);
        }

        var io = _sessionIo!;
        var sender = new ZmodemSender(
            io,
            files,
            initialZrinit: header,
            onFileStart: (name, size) => ReportProgress(name, 0, size, isSend: true),
            onProgress: (name, sent, total) => ReportProgress(name, sent, total, isSend: true),
            onFileComplete: name => ReportProgress(name, 0, null, isSend: true));
        _ = Task.Run(() =>
        {
            try
            {
                io.Feed(remainder);
                sender.Run();
                FinishTransfer(new ZmodemTransferResult(
                    true,
                    Array.Empty<ZmodemReceivedFile>(),
                    null,
                    sender.Trailing));
            }
            catch (Exception ex)
            {
                FailTransfer(ex);
            }
        });
    }

    public void CancelSend()
    {
        lock (_gate)
        {
            if (_pendingSendHeader is null)
            {
                return;
            }

            _pendingSendHeader = null;
            _pendingSendRemainder = Array.Empty<byte>();
        }

        _write(CancelSequence());
    }

    public void CancelTransfer()
    {
        ZmodemIo? io;
        lock (_gate)
        {
            io = _sessionIo;
        }

        io?.Cancel();
        _write(CancelSequence());
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

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        lock (_gate)
        {
            while (_terminal.Count == 0 && !_completed && !_disposed)
            {
                Monitor.Wait(_gate);
            }

            if (_terminal.Count == 0)
            {
                return 0;
            }

            var taken = Math.Min(count, _terminal.Count);
            _terminal.CopyTo(0, buffer, offset, taken);
            _terminal.RemoveRange(0, taken);
            return taken;
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (count <= 0)
        {
            return;
        }

        // Frontend keystrokes are not part of the ZMODEM byte stream. Ignore
        // them while the engine is driving the connection.
        lock (_gate)
        {
            if (_sessionActive)
            {
                return;
            }
        }

        _write(buffer.AsSpan(offset, count).ToArray());
    }

    public override void Flush() => _inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var cancelRemote = false;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                cancelRemote = _sessionActive || _pendingSendHeader is not null;
                _sessionIo?.Cancel();
                Monitor.PulseAll(_gate);
            }

            _cancellation.Cancel();
            if (cancelRemote)
            {
                _write(CancelSequence());
            }
            if (_ownsInner)
            {
                _inner.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private void Pump()
    {
        var buffer = new byte[8192];
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                var read = _inner.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                HandleReceived(buffer.AsSpan(0, read));
            }
        }
        catch
        {
            // A closed transport is the normal end of the pump.
        }
        finally
        {
            lock (_gate)
            {
                _completed = true;
                Monitor.PulseAll(_gate);
            }
        }
    }

    private void HandleReceived(ReadOnlySpan<byte> data)
    {
        ZmodemIo? activeIo;
        lock (_gate)
        {
            activeIo = _sessionActive ? _sessionIo : null;
            if (_sessionActive)
            {
                activeIo?.Feed(data);
                return;
            }

            if (_pendingSendHeader is not null)
            {
                // The remote repeats ZRINIT while the file picker is open.
                return;
            }
        }

        var result = _detector.Feed(data);
        if (result.TerminalData.Length > 0)
        {
            var terminalData = _highlighter.Transform(result.TerminalData);
            lock (_gate)
            {
                _terminal.AddRange(terminalData);
                Monitor.PulseAll(_gate);
            }
        }

        if (result.Detection is { } detection)
        {
            if (detection.Direction == "receive")
            {
                StartReceive(detection);
            }
            else
            {
                StartPendingSend(detection);
            }
        }
    }

    private void StartReceive(ZmodemDetection detection)
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "comtool-zmodem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var io = new ZmodemIo(WriteProtocol);
        var receiver = new ZmodemReceiver(
            io,
            tempDirectory,
            onFileStart: (meta, path) =>
                ReportProgress(meta.Name, 0, meta.Size, isSend: false),
            onProgress: (name, sent, total) =>
                ReportProgress(name, sent, total, isSend: false));

        lock (_gate)
        {
            _sessionIo = io;
            _sessionActive = true;
            _detector.Reset();
        }

        _ = Task.Run(() =>
        {
            try
            {
                io.Feed(detection.Remainder);
                receiver.Run();
                FinishTransfer(new ZmodemTransferResult(
                    false,
                    receiver.Files,
                    tempDirectory,
                    receiver.Trailing));
            }
            catch (Exception ex)
            {
                TryDeleteDirectory(tempDirectory);
                FailTransfer(ex);
            }
        });
    }

    private void StartPendingSend(ZmodemDetection detection)
    {
        lock (_gate)
        {
            _pendingSendHeader = detection.Header;
            _pendingSendRemainder = detection.Remainder;
            _detector.Reset();
        }

        SendRequested?.Invoke(new ZmodemSendRequest(detection.Header));
    }

    private void FinishTransfer(ZmodemTransferResult result)
    {
        lock (_gate)
        {
            _sessionActive = false;
            _sessionIo = null;
            if (result.Trailing.Length > 0)
            {
                _terminal.AddRange(_highlighter.Transform(result.Trailing.TakeLast(4096).ToArray()));
                Monitor.PulseAll(_gate);
            }
        }

        TransferCompleted?.Invoke(result);
    }

    private void FailTransfer(Exception exception)
    {
        lock (_gate)
        {
            _sessionActive = false;
            _sessionIo = null;
            _pendingSendHeader = null;
            _pendingSendRemainder = Array.Empty<byte>();
        }

        TransferFailed?.Invoke(exception.Message);
    }

    private void ReportProgress(string name, long sent, long? total, bool isSend) =>
        Progress?.Invoke(new ZmodemProgressInfo(name, sent, total, isSend));

    private void WriteProtocol(byte[] data) => _write(data);

    private static byte[] CancelSequence() =>
        Enumerable.Repeat(ZmodemCodec.Zdle, 8)
            .Concat(Enumerable.Repeat((byte)0x08, 8))
            .ToArray();

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }
}

/// <summary>
/// Writer proxy for TerminalControl. It suppresses frontend keystrokes while
/// a transfer owns the byte stream, but flushes/disposes the real transport.
/// </summary>
public sealed class ZmodemWriterStream : Stream
{
    private readonly ZmodemStream _owner;
    private readonly Stream _inner;

    public ZmodemWriterStream(ZmodemStream owner, Stream inner)
    {
        _owner = owner;
        _inner = inner;
    }

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
        _owner.Write(buffer, offset, count);

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
