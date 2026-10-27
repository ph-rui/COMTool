using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using Iciclecreek.Terminal;

namespace COMTool.App.Tests;

/// <summary>
/// The terminal face must match MobaXterm's default (Consolas 10pt). Consolas
/// carries no CJK glyphs, so the family list ends with the bundled Noto Sans
/// Mono CJK SC: on Windows Chinese falls back to Microsoft YaHei UI (like
/// MobaXterm does), and the bundled face guarantees coverage everywhere else
/// instead of rendering .notdef. Ctrl+wheel resizes the terminal font like
/// MobaXterm (TERM-18).
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

    [AvaloniaFact]
    public void TERM18_CtrlWheelStepsAndClampsTheTerminalFontSize()
    {
        // The pure step/clamp contract: ±1 per wheel notch, clamped to
        // [8, 48], zero delta is a no-op.
        Assert.Equal(14.5, MainWindow.AdjustTerminalFontSize(13.5, 1));
        Assert.Equal(12.5, MainWindow.AdjustTerminalFontSize(13.5, -1));
        Assert.Equal(13.5, MainWindow.AdjustTerminalFontSize(13.5, 0));
        Assert.Equal(MainWindow.TerminalFontSizeMax,
            MainWindow.AdjustTerminalFontSize(MainWindow.TerminalFontSizeMax, 1));
        Assert.Equal(MainWindow.TerminalFontSizeMin,
            MainWindow.AdjustTerminalFontSize(MainWindow.TerminalFontSizeMin, -1));
    }

    [AvaloniaFact]
    public void TERM18_CtrlWheelOverTerminalChangesItsFontSize()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                var viewModel = (MainViewModel)window.DataContext!;

                var terminal = new TerminalControl
                {
                    Width = 300,
                    Height = 100,
                    FontFamily = MainWindow.TerminalFontFamily(),
                    FontSize = MainWindow.TerminalFontSize,
                };
                var tab = new TerminalTab
                {
                    Title = "ctrl-wheel",
                    SessionId = "ctrl-wheel",
                    Kind = SessionKind.Local,
                    Content = terminal,
                    Terminal = terminal,
                    CloseAction = () => { },
                };
                viewModel.AddTab(tab);
                Dispatcher.UIThread.RunJobs();
                terminal.ApplyTemplate();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(MainWindow.TerminalFontSize, terminal.FontSize);

                // Ctrl + wheel up over the terminal: +1.
                terminal.RaiseEvent(WheelEvent(terminal, window, 1));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(MainWindow.TerminalFontSize + 1, terminal.FontSize);

                // Ctrl + wheel down: back to the base size.
                terminal.RaiseEvent(WheelEvent(terminal, window, -1));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(MainWindow.TerminalFontSize, terminal.FontSize);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static PointerWheelEventArgs WheelEvent(
        TerminalControl terminal, MainWindow window, int deltaY)
    {
        return new PointerWheelEventArgs(
            terminal,
            new Pointer(0, PointerType.Mouse, isPrimary: true),
            window,
            new Point(10, 10),
            0,
            new PointerPointProperties(RawInputModifiers.Control, PointerUpdateKind.Other),
            KeyModifiers.Control,
            new Vector(0, deltaY));
    }
}
