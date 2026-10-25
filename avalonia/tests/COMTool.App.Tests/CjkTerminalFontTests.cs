using Avalonia.Headless.XUnit;
using Avalonia.Media;
using COMTool.App.Views;

namespace COMTool.App.Tests;

/// <summary>
/// The terminal face must match MobaXterm's default (Consolas 10pt). Consolas
/// carries no CJK glyphs, so the family list ends with the bundled Noto Sans
/// Mono CJK SC: on Windows Chinese falls back to Microsoft YaHei UI (like
/// MobaXterm does), and the bundled face guarantees coverage everywhere else
/// instead of rendering .notdef.
/// </summary>
public sealed class CjkTerminalFontTests
{
    [AvaloniaFact]
    public void TERM17_TerminalFontMatchesMobaxterm()
    {
        var family = MainWindow.TerminalFontFamily().ToString();

        Assert.Contains("Consolas", family);
        Assert.Contains("Microsoft YaHei UI", family);
        Assert.Contains("Noto Sans Mono CJK SC", family);
        Assert.Equal(13.5, MainWindow.TerminalFontSize);
    }

    [AvaloniaFact]
    public void TERM17_BundledFallbackCoversBothScripts()
    {
        // Chinese must never fall through to .notdef on a machine without
        // YaHei: the bundled face has to answer for latin, CJK and CJK
        // punctuation on its own.
        var bundled = new FontFamily(
            "avares://COMTool.App/Assets/Fonts/NotoSansMonoCJKsc-Regular.otf#Noto Sans Mono CJK SC");
        var glyphs = new Typeface(bundled, FontStyle.Normal, FontWeight.Normal).GlyphTypeface;

        Assert.NotNull(glyphs);
        var map = glyphs.CharacterToGlyphMap;
        Assert.True(map.TryGetGlyph('L', out var latin) && latin != 0, "latin glyph missing");
        Assert.True(map.TryGetGlyph('個', out var cjk) && cjk != 0, "CJK glyph missing");
        Assert.True(map.TryGetGlyph('。', out var punct) && punct != 0, "CJK punctuation missing");
    }
}
