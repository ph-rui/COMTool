using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
