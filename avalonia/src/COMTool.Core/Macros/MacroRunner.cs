using System.Text.RegularExpressions;

namespace COMTool.Core.Macros;

/// <summary>
/// The playback target of a macro run. The app implements this over its
/// terminal control; tests use a fake that records what was sent.
/// </summary>
public interface ITerminalSink
{
    /// <summary>Sends raw text (already final bytes) to the terminal.</summary>
    Task SendTextAsync(string text, CancellationToken cancellationToken);

    /// <summary>Current terminal screen/scrollback text, for wait-for-string.</summary>
    string CaptureOutput();
}

public sealed record MacroRunResult(bool Completed, bool Cancelled, string? Message);

/// <summary>
/// Plays a macro's steps into a terminal. Text steps with KeyDelayMs &gt; 0
/// are sent character by character with the delay between them; wait steps
/// poll <see cref="ITerminalSink.CaptureOutput"/> every 50 ms and skip the
/// step (continue with the rest) when the pattern does not appear within
/// the timeout, so a macro can never wedge the app.
/// </summary>
public static class MacroRunner
{
    public const int WaitPollIntervalMs = 50;

    public static async Task<MacroRunResult> RunAsync(
        MacroDefinition macro,
        ITerminalSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentNullException.ThrowIfNull(sink);

        try
        {
            foreach (var step in macro.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (step.Kind)
                {
                    case MacroStepKind.Text:
                        await SendTextStepAsync(step, sink, cancellationToken);
                        break;

                    case MacroStepKind.Key:
                        if (MacroKeys.TryGetSequence(step.KeyName, out var sequence))
                        {
                            await sink.SendTextAsync(sequence, cancellationToken);
                        }

                        break;

                    case MacroStepKind.Sleep:
                        if (step.Milliseconds > 0)
                        {
                            await Task.Delay(step.Milliseconds, cancellationToken);
                        }

                        break;

                    case MacroStepKind.WaitString:
                        await WaitForStringAsync(step, sink, cancellationToken);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new MacroRunResult(Completed: false, Cancelled: true, Message: null);
        }

        return new MacroRunResult(Completed: true, Cancelled: false, Message: null);
    }

    private static async Task SendTextStepAsync(
        MacroStep step,
        ITerminalSink sink,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(step.Text))
        {
            return;
        }

        if (step.KeyDelayMs <= 0)
        {
            await sink.SendTextAsync(step.Text, cancellationToken);
            return;
        }

        foreach (var ch in step.Text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await sink.SendTextAsync(ch.ToString(), cancellationToken);
            await Task.Delay(step.KeyDelayMs, cancellationToken);
        }
    }

    private static async Task WaitForStringAsync(
        MacroStep step,
        ITerminalSink sink,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(step.Pattern))
        {
            return;
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(step.TimeoutMs);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (OutputMatches(sink.CaptureOutput(), step.Pattern))
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                // MobaXterm parity is not observable here; skipping keeps the
                // macro from wedging when the pattern never appears.
                return;
            }

            await Task.Delay(WaitPollIntervalMs, cancellationToken);
        }
    }

    public static bool OutputMatches(string output, string pattern)
    {
        if (output.Length == 0 || pattern.Length == 0)
        {
            return false;
        }

        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            return output.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        var regex = "^.*(" +
                    string.Join(".*", pattern.Split(new char[] { '*', '?' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(c => Regex.Escape(c))) +
                    ").*$";
        return Regex.IsMatch(output, regex, RegexOptions.IgnoreCase);
    }
}
