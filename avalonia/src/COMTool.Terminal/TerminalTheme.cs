using XTerm.Options;

namespace COMTool.Terminal;

/// <summary>
/// Terminal colors are 1:1 with the user's MobaXterm v22.1 (light skin), as
/// recorded in its MobaXterm.ini [Colors] section and verified against a
/// full-screen capture (r54): background #1c1c1c (measured on screen; the
/// ini's BackgroundColour=0,0,0 is the raw setting but the rendered surface
/// is #1c1c1c), foreground #ececec, cursor #b4b4c0. The base 16 ANSI colors
/// are MobaXterm's defaults (e.g. blue #6060ff), NOT XTerm.NET's stock
/// palette (whose near-black blues are unreadable) and NOT the old Python
/// reference theme (superseded in r54 by the MobaXterm 1:1 parity goal).
/// r58: the user overrides Magenta=255,77,255 / BoldMagenta=255,125,255 in
/// their ini (found on the same-board ifconfig capture: IP/MAC pixels are
/// #ff4dff), so those two slots follow the user's values instead of the
/// factory #ff36ff/#ff80ff.
/// </summary>
public static class TerminalTheme
{
    public const string Background = "#1c1c1c";
    public const string Foreground = "#ececec";
    public const string Cursor = "#b4b4c0";

    public static ThemeOptions CreateMobaXtermTheme() => new()
    {
        Background = Background,
        Foreground = Foreground,
        Cursor = Cursor,
        Black = "#000000",
        Red = "#ff6060",
        Green = "#60ff60",
        Yellow = "#ffff36",
        Blue = "#6060ff",
        Magenta = "#ff4dff",
        Cyan = "#36ffff",
        White = "#ececec",
        BrightBlack = "#363636",
        BrightRed = "#ff8080",
        BrightGreen = "#80ff80",
        BrightYellow = "#ffff80",
        BrightBlue = "#8080ff",
        BrightMagenta = "#ff7dff",
        BrightCyan = "#80ffff",
        BrightWhite = "#ffffff",
    };
}
