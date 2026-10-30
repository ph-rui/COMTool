using System.Runtime.CompilerServices;
using COMTool.Zmodem;
using Porta.Pty;

namespace COMTool.Connections;

/// <summary>
/// Adapts a pair of already-open streams (serial port, SSH shell, test pipe)
/// to the IPtyConnection contract consumed by Iciclecreek TerminalControl.
/// </summary>
public sealed class PtyStreamConnection : IPtyConnection, IDisposable
{
    private Stream _readerStream;
    private readonly Stream _writerStream;
    private readonly object _writeGate = new();
    private readonly Action<int, int>? _resize;
    private readonly Action? _close;
    private readonly ManualResetEventSlim _exited = new(false);
    private int _exitCode;
    private int _closed;

    public PtyStreamConnection(
        Stream readerStream,
        Stream writerStream,
        Action<int, int>? resize = null,
        Action? close = null,
        bool ownsStreams = true,
        bool interceptZmodem = true)
    {
        _writerStream = writerStream;
        _resize = resize;
        _close = close;
        OwnsStreams = ownsStreams;
        if (interceptZmodem)
        {
            Zmodem = new ZmodemStream(
                readerStream,
                data =>
                {
                    lock (_writeGate)
                    {
                        writerStream.Write(data, 0, data.Length);
                        writerStream.Flush();
                    }
                },
                ownsInner: true);
            _readerStream = Zmodem;
            _writerStream = new ZmodemWriterStream(Zmodem, writerStream);
        }
        else
        {
            _readerStream = readerStream;
        }
    }

    public ZmodemStream? Zmodem { get; }

    public bool OwnsStreams { get; }

    public Stream ReaderStream => _readerStream;

    public Stream WriterStream => _writerStream;

    /// <summary>
    /// 在读流外层再套一层变换流（行尾仿真用）。必须在 TerminalControl
    /// AttachConnection 之前调用——库的读循环每次取 <c>ReaderStream</c> 属性，
    /// 且 reader 线程在 attach 时才启动，故此处替换立即对全部后续读取生效。
    /// </summary>
    public void WrapReader(Stream wrapped) => _readerStream = wrapped;

    public bool IsInputCompleted => Zmodem?.IsInputCompleted ?? false;

    public bool SupportsCancellableRead => false;

    public int Pid => 0;

    public int ExitCode => _exitCode;

    public event EventHandler<PtyExitedEventArgs>? ProcessExited;

    public void Resize(int columns, int rows)
    {
        if (columns <= 0
            || rows <= 0
            || Volatile.Read(ref _closed) != 0
            || _resize is null)
        {
            return;
        }

        try
        {
            _resize(columns, rows);
        }
        catch (ObjectDisposedException)
        {
            // The terminal can arrange once more after its tab closed. The
            // transport is already gone, so there is nothing to resize.
        }
    }

    public bool WaitForExit(int milliseconds) =>
        _exited.Wait(milliseconds < 0 ? Timeout.Infinite : milliseconds);

    public void Kill() => Close();

    public void Close(int exitCode = 0)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        _exitCode = exitCode;
        try
        {
            // Stop the ZMODEM pump even when the transport itself is owned by
            // the caller (SSH disposes its shell in the close callback).
            try
            {
                Zmodem?.Dispose();
            }
            catch
            {
                // The transport may already be gone.
            }

            _close?.Invoke();
        }
        finally
        {
            if (OwnsStreams)
            {
                try
                {
                    _readerStream.Dispose();
                }
                catch
                {
                    // The underlying transport may already be gone.
                }

                if (!ReferenceEquals(_readerStream, _writerStream))
                {
                    try
                    {
                        _writerStream.Dispose();
                    }
                    catch
                    {
                        // The underlying transport may already be gone.
                    }
                }
            }

            _exited.Set();
            var eventArgs =
                (PtyExitedEventArgs)RuntimeHelpers.GetUninitializedObject(
                    typeof(PtyExitedEventArgs));
            eventArgs.ExitCode = exitCode;
            ProcessExited?.Invoke(this, eventArgs);
        }
    }

    public void Dispose()
    {
        Close();
        _exited.Dispose();
    }
}
