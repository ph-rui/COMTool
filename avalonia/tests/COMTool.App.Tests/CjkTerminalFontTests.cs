using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using COMTool.App.Views;

namespace COMTool.App.Tests;

/// <summary>
/// Mixed Chinese/English terminal output must share one baseline. The
/// terminal control shapes every run independently, and a CJK run shaped from
/// a latin-only primary font falls back to a CJK font with different line
/// metrics, so the CJK row sat a few pixels below the latin rows (the
/// "uneven heights" on mixed output). The terminal font is now the bundled
/// Noto Sans Mono CJK SC, which covers both scripts on one face grid, so no
/// fallback can happen and every baseline agrees by construction.
/// </summary>
public sealed class CjkTerminalFontTests
{
    [AvaloniaFact]
    public void TERM17_TerminalFontIsTheBundledCjkMono()
    {
        var family = MainWindow.TerminalFontFamily();

        Assert.Contains("Noto Sans Mono CJK SC", family.FamilyNames);
        // Both weights are registered as avares sources, so bold cells stay
        // inside the bundled family instead of escaping to a system font.
        Assert.Contains("NotoSansMonoCJKsc-Regular.otf", family.ToString());
        Assert.Contains("NotoSansMonoCJKsc-Bold.otf", family.ToString());

        var regular = new Typeface(family, FontStyle.Normal, FontWeight.Normal).GlyphTypeface;
        var bold = new Typeface(family, FontStyle.Normal, FontWeight.Bold).GlyphTypeface;
        Assert.NotNull(regular);
        Assert.NotNull(bold);
        Assert.Equal("Noto Sans Mono CJK SC", regular.FamilyName);
        Assert.Equal("Noto Sans Mono CJK SC", bold.FamilyName);
    }

    [AvaloniaFact]
    public void TERM17_OneFontAnswersForBothScripts()
    {
        // The structural fix: the terminal font itself carries the CJK glyphs,
        // so a CJK run never falls back to a second font with different line
        // metrics. Before the fix the primary face (Consolas/DejaVu) had no
        // CJK and these maps returned .notdef.
        var regular = new Typeface(MainWindow.TerminalFontFamily(), FontStyle.Normal, FontWeight.Normal).GlyphTypeface!;
        var map = regular.CharacterToGlyphMap;

        Assert.True(map.TryGetGlyph('L', out var latin) && latin != 0, "latin glyph missing from the terminal font");
        Assert.True(map.TryGetGlyph('個', out var cjk) && cjk != 0, "CJK glyph missing from the terminal font (fallback would occur)");
        Assert.True(map.TryGetGlyph('。', out var punct) && punct != 0, "CJK punctuation missing from the terminal font");
    }

    [AvaloniaFact]
    public void TERM17_LatinAndCjkRunsShareTheLineMetrics()
    {
        var family = MainWindow.TerminalFontFamily();
        var typeface = new Typeface(family, FontStyle.Normal, FontWeight.Normal);
        var brush = Brushes.Black;

        // With one font answering for both scripts, the line metrics — and
        // therefore the baseline the renderer draws every run on — are
        // identical for latin-only, CJK-only and mixed runs.
        var latin = new FormattedText("L", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 15, brush);
        var cjk = new FormattedText("個", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 15, brush);
        var mixed = new FormattedText("L個L", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 15, brush);

        Assert.Equal(latin.Baseline, cjk.Baseline);
        Assert.Equal(latin.Baseline, mixed.Baseline);
        Assert.Equal(latin.Height, cjk.Height);
    }
}
