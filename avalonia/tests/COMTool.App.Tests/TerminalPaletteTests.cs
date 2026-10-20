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
    [AvaloniaFact]
    public void TERM02_BaseAnsiPaletteMatchesPythonAndPromptTextKeepsContrast()
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
                var expected = new[]
                {
                    0x212121, 0xf44336, 0x4caf50, 0xa1887f,
                    0x2196f3, 0xe85aad, 0x26c6da, 0xcacaca,
                    0x666666, 0xff6b6b, 0x7ee787, 0xffff66,
                    0x6cb6ff, 0xff7cd6, 0x66e0ff, 0xffffff,
                };

                for (var index = 0; index < expected.Length; index++)
                {
                    Assert.Equal(expected[index], colors[index] & 0xffffff);
                }

                Assert.Equal(0xffffff, colors.Foreground & 0xffffff);
                Assert.Equal(0x212121, colors.Background & 0xffffff);
                Assert.Equal(0x4caf50, colors.Cursor & 0xffffff);

                // Prompt and directory names normally use ANSI blue or bright
                // blue. XTerm.NET's stock palette rendered both almost black on
                // #212121; require readable WCAG contrast after applying the
                // Python palette.
                Assert.True(
                    Contrast(colors[4], colors.Background) >= 4.5,
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
