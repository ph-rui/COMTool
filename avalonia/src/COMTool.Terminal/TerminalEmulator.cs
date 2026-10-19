using XTerm;
using XTerm.Buffer;
using XTerm.Options;
using XTerm.Selection;
using XTermTerminal = XTerm.Terminal;

namespace COMTool.Terminal;

public readonly record struct TerminalCell(
    string Content,
    int Width,
    int Foreground,
    int Background,
    int Extended);

/// <summary>
/// Thin, testable wrapper around XTerm.NET. The Avalonia control owns
/// rendering and input; this type is the shared parser/screen used for
/// golden tests, performance checks and headless validation.
/// </summary>
public sealed class TerminalEmulator : IDisposable
{
    private readonly XTermTerminal _terminal;

    public TerminalEmulator(int columns = 120, int rows = 40, int scrollback = 10000)
    {
        if (columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }

        if (rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        if (scrollback < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scrollback));
        }

        _terminal = new XTermTerminal(new TerminalOptions
        {
            Cols = columns,
            Rows = rows,
            Scrollback = scrollback,
            TermName = "xterm-256color",
            ConvertEol = false,
            Wraparound = true,
        });
    }

    public XTermTerminal Terminal => _terminal;

    public int Columns => _terminal.Cols;

    public int Rows => _terminal.Rows;

    public int CursorX => _terminal.Buffer.X;

    public int CursorY => _terminal.Buffer.Y;

    public bool CursorVisible => _terminal.CursorVisible;

    public void Write(ReadOnlySpan<byte> bytes) => _terminal.Write(bytes);

    public void Write(string text) => _terminal.Write(text);

    public void Resize(int columns, int rows)
    {
        if (columns <= 0 || rows <= 0)
        {
            return;
        }

        _terminal.Resize(columns, rows);
    }

    public string[] VisibleLines() => _terminal.GetVisibleLines();

    public string LineText(int row)
    {
        if (row < 0 || row >= _terminal.Buffer.Length)
        {
            return "";
        }

        var line = _terminal.Buffer.GetLine(row);
        return line is null ? "" : line.TranslateToString(true, 0, line.Length);
    }

    public TerminalCell GetCell(int row, int column)
    {
        var line = _terminal.Buffer.GetLine(row);
        if (line is null || column < 0 || column >= line.Length)
        {
            return default;
        }

        var cell = line[column];
        return new TerminalCell(
            cell.Content ?? "",
            cell.Width,
            cell.Attributes.Fg,
            cell.Attributes.Bg,
            cell.Attributes.Extended);
    }

    public string Select(int startColumn, int startRow, int endColumn, int endRow)
    {
        _terminal.Selection.StartSelection(
            startRow,
            startColumn,
            SelectionMode.Normal);
        _terminal.Selection.UpdateSelection(endColumn, endRow);
        _terminal.Selection.EndSelection();
        return _terminal.Selection.GetSelectionText();
    }

    public bool HasSelection => _terminal.Selection.HasSelection;

    public string SelectionText => _terminal.Selection.GetSelectionText();

    public void ClearSelection() => _terminal.Selection.ClearSelection();

    public int PaletteColor(int index) => _terminal.Colors[index];

    public bool BracketedPasteMode => _terminal.BracketedPasteMode;

    public void Dispose() => _terminal.Dispose();
}
