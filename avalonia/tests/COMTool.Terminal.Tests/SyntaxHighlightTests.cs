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
    public void TERM30_EwsColorsMacAddressMagenta()
    {
        // r56：复刻用户 MobaXterm 的个人规则——ifconfig 输出里的 MAC 地址
        // 染品红（Color 5，MobaXterm 主题下 r58 起为 #ff4dff）。
        var spans = SyntaxHighlightRules.FindSpans(
            "      ether 82:37:a3:fe:87:0b   txqueuelen 1000  (Ethernet)",
            HighlightScheme.ErrorWarningSuccess);
        Assert.Contains(spans, s => s.Color == 5 && s.Text == "82:37:a3:fe:87:0b");
        // 只染 MAC 本身，不得吞掉相邻单词
        Assert.DoesNotContain(spans, s => s.Text.Contains("ether"));
        Assert.DoesNotContain(spans, s => s.Text.Contains("txqueuelen"));
    }

    [Fact]
    public void TERM30_EwsIpv4SkipsNetmaskAndKeepsBroadcast()
    {
        // r58：照抄 MobaXterm EWS 的 IPv4 规则——首段只允许 1..254
        // （([1-9]|[1-9][0-9]|1[0-9][0-9]|2[0-4][0-9]|25[0-4])\.…），
        // 所以 netmask 255.255.255.0 / 255.255.0.0 不染品红（与用户 MobaXterm
        // 截图一致），而 inet / broadcast 地址仍染品红。
        var spans = SyntaxHighlightRules.FindSpans(
            "        inet 10.0.9.5  netmask 255.255.255.0  broadcast 172.17.255.255",
            HighlightScheme.ErrorWarningSuccess);
        var magenta = spans.Where(s => s.Color == 5).Select(s => s.Text).ToList();
        Assert.Contains("10.0.9.5", magenta);
        Assert.Contains("172.17.255.255", magenta);
        Assert.DoesNotContain("255.255.255.0", magenta);
        Assert.DoesNotContain(spans, s => s.Text.Contains("255.255.255.0"));
    }

    [Fact]
    public void TERM30_EwsMacIgnoresHyphenChain()
    {
        // r58：MobaXterm 的 MAC 规则两侧边界字符类排除 '-'（[^0-9A-Za-z_&-]），
        // unspec 的 16 组 00-00-…-00 连字符链因此不会命中（用户 MobaXterm
        // 截图中该链不是品红）。
        var spans = SyntaxHighlightRules.FindSpans(
            "        unspec 00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00  txqueuelen 1000",
            HighlightScheme.ErrorWarningSuccess);
        Assert.DoesNotContain(spans, s => s.Color == 5);

        // 连字符分隔的独立 MAC 仍应命中（MobaXterm 的 MAC 规则允许 : 或 - 分隔）
        var dash = SyntaxHighlightRules.FindSpans(
            "      ether 82-37-a3-fe-87-0b  txqueuelen 1000",
            HighlightScheme.ErrorWarningSuccess);
        Assert.Contains(dash, s => s.Color == 5 && s.Text == "82-37-a3-fe-87-0b");
    }

    [Fact]
    public void TERM30_ThemeMagentaMatchesUserIni()
    {
        // r58：用户 MobaXterm.ini [Colors] 实测 Magenta=255,77,255、
        // BoldMagenta=255,125,255（截图像素亦为 #ff4dff），此前沿用的是
        // MobaXterm 出厂值 #ff36ff。
        var theme = TerminalTheme.CreateMobaXtermTheme();
        Assert.Equal("#ff4dff", theme.Magenta);
        Assert.Equal("#ff7dff", theme.BrightMagenta);
        Assert.Equal("#ff6060", theme.Red);
    }

    // ───────────────── r59：MobaXterm「标准关键字」方案 ─────────────────

    [Fact]
    public void TERM30_StandardSchemeColorsNullNoneMagenta()
    {
        // r59：MobaXterm Standard keywords（SyntaxType=1）的 null/none 规则
        // （EWS 导出原文 Magneta 组含 `null|none`，r39 抄规则时漏掉）。
        // 边界照抄原文：前后不得是 [0-9A-Za-z_&-]，故 --none / none-arg 不命中。
        var spans = SyntaxHighlightRules.FindSpans(
            "Variant = \"none\" Options = \"null\" kernel=none-arg flags=--none",
            HighlightScheme.Standard);
        var magenta = spans.Where(s => s.Color == 5).Select(s => s.Text).ToList();
        Assert.Equal(2, magenta.Count);
        Assert.Contains("none", magenta);
        Assert.Contains("null", magenta);

        // EWS 方案保持不变（两条新规则只属于标准关键字）
        Assert.DoesNotContain(
            SyntaxHighlightRules.FindSpans("none null", HighlightScheme.ErrorWarningSuccess),
            s => s.Color == 5);
    }

    [Fact]
    public void TERM30_StandardSchemeColorsTimestamps()
    {
        // r59：Standard keywords 的时间戳规则（博客实机截图：`[ 67705.015]`
        // 与日志时间戳染黄）。日期时间优先整体命中，不被日期/时间子规则拆开。
        var spans = SyntaxHighlightRules.FindSpans(
            "[ 67705.015] 2026-11-10 15:23:51 INFO boot at 15:23:51.250 done",
            HighlightScheme.Standard);
        var stamps = spans.Where(s => s.Color == 3).Select(s => s.Text).ToList();
        Assert.Contains("[ 67705.015]", stamps);
        Assert.Contains("2026-11-10 15:23:51", stamps);
        Assert.Contains("15:23:51.250", stamps);
        // 日期时间不得被拆成两段（先整体声明，抢占机制下后声明的时间规则不重叠）
        Assert.DoesNotContain("2026-11-10", stamps);
    }

    [Fact]
    public void TERM30_StandardSchemeKeepsEwsRules()
    {
        // r59：标准关键字 = EWS 全部规则 + null/none + 时间戳，原有分组不得回退。
        var spans = SyntaxHighlightRules.FindSpans(
            "error WARNING success 10.0.9.5 02:42:5b:d8:95:d3",
            HighlightScheme.Standard);
        Assert.Contains(spans, s => s.Color == 1 && s.Text == "error");
        Assert.Contains(spans, s => s.Color == 3 && s.Text == "WARNING");
        Assert.Contains(spans, s => s.Color == 2 && s.Text == "success");
        Assert.Contains(spans, s => s.Color == 5 && s.Text == "10.0.9.5");
        Assert.Contains(spans, s => s.Color == 5 && s.Text == "02:42:5b:d8:95:d3");
    }

    [Fact]
    public void TERM30_StandardSchemeKeyRoundTrip()
    {
        Assert.Equal("standard", HighlightSchemes.KeyOf(HighlightScheme.Standard));
        Assert.Equal(HighlightScheme.Standard, HighlightSchemes.FromName("standard"));
        Assert.Equal(HighlightScheme.Standard, HighlightSchemes.FromName(" Standard "));
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
    public void TERM30_HlighterRecolorsAfterReset()
    {
        var emu = new TerminalEmulator(40, 10, 200);
        using var hl = new TerminalHighlighter(emu.Terminal, HighlightScheme.ErrorWarningSuccess);
        emu.Write("first error line\r\n");
        // "error" 从第 6 列开始（"first " 6 字符）
        Assert.Equal(1, emu.Terminal.Buffer.GetLine(0)![6].Attributes.GetFgColor());
        // RIS 全复位后缓冲区收缩、水印失效——复位后写入的新行仍必须被着色
        emu.Terminal.Reset();
        emu.Write("second warning line\r\n");
        var line = emu.Terminal.Buffer.GetLine(0)!;
        Assert.Contains("warning", line.TranslateToString(true, 0, emu.Terminal.Cols));
        // "warning" 从第 7 列开始（"second " 7 字符）
        Assert.Equal(3, line[7].Attributes.GetFgColor());
    }

    [Fact]
    public void TERM30_HlighterColorsLinesAfterCommandEcho()
    {
        // r57 回归：真实会话先有命令回显行，之后每条输出行都必须着色。
        // 旧实现把水印推进到 LF 后的“下一行”，导致回显之后的每一行都被跳过
        // （用户实测：切到 EWS 方案时 ApplyToAll 把历史全量染一次，之后新
        // ifconfig 输出再无颜色）。
        var emu = new TerminalEmulator(60, 20, 500);
        using var hl = new TerminalHighlighter(emu.Terminal, HighlightScheme.ErrorWarningSuccess);
        emu.Write("$ ifconfig\r\n");
        emu.Write("      ether 82:37:a3:fe:87:0b   txqueuelen 1000  (Ethernet)\r\n");
        emu.Write("        RX errors 0  dropped 0  overruns 0  frame 0\r\n");
        emu.Write("        plain text row\r\n");

        var macLine = emu.Terminal.Buffer.GetLine(1)!;
        var macText = macLine.TranslateToString(true, 0);
        var macCol = macText.IndexOf("82:37:a3:fe:87:0b", StringComparison.Ordinal);
        Assert.True(macCol >= 0);
        Assert.Equal(5, macLine[macCol].Attributes.GetFgColor()); // 品红

        var errLine = emu.Terminal.Buffer.GetLine(2)!;
        var errText = errLine.TranslateToString(true, 0);
        var errCol = errText.IndexOf("errors", StringComparison.Ordinal);
        Assert.True(errCol >= 0);
        Assert.Equal(1, errLine[errCol].Attributes.GetFgColor()); // 红

        // 普通行保持默认前景色
        Assert.Equal(256, emu.Terminal.Buffer.GetLine(3)![0].Attributes.GetFgColor());
    }

    [Fact]
    public void TERM30_HlighterColorsAcrossScrollbackTrim()
    {
        // r57 回归：回滚缓冲写满后 XTerm 整体上移（绝对行号漂移），
        // 新行仍必须着色，且不能因“缓冲区收缩”误判而重复全量重染。
        var emu = new TerminalEmulator(30, 6, 6);
        using var hl = new TerminalHighlighter(emu.Terminal, HighlightScheme.ErrorWarningSuccess);
        emu.Write("$ run\r\n");
        for (var i = 0; i < 20; i++)
        {
            emu.Write($"error {i:00}\r\n");
        }

        var checkedLines = 0;
        for (var row = 0; row < emu.Terminal.Buffer.Length; row++)
        {
            var line = emu.Terminal.Buffer.GetLine(row)!;
            var text = line.TranslateToString(true, 0);
            if (!text.StartsWith("error ", StringComparison.Ordinal))
            {
                continue;
            }
            checkedLines++;
            Assert.Equal(1, line[0].Attributes.GetFgColor());
        }
        Assert.True(checkedLines >= 5, $"仅检查到 {checkedLines} 条 error 行");
    }

    [Fact]
    public void TERM30_HlighterColorsWrappedLineAfterEcho()
    {
        // r57 回归：自动折行的长行在最后一个视觉行才触发 LF，
        // 着色流程必须覆盖折行涉及的整段（水印到刚结束行）。
        var emu = new TerminalEmulator(30, 10, 200);
        using var hl = new TerminalHighlighter(emu.Terminal, HighlightScheme.ErrorWarningSuccess);
        emu.Write("$ run\r\n");
        emu.Write("error " + new string('a', 29) + " warning here\r\n");

        // 第 1 视觉行以 error 开头，第 2 视觉行含 warning：
        // 折行涉及的整段（水印到 LF 结束行）都必须被着色。
        var first = emu.Terminal.Buffer.GetLine(1)!;
        Assert.Equal(1, first[0].Attributes.GetFgColor());
        var second = emu.Terminal.Buffer.GetLine(2)!;
        var text = second.TranslateToString(true, 0);
        var col = text.IndexOf("warning", StringComparison.Ordinal);
        Assert.True(col >= 0, $"第 2 行无 warning：{text}");
        Assert.Equal(3, second[col].Attributes.GetFgColor());
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
