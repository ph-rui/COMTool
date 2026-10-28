using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using COMTool.Core.Macros;
using Xunit;

namespace COMTool.Core.Tests;

internal sealed class FakeSink : ITerminalSink
{
    private readonly object _gate = new();
    public List<string> Sent { get; } = new();
    public Func<string> OutputProvider { get; set; } = () => "";

    public Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Sent.Add(text);
        }

        return Task.CompletedTask;
    }

    public string CaptureOutput() => OutputProvider();

    public string AllSent => string.Concat(Sent);
}

public sealed class MacroRunnerTests
{
    [Fact]
    public async Task MRUN01_TextNoDelay_SentOnce()
    {
        var sink = new FakeSink();
        var macro = new MacroDefinition("t", new[] { MacroStep.TextStep("ls -la") });

        var result = await MacroRunner.RunAsync(macro, sink);

        Assert.True(result.Completed);
        var single = Assert.Single(sink.Sent);
        Assert.Equal("ls -la", single);
    }

    [Fact]
    public async Task MRUN02_TextWithDelay_PerCharSends()
    {
        var sink = new FakeSink();
        var macro = new MacroDefinition("t", new[] { MacroStep.TextStep("abc", 1) });

        var result = await MacroRunner.RunAsync(macro, sink);

        Assert.True(result.Completed);
        Assert.Equal(new[] { "a", "b", "c" }, sink.Sent);
    }

    [Fact]
    public async Task MRUN03_KeyStep_SendsEscapeSequence()
    {
        var sink = new FakeSink();
        var macro = new MacroDefinition("t", new[]
        {
            MacroStep.KeyStep("RETURN"),
            MacroStep.KeyStep("UP"),
        });

        await MacroRunner.RunAsync(macro, sink);

        Assert.Equal(new[] { "\r", "\u001b[A" }, sink.Sent);
    }

    [Fact]
    public async Task MRUN04_SleepStep_Delays()
    {
        var sink = new FakeSink();
        var macro = new MacroDefinition("t", new[] { MacroStep.SleepStep(40) });
        var sw = Stopwatch.StartNew();

        var result = await MacroRunner.RunAsync(macro, sink);

        Assert.True(result.Completed);
        Assert.True(sw.ElapsedMilliseconds >= 30, $"only waited {sw.ElapsedMilliseconds}ms");
        Assert.Empty(sink.Sent);
    }

    [Fact]
    public async Task MRUN05_Wait_MatchesThenContinues()
    {
        var sink = new FakeSink();
        var polls = 0;
        sink.OutputProvider = () =>
        {
            polls++;
            return polls >= 3 ? "login: waiting" : "booting";
        };

        var macro = new MacroDefinition("t", new[]
        {
            MacroStep.WaitStep("login:", 5000),
            MacroStep.TextStep("user"),
        });

        var result = await MacroRunner.RunAsync(macro, sink);

        Assert.True(result.Completed);
        Assert.Equal("user", sink.AllSent);
        Assert.True(polls >= 3);
    }

    [Fact]
    public async Task MRUN06_Wait_TimeoutSkipsAndContinues()
    {
        var sink = new FakeSink();
        var macro = new MacroDefinition("t", new[]
        {
            MacroStep.WaitStep("never-appears", 120),
            MacroStep.TextStep("after"),
        });
        var sw = Stopwatch.StartNew();

        var result = await MacroRunner.RunAsync(macro, sink);

        Assert.True(sw.ElapsedMilliseconds < 5000, "wait must not hang");
        Assert.True(result.Completed);
        Assert.Equal("after", sink.AllSent);
    }

    [Fact]
    public async Task MRUN07_Wait_WildcardMatches()
    {
        // '*' matches any run of characters, '?' matches exactly one.
        Assert.True(MacroRunner.OutputMatches("password: ", "pass*word:"));
        Assert.True(MacroRunner.OutputMatches("pass1234word:", "pass*word:"));
        Assert.True(MacroRunner.OutputMatches("Password for root@host:", "ass*host:"));
        Assert.True(MacroRunner.OutputMatches("Press [Enter] to continue", "Press [?]"));
        Assert.True(MacroRunner.OutputMatches("Press [a] to continue", "Press [?]"));
        Assert.False(MacroRunner.OutputMatches("nothing here", "zzz*"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task MRUN08_Cancel_StopsPromptly()
    {
        var sink = new FakeSink();
        using var cts = new CancellationTokenSource();
        var macro = new MacroDefinition("t", new[]
        {
            MacroStep.SleepStep(5000),
            MacroStep.TextStep("never-sent"),
        });

        var task = MacroRunner.RunAsync(macro, sink, cts.Token);
        await Task.Delay(80);
        cts.Cancel();

        var result = await task;

        Assert.True(result.Cancelled);
        Assert.False(result.Completed);
        Assert.DoesNotContain("never-sent", sink.Sent);
    }
}
