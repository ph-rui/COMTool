using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;
using Iciclecreek.Terminal;

namespace COMTool.App.Tests;

// r67：标签拖动换位。纯函数 + VM 行为 + 无头 UI 拖拽三层测试。
public sealed class TabDragReorderTests
{
    private static TerminalTab Tab(string sessionId) =>
        TestTab.Create(sessionId: sessionId, kind: SessionKind.Local);

    // ---- TabReorderIndex 纯函数 ----

    [Fact]
    public void UI13_Resolve_InsertsBeforeTabWhoseCenterIsPassed()
    {
        var centers = new[] { 100.0, 200.0, 300.0 };
        Assert.Equal(0, TabReorderIndex.Resolve(0, centers));
        Assert.Equal(0, TabReorderIndex.Resolve(99.9, centers));
        Assert.Equal(1, TabReorderIndex.Resolve(150, centers));
        Assert.Equal(2, TabReorderIndex.Resolve(250, centers));
    }

    [Fact]
    public void UI13_Resolve_AppendsPastLastCenter()
    {
        var centers = new[] { 100.0, 200.0, 300.0 };
        Assert.Equal(3, TabReorderIndex.Resolve(300, centers));
        Assert.Equal(3, TabReorderIndex.Resolve(999, centers));
    }

    [Fact]
    public void UI13_Resolve_EmptyCenters_ReturnsZero()
    {
        Assert.Equal(0, TabReorderIndex.Resolve(5, Array.Empty<double>()));
    }

    // ---- MainViewModel.MoveTab ----

    [Fact]
    public void UI13_MoveTab_ReordersWithoutChangingSelection()
    {
        var vm = new MainViewModel();
        var a = Tab("a");
        var b = Tab("b");
        var c = Tab("c");
        vm.AddTab(a);
        vm.AddTab(b);
        vm.AddTab(c);
        vm.SelectedTab = b;

        vm.MoveTab(c, 0);

        Assert.Equal(new[] { c, a, b }, vm.Tabs);
        Assert.Same(b, vm.SelectedTab);
    }

    [Fact]
    public void UI13_MoveTab_ClampsOutOfRangeTarget()
    {
        var vm = new MainViewModel();
        var a = Tab("a");
        var b = Tab("b");
        var c = Tab("c");
        vm.AddTab(a);
        vm.AddTab(b);
        vm.AddTab(c);
        vm.SelectedTab = c;

        vm.MoveTab(a, 99);

        Assert.Equal(new[] { b, c, a }, vm.Tabs);

        vm.MoveTab(c, -5);

        Assert.Equal(new[] { c, b, a }, vm.Tabs);
        Assert.Same(c, vm.SelectedTab);
    }

    [Fact]
    public void UI13_MoveTab_SamePositionIsNoOp()
    {
        var vm = new MainViewModel();
        var a = Tab("a");
        var b = Tab("b");
        vm.AddTab(a);
        vm.AddTab(b);
        vm.SelectedTab = b;

        vm.MoveTab(b, 1);
        vm.MoveTab(a, -1);

        Assert.Equal(new[] { a, b }, vm.Tabs);
        Assert.Same(b, vm.SelectedTab);
    }

    // ---- 无头 UI：真实指针事件驱动的拖拽 ----

    [AvaloniaFact]
    public void UI13_DragTabReordersTabsLive()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        TestTerminal? a = null;
        TestTerminal? b = null;
        TestTerminal? c = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();
                var viewModel = (MainViewModel)window.DataContext!;
                a = OpenTab(window, "a");
                b = OpenTab(window, "b");
                c = OpenTab(window, "c");
                RunJobs(window);

                var strip = window.FindControl<Grid>("TabStripGrid");
                Assert.NotNull(strip);

                // 把第 3 个标签 c 依次拖到 b 之前、再拖到最前。
                var start = Center(window, FindTabBorder(window, c.Tab));
                window.MouseDown(start, MouseButton.Left);

