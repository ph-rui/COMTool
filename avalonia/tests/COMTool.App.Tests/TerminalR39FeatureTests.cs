using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Iciclecreek.Terminal;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;
using Xunit;

namespace COMTool.App.Tests;

/// <summary>
/// r39：终端显示子菜单（TERM-29）/ 语法高亮（TERM-30）/ 特殊命令（TERM-31）。
/// 语义对齐 MobaXterm 22.1 终端右键三个子菜单（用户截图 + 官方 CustomSyntax 默认规则）。
/// </summary>
public sealed class TerminalR39FeatureTests
{
    // ───────────────── 本地助手（隔离复制 TerminalMenuFeatureTests 模式）─────────────────

    private static (TerminalControl terminal, InMemoryPty pty, TerminalTab tab) CreateTestTerminal(
        MainWindow window, string id, SessionKind kind = SessionKind.Ssh)
    {
        var vm = (MainViewModel)window.DataContext!;
        var terminal = window.CreateTerminal();
        var pty = new InMemoryPty();
        var tab = new TerminalTab
        {
            Title = id,
            SessionId = id,
            Kind = kind,
            Content = terminal,
            Terminal = terminal,
            IsAlive = true,
            IsTransportOpen = () => true,
            CloseAction = () => { },
        };
        vm.AddTab(tab);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        terminal.AttachConnection(pty);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (terminal, pty, tab);
    }

