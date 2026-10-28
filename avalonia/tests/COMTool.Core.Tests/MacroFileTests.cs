using COMTool.Core.Macros;
using Xunit;

namespace COMTool.Core.Tests;

public sealed class MacroFileTests
{
    // Real sample exported from MobaXterm Professional 22.1 (2026-10-28).
    private const string MobaXtermSample =
        "[Macros]\n" +
        "COMTOOL-PROBE=12:0:0:echo probe-macro|258:13:1835009:RETURN|0:0:0:SLEEPEQUAL1200|\n" +
        "\n" +
        "[MacrosHotkeys]\n" +
        "COMTOOL-PROBE=0\n";

    [Fact]
    public void Parse_MobaXtermRealSample_ProducesThreeSteps()
    {
        var (macros, warnings) = MacroFile.Parse(MobaXtermSample);

        Assert.Empty(warnings);
        var single = Assert.Single(macros);
        Assert.Equal("COMTOOL-PROBE", single.Name);
        Assert.Null(single.Hotkey);

        var steps = single.Steps;
        Assert.Equal(3, steps.Count);
        Assert.Equal(MacroStepKind.Text, steps[0].Kind);
        Assert.Equal("echo probe-macro", steps[0].Text);
        Assert.Equal(0, steps[0].KeyDelayMs);

        Assert.Equal(MacroStepKind.Key, steps[1].Kind);
        Assert.Equal("RETURN", steps[1].KeyName);

        Assert.Equal(MacroStepKind.Sleep, steps[2].Kind);
        Assert.Equal(1200, steps[2].Milliseconds);
    }

    [Fact]
    public void Export_RoundTripsAllFourStepKinds()
    {
        var macros = new List<MacroDefinition>
        {
            new("m1", new[]
            {
                MacroStep.TextStep("echo hello", 30),
                MacroStep.KeyStep("TAB"),
                MacroStep.SleepStep(500),
                MacroStep.WaitStep("ready", 8000),
            }),
            new("m2", new[] { MacroStep.TextStep("pwd") }),
        };

        var text = MacroFile.Export(macros);
        var (parsed, warnings) = MacroFile.Parse(text);

        Assert.Empty(warnings);
        Assert.Equal(2, parsed.Count);

        var first = Assert.Single(parsed, m => m.Name == "m1");
        Assert.Equal(MacroStepKind.Text, first.Steps[0].Kind);
        Assert.Equal("echo hello", first.Steps[0].Text);
        Assert.Equal(30, first.Steps[0].KeyDelayMs);
        Assert.Equal(MacroStepKind.Key, first.Steps[1].Kind);
        Assert.Equal("TAB", first.Steps[1].KeyName);
        Assert.Equal(MacroStepKind.Sleep, first.Steps[2].Kind);
        Assert.Equal(500, first.Steps[2].Milliseconds);
        Assert.Equal(MacroStepKind.WaitString, first.Steps[3].Kind);
        Assert.Equal("ready", first.Steps[3].Pattern);
        Assert.Equal(8000, first.Steps[3].TimeoutMs);

        var second = Assert.Single(parsed, m => m.Name == "m2");
        var only = Assert.Single(second.Steps);
        Assert.Equal(MacroStepKind.Text, only.Kind);
        Assert.Equal("pwd", only.Text);
    }

    [Fact]
    public void Export_UsesMobaXtermEncodingForKnownTypes()
    {
        var macros = new[]
        {
            new MacroDefinition("COMTOOL-PROBE", new[]
            {
                MacroStep.TextStep("echo probe-macro"),
                MacroStep.KeyStep("RETURN"),
                MacroStep.SleepStep(1200),
            }),
        };

        var text = MacroFile.Export(macros);
        Assert.Contains("[Macros]", text);
        Assert.Contains("[MacrosHotkeys]", text);
        // text / key / sleep encodings must match the verified MobaXterm sample.
        Assert.Contains("12:0:0:echo probe-macro|", text);
        Assert.Contains("258:13:", text);
        Assert.Contains(":RETURN|", text);
        Assert.Contains("0:0:0:SLEEPEQUAL1200|", text);
        Assert.Contains("COMTOOL-PROBE=0", text);
    }

    [Fact]
    public void Parse_UnknownStepType_SkipsStepWithWarning()
    {
        const string content =
            "[Macros]\n" +
            "M=12:0:0:echo hi|99:1:2:MYSTERY|0:0:0:SLEEPEQUAL100|\n";
        var (macros, warnings) = MacroFile.Parse(content);

        var single = Assert.Single(macros);
        Assert.Equal(2, single.Steps.Count); // text + sleep kept, mystery skipped
        Assert.Contains(warnings, w => w.Contains("unknown step type 99"));
    }

    [Fact]
    public void Parse_MacroWithOnlyUnknownSteps_SkippedWithWarning()
    {
        const string content = "[Macros]\nM=77:0:0:???\n";
        var (macros, warnings) = MacroFile.Parse(content);

        Assert.Empty(macros);
        Assert.Contains(warnings, w => w.Contains("M"));
    }

    [Fact]
    public void Parse_NameWithEquals_SkippedWithWarning()
    {
        const string content = "[Macros]\nA=B=12:0:0:x|\n";
        var (macros, warnings) = MacroFile.Parse(content);

        Assert.Empty(macros);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsEmpty()
    {
        var (macros, warnings) = MacroFile.Parse("");
        Assert.Empty(macros);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Parse_HotkeyZeroMeansNone_NonZeroWarns()
    {
        const string content =
            "[Macros]\n" +
            "A=12:0:0:hi|\n" +
            "B=12:0:0:yo|\n" +
            "\n" +
            "[MacrosHotkeys]\n" +
            "A=0\n" +
            "B=42\n";
        var (macros, warnings) = MacroFile.Parse(content);

        Assert.Equal(2, macros.Count);
        Assert.Null(macros[0].Hotkey);
        Assert.Null(macros[1].Hotkey);
        Assert.Contains(warnings, w => w.Contains("hotkey"));
    }

    [Fact]
    public void Parse_MalformedLines_AreIgnored()
    {
        const string content =
            "[Macros]\n" +
            "no-equals-line\n" +
            "A=12:0:0:ok|\n" +
            "[Unknown]\n" +
            "junk=whatever\n";
        var (macros, warnings) = MacroFile.Parse(content);

        var single = Assert.Single(macros);
        Assert.Equal("A", single.Name);
    }
}
