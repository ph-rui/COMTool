using System;
using System.Collections.Generic;
using System.Linq;
using COMTool.Core.Macros;
using Iciclecreek.Terminal;

namespace COMTool.App.Services;

/// <summary>
/// Represents an active macro recording session. Captures keystrokes and
/// text input from a specific terminal, merging them into macro steps.
/// </summary>
public sealed class MacroRecordingSession
{
    private readonly MacroRecorder _recorder;

    public TerminalControl Target { get; }
    public MacroRecorder Recorder => _recorder;

    public MacroRecordingSession(TerminalControl terminal)
    {
        Target = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _recorder = new MacroRecorder();
    }

    public void FeedText(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _recorder.FeedText(text, DateTime.Now);
        }
    }

    public void FeedKey(string keyName)
    {
        _recorder.FeedKey(keyName, DateTime.Now);
    }

    /// <summary>
    /// Finalizes the recording and returns the captured steps.
    /// Returns null if nothing was recorded.
    /// </summary>
    public IReadOnlyList<MacroStep>? Finish()
    {
        var steps = _recorder.Finish(DateTime.Now);
        return steps.Count > 0 ? steps : null;
    }
}
