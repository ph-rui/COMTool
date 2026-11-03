using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;
using Iciclecreek.Terminal;

namespace COMTool.App.Tests;

/// <summary>
/// The terminal face must match MobaXterm's default (Consolas 10pt). Consolas
/// carries no CJK glyphs, so the family list falls back to the bundled Noto
/// Sans Mono CJK SC first (r41: terminal-grade CJK face, lighter strokes like
/// MobaXterm, 0.5em/1em metrics keep the r33 two-cell alignment), with
/// Microsoft YaHei UI as last-resort system fallback. Ctrl+wheel resizes the
/// terminal font like MobaXterm (TERM-18).
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
        // r41：CJK 必须先落到内置 Noto（笔画细、跨平台一致），雅黑只做最后兜底
        Assert.True(
            family.IndexOf("Noto Sans Mono CJK SC", StringComparison.Ordinal)
            < family.IndexOf("Microsoft YaHei UI", StringComparison.Ordinal));
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
    public void TERM22_WideCjkGlyphIsPinnedToTwoCellAdvances()
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

                // Add the terminal to the window's visual tree so its template (the inner
                // TerminalView) is realised, then drive a layout pass.
                var terminal = AddTerminalTab(viewModel, "term22");
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var view = terminal.GetVisualDescendants().OfType<TerminalView>().Single();

                // Metrics must be resolvable so the fast path can pin advances to cells.
                var cell = view.CharWidth;
                Assert.True(cell > 0);

                // A lone full-width CJK character occupies two cells. It must take the glyph fast
                // path and be emitted as ONE glyph whose advance is exactly TWO cell widths -- not
                // the fallback font's natural (proportional) width. The natural width is what makes
                // mixed CJK+Latin lines drift off the grid on Windows (CJK -> Microsoft YaHei UI).
                var run = view.TryBuildGlyphRun("個", 2, FontStyle.Normal, FontWeight.Normal);
                Assert.NotNull(run);

                var glyphs = run!.GlyphInfos.ToList();
                Assert.Single(glyphs);
                Assert.Equal(cell * 2, glyphs[0].GlyphAdvance, precision: 4);

                // Sanity: a width-1 run of one latin char still pins to a single cell.
                var latin = view.TryBuildGlyphRun("A", 1, FontStyle.Normal, FontWeight.Normal);
                Assert.NotNull(latin);
                Assert.Equal(cell, latin!.GlyphInfos.Single().GlyphAdvance, precision: 4);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
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
    public void TERM18_ClampTerminalFontSizeFallsBackAndBounds()
    {
        Assert.Equal(13.5, MainWindow.ClampTerminalFontSize(0));
        Assert.Equal(13.5, MainWindow.ClampTerminalFontSize(double.NaN));
        Assert.Equal(8, MainWindow.ClampTerminalFontSize(1));
        Assert.Equal(48, MainWindow.ClampTerminalFontSize(200));
        Assert.Equal(18, MainWindow.ClampTerminalFontSize(18));
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

    [AvaloniaFact]
    public void TERM18_CtrlWheelZoomAppliesToEveryOpenTerminalAndPersists()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                var viewModel = (MainViewModel)window.DataContext!;

                var first = AddTerminalTab(viewModel, "zoom-a");
                var second = AddTerminalTab(viewModel, "zoom-b");
                Dispatcher.UIThread.RunJobs();

                // MobaXterm 的终端字号是全局设置：缩放一个终端，其他终端同步。
                // 只有选中标签的终端在可视树里，事件要打在它上面。
                second.RaiseEvent(WheelEvent(second, window, 1));
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(MainWindow.TerminalFontSize + 1, first.FontSize);
                Assert.Equal(MainWindow.TerminalFontSize + 1, second.FontSize);

                // 同时写进 config.json，重启后仍用这个字号。
                var saved = ComtoolConfig.Load(
                    Path.Combine(env.AvaloniaConfigDir, "config.json"));
                Assert.Equal(MainWindow.TerminalFontSize + 1, saved.Terminal.FontSize);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TERM18_SavedFontSizeIsUsedByTerminalsAfterRestart()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig
                {
                    Terminal = new TerminalConfig { FontSize = 18 },
                });
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                Dispatcher.UIThread.RunJobs();

                // 重启后新建的终端直接用记忆的字号。
                var terminal = window.CreateTerminal();
                Assert.Equal(18, terminal.FontSize);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static TerminalControl AddTerminalTab(MainViewModel viewModel, string id)
    {
        var terminal = new TerminalControl
        {
            Width = 300,
            Height = 100,
            FontFamily = MainWindow.TerminalFontFamily(),
            FontSize = MainWindow.TerminalFontSize,
        };
        viewModel.AddTab(new TerminalTab
        {
            Title = id,
            SessionId = id,
            Kind = SessionKind.Local,
            Content = terminal,
            Terminal = terminal,
            CloseAction = () => { },
        });
        terminal.ApplyTemplate();
        return terminal;
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
