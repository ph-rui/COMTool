using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;
using Iciclecreek.Terminal;

namespace COMTool.App.Tests;

public sealed class TerminalPaletteTests
{
    // r54: the terminal palette is 1:1 with the user's MobaXterm v22.1
    // (light skin) as recorded in its MobaXterm.ini [Colors] section and
    // verified against a full-screen capture (terminal background #1c1c1c,
    // prompt text #ececec, cursor #b4b4c0). r58: the user's ini overrides
    // Magenta=255,77,255 / BoldMagenta=255,125,255 (#ff4dff / #ff7dff),
    // confirmed by the same-board ifconfig capture.
    private static readonly int[] ExpectedPalette =
    {
        0x000000, // Black
        0xff6060, // Red
        0x60ff60, // Green
        0xffff36, // Yellow
        0x6060ff, // Blue
        0xff4dff, // Magenta
        0x36ffff, // Cyan
        0xececec, // White
        0x363636, // BrightBlack
        0xff8080, // BrightRed
        0x80ff80, // BrightGreen
        0xffff80, // BrightYellow
        0x8080ff, // BrightBlue
        0xff7dff, // BrightMagenta
        0x80ffff, // BrightCyan
        0xffffff, // BrightWhite
    };

    [AvaloniaFact]
    public void TERM02_BaseAnsiPaletteMatchesMobaXtermDefaultAndKeepsPromptContrast()
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
                Dispatcher.UIThread.RunJobs();

                var terminal = CreateTerminal(window);
                var viewModel = (MainViewModel)window.DataContext!;
                viewModel.AddTab(TestTab.Create(terminal));
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var colors = terminal.Terminal.Colors;
                for (var index = 0; index < ExpectedPalette.Length; index++)
                {
                    Assert.Equal(ExpectedPalette[index], colors[index] & 0xffffff);
                }

                Assert.Equal(0xececec, colors.Foreground & 0xffffff);
                Assert.Equal(0x1c1c1c, colors.Background & 0xffffff);
                Assert.Equal(0xb4b4c0, colors.Cursor & 0xffffff);

                // Prompt and directory names normally use ANSI blue or bright
                // blue. MobaXterm's own default blue (#6060ff) on its default
                // background (#1c1c1c) measures 3.74:1 — require at least that
                // so we stay on MobaXterm's palette instead of shifting hues to
                // chase a stricter ratio. Bright blue stays well above 4.5.
                Assert.True(
                    Contrast(colors[4], colors.Background) >= 3.5,
                    $"ANSI blue contrast was {Contrast(colors[4], colors.Background):0.00}");
                Assert.True(
                    Contrast(colors[12], colors.Background) >= 4.5,
                    $"ANSI bright-blue contrast was {Contrast(colors[12], colors.Background):0.00}");
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static TerminalControl CreateTerminal(MainWindow window)
    {
        var factory = typeof(MainWindow).GetMethod(
            "CreateTerminal",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(factory);

        var terminal = Assert.IsType<TerminalControl>(factory.Invoke(window, null));
        Assert.Equal(string.Empty, terminal.Process);
        return terminal;
    }

    private static double Contrast(int foreground, int background)
    {
        var lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        var darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(int rgb)
    {
        static double Channel(int value)
        {
            var normalized = value / 255.0;
            return normalized <= 0.04045
                ? normalized / 12.92
                : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel((rgb >> 16) & 0xff)
            + 0.7152 * Channel((rgb >> 8) & 0xff)
            + 0.0722 * Channel(rgb & 0xff);
    }
}
