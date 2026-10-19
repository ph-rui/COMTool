using System.Text;
using COMTool.Zmodem;

namespace COMTool.Zmodem.Tests;

public sealed class MobaKeywordHighlighterTests
{
    private const string Red = "\u001b[38;2;255;107;107m";
    private const string Green = "\u001b[38;2;126;231;135m";
    private const string Yellow = "\u001b[38;2;255;255;102m";
    private const string ResetForeground = "\u001b[39m";

    [Fact]
    public void PlainTextWithoutKeywordsIsUnchanged()
    {
        var highlighter = new MobaKeywordHighlighter();
        var input = Encoding.UTF8.GetBytes("hello world\r\n");
        Assert.Equal(input, highlighter.Transform(input));
    }

    [Fact]
    public void ErrorKeywordsAreBrightRed()
    {
        var highlighter = new MobaKeywordHighlighter();
        var output = Encoding.UTF8.GetString(
            highlighter.Transform(Encoding.UTF8.GetBytes("error\n")));
        Assert.Contains(Red + "error" + ResetForeground, output);
    }

    [Fact]
    public void SuccessKeywordsAreBrightGreen()
    {
        var highlighter = new MobaKeywordHighlighter();
        var output = Encoding.UTF8.GetString(
            highlighter.Transform(Encoding.UTF8.GetBytes("connected\n")));
        Assert.Contains(Green + "connected" + ResetForeground, output);
    }

    [Fact]
    public void WarningKeywordsAreBrightYellow()
    {
        var highlighter = new MobaKeywordHighlighter();
        var output = Encoding.UTF8.GetString(
            highlighter.Transform(Encoding.UTF8.GetBytes("not found\n")));
        Assert.Contains(Yellow + "not found" + ResetForeground, output);
    }

    [Fact]
    public void ExistingAnsiForegroundTakesPrecedence()
    {
        var highlighter = new MobaKeywordHighlighter();
        var input = Encoding.UTF8.GetBytes("\u001b[31mnot found\u001b[0m\n");
        Assert.Equal(input, highlighter.Transform(input));
    }

    [Fact]
    public void ExistingAnsiBackgroundTakesPrecedence()
    {
        var highlighter = new MobaKeywordHighlighter();
        var input = Encoding.UTF8.GetBytes("\u001b[44merror\u001b[0m\n");
        Assert.Equal(input, highlighter.Transform(input));
    }

    [Fact]
    public void UrlsAreUnderlined()
    {
        var highlighter = new MobaKeywordHighlighter();
        var output = Encoding.UTF8.GetString(
            highlighter.Transform(Encoding.UTF8.GetBytes("see http://example.com/a\n")));
        Assert.Contains("\u001b[4mhttp://example.com/a\u001b[24m", output);
    }

    [Fact]
    public void BooleanValuesAreHighlightedWithoutPunctuation()
    {
        var highlighter = new MobaKeywordHighlighter();
        var output = Encoding.UTF8.GetString(
            highlighter.Transform(Encoding.UTF8.GetBytes("status=false\n")));
        Assert.Contains("status=" + Red + "false" + ResetForeground, output);
    }

    [Fact]
    public void KeywordsSplitAcrossChunksAreStillHighlighted()
    {
        var highlighter = new MobaKeywordHighlighter();
        var first = highlighter.Transform(Encoding.UTF8.GetBytes("not fo"));
        var second = highlighter.Transform(Encoding.UTF8.GetBytes("und\n"));
        var output = Encoding.UTF8.GetString(first.Concat(second).ToArray());
        Assert.Contains(Yellow + "not found" + ResetForeground, output);
    }

    [Fact]
    public void LastLoginAcrossChunksIsCyan()
    {
        const string cyan = "\u001b[38;2;102;224;255m";
        var highlighter = new MobaKeywordHighlighter();
        var first = highlighter.Transform(Encoding.UTF8.GetBytes("Last log"));
        var second = highlighter.Transform(Encoding.UTF8.GetBytes("in:\n"));
        var output = Encoding.UTF8.GetString(first.Concat(second).ToArray());
        Assert.Contains(cyan + "Last login:" + ResetForeground, output);
    }

    [Fact]
    public void CompleteKeywordDoesNotWaitForAnotherChunk()
    {
        var highlighter = new MobaKeywordHighlighter();
        var output = Encoding.UTF8.GetString(
            highlighter.Transform(Encoding.UTF8.GetBytes("connected")));
        Assert.Contains(Green + "connected" + ResetForeground, output);
    }

    [Fact]
    public void NonUtf8BytesArePassedThroughUnchanged()
    {
        var highlighter = new MobaKeywordHighlighter();
        var input = new byte[] { 0xB4, 0xED, 0xCE, 0xF3, 0x0A };
        Assert.Equal(input, highlighter.Transform(input));
    }

    [Fact]
    public void Utf8TextSurvivesHighlighting()
    {
        var highlighter = new MobaKeywordHighlighter();
        var output = Encoding.UTF8.GetString(
            highlighter.Transform(Encoding.UTF8.GetBytes("警告 error 完成\n")));
        Assert.Contains("警告 ", output);
        Assert.Contains("完成", output);
    }
}
