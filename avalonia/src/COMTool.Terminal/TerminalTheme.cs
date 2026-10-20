using XTerm.Options;

namespace COMTool.Terminal;

/// <summary>
/// Terminal colors are kept in sync with the Python reference terminal
/// (COMTool/plugins/terminal.py). The base 16 ANSI colors are deliberately
/// explicit: XTerm.NET's stock palette uses very dark blue values that are
/// almost unreadable on the dark terminal background.
/// </summary>
public static class TerminalTheme
{
    public const string Background = "#212121";
    public const string Foreground = "#ffffff";
    public const string Cursor = "#4caf50";

    public static ThemeOptions CreatePythonTheme() => new()
    {
        Background = Background,
        Foreground = Foreground,
        Cursor = Cursor,
        Black = "#212121",
        Red = "#f44336",
        Green = "#4caf50",
        Yellow = "#a1887f",
        Blue = "#2196f3",
        Magenta = "#e85aad",
        Cyan = "#26c6da",
        White = "#cacaca",
        BrightBlack = "#666666",
        BrightRed = "#ff6b6b",
        BrightGreen = "#7ee787",
        BrightYellow = "#ffff66",
        BrightBlue = "#6cb6ff",
        BrightMagenta = "#ff7cd6",
        BrightCyan = "#66e0ff",
        BrightWhite = "#ffffff",
    };
}
