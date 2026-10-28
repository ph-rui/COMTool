using System.Text;

namespace COMTool.Core.Macros;

/// <summary>
/// Records raw keystrokes into MobaXterm-style macro steps. Feed events as
/// they arrive (with their timestamps) and call <see cref="Finish"/> when the
/// user stops recording.
///
/// Observed MobaXterm 22.1 behavior (real-machine verification):
/// consecutive printable characters merge into a single Text step with
/// KeyDelayMs 0; a gap between events longer than <c>GapThresholdMs</c>
/// becomes a SLEEP step (capped at <c>MaxSleepMs</c>, rounded to 10 ms);
/// special keys become Key steps and split text runs. A gap before the stop
/// is recorded as a trailing SLEEP step the same way.
/// </summary>
public sealed class MacroRecorder
{
    private sealed record KeystrokeEvent(DateTime Time, bool IsKey, string Value);

    private readonly int _gapThresholdMs;
    private readonly int _maxSleepMs;
    private readonly List<KeystrokeEvent> _events = new();
    private DateTime? _last;

    public MacroRecorder(int gapThresholdMs = 1000, int maxSleepMs = 1200)
    {
        _gapThresholdMs = gapThresholdMs;
        _maxSleepMs = maxSleepMs;
    }

    public bool HasInput => _events.Count > 0;

    public void FeedText(string text, DateTime now)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (var character in text)
        {
            if (char.IsControl(character) && character != '\t' && character != '\r')
            {
                continue;
            }

            _events.Add(new KeystrokeEvent(now, IsKey: false, character.ToString()));
        }
    }

    public void FeedKey(string keyName, DateTime now)
    {
        if (!MacroKeys.All.Contains(keyName, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _events.Add(new KeystrokeEvent(now, IsKey: true, keyName));
        _last = now;
    }

    /// <summary>
    /// Builds the step list. Returns an empty list when nothing was typed;
    /// the caller treats that as "discard, no save dialog".
    /// </summary>
    public IReadOnlyList<MacroStep> Finish(DateTime stopTime)
    {
        var steps = new List<MacroStep>();
        if (_events.Count == 0)
        {
            return steps;
        }

        var textRun = new StringBuilder();
        var last = _events[0].Time;

        void FlushText()
        {
            if (textRun.Length > 0)
            {
                steps.Add(MacroStep.TextStep(textRun.ToString()));
                textRun.Clear();
            }
        }

        foreach (var ev in _events)
        {
            if (_last is { } previous)
            {
                var gap = (int)(ev.Time - previous).TotalMilliseconds;
                if (gap > _gapThresholdMs)
                {
                    FlushText();
                    steps.Add(MacroStep.SleepStep(ClampGap(gap)));
                }
            }

            if (ev.IsKey)
            {
                FlushText();
                steps.Add(MacroStep.KeyStep(ev.Value));
            }
            else
            {
                textRun.Append(ev.Value);
            }

            last = ev.Time;
            _last = ev.Time;
        }

        FlushText();

        if (_last is { } stopRef)
        {
            var tail = (int)(stopTime - stopRef).TotalMilliseconds;
            if (tail > _gapThresholdMs)
            {
                steps.Add(MacroStep.SleepStep(ClampGap(tail)));
            }
        }

        return steps;
    }

    private int ClampGap(int ms) =>
        Math.Min(_maxSleepMs, Math.Max(0, (int)Math.Round(ms / 10.0, MidpointRounding.AwayFromZero) * 10));
}
