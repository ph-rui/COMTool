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
    private readonly Stream _readerStream;
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

    public bool SupportsCancellableRead => false;

    public int Pid => 0;

    public int ExitCode => _exitCode;

    public event EventHandler<PtyExitedEventArgs>? ProcessExited;

    public void Resize(int columns, int rows)
    {
        if (columns > 0 && rows > 0)
        {
            _resize?.Invoke(columns, rows);
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
