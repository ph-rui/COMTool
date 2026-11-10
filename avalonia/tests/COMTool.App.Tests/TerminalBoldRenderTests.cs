using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Terminal;
using Iciclecreek.Terminal;
using Xunit;

namespace COMTool.App.Tests;

/// <summary>
/// r56：SGR 粗体（SGR 1）渲染对齐 MobaXterm。用户同板（imx95evk ifconfig）
/// 对照截图取证：远端 ifconfig 自带 SGR 粗体标数值，MobaXterm 字体无粗体面，
/// 粗体视觉无效果（细体、不变亮）；COMTool 原用 Consolas 粗体面画成粗字。
/// BoldFont=false（COMTool 默认）：常规字重 + 不提亮；
/// BoldFont=true：保留原行为（粗体面 + 提亮），供回退。
/// </summary>
public sealed class TerminalBoldRenderTests
{
    // SGR 1 粗体（默认色）+ SGR 1;35 粗体品红
    private const string Probe = "\x1b[1mBOLD\x1b[0m \x1b[1;35mBMAG\x1b[0m\r\n";

    private sealed record RunInfo(FontWeight Weight, Color Color);

    private static (TerminalControl Terminal, MainWindow Window) CreateBoldTab(
        TestEnvironment env, bool boldFont)
    {
        MainWindow window = null!;
        TerminalControl terminal = null!;
        env.Run(() =>
        {
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm };
            window.Show();
            window.StopPortTimerForTests();
            terminal = window.CreateTerminal();
            terminal.BoldFont = boldFont;
            vm.AddTab(new TerminalTab
            {
                Title = boldFont ? "boldon" : "boldoff",
                SessionId = boldFont ? "boldon" : "boldoff",
                Kind = SessionKind.Local,
                Content = terminal,
                Terminal = terminal,
                IsAlive = true,
                IsTransportOpen = () => true,
                CloseAction = () => { },
            });
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        });
        return (terminal, window);
    }

    private static bool BufferContains(TerminalControl terminal, string needle)
    {
        var term = terminal.Terminal;
        if (term is null || term.Buffer is null)
        {
            return false;
        }
        for (var row = 0; row < term.Rows; row++)
        {
            var line = term.Buffer.GetLine(row);
            if (line is not null && line.TranslateToString(true, 0).Contains(needle))
            {
                return true;
            }
        }
        return false;
    }

    private static List<RunInfo> CollectRuns(TerminalControl terminal)
    {
        var view = terminal.GetVisualDescendants()
            .OfType<TerminalView>().Single();
        var group = new DrawingGroup();
        using (var ctx = group.Open())
        {
            view.Render(ctx);
        }
        var runs = new List<RunInfo>();
        foreach (var d in Flatten(group))
        {
            if (d is GlyphRunDrawing grd && grd.GlyphRun is { } gr)
            {
                var color = grd.Foreground is ISolidColorBrush solid ? solid.Color : default;
                runs.Add(new RunInfo(gr.GlyphTypeface.Weight, color));
            }
        }
        return runs;
    }

    private static IEnumerable<Drawing> Flatten(Drawing drawing)
    {
        if (drawing is DrawingGroup group)
        {
            foreach (var child in group.Children)
            {
                foreach (var d in Flatten(child))
                {
                    yield return d;
                }
            }
        }
        else
        {
            yield return drawing;
        }
    }

    private static void WriteAndSettle(TerminalControl terminal, TestEnvironment env, MainWindow window)
    {
        Assert.NotNull(terminal.Terminal);
        var term = terminal.Terminal;
        // 显式套用 MobaXterm 16 色主题，保证调色板索引色确定（与 App 一致）
        term.Colors.ApplyTheme(TerminalTheme.CreateMobaXtermTheme());
        env.Run(() => term.Write(Probe));
        for (var i = 0; i < 100 && !BufferContains(terminal, "BOLD"); i++)
        {
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(20);
        }
        Assert.True(BufferContains(terminal, "BOLD"), "probe text did not reach the buffer");
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void TERM35_BoldFontOffDrawsNormalWeightPlainColor()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            var (terminal, win) = CreateBoldTab(env, boldFont: false);
            window = win;
            Assert.False(terminal.BoldFont, "COMTool 默认应为 BoldFont=false（对齐 MobaXterm）");

            WriteAndSettle(terminal, env, window);

            var runs = CollectRuns(terminal);
            Assert.NotEmpty(runs);
            // 全部 run 均不得出现粗体面
            Assert.All(runs, r => Assert.True(
                r.Weight != FontWeight.Bold && r.Weight != FontWeight.ExtraBold
                    && r.Weight != FontWeight.Black,
                $"BoldFont=false 不应出现粗体面: {r.Weight}"));
            // SGR 1;35（粗体品红）按普通品红 #ff4dff 画，不提亮为 #ff7dff
            var colors = runs.Where(r => r.Color.A > 0).Select(r => r.Color).ToList();
            Assert.Contains(Color.Parse("#ff4dff"), colors);
            Assert.DoesNotContain(Color.Parse("#ff7dff"), colors);
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TERM35_BoldFontOnBrightensBoldColor()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            var (terminal, win) = CreateBoldTab(env, boldFont: true);
            window = win;

            WriteAndSettle(terminal, env, window);

            var runs = CollectRuns(terminal);
            Assert.NotEmpty(runs);
            // 粗体品红提亮为亮品红 #ff7dff，且不出现普通品红。
            // 只断言颜色（调色板确定）；字重取决于本机字体解析，不做跨平台断言。
            var colors = runs.Where(r => r.Color.A > 0).Select(r => r.Color).ToList();
            Assert.Contains(Color.Parse("#ff7dff"), colors);
            Assert.DoesNotContain(Color.Parse("#ff4dff"), colors);
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
