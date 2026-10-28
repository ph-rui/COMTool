using System.Threading;
using System.Threading.Tasks;
using COMTool.Core.Macros;
using Iciclecreek.Terminal;

namespace COMTool.App.Services;

/// <summary>
/// Implements <see cref="ITerminalSink"/> for a specific <see cref="TerminalControl"/>.
/// Note: Assumes execution occurs on the UI thread, as TerminalControl operations
/// are UI-bound.
/// </summary>
public sealed class TerminalMacroSink : ITerminalSink
{
    private readonly TerminalControl _terminal;

    public TerminalMacroSink(TerminalControl terminal)
    {
        _terminal = terminal;
    }

    public Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        // TerminalControl.SendInputAsync is expected to be thread-safe or
        // called from UI thread. Since MacroRunner runs on UI thread context
        // (via MainViewModel), this is safe.
        return _terminal.SendInputAsync(text);
    }

    public string CaptureOutput()
    {
        return TerminalTranscript.Capture(_terminal);
    }
}
