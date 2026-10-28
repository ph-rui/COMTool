using System;
using System.Linq;
using COMTool.Core.Macros;
using Xunit;

namespace COMTool.Core.Tests;

public sealed class MacroRecorderTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public void MREC01_ConsecutiveChars_MergeIntoSingleTextStep()
    {
        var r = new MacroRecorder();
        // 60ms between characters (normal typing speed)
        var t = T0;
        foreach (var ch in "abc")
        {
            r.FeedText(ch.ToString(), t);
            t = t.AddMilliseconds(60);
        }

        var steps = r.Finish(t);

        var single = Assert.Single(steps);
        Assert.Equal(MacroStepKind.Text, single.Kind);
        Assert.Equal("abc", single.Text);
        Assert.Equal(0, single.KeyDelayMs);
    }

    [Fact]
    public void MREC02_GapBelowThreshold_NoSleepStep()
    {
        var r = new MacroRecorder();
        r.FeedText("echo hi", T0);
        r.FeedKey("RETURN", T0.AddMilliseconds(500)); // 500ms gap — under 1000ms threshold

        var steps = r.Finish(T0.AddMilliseconds(600));

        Assert.Equal(2, steps.Count);
        Assert.Equal(MacroStepKind.Text, steps[0].Kind);
        Assert.Equal(MacroStepKind.Key, steps[1].Kind);
    }

    [Fact]
    public void MREC03_GapAboveThreshold_InsertsSleep()
    {
        var r = new MacroRecorder();
        r.FeedText("a", T0);
        r.FeedKey("RETURN", T0.AddMilliseconds(1500));

        var steps = r.Finish(T0.AddMilliseconds(1600));

        Assert.Equal(3, steps.Count);
        Assert.Equal(MacroStepKind.Sleep, steps[1].Kind);
        Assert.Equal(1200, steps[1].Milliseconds); // 1500 capped at maxSleepMs
    }

    [Fact]
    public void MREC04_GapCappedAtMaxSleep()
    {
        var r = new MacroRecorder();
        r.FeedText("a", T0);
        r.FeedKey("RETURN", T0.AddSeconds(60));

        var steps = r.Finish(T0.AddSeconds(61));

        var sleeps = steps.OfType<MacroStep>().Where(s => s.Kind == MacroStepKind.Sleep).ToList();
        var mid = sleeps.First();
        Assert.Equal(1200, mid.Milliseconds); // capped at max 1200
    }

    [Fact]
    public void MREC05_TrailingGap_AppendsTrailingSleep()
    {
        var r = new MacroRecorder();
        r.FeedKey("RETURN", T0);

        var steps = r.Finish(T0.AddMilliseconds(2000));

        var last = steps.Last();
        Assert.Equal(MacroStepKind.Sleep, last.Kind);
        Assert.Equal(1200, last.Milliseconds); // 2000 capped to 1200
    }

    [Fact]
    public void MREC06_TrailingShortGap_NoSleep()
    {
        var r = new MacroRecorder();
        r.FeedKey("RETURN", T0);

        var steps = r.Finish(T0.AddMilliseconds(300));

        var single = Assert.Single(steps);
        Assert.Equal(MacroStepKind.Key, single.Kind);
    }

    [Fact]
    public void MREC07_KeySplitsTextRuns()
    {
        var r = new MacroRecorder();
        r.FeedText("a", T0);
        r.FeedKey("RETURN", T0.AddMilliseconds(100));
        r.FeedText("b", T0.AddMilliseconds(200));

        var steps = r.Finish(T0.AddMilliseconds(300));

        Assert.Equal(3, steps.Count);
        Assert.Equal("a", steps[0].Text);
        Assert.Equal(MacroStepKind.Key, steps[1].Kind);
        Assert.Equal("b", steps[2].Text);
    }

    [Fact]
    public void MREC08_NoInput_FinishEmpty()
    {
        var r = new MacroRecorder();
        Assert.False(r.HasInput);
        Assert.Empty(r.Finish(T0.AddSeconds(5)));
    }

    [Fact]
    public void MREC09_UnknownKey_Ignored()
    {
        var r = new MacroRecorder();
        r.FeedKey("NOTAKEY", T0);
        r.FeedText("x", T0.AddMilliseconds(50));

        var steps = r.Finish(T0.AddMilliseconds(100));

        var single = Assert.Single(steps);
        Assert.Equal(MacroStepKind.Text, single.Kind);
    }
}
