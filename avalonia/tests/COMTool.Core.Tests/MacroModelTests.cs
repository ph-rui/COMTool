using COMTool.Core.Macros;
using Xunit;

namespace COMTool.Core.Tests;

public sealed class MacroModelTests
{
    [Fact]
    public void MacroStep_Factories_ProduceCorrectKindAndFields()
    {
        var t = MacroStep.TextStep("echo hello", 30);
        Assert.Equal(MacroStepKind.Text, t.Kind);
        Assert.Equal("echo hello", t.Text);
        Assert.Equal(30, t.KeyDelayMs);

        var k = MacroStep.KeyStep("RETURN");
        Assert.Equal(MacroStepKind.Key, k.Kind);
        Assert.Equal("RETURN", k.KeyName);

        var s = MacroStep.SleepStep(1200);
        Assert.Equal(MacroStepKind.Sleep, s.Kind);
        Assert.Equal(1200, s.Milliseconds);

        var w = MacroStep.WaitStep("login:", 5000);
        Assert.Equal(MacroStepKind.WaitString, w.Kind);
        Assert.Equal("login:", w.Pattern);
        Assert.Equal(5000, w.TimeoutMs);
    }

    [Fact]
    public void MacroStep_DisplaySummary_MatchesMobaXtermWording()
    {
        Assert.Equal("\"echo probe-macro\"", MacroStep.TextStep("echo probe-macro").DisplaySummary);
        Assert.Equal("RETURN", MacroStep.KeyStep("RETURN").DisplaySummary);
        Assert.Equal("SLEEP ms=1200", MacroStep.SleepStep(1200).DisplaySummary);
        Assert.Equal("WAIT \"login:\"", MacroStep.WaitStep("login:").DisplaySummary);
        // Multi-line text is truncated to the first line.
        Assert.Equal("\"first…\"", MacroStep.TextStep("first\nsecond").DisplaySummary);
    }

    [Fact]
    public void MacroStep_ToFromDto_RoundTrips()
    {
        var original = new[]
        {
            MacroStep.TextStep("echo hello", 20),
            MacroStep.KeyStep("RETURN"),
            MacroStep.SleepStep(1200),
            MacroStep.WaitStep("ready", 8000),
        };

        foreach (var step in original)
        {
            var round = MacroStep.FromDto(step.ToDto());
            Assert.Equal(step.Kind, round.Kind);
            Assert.Equal(step.Text, round.Text);
            Assert.Equal(step.KeyDelayMs, round.KeyDelayMs);
            Assert.Equal(step.KeyName, round.KeyName);
            Assert.Equal(step.Milliseconds, round.Milliseconds);
            Assert.Equal(step.Pattern, round.Pattern);
            Assert.Equal(step.TimeoutMs, round.TimeoutMs);
        }
    }

    [Fact]
    public void MacroKeys_CoversDocumentedKeys()
    {
        var all = MacroKeys.All;
        Assert.Contains("RETURN", all);
        Assert.Contains("F12", all);
        Assert.Equal(26, all.Length);
    }
}