                // 小于阈值的位移：不应触发换位。
                window.MouseMove(new Point(start.X + 2, start.Y), RawInputModifiers.LeftMouseButton);
                RunJobs(window);
                Assert.Equal(new[] { a.Tab, b.Tab, c.Tab }, viewModel.Tabs);

                // 超过阈值：开始拖动，但仍停在 c 自己右侧邻域，顺序不变。
                window.MouseMove(new Point(start.X - 10, start.Y), RawInputModifiers.LeftMouseButton);
                RunJobs(window);
                Assert.Equal(new[] { a.Tab, b.Tab, c.Tab }, viewModel.Tabs);

                // 移到 a、b 之间：c 插入 b 之前。
                var aCenter = Center(window, FindTabBorder(window, a.Tab));
                var bCenter = Center(window, FindTabBorder(window, b.Tab));
                window.MouseMove(new Point((aCenter.X + bCenter.X) / 2, start.Y), RawInputModifiers.LeftMouseButton);
                RunJobs(window);
                Assert.Equal(new[] { a.Tab, c.Tab, b.Tab }, viewModel.Tabs);

                // 移到 a 中线左侧：c 排到最前。
                aCenter = Center(window, FindTabBorder(window, a.Tab));
                window.MouseMove(new Point(aCenter.X - 5, start.Y), RawInputModifiers.LeftMouseButton);
                RunJobs(window);
                Assert.Equal(new[] { c.Tab, a.Tab, b.Tab }, viewModel.Tabs);

                var end = new Point(aCenter.X - 5, start.Y);
                window.MouseUp(end, MouseButton.Left);
                RunJobs(window);

