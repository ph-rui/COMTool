using COMTool.Terminal;
using Xunit;

namespace COMTool.Terminal.Tests;

// r41 探针（TERM-01）：XTerm.NET 解析层是否保留单引号（ls 对含空格文件名加
// '...' 引号，远端 zh_CN.UTF-8）。实机曾见引号丢失，此探针证明解析层完整保留。
public sealed class QuoteProbeTests
{
    [Fact]
    public void TERM01_QuoteCharSurvivesParser()
    {
        var emu = new TerminalEmulator(columns: 120, rows: 40, scrollback: 100);
        var line = "'i.MX95 frdm imx623 cms方案搭建.docx'  modules.tar.zst";
        emu.Write(line);
        var text = emu.LineText(0);
        Assert.Contains("'i.MX95", text);
        Assert.Contains("docx'", text);
        Assert.Equal(line, text.TrimEnd());
    }
}