    private static void RaiseShiftRightClick(TerminalControl terminal, Window window)
    {
        var view = terminal.GetVisualDescendants()
            .OfType<Iciclecreek.Terminal.TerminalView>()
            .FirstOrDefault();
        var target = (Avalonia.Controls.Control)view ?? terminal;
        var position = target.TranslatePoint(new Point(10, 10), window) ?? new Point(10, 10);
        target.RaiseEvent(new PointerPressedEventArgs(
            target,
            new Pointer(0, PointerType.Mouse, isPrimary: true),
            window,
            position,
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.RightButtonPressed),
            KeyModifiers.Shift,
            clickCount: 1));
    }

    private static MenuItem ItemByLabel(MenuItem[] items, string label) =>
        items.Single(mi => ItemLabel(mi) == label);

    /// <summary>取菜单项文案：字符串 Header 直接取；带快捷键标注的取 Grid 内第一个 TextBlock。</summary>
    private static string? ItemLabel(MenuItem item) => item.Header switch
    {
        string s => s,
        Grid g => g.Children.OfType<TextBlock>().FirstOrDefault()?.Text,
        _ => null,
    };

    private static void RunJobs(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Timed out waiting for: " + what);
            }
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }

    // ═══════════════════════ TERM-29：终端显示子菜单 ═══════════════════════

    [AvaloniaFact]
    public void TERM29_DisplayMenu_StructureAndLabels()
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
                RunJobs(window);

                var (terminal, _, _) = CreateTestTerminal(window, "disp-menu");
                RaiseShiftRightClick(terminal, window);
                var menu = window.LastTerminalMenuForTests;
                Assert.NotNull(menu);

                // 顶层 19 项 + 4 分隔线不变（三项升级为带子项的 MenuItem）
                var items = menu!.Items.OfType<MenuItem>().ToArray();
                Assert.Equal(19, items.Length);
                Assert.Equal(4, menu.Items.OfType<Separator>().Count());

                // 终端显示：6 项，MobaXterm 顺序
                var display = ItemByLabel(items, "终端显示");
                var displayKids = display.Items.OfType<MenuItem>().ToList();
                Assert.Equal(new[]
                {
                    "重置终端输出", "清除终端回滚", "设置终端标题",
                    "切换终端滚动条", "全屏终端", "只读终端",
                },
                displayKids.Select(k => ItemLabel(k)).ToList());
                Assert.All(displayKids, k => Assert.True(k.IsEnabled));
                // 全屏终端带 F11 快捷键标注（右侧灰字）
                var fullGrid = Assert.IsType<Grid>(displayKids[4].Header);
                Assert.Equal("F11", fullGrid.Children.OfType<TextBlock>().Last().Text);

                // 语法高亮：6 项单选，当前方案（默认）打勾，「逐行」置灰
                var hl = ItemByLabel(items, "语法高亮");
                var hlKids = hl.Items.OfType<MenuItem>().ToList();
                Assert.Equal(new[]
                {
                    "默认", "错误/警告/成功关键字", "Unix shell 脚本",
                    "Cisco（网络配置）", "逐行", "SQL 语法",
                },
                hlKids.Select(k => ItemLabel(k)).ToList());
                Assert.All(hlKids, k => Assert.Equal(MenuItemToggleType.Radio, k.ToggleType));
                Assert.Equal("TerminalSyntaxHighlight", hlKids[0].GroupName);
                Assert.All(hlKids, k => Assert.Equal("TerminalSyntaxHighlight", k.GroupName));
                Assert.False(hlKids.Single(k => ItemLabel(k) == "逐行").IsEnabled);
                Assert.True(hlKids.Single(k => ItemLabel(k) == "默认").IsChecked);
                Assert.Single(hlKids, k => k.IsChecked);

                // 特殊命令：5 顶层 + 更多信号(14) + 忽略消息(置灰)
                var sp = ItemByLabel(items, "特殊命令");
                var spKids = sp.Items.OfType<MenuItem>().ToList();
                Assert.Equal(new[]
                {
                    "Break", "SIGINT（中断）", "SIGTERM（终止）",
                    "SIGKILL（强制结束）", "SIGHUP（挂断）", "更多信号", "忽略消息",
                },
                spKids.Select(k => ItemLabel(k)).ToList());
                Assert.Equal(14, spKids[5].Items.OfType<MenuItem>().Count());
                Assert.False(spKids[6].IsEnabled);
                // SSH 会话：协议信号全可用
                Assert.All(spKids.Take(5), k => Assert.True(k.IsEnabled));
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TERM29_ResetOutputAndClearScrollback()
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
                RunJobs(window);

                var (terminal, _, _) = CreateTestTerminal(window, "disp-rc");
                var buf = terminal.Terminal.Buffer;
                for (var i = 0; i < 60; i++)
                {
                    terminal.Terminal.Write($"line{i:00}\r\n");
                }
                RunJobs(window);
                Assert.True(buf.YBase > 0);

                // 清除终端回滚：MobaXterm 语义=同时清空当前屏+回滚（官方 changelog：
                // "clear the screen AND the scrollback buffer"）——历史丢弃且屏幕全白
                window.ClearTerminalScrollback(terminal);
                Assert.Equal(0, buf.YBase);
                var screenBlankAfterClear = true;
                for (var y = 0; y < buf.Rows; y++)
                {
                    var t = buf.GetLine(y)?.TranslateToString(true, 0, buf.Cols) ?? "";
                    if (t.Trim().Length > 0)
                    {
                        screenBlankAfterClear = false;
                    }
                }
                Assert.True(screenBlankAfterClear);

                // 重置终端输出：RIS 全复位——全空 + 光标回原点
                window.ResetTerminalOutput(terminal);
                Assert.Equal(0, buf.X);
                Assert.Equal(0, buf.Y);
                var allBlank = true;
                for (var y = 0; y < buf.Rows; y++)
                {
                    var t = buf.GetLine(y)?.TranslateToString(true, 0, buf.Cols) ?? "";
                    if (t.Trim().Length > 0)
                    {
                        allBlank = false;
                    }
                }
                Assert.True(allBlank);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task TERM29_SetTerminalTitleDialog()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                RunJobs(window);

                var (terminal, _, tab) = CreateTestTerminal(window, "old-title");
                Assert.Equal("old-title", tab.Title);

                var task = window.ShowTitleDialogForTestsAsync(tab);
                WaitUntil(() => window.LastTitleDialogForTests is not null, "title dialog");
                var dialog = window.LastTitleDialogForTests!;
                RunJobs(window);
                var input = dialog.GetVisualDescendants().OfType<TextBox>().First();
                Assert.Equal("old-title", input.Text);
                input.Text = "新标题ABC";
                var ok = dialog.GetVisualDescendants().OfType<Button>()
                    .Single(b => string.Equals(b.Content, "确定"));
                ok.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                RunJobs(window);
                await task;
                Assert.Equal("新标题ABC", tab.Title);

                // 空白输入：标题不变
                var task2 = window.ShowTitleDialogForTestsAsync(tab);
                WaitUntil(() => window.LastTitleDialogForTests is not null, "title dialog 2");
                var dialog2 = window.LastTitleDialogForTests!;
                var input2 = dialog2.GetVisualDescendants().OfType<TextBox>().First();
                input2.Text = "   ";
                dialog2.GetVisualDescendants().OfType<Button>()
                    .Single(b => string.Equals(b.Content, "确定"))
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                RunJobs(window);
                await task2;
                Assert.Equal("新标题ABC", tab.Title);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TERM29_ScrollbarToggleFlipsVisibility()
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
                RunJobs(window);

                var (terminal, _, _) = CreateTestTerminal(window, "sb");
                Assert.True(window.ToggleTerminalScrollBar(terminal));
                var v1 = terminal.GetVisualDescendants().OfType<ScrollBar>()
                    .Select(b => b.IsVisible).Distinct().Single();
                Assert.True(window.ToggleTerminalScrollBar(terminal));
                var v2 = terminal.GetVisualDescendants().OfType<ScrollBar>()
                    .Select(b => b.IsVisible).Distinct().Single();
                Assert.NotEqual(v1, v2);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TERM29_FullscreenToggle()
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
                RunJobs(window);

                // MobaXterm 行为（用户 r40 反馈）：退出全屏必须恢复到进入前的窗口态
                // （最大化进全屏 → 退出后仍最大化，而不是退回 Normal）
                window.WindowState = Avalonia.Controls.WindowState.Maximized;
                window.ToggleFullScreen();
                Assert.Equal(Avalonia.Controls.WindowState.FullScreen, window.WindowState);
                window.ToggleFullScreen();
                Assert.Equal(Avalonia.Controls.WindowState.Maximized, window.WindowState);

                window.WindowState = Avalonia.Controls.WindowState.Normal;
                window.ToggleFullScreen();
                Assert.Equal(Avalonia.Controls.WindowState.FullScreen, window.WindowState);
                window.ToggleFullScreen();
                Assert.Equal(Avalonia.Controls.WindowState.Normal, window.WindowState);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TERM29_ReadOnlyBlocksKeyboardAndPaste()
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
                RunJobs(window);

                var (terminal, pty, tab) = CreateTestTerminal(window, "ro", SessionKind.Serial);
                var view = terminal.GetVisualDescendants()
                    .OfType<Iciclecreek.Terminal.TerminalView>().First();

                // 非只读：按键不被守卫拦截
                var e1 = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.A,
                    PhysicalKey = PhysicalKey.A,
                };
                view.RaiseEvent(e1);
                Assert.False(e1.Handled);
                Assert.False(window.IsTerminalInputBlockedForTests(terminal));

                // 只读：隧道阶段拦截（库的键输入→pty 路径被阻断）
                tab.IsReadOnlyTerminal = true;
                Assert.True(window.IsTerminalInputBlockedForTests(terminal));
                var e2 = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.A,
                    PhysicalKey = PhysicalKey.A,
                };
                view.RaiseEvent(e2);
                Assert.True(e2.Handled);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // ═══════════════════════ TERM-30：语法高亮接线 ═══════════════════════

    [AvaloniaFact]
    public async Task TERM30_HighlightWiringColorsNewLines()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig
                {
                    Terminal = new TerminalConfig { SyntaxHighlight = "error_warning" },
                });
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                RunJobs(window);

                var (terminal, _, _) = CreateTestTerminal(window, "hl");
                window.AttachTerminalHighlighter(terminal);
                Assert.NotNull(window.GetHighlighterForTests(terminal));
                Assert.Equal(COMTool.Terminal.Highlight.HighlightScheme.ErrorWarningSuccess,
                    window.GetHighlighterForTests(terminal)!.Scheme);

                // LineFed 驱动着色："Error: disk full" → "Error" 红(1)，"disk" 默认(256)
                terminal.Terminal.Write("Error: disk full\r\n");
                RunJobs(window);
                var l0 = terminal.Terminal.Buffer.GetLine(0)!;
                Assert.Equal(1, l0[0].Attributes.GetFgColor());
                Assert.Equal(0, l0[0].Attributes.GetFgColorMode());
                Assert.Equal(256, l0[6].Attributes.GetFgColor());

                // 切回默认：新行不再着色
                window.SetSyntaxHighlightScheme("default");
                terminal.Terminal.Write("Error again\r\n");
                RunJobs(window);
                var l1 = terminal.Terminal.Buffer.GetLine(1)!;
                Assert.Equal(256, l1[0].Attributes.GetFgColor());

                // 再切回 error_warning：ApplyToAll 全量重染历史行
                window.SetSyntaxHighlightScheme("error_warning");
                Assert.Equal(1, terminal.Terminal.Buffer.GetLine(0)![0].Attributes.GetFgColor());
                Assert.Equal(1, l1[0].Attributes.GetFgColor());

                // 方案落盘 config.json（SaveConfig 同步，Delay(0) 仅为保 async 合法）
                await Task.Delay(0);
                var saved = COMTool.Core.Config.ComtoolConfig.Load(
                    System.IO.Path.Combine(env.AvaloniaConfigDir, "config.json"));
                Assert.Equal("error_warning", saved.Terminal.SyntaxHighlight);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // ═══════════════════════ TERM-31：特殊命令 ═══════════════════════

    [Fact]
    public void TERM31_AvailabilityMatrix()
    {
        var ssh = SessionKind.Ssh;
        var ser = SessionKind.Serial;
        var tcp = SessionKind.Tcp;
        var tel = SessionKind.Telnet;

        // SSH：协议信号，全表可用
        Assert.All(COMTool.App.Services.SpecialCommands.TopLevel,
            d => Assert.True(COMTool.App.Services.SpecialCommands.IsAvailable(d, ssh)));
        Assert.All(COMTool.App.Services.SpecialCommands.More,
            d => Assert.True(COMTool.App.Services.SpecialCommands.IsAvailable(d, ssh)));

        // 串口：Break + 控制字节等价物（^C/^\/^Z）
        var top = COMTool.App.Services.SpecialCommands.TopLevel;
        Assert.True(COMTool.App.Services.SpecialCommands.IsAvailable(top[0], ser));
        Assert.True(COMTool.App.Services.SpecialCommands.IsAvailable(top[1], ser));
        Assert.False(COMTool.App.Services.SpecialCommands.IsAvailable(top[2], ser));
        Assert.False(COMTool.App.Services.SpecialCommands.IsAvailable(top[3], ser));
        Assert.False(COMTool.App.Services.SpecialCommands.IsAvailable(top[4], ser));
        var more = COMTool.App.Services.SpecialCommands.More;
        Assert.True(COMTool.App.Services.SpecialCommands.IsAvailable(
            more.Single(d => d.Label.StartsWith("SIGTSTP")), ser));
        Assert.True(COMTool.App.Services.SpecialCommands.IsAvailable(
            more.Single(d => d.Label.StartsWith("SIGQUIT")), ser));
        Assert.False(COMTool.App.Services.SpecialCommands.IsAvailable(
            more.Single(d => d.Label.StartsWith("SIGABRT")), ser));

        // TCP/Telnet：仅控制字节
        Assert.True(COMTool.App.Services.SpecialCommands.IsAvailable(top[1], tcp));
        Assert.True(COMTool.App.Services.SpecialCommands.IsAvailable(top[1], tel));
        Assert.False(COMTool.App.Services.SpecialCommands.IsAvailable(top[0], tcp));
        Assert.False(COMTool.App.Services.SpecialCommands.IsAvailable(top[2], tel));

        // Local：无可靠信号通道，全置灰
        Assert.All(top, d => Assert.False(COMTool.App.Services.SpecialCommands.IsAvailable(d, SessionKind.Local)));
    }

    [AvaloniaFact]
    public async Task TERM31_DispatchSendsControlBytes()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                RunJobs(window);

                var (terminal, pty, tab) = CreateTestTerminal(window, "spc", SessionKind.Serial);
                var top = COMTool.App.Services.SpecialCommands.TopLevel;

                // SIGINT → 0x03 经 pty 发送路径
                await window.DispatchSpecialCommandAsync(tab, terminal, top[1]);
                RunJobs(window);
                Assert.True(pty.WriterText.Contains('\x03'));

                // SIGTERM 无控制字节等价物（串口）→ 不发送 + 错误提示对话框
                var lenBefore = pty.WriterText.Length;
                await window.DispatchSpecialCommandAsync(tab, terminal, top[2]);
                Assert.Equal(lenBefore, pty.WriterText.Length);
                WaitUntil(() => window.LastErrorDialogForTests is not null, "unsupported error dialog");
                var errDlg = window.LastErrorDialogForTests!;
                errDlg.GetVisualDescendants().OfType<Button>().First()
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                RunJobs(window);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
