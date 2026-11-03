using COMTool.Terminal;
using COMTool.Terminal.Highlight;

namespace COMTool.Terminal.Tests;

/// <summary>
/// TERM-30：语法高亮规则引擎（纯函数）+ 终端缓冲区着色器（LineFed 驱动）。
/// 规则集照抄 MobaXterm 默认 CustomSyntax 定义（EWS / Unix shell / Cisco / SQL）。
/// </summary>
public sealed class SyntaxHighlightTests
{
    // ───────────────── 纯规则引擎 ─────────────────

    [Fact]
    public void TERM30_DefaultSchemeProducesNoSpans()
    {
        Assert.Empty(SyntaxHighlightRules.FindSpans("error warning success SELECT 127.0.0.1", HighlightScheme.Default));
    }

    [Fact]
    public void TERM30_EwsColorsErrorWarningSuccessAndIp()
    {
        var spans = SyntaxHighlightRules.FindSpans(
            "ERROR: disk full / WARNING low memory / SUCCESS done / host 192.168.1.1",
            HighlightScheme.ErrorWarningSuccess);
        var red = spans.Where(s => s.Color == 1).Select(s => s.Text).ToList();
        var yellow = spans.Where(s => s.Color == 3).Select(s => s.Text).ToList();
        var green = spans.Where(s => s.Color == 2).Select(s => s.Text).ToList();
        var magenta = spans.Where(s => s.Color == 5).Select(s => s.Text).ToList();
        Assert.Contains("ERROR", red);
        Assert.Contains("WARNING", yellow);
        Assert.Contains("SUCCESS", green);
        Assert.Contains("192.168.1.1", magenta);
    }

    [Fact]
    public void TERM30_EwsPhraseRules()
    {
        var red = SyntaxHighlightRules.FindSpans(
            "permission denied / connection refused / segfault in core", HighlightScheme.ErrorWarningSuccess)
            .Where(s => s.Color == 1).Select(s => s.Text).ToList();
        Assert.Contains("permission denied", red);
        Assert.Contains("connection refused", red);
        Assert.Contains("segfault", red);
    }

    [Fact]
    public void TERM30_UnixShellColorsCommentsVariablesKeywords()
    {
        var spans = SyntaxHighlightRules.FindSpans("# install the thing", HighlightScheme.UnixShell);
        Assert.Contains(spans, s => s.Color == 2 && s.Text == "# install the thing"); // 整行注释→绿

        var line2 = SyntaxHighlightRules.FindSpans(
            "echo $HOME && if [ -x a ]; then done", HighlightScheme.UnixShell);
        Assert.Contains(line2, s => s.Color == 6 && s.Text == "echo");          // 内建命令→青
        Assert.Contains(line2, s => s.Color == 3 && s.Text == "$HOME");         // 变量→黄
        Assert.Contains(line2, s => s.Color == 5 && s.Text == "if");            // 控制词→品红
        Assert.Contains(line2, s => s.Color == 5 && s.Text == "then");
    }

    [Fact]
    public void TERM30_CiscoColors()
    {
        var c1 = SyntaxHighlightRules.FindSpans("! backup done", HighlightScheme.Cisco);
        Assert.Contains(c1, s => s.Color == 4 && s.Text == "! backup done");     // 行注释→蓝（整行）

        var c2 = SyntaxHighlightRules.FindSpans("interface GigabitEthernet0/0", HighlightScheme.Cisco);
        Assert.Contains(c2, s => s.Color == 5 && s.Text == "interface");         // 配置词→品红
        Assert.Contains(c2, s => s.Color == 3 && s.Text == "GigabitEthernet0/0");// 接口号→黄

        var c3 = SyntaxHighlightRules.FindSpans(" no shutdown", HighlightScheme.Cisco);
        Assert.Contains(c3, s => s.Color == 1 && s.Text.Trim() == "no");          // 行首 no→红（span 含前导空格）
        Assert.Contains(c3, s => s.Color == 1 && s.Text == "shutdown");          // shutdown→红

        var c4 = SyntaxHighlightRules.FindSpans(" ip address 10.1.1.1 255.255.255.0", HighlightScheme.Cisco);
        Assert.Contains(c4, s => s.Color == 3 && s.Text == "10.1.1.1");          // IP→黄
        Assert.Contains(c4, s => s.Color == 6 && s.Text == "ip address");        // ip address→青
    }

    [Fact]
    public void TERM30_SqlColorsKeywords()
    {
        var spans = SyntaxHighlightRules.FindSpans(
            "SELECT id FROM users WHERE name='x' -- note", HighlightScheme.Sql);
        Assert.Contains(spans, s => s.Color == 6 && s.Text == "SELECT");
        Assert.Contains(spans, s => s.Color == 6 && s.Text == "FROM");
        Assert.Contains(spans, s => s.Color == 6 && s.Text == "WHERE");
    }

    [Fact]
    public void TERM30_NoOverlappingClaims()
    {
        var spans = SyntaxHighlightRules.FindSpans("echo $HOME", HighlightScheme.UnixShell);
        for (var i = 0; i + 1 < spans.Count; i++)
        {
            Assert.True(spans[i].End <= spans[i + 1].Start || spans[i + 1].End <= spans[i].Start,
                $"overlap: {spans[i]} vs {spans[i + 1]}");
        }
    }

