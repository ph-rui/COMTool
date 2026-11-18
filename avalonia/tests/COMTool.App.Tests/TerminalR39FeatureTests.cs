using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Iciclecreek.Terminal;
using COMTool.App.Services;
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

                // 语法高亮：6 项单选（r59 新增「标准关键字」，对齐 MobaXterm
                // SyntaxType 选项；r40.2 用户决定移除「逐行」——语义无公开资料），
                // 当前方案（默认）打勾
                var hl = ItemByLabel(items, "语法高亮");
                var hlKids = hl.Items.OfType<MenuItem>().ToList();
                Assert.Equal(new[]
                {
                    "默认", "标准关键字", "错误/警告/成功关键字", "Unix shell 脚本",
                    "Cisco（网络配置）", "SQL 语法",
                },
                hlKids.Select(k => ItemLabel(k)).ToList());
                Assert.All(hlKids, k => Assert.Equal(MenuItemToggleType.Radio, k.ToggleType));
                Assert.Equal("TerminalSyntaxHighlight", hlKids[0].GroupName);
                Assert.All(hlKids, k => Assert.Equal("TerminalSyntaxHighlight", k.GroupName));
                Assert.All(hlKids, k => Assert.True(k.IsEnabled));
                Assert.True(hlKids.Single(k => ItemLabel(k) == "默认").IsChecked);
                Assert.Single(hlKids, k => k.IsChecked);

                // 特殊命令：5 顶层 + 更多信号(14)（r40.2 用户决定移除「忽略消息」）
                var sp = ItemByLabel(items, "特殊命令");
                var spKids = sp.Items.OfType<MenuItem>().ToList();
                Assert.Equal(new[]
                {
                    "Break", "SIGINT（中断）", "SIGTERM（终止）",
                    "SIGKILL（强制结束）", "SIGHUP（挂断）", "更多信号",
                },
                spKids.Select(k => ItemLabel(k)).ToList());
                Assert.Equal(14, spKids[5].Items.OfType<MenuItem>().Count());
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
                var bar = SingleScrollBar(terminal);
                // r67.7：默认就是可见的 16px 常显滚动条（此前附加时被隐藏）。
                Assert.True(bar.IsVisible);
                Assert.Equal(16, bar.Width);

                Assert.True(window.ToggleTerminalScrollBar(terminal));
                Assert.False(bar.IsVisible);
                Assert.Equal(0, bar.Width);

                Assert.True(window.ToggleTerminalScrollBar(terminal));
                Assert.True(bar.IsVisible);
                Assert.Equal(16, bar.Width);
                Assert.Equal(16, bar.MinWidth);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // ───────────────── r67.7：MobaXterm 式常显终端滚动条（TERM-36）─────────────────

    private static ScrollBar SingleScrollBar(TerminalControl terminal) =>
        terminal.GetVisualDescendants().OfType<ScrollBar>().Single();

    private static void FillScrollback(TerminalControl terminal, int lines = 120)
    {
        for (var i = 0; i < lines; i++)
        {
            terminal.Terminal.Write($"line{i:000}\r\n");
        }
    }

    /// <summary>通过 PTY 输出灌满回滚（库只在真实输出/属性通知时刷新 ScrollBar 的
    /// Maximum/Value；直接写 TerminalBuffer 不会触发，headless 里须走 PTY 路径）。</summary>
    private static void PumpScrollback(InMemoryPty pty, TerminalControl terminal, int lines = 120)
    {
        for (var i = 0; i < lines; i++)
        {
            pty.Send($"line{i:000}\r\n");
        }

        var deadline = System.DateTime.UtcNow.AddSeconds(5);
        while (System.DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (terminal.MaxScrollback > 20 && terminal.ViewportY == terminal.MaxScrollback)
            {
                break;
            }
            Thread.Sleep(20);
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>r67.7：滚动条默认常显、16px 宽、贴终端右缘（MobaXterm 实机采样），
    /// 且浅色主题用亮灰轨道 #EDEDED / 滑块 #BDBDBD，而不是 Fluent 的悬停才浮现。</summary>
    [AvaloniaFact]
    public void TERM36_ScrollBarAlwaysVisibleAtMobaWidth()
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

                var (terminal, _, _) = CreateTestTerminal(window, "term30-visible");
                window.UpdateLayout();
                RunJobs(window);

                var bar = SingleScrollBar(terminal);
                Assert.True(bar.IsVisible);
                Assert.Equal(16, bar.Width);
                Assert.Equal(16, bar.MinWidth);
                Assert.Equal(16, bar.Bounds.Width);
                Assert.Equal(1, bar.Opacity);
                Assert.Equal(ScrollBarVisibility.Visible, bar.Visibility);

                // 不悬停也必须看得见：Fluent 默认给轨道/按钮 Opacity=0，我们换成实心配色。
                var trackBrush = Assert.IsType<SolidColorBrush>(bar.Background);
                Assert.Equal(Color.Parse("#EDEDED"), trackBrush.Color);
                Assert.True(trackBrush.Opacity > 0.99);

                // 上下箭头按钮常显（不再等悬停），行高 16px 且带箭头图标。
                var lineUp = bar.GetVisualDescendants().OfType<RepeatButton>()
                    .Single(b => b.Name == "PART_LineUpButton");
                var lineDown = bar.GetVisualDescendants().OfType<RepeatButton>()
                    .Single(b => b.Name == "PART_LineDownButton");
                foreach (var button in new[] { lineUp, lineDown })
                {
                    Assert.Equal(1, button.Opacity);
                    Assert.Equal(16, button.Bounds.Height);
                    var arrow = button.GetVisualDescendants().OfType<PathIcon>().Single();
                    Assert.Equal(8, arrow.Width);
                    Assert.Equal(8, arrow.Height);
                }

                // 贴终端右缘：滚动条右边界 = 终端右边界（MobaXterm 就是紧贴窗口边）。
                var terminalRight = terminal.Bounds.Width;
                var barRight = bar.TranslatePoint(new Point(bar.Bounds.Width, 0), terminal)!.Value.X;
                Assert.Equal(terminalRight, barRight, 1);

                // 滑块满宽，且不再套 Fluent 的 0.5 缩放变换（否则视觉只有 8px 细条）。
                var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
                Assert.Equal(16, thumb.Bounds.Width);
                Assert.Null(thumb.RenderTransform);
                var thumbBrush = Assert.IsType<SolidColorBrush>(thumb.Background);
                Assert.Equal(Color.Parse("#BDBDBD"), thumbBrush.Color);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>r67.8：轨道空白区按住 = 按下先翻一整屏，350ms 后逐行连续滚动
    /// （1 行 / 16ms ≈ 60 行/秒），松开立即停。r67.7 的 300/250 整屏重复被替换——
    /// 实机就是 4 次/秒的整屏跳，用户反馈“一顿一顿”。</summary>
    [AvaloniaFact]
    public void TERM36_PagePressPagesOnceThenSmoothLines()
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

                var (terminal, pty, _) = CreateTestTerminal(window, "term30-page");
                PumpScrollback(pty, terminal);
                window.UpdateLayout();
                RunJobs(window);

                var bar = SingleScrollBar(terminal);
                Assert.True(bar.ViewportSize > 2, $"视口行数应 >2，实际 {bar.ViewportSize}");
                // 翻一整屏的语义仍在（自动化/无障碍与单击共用）。
                Assert.Equal(bar.ViewportSize, bar.LargeChange);
                // 库把滚动位置同步到滑块：灌满输出后滑块应在底部。
                Assert.True(terminal.ViewportY > 20, $"应已回滚，viewportY={terminal.ViewportY}");
                Assert.Equal(terminal.ViewportY, (int)bar.Value);
                Assert.NotNull(TerminalSmoothPager.ForTest(bar));

                var pageUp = bar.GetVisualDescendants().OfType<RepeatButton>()
                    .Single(b => b.Name == "PART_PageUpButton");
                var pageDown = bar.GetVisualDescendants().OfType<RepeatButton>()
                    .Single(b => b.Name == "PART_PageDownButton");
                var win = TopLevel.GetTopLevel(window)
                    ?? throw new InvalidOperationException("TopLevel 未就绪");
                var viewport = (int)bar.ViewportSize;
                var before = terminal.ViewportY;
                Assert.True(before >= viewport, $"回滚不足一屏：viewportY={before} viewport={viewport}");

                var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
                var where = new Point(4, 4);
                pageUp.RaiseEvent(new PointerPressedEventArgs(
                    pageUp, pointer, win, where, 0,
                    new PointerPointProperties(
                        RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                    KeyModifiers.None, clickCount: 1)
                {
                    RoutedEvent = InputElement.PointerPressedEvent,
                });
                RunJobs(window);
                // 按下即翻一屏（不是等松开才翻）。
                Assert.Equal(before - viewport, terminal.ViewportY);

                pageUp.RaiseEvent(new PointerReleasedEventArgs(
                    pageUp, pointer, win, where, 0,
                    new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                    KeyModifiers.None, MouseButton.Left)
                {
                    RoutedEvent = InputElement.PointerReleasedEvent,
                });
                RunJobs(window);
                // 松开不再重复一屏（r67.7 的 RepeatButton Click 被拦下）。
                Assert.Equal(before - viewport, terminal.ViewportY);

                // 再按下并“按住”：350ms 首延迟内不动，之后每 16ms 正好一行。
                pageUp.RaiseEvent(new PointerPressedEventArgs(
                    pageUp, pointer, win, where, 0,
                    new PointerPointProperties(
                        RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                    KeyModifiers.None, clickCount: 1)
                {
                    RoutedEvent = InputElement.PointerPressedEvent,
                });
                var pager = TerminalSmoothPager.ForTest(bar)!;
                var afterPress = terminal.ViewportY;
                Assert.Equal(before - 2 * viewport, afterPress);

                pager.Advance(TimeSpan.FromMilliseconds(349));
                Assert.Equal(afterPress, terminal.ViewportY);
                pager.Advance(TimeSpan.FromMilliseconds(1));   // 正好走完首延迟
                Assert.Equal(afterPress, terminal.ViewportY);
                pager.Advance(TimeSpan.FromMilliseconds(16));  // 第一行
                Assert.Equal(afterPress - 1, terminal.ViewportY);
                pager.Advance(TimeSpan.FromMilliseconds(8));   // 8ms 不够一步
                Assert.Equal(afterPress - 1, terminal.ViewportY);
                pager.Advance(TimeSpan.FromMilliseconds(8));   // 凑满 16ms → 第二行
                Assert.Equal(afterPress - 2, terminal.ViewportY);
                Assert.Equal(terminal.ViewportY, (int)bar.Value);

                // 松手立即停：再推进再多时间也不动。
                pageUp.RaiseEvent(new PointerReleasedEventArgs(
                    pageUp, pointer, win, where, 0,
                    new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                    KeyModifiers.None, MouseButton.Left)
                {
                    RoutedEvent = InputElement.PointerReleasedEvent,
                });
                pager.Advance(TimeSpan.FromMilliseconds(1000));
                Assert.Equal(afterPress - 2, terminal.ViewportY);

                // 向下翻页按钮走同一套（按下即一屏）。
                var beforeDown = terminal.ViewportY;
                pageDown.RaiseEvent(new PointerPressedEventArgs(
                    pageDown, pointer, win, where, 0,
                    new PointerPointProperties(
                        RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                    KeyModifiers.None, clickCount: 1)
                {
                    RoutedEvent = InputElement.PointerPressedEvent,
                });
                RunJobs(window);
                Assert.Equal(beforeDown + viewport, terminal.ViewportY);
                pageDown.RaiseEvent(new PointerReleasedEventArgs(
                    pageDown, pointer, win, where, 0,
                    new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                    KeyModifiers.None, MouseButton.Left)
                {
                    RoutedEvent = InputElement.PointerReleasedEvent,
                });
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>r67.8：平滑翻页在顶部边界夹住，不会把 ViewportY 推成负数；
    /// 松手后状态完全复位。</summary>
    [AvaloniaFact]
    public void TERM37_SmoothPagerClampsAndStops()
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

                var (terminal, pty, _) = CreateTestTerminal(window, "term37-clamp");
                PumpScrollback(pty, terminal);
                window.UpdateLayout();
                RunJobs(window);

                var bar = SingleScrollBar(terminal);
                var pager = TerminalSmoothPager.ForTest(bar);
                Assert.NotNull(pager);
                var viewport = (int)bar.ViewportSize;

                terminal.ViewportY = Math.Max(0, viewport / 2);
                RunJobs(window);
                pager!.Press(-1);
                Assert.Equal(0, terminal.ViewportY);
                pager.Advance(TimeSpan.FromSeconds(1));
                Assert.Equal(0, terminal.ViewportY);

                pager.Release();
                Assert.Equal(0, pager.Direction);
                Assert.Equal(0, terminal.ViewportY);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>r67.7：没有回滚内容时滚动条同样占位 16px（库会禁用但仍保留列），
    /// 这样终端列宽不会随回滚内容出现/消失而抖动。</summary>
    [AvaloniaFact]
    public void TERM36_ScrollBarReservesWidthWithoutScrollback()
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

                var (terminal, _, _) = CreateTestTerminal(window, "term30-empty");
                window.UpdateLayout();
                RunJobs(window);

                var bar = SingleScrollBar(terminal);
                var narrow = terminal.Terminal.Buffer.Cols;
                // 起步（几乎无回滚）就已占 16px：终端列宽不随回滚内容出现而抖动。
                Assert.True(bar.IsVisible);
                Assert.Equal(16, bar.Bounds.Width);

                FillScrollback(terminal);
                window.UpdateLayout();
                RunJobs(window);
                Assert.True(terminal.Terminal.Buffer.YBase > 0);
                Assert.True(bar.IsVisible);
                Assert.Equal(16, bar.Bounds.Width);
                Assert.Equal(narrow, terminal.Terminal.Buffer.Cols);
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
    public async Task TERM30_HighlightColorsOutputAfterPromptEcho()
    {
        // r57 回归（用户实机反馈“只生效一次，下次执行 ifconfig 就没效果了”）：
        // 真实会话先有命令回显行，之后每条输出行都必须着色，且连续两轮输出都要着色。
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

                var (terminal, _, _) = CreateTestTerminal(window, "hl-prompt");
                window.AttachTerminalHighlighter(terminal);
                Assert.NotNull(window.GetHighlighterForTests(terminal));

                const string mac = "82:37:a3:fe:87:0b";
                for (var round = 0; round < 2; round++)
                {
                    terminal.Terminal.Write("$ ifconfig\r\n");
                    terminal.Terminal.Write("      ether " + mac + "  txqueuelen 1000  (Ethernet)\r\n");
                    terminal.Terminal.Write("        RX errors 0  dropped 0  overruns 0  frame 0\r\n");
                    RunJobs(window);

                    var etherRow = round * 3 + 1;
                    var line = terminal.Terminal.Buffer.GetLine(etherRow)!;
                    var text = line.TranslateToString(true, 0);
                    var col = text.IndexOf(mac, StringComparison.Ordinal);
                    Assert.True(col >= 0, $"第 {round + 1} 轮 ether 行缺失：{text}");
                    Assert.Equal(5, line[col].Attributes.GetFgColor()); // 品红

                    var errLine = terminal.Terminal.Buffer.GetLine(etherRow + 1)!;
                    var errText = errLine.TranslateToString(true, 0);
                    var errCol = errText.IndexOf("errors", StringComparison.Ordinal);
                    Assert.True(errCol >= 0, $"第 {round + 1} 轮 errors 行缺失：{errText}");
                    Assert.Equal(1, errLine[errCol].Attributes.GetFgColor()); // 红
                }

                await Task.Delay(0);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task TERM30_NetmaskAndUnspecStayDefaultAfterEcho()
    {
        // r58（用户同板 ifconfig 对照截图）：MobaXterm 的 IPv4 规则首段排除 255，
        // 故 netmask 值保持默认色；MAC 规则边界排除 '-'，故 unspec 的
        // 00-00-…-00 链不染品红；inet/broadcast 地址与 ether MAC 仍为品红。
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

                var (terminal, _, _) = CreateTestTerminal(window, "hl-netmask");
                window.AttachTerminalHighlighter(terminal);
                Assert.NotNull(window.GetHighlighterForTests(terminal));

                terminal.Terminal.Write("$ ifconfig\r\n");
                terminal.Terminal.Write(
                    "inet 10.0.9.5 netmask 255.255.255.0 broadcast 10.0.9.255\r\n");
                terminal.Terminal.Write(
                    "unspec 00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00\r\n");
                terminal.Terminal.Write(
                    "ether 02:42:5b:d8:95:d3\r\n");
                RunJobs(window);

                static int ColOf(string text, string token)
                {
                    var col = text.IndexOf(token, StringComparison.Ordinal);
                    Assert.True(col >= 0, $"未找到 {token}：{text}");
                    return col;
                }

                var inetLine = terminal.Terminal.Buffer.GetLine(1)!;
                var inetText = inetLine.TranslateToString(true, 0);
                Assert.Equal(5, inetLine[ColOf(inetText, "10.0.9.5")].Attributes.GetFgColor());        // inet 地址→品红
                Assert.Equal(256, inetLine[ColOf(inetText, "255.255.255.0")].Attributes.GetFgColor()); // netmask→默认
                Assert.Equal(5, inetLine[ColOf(inetText, "10.0.9.255")].Attributes.GetFgColor());      // broadcast→品红

                var unspecLine = terminal.Terminal.Buffer.GetLine(2)!;
                var unspecText = unspecLine.TranslateToString(true, 0);
                Assert.Equal(256, unspecLine[ColOf(unspecText,
                    "00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00")].Attributes.GetFgColor());

                var etherLine = terminal.Terminal.Buffer.GetLine(3)!;
                var etherText = etherLine.TranslateToString(true, 0);
                Assert.Equal(5, etherLine[ColOf(etherText, "02:42:5b:d8:95:d3")].Attributes.GetFgColor()); // MAC→品红

                await Task.Delay(0);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task TERM30_StandardSchemeWiringColorsTimestampsAndNone()
    {
        // r59：MobaXterm「标准关键字」方案（SyntaxType=1）接线——真实 MainWindow，
        // 配置落 terminal.syntaxHighlight="standard" 后新输出行自动着色：
        // 时间戳→黄(3)、null/none→品红(5)，原有 error 红不变。
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig
                {
                    Terminal = new TerminalConfig { SyntaxHighlight = "standard" },
                });
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                RunJobs(window);

                // 菜单里「标准关键字」被勾选
                var menu = window.BuildSyntaxHighlightMenuItem();
                var kids = menu.Items.OfType<MenuItem>().ToList();
                Assert.Equal(6, kids.Count);
                Assert.True(kids.Single(k => ItemLabel(k) == "标准关键字").IsChecked);

                var (terminal, _, _) = CreateTestTerminal(window, "hl-standard");
                window.AttachTerminalHighlighter(terminal);
                Assert.Equal(COMTool.Terminal.Highlight.HighlightScheme.Standard,
                    window.GetHighlighterForTests(terminal)!.Scheme);

                terminal.Terminal.Write("$ dmesg\r\n");
                terminal.Terminal.Write("[ 67705.015] variant=\"none\" error\r\n");
                RunJobs(window);

                var line = terminal.Terminal.Buffer.GetLine(1)!;
                var text = line.TranslateToString(true, 0);
                static int ColOf(string s, string token)
                {
                    var c = s.IndexOf(token, StringComparison.Ordinal);
                    Assert.True(c >= 0, $"未找到 {token}：{s}");
                    return c;
                }
                Assert.Equal(3, line[ColOf(text, "[ 67705.015]")].Attributes.GetFgColor()); // 时间戳→黄
                Assert.Equal(5, line[ColOf(text, "\"none\"") + 1].Attributes.GetFgColor()); // none→品红
                Assert.Equal(1, line[ColOf(text, "error")].Attributes.GetFgColor());        // error→红

                await Task.Delay(0);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

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
