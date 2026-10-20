using System.Text;
using Iciclecreek.Terminal;

namespace COMTool.App.Services;

internal static class TerminalTranscript
{
    /// <summary>
    /// Captures the complete scrollback, not just the visible viewport, as
    /// plain text. Trailing blank rows are omitted so the saved file ends at
    /// the stopped-session prompt rather than at the bottom of the buffer.
    /// </summary>
    public static string Capture(TerminalControl terminal)
    {
        var lines = terminal.Terminal.Buffer.Lines;
        var last = lines.Length - 1;
        while (last >= 0 && string.IsNullOrWhiteSpace(terminal.Terminal.GetLine(last)))
        {
            last--;
        }

        var builder = new StringBuilder();
        for (var i = 0; i <= last; i++)
        {
            builder.Append(terminal.Terminal.GetLine(i)).Append('\n');
        }

        return builder.ToString();
    }

    public static async Task SaveAsync(
        TerminalControl terminal,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var text = Capture(terminal);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}