    [Fact]
    public void TERM30_SchemeKeysRoundTrip()
    {
        foreach (var s in Enum.GetValues<HighlightScheme>())
        {
            Assert.Equal(s, HighlightSchemes.FromName(HighlightSchemes.KeyOf(s)));
        }
        Assert.Equal(HighlightScheme.Default, HighlightSchemes.FromName(null));
        Assert.Equal(HighlightScheme.Default, HighlightSchemes.FromName("bogus"));
    }

    // ───────────────── 缓冲区着色器（LineFed 驱动）─────────────────

    [Fact]
    public void TERM30_HighlighterColorsNewLinesOnly()
    {
        var emu = new TerminalEmulator(40, 10, 200);
        using var hl = new TerminalHighlighter(emu.Terminal, HighlightScheme.ErrorWarningSuccess);
        emu.Write("fatal error here\r\nall good\r\nplain line\r\n");
        var l0 = emu.Terminal.Buffer.GetLine(0)!;
        var l1 = emu.Terminal.Buffer.GetLine(1)!;
        var l2 = emu.Terminal.Buffer.GetLine(2)!;
        // "fatal error here"：fatal/error 红(1)，其余默认(256)
        Assert.Equal(1, l0[0].Attributes.GetFgColor());   // 'f' (fatal)
        Assert.Equal(1, l0[6].Attributes.GetFgColor());   // 'e' (error)
        Assert.Equal(256, l0[11].Attributes.GetFgColor());// 空格
        // "all good"：全绿？good 不在词表 → 全默认
        Assert.Equal(256, l1[0].Attributes.GetFgColor());
        // "plain line"：默认
        Assert.Equal(256, l2[0].Attributes.GetFgColor());
    }

    [Fact]
    public void TERM30_HlighterDoesNotClobberExistingColors()
    {
        var emu = new TerminalEmulator(40, 10, 200);
        using var hl = new TerminalHighlighter(emu.Terminal, HighlightScheme.ErrorWarningSuccess);
        emu.Write("\x1b[32mGREEN\x1b[0m error text\r\n");
        var l0 = emu.Terminal.Buffer.GetLine(0)!;
        Assert.Equal(2, l0[0].Attributes.GetFgColor());  // GREEN 保持绿(2)，不被染
        // "error" 在第 7 列起（GREEN + 空格）
        Assert.Equal(1, l0[7].Attributes.GetFgColor());
    }

    [Fact]
    public void TERM30_HlighterSchemeSwitchRecolorsAll()
    {
        var emu = new TerminalEmulator(40, 10, 200);
        var hl = new TerminalHighlighter(emu.Terminal, HighlightScheme.Default);
        emu.Write("SELECT * FROM t\r\n");
        Assert.Equal(256, emu.Terminal.Buffer.GetLine(0)![0].Attributes.GetFgColor());
        hl.Scheme = HighlightScheme.Sql;
        hl.ApplyToAll();
        Assert.Equal(6, emu.Terminal.Buffer.GetLine(0)![0].Attributes.GetFgColor()); // SELECT→青
        hl.Dispose();
    }

    [Fact]
    public void TERM30_HlighterSkipsAltBuffer()
    {
        var emu = new TerminalEmulator(40, 10, 200);
        using var hl = new TerminalHighlighter(emu.Terminal, HighlightScheme.ErrorWarningSuccess);
        emu.Write("\x1b[?1049h\x1b[H"); // 进入 alternate screen
        Assert.True(emu.Terminal.IsAlternateBufferActive);
        emu.Write("error on alt screen\r\n");
        var alt = emu.Terminal.Buffer.GetLine(0)!;
        Assert.Equal(256, alt[0].Attributes.GetFgColor()); // alt 屏不着色
    }

    [Fact]
    public void TERM30_HlighterWideCharCells()
    {
        var emu = new TerminalEmulator(40, 10, 200);
        using var hl = new TerminalHighlighter(emu.Terminal, HighlightScheme.ErrorWarningSuccess);
        emu.Write("汉字 error 汉字\r\n");
        var l0 = emu.Terminal.Buffer.GetLine(0)!;
        // 找到 'e' 所在列：汉(2)+字(2)+空格(1)=5 → 列5 应为红
        Assert.Equal(1, l0[5].Attributes.GetFgColor());
        // 汉字列保持默认
        Assert.Equal(256, l0[0].Attributes.GetFgColor());
    }

    [Fact]
    public void TERM30_DefaultFgProbe()
    {
        // 锁定 XTerm 默认前景色编码（256/mode0），防止库升级破坏着色判定
        var emu = new TerminalEmulator(10, 3, 10);
        emu.Write("x\r\n");
        var cell = emu.Terminal.Buffer.GetLine(0)![0];
        Assert.Equal(256, cell.Attributes.GetFgColor());
        Assert.Equal(0, cell.Attributes.GetFgColorMode());
        cell.Attributes.SetFgColor(1, 0);
        Assert.Equal(1, cell.Attributes.GetFgColor());
    }
}
