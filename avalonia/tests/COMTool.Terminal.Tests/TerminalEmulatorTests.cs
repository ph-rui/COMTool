using System.Diagnostics;
using System.Text;
using COMTool.Terminal;

namespace COMTool.Terminal.Tests;

public sealed class TerminalEmulatorTests
{
    [Fact]
    public void ParsesAnsiColorAndWideCharacters()
    {
        using var terminal = new TerminalEmulator(columns: 80, rows: 24);
        terminal.Write("\x1b[38;5;196mred\x1b[0m \x1b[48;5;21mblue\x1b[0m 中文\r\n");

        var red = terminal.GetCell(0, 0);
        Assert.Equal("r", red.Content);
        Assert.Equal(196, red.Foreground);

        var blueBackground = terminal.GetCell(0, 4);
        Assert.Equal("b", blueBackground.Content);
        Assert.Equal(21, blueBackground.Background);

        var wide = terminal.GetCell(0, 9);
        Assert.Equal("中", wide.Content);
        Assert.Equal(2, wide.Width);
        Assert.Equal(0, terminal.GetCell(0, 10).Width);
        Assert.Equal("文", terminal.GetCell(0, 11).Content);
    }

    [Fact]
    public void MatchesPythonPyteGoldenScreen()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Golden",
            "terminal-basic.json");
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var data = Convert.FromHexString(root.GetProperty("data_hex").GetString()!);
        var expectedLines = root.GetProperty("lines")
            .EnumerateArray()
            .Select(line => line.GetString() ?? "")
            .ToArray();
        var expectedCursor = root.GetProperty("cursor")
            .EnumerateArray()
            .Select(value => value.GetInt32())
            .ToArray();

        using var terminal = new TerminalEmulator(columns: 60, rows: 20);
        terminal.Write(data);

        Assert.Equal(
            expectedLines,
            terminal.VisibleLines().Select(line => line.TrimEnd()).ToArray());
        Assert.Equal(expectedCursor[0], terminal.CursorX);
        Assert.Equal(expectedCursor[1], terminal.CursorY);
    }

    [Fact]
    public void TracksCursorVisibilityAndWideCharacterAdvance()
    {
        using var terminal = new TerminalEmulator(columns: 80, rows: 24);
        Assert.True(terminal.CursorVisible);

        terminal.Write("\x1b[?25l");
        Assert.False(terminal.CursorVisible);

        terminal.Write("中文");
        Assert.Equal(4, terminal.CursorX);

        terminal.Write("\x1b[?25h");
        Assert.True(terminal.CursorVisible);
    }

    [Fact]
    public void HandlesWrappingTabsAndEraseSequences()
    {
        using var terminal = new TerminalEmulator(columns: 8, rows: 4);
        terminal.Write("123456789");
        Assert.Equal(1, terminal.CursorY);
        Assert.Equal(1, terminal.CursorX);

        terminal.Write("\r\n\tX");
        Assert.Equal(8, terminal.CursorX);

        terminal.Write("\x1b[2K\rY");
        Assert.StartsWith("Y", terminal.LineText(2));

        terminal.Write("\x1b[2J");
        terminal.Write("\x1b[HZ");
        Assert.StartsWith("Z", terminal.LineText(0));
    }

    [Fact]
    public void UsesXterm256Palette()
    {
        using var terminal = new TerminalEmulator();

        Assert.Equal(0x00FF0000, terminal.PaletteColor(196));
        Assert.Equal(0x000000FF, terminal.PaletteColor(21));
        Assert.Equal(0x00FFFFFF, terminal.PaletteColor(15));
        Assert.Equal(0x00000000, terminal.PaletteColor(0));
    }

    [Fact]
    public void TracksBracketedPasteMode()
    {
        using var terminal = new TerminalEmulator();
        Assert.False(terminal.BracketedPasteMode);

        terminal.Write("\x1b[?2004h");
        Assert.True(terminal.BracketedPasteMode);

        terminal.Write("\x1b[?2004l");
        Assert.False(terminal.BracketedPasteMode);
    }

    [Fact]
    public void SelectionReturnsSelectedText()
    {
        using var terminal = new TerminalEmulator();
        terminal.Write("hello world\r\nsecond line\r\n");

        var selected = terminal.Select(0, 0, 5, 0);

        Assert.Equal("hello", selected.TrimEnd());
        Assert.True(terminal.HasSelection);
    }

    [Fact]
    public void ResizeUpdatesDimensionsAndVisibleRows()
    {
        using var terminal = new TerminalEmulator(columns: 80, rows: 24);
        terminal.Write("resize me\r\n");

        terminal.Resize(100, 30);

        Assert.Equal(100, terminal.Columns);
        Assert.Equal(30, terminal.Rows);
        Assert.Equal(30, terminal.VisibleLines().Length);
    }

    [Fact]
    public void HandlesScrollingAndScrollback()
    {
        using var terminal = new TerminalEmulator(columns: 80, rows: 10, scrollback: 1000);
        for (var i = 0; i < 200; i++)
        {
            terminal.Write($"line {i}\r\n");
        }

        Assert.True(terminal.Terminal.Buffer.Length > terminal.Rows);
        Assert.Contains(terminal.VisibleLines(), line => line.Contains("line 199"));
    }

    [Fact]
    public void OneMegabyteAtLineRateStaysResponsive()
    {
        using var terminal = new TerminalEmulator(columns: 120, rows: 40);
        var chunk = Encoding.UTF8.GetBytes(string.Concat(
            Enumerable.Repeat("The quick brown fox jumps over the lazy dog 0123456789\r\n", 1000)));

        var watch = Stopwatch.StartNew();
        var bytes = Feed(terminal, chunk, 1_048_576);
        watch.Stop();

        Assert.True(bytes >= 1_048_576);
        Assert.True(
            watch.Elapsed < TimeSpan.FromSeconds(15),
            $"1 MB took {watch.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"1MB: {bytes} bytes in {watch.Elapsed.TotalMilliseconds:F1} ms");
    }

    [Fact]
    public void TenMegabyteBurstStaysResponsive()
    {
        using var terminal = new TerminalEmulator(columns: 120, rows: 40);
        var chunk = Encoding.UTF8.GetBytes(string.Concat(
            Enumerable.Repeat("Burst output 0123456789 abcdefghijklmnopqrstuvwxyz\r\n", 1000)));

        var watch = Stopwatch.StartNew();
        var bytes = Feed(terminal, chunk, 10_485_760);
        watch.Stop();

        Assert.True(bytes >= 10_485_760);
        Assert.True(
            watch.Elapsed < TimeSpan.FromSeconds(20),
            $"10 MB took {watch.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"10MB: {bytes} bytes in {watch.Elapsed.TotalMilliseconds:F1} ms");
    }

    private static long Feed(TerminalEmulator terminal, byte[] chunk, long target)
    {
        long total = 0;
        while (total < target)
        {
            terminal.Write(chunk);
            total += chunk.Length;
        }

        return total;
    }
}