                Assert.Equal(new[] { c.Tab, a.Tab, b.Tab }, viewModel.Tabs);
                // 拖动不改变选中项，也不把标签拖出主窗口。
                Assert.Same(c.Tab, viewModel.SelectedTab);
                Assert.Equal(3, viewModel.Tabs.Count);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            a?.Dispose();
            b?.Dispose();
            c?.Dispose();
        }
    }

    [AvaloniaFact]
    public void UI13_ClickWithoutMoveDoesNotReorder()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        TestTerminal? a = null;
        TestTerminal? b = null;
        TestTerminal? c = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();
                var viewModel = (MainViewModel)window.DataContext!;
                a = OpenTab(window, "a");
                b = OpenTab(window, "b");
                c = OpenTab(window, "c");
                RunJobs(window);

                var p = Center(window, FindTabBorder(window, b.Tab));
                window.MouseDown(p, MouseButton.Left);
                window.MouseUp(p, MouseButton.Left);
                RunJobs(window);

                Assert.Equal(new[] { a.Tab, b.Tab, c.Tab }, viewModel.Tabs);
                Assert.Same(b.Tab, viewModel.SelectedTab);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            a?.Dispose();
            b?.Dispose();
            c?.Dispose();
        }
    }

    // ---- r67.1：换位动画（MobaXterm 式：被拖跟手 + 其余滑动 + 松手落位） ----

    [AvaloniaFact]
    public async Task UI13_DragTransformsFollowAndSettle()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        TestTerminal? a = null;
        TestTerminal? b = null;
        TestTerminal? c = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();
                var viewModel = (MainViewModel)window.DataContext!;
                a = OpenTab(window, "a");
                b = OpenTab(window, "b");
                c = OpenTab(window, "c");
                RunJobs(window);

                var start = Center(window, FindTabBorder(window, c.Tab));
                var aCenter = Center(window, FindTabBorder(window, a.Tab));
                var bCenter = Center(window, FindTabBorder(window, b.Tab));

                window.MouseDown(start, MouseButton.Left);
                // 拖到 a、b 之间：c 换位到 b 之前，拖动未结束。
                window.MouseMove(new Point((aCenter.X + bCenter.X) / 2, start.Y), RawInputModifiers.LeftMouseButton);
                RunJobs(window);

                Assert.Equal(new[] { a.Tab, c.Tab, b.Tab }, viewModel.Tabs);

                // 换位会重排 ItemsControl 容器，Border 引用需重新获取。
                var cBorder = FindTabBorder(window, c.Tab);
                var bBorder = FindTabBorder(window, b.Tab);

                // 被拖标签跟手：RenderTransform 位移非零（标签中心停在指针 X 下）。
                Assert.IsType<TranslateTransform>(cBorder.RenderTransform);
                Assert.True(
                    Math.Abs(((TranslateTransform)cBorder.RenderTransform).X) > 1,
                    "被拖标签应带跟手位移");

                // 被挤开的标签带滑动位移（120ms ease-out 刚开始，偏移≈整槽位宽）。
                Assert.IsType<TranslateTransform>(bBorder.RenderTransform);
                Assert.True(
                    Math.Abs(((TranslateTransform)bBorder.RenderTransform).X) > 1,
                    "被挤开的标签应带滑动位移");

                window.MouseUp(new Point((aCenter.X + bCenter.X) / 2, start.Y), MouseButton.Left);
                RunJobs(window);

                // 松手后被拖标签滑回槽位；等 120ms 动画结束，位移全部清零。
                for (var i = 0; i < 40; i++)
                {
                    await Task.Delay(50);
                    RunJobs(window);
                    if (cBorder.RenderTransform is null
                        && bBorder.RenderTransform is null
                        && FindTabBorder(window, a.Tab).RenderTransform is null)
                    {
                        break;
                    }
                }

                Assert.Null(cBorder.RenderTransform);
                Assert.Null(bBorder.RenderTransform);
                Assert.Null(FindTabBorder(window, a.Tab).RenderTransform);
                Assert.Equal(new[] { a.Tab, c.Tab, b.Tab }, viewModel.Tabs);
                Assert.Same(c.Tab, viewModel.SelectedTab);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            a?.Dispose();
            b?.Dispose();
            c?.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task UI13_ClickWithoutMoveLeavesNoTransform()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        TestTerminal? a = null;
        TestTerminal? b = null;
        TestTerminal? c = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();
                var viewModel = (MainViewModel)window.DataContext!;
                a = OpenTab(window, "a");
                b = OpenTab(window, "b");
                c = OpenTab(window, "c");
                RunJobs(window);

                var p = Center(window, FindTabBorder(window, b.Tab));
                window.MouseDown(p, MouseButton.Left);
                window.MouseUp(p, MouseButton.Left);
                RunJobs(window);
                await Task.Delay(200);
                RunJobs(window);

                Assert.Equal(new[] { a.Tab, b.Tab, c.Tab }, viewModel.Tabs);
                Assert.Same(b.Tab, viewModel.SelectedTab);
                Assert.Null(FindTabBorder(window, a.Tab).RenderTransform);
                Assert.Null(FindTabBorder(window, b.Tab).RenderTransform);
                Assert.Null(FindTabBorder(window, c.Tab).RenderTransform);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            a?.Dispose();
            b?.Dispose();
            c?.Dispose();
        }
    }

    // ---- helpers（与 MainWindowHeadlessTests 同一套路） ----

    private static MainWindow CreateWindow()
    {
        var window = new MainWindow { DataContext = new MainViewModel() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static TestTerminal OpenTab(MainWindow window, string title)
    {
        var viewModel = (MainViewModel)window.DataContext!;
        var pty = new InMemoryPty();
        var terminal = new TerminalControl { Process = string.Empty };
        var tab = TestTab.Create(terminal, title: title, sessionId: title);
        viewModel.AddTab(tab);
        RunJobs(window);

        terminal.ApplyTemplate();
        terminal.AttachConnection(pty);
        RunJobs(window);
        return new TestTerminal(tab, terminal, pty);
    }

    private static Border FindTabBorder(MainWindow window, TerminalTab tab) =>
        window.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => ReferenceEquals(border.Tag, tab));

    private static Point Center(MainWindow window, Border border) =>
        border.TranslatePoint(
            new Point(border.Bounds.Width / 2, border.Bounds.Height / 2),
            window)
            .Value;

    private static void RunJobs(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private sealed record TestTerminal(TerminalTab Tab, TerminalControl Terminal, InMemoryPty Pty)
        : IDisposable
    {
        public void Dispose() => Pty.Dispose();
    }
}
