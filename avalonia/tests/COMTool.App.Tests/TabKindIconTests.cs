using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;
using Iciclecreek.Terminal;

namespace COMTool.App.Tests;

/// <summary>
/// MobaXterm-style per-session tab icons (r44): each tab head shows a type
/// specific glyph so serial / SSH / telnet / TCP / local are distinguishable
/// at a glance instead of a generic status dot. The glyphs and colours match
/// the "new session" menu and the sidebar, and the SSH icon is the gold key
/// MobaXterm uses.
/// </summary>
public sealed class TabKindIconTests
{
    private static (SessionKind Kind, string Glyph, Color Color)[] Expected()
    {
        return new[]
        {
            (SessionKind.Ssh, "\uf084", Color.Parse("#f0b429")),
            (SessionKind.Serial, "\uf1e6", Color.Parse("#43a047")),
            (SessionKind.Telnet, "\uf120", Color.Parse("#00897b")),
            (SessionKind.Tcp, "\uf0ac", Color.Parse("#f57c00")),
            (SessionKind.Local, "\uf108", Color.Parse("#607d8b")),
        };
    }

    [AvaloniaFact]
    public void UI05_TerminalTabKindIconGlyphPerKind()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            foreach (var (kind, glyph, colour) in Expected())
            {
                var tab = MakeTab(kind);
                Assert.Equal(glyph, tab.KindIconGlyph);
                var brush = Assert.IsAssignableFrom<ISolidColorBrush>(tab.KindIconBrush);
                Assert.Equal(colour, brush.Color);
            }
        });
    }

    [AvaloniaFact]
    public void UI05_SelectedTabKeepsKindIconColor()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            // r48：MobaXterm 的选中 tab 只换底色，类型图标保持原色（金色钥匙/绿色插头，
            // 用户 MobaXterm 截图佐证）；r44 曾实现「选中变白」，用户反馈不符合预期，改回恒定类型色。
            var tab = MakeTab(SessionKind.Ssh);
            var gold = Assert.IsAssignableFrom<ISolidColorBrush>(tab.KindIconBrush);
            Assert.Equal(Color.Parse("#f0b429"), gold.Color);

            tab.IsSelected = true;
            var stillGold = Assert.IsAssignableFrom<ISolidColorBrush>(tab.KindIconBrush);
            Assert.Equal(Color.Parse("#f0b429"), stillGold.Color);

            tab.IsSelected = false;
            Assert.Equal(Color.Parse("#f0b429"),
                ((ISolidColorBrush)tab.KindIconBrush).Color);
        });
    }

    [AvaloniaFact]
    public void UI05_TabTemplateShowsKindIconGlyph()
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
                RunJobs(window);

                var viewModel = (MainViewModel)window.DataContext!;
                var ssh = MakeTab(SessionKind.Ssh);
                var serial = MakeTab(SessionKind.Serial);
                viewModel.AddTab(ssh);
                viewModel.AddTab(serial);
                RunJobs(window);

                AssertIconInTab(window, ssh, "\uf084");
                AssertIconInTab(window, serial, "\uf1e6");

                // 类型图标替代了原来统一的绿色状态圆点：标签里不应再出现 Ellipse。
                foreach (var tab in new[] { ssh, serial })
                {
                    var border = TabBorder(window, tab);
                    Assert.Empty(border.GetVisualDescendants().OfType<Ellipse>());
                }
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static TerminalTab MakeTab(SessionKind kind)
    {
        var terminal = new TerminalControl
        {
            Width = 300,
            Height = 100,
            Process = string.Empty,
            FontFamily = MainWindow.TerminalFontFamily(),
            FontSize = MainWindow.TerminalFontSize,
        };
        terminal.ApplyTemplate();
        var tab = new TerminalTab
        {
            Title = kind.ToString(),
            SessionId = "tabicon-" + kind,
            Kind = kind,
            Content = terminal,
            Terminal = terminal,
            CloseAction = () => { },
        };
        return tab;
    }

    private static Border TabBorder(MainWindow window, TerminalTab tab)
    {
        var border = window.GetVisualDescendants()
            .OfType<Border>()
            .Single(candidate => ReferenceEquals(candidate.Tag, tab));
        return border;
    }

    private static void AssertIconInTab(MainWindow window, TerminalTab tab, string glyph)
    {
        var border = TabBorder(window, tab);
        var icon = border.GetVisualDescendants().OfType<TextBlock>()
            .Single(tb => tb.Classes.Contains("TabKindIcon"));
        Assert.Equal(glyph, icon.Text);
        Assert.True(icon.FontFamily?.ToString()?.Contains("fontawesome6-solid", StringComparison.OrdinalIgnoreCase) == true,
            "icon font family is not the bundled FontAwesome");
    }

    private static void RunJobs(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
