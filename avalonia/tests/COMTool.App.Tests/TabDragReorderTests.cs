using System;
using System.Threading.Tasks;
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
using Xunit;

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

    // ---- r67.2：被拖标签置顶（修复「移动过去就消失」） ----

    // ---- r67.3：拖动被裁剪 / 二次拖动不再落位（「拖过去就消失」的两个真因） ----

    [AvaloniaFact]
    public async Task UI13_TabStripDoesNotClipDraggedTab()
    {
        // Avalonia 12 的 ItemsControl 默认 ClipToBounds=True，会把标签条裁到
        // 「所有标签槽位总和」那么宽：被拖标签靠 RenderTransform 跟手，越出这个
        // 矩形就被裁成一条缝（实测只剩 39px 的图标）甚至完全不可见。
        using var env = new TestEnvironment();
        MainWindow? window = null;
        TestTerminal? a = null;
        TestTerminal? b = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();
                a = OpenTab(window, "a");
                b = OpenTab(window, "b");
                RunJobs(window);

                var items = window.FindControl<ItemsControl>("TabItemsControl")!;
                Assert.False(items.ClipToBounds, "标签条不得裁剪跟手标签");

                // 拖最右标签继续向右 200px：标签视觉位置应超出标签条自身宽度，
                // 且不被任何祖先裁剪（这里守住属性层面；像素层面由 VM 实机截图核）。
                var bCenter = Center(window, FindTabBorder(window, b.Tab));
                window.MouseDown(bCenter, MouseButton.Left);
                window.MouseMove(new Point(bCenter.X + 200, bCenter.Y), RawInputModifiers.LeftMouseButton);
                RunJobs(window);

                var border = FindTabBorder(window, b.Tab);
                var pos = border.TranslatePoint(new Point(0, 0), window)!.Value;
                Assert.Equal(200, border.RenderTransform is TranslateTransform tt ? tt.X : 0, 1);
                Assert.True(pos.X + border.Bounds.Width > items.Bounds.Width + 100,
                    $"被拖标签应越出标签条范围（tab 右沿 {pos.X + border.Bounds.Width:0}，items 宽 {items.Bounds.Width:0}）");
                Assert.False(items.ClipToBounds);

                window.MouseUp(new Point(bCenter.X + 200, bCenter.Y), MouseButton.Left);
                RunJobs(window);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            a?.Dispose();
            b?.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task UI13_SecondDragSettlesBackAndClearsTransforms()
    {
        // 落位动画结束会 Stop 计时器但对象仍存活；若 Ensure 只判 null，后续拖动
        // 就再也不推进 → 松手位移永远清不掉，标签停在屏外（实测第二次拖动后
        // RenderTransform 卡在 -300）。松手点落在标签条/窗口外（仅收到
        // PointerCaptureLost）同样必须落位归零。
        using var env = new TestEnvironment();
        MainWindow? window = null;
        TestTerminal? a = null;
        TestTerminal? b = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                window = CreateWindow();
                var viewModel = (MainViewModel)window.DataContext!;
                a = OpenTab(window, "a");
                b = OpenTab(window, "b");
                RunJobs(window);

                // 第一次：窗口内松手，落位动画跑完（此后计时器自停）。
                var bCenter = Center(window, FindTabBorder(window, b.Tab));
                window.MouseDown(bCenter, MouseButton.Left);
                window.MouseMove(new Point(bCenter.X + 60, bCenter.Y), RawInputModifiers.LeftMouseButton);
                RunJobs(window);
                window.MouseUp(new Point(bCenter.X + 60, bCenter.Y), MouseButton.Left);
                RunJobs(window);
                await WaitNoTransform(window, a.Tab, b.Tab);

                // 第二次：把最左标签拖到窗口左侧外面再松手（只会收到捕获丢失）。
                var aCenter = Center(window, FindTabBorder(window, a.Tab));
                window.MouseDown(aCenter, MouseButton.Left);
                window.MouseMove(new Point(aCenter.X - 150, aCenter.Y), RawInputModifiers.LeftMouseButton);
                RunJobs(window);
                Assert.True(FindTabBorder(window, a.Tab).RenderTransform is TranslateTransform,
                    "拖动中应有跟手位移");
                window.MouseUp(new Point(aCenter.X - 150, aCenter.Y), MouseButton.Left);
                RunJobs(window);

                // 第二次也必须落位：位移归零、顺序不变。
                await WaitNoTransform(window, a.Tab, b.Tab);
                Assert.Equal(new[] { a.Tab, b.Tab }, viewModel.Tabs);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            a?.Dispose();
            b?.Dispose();
        }
    }

    private static async Task WaitNoTransform(MainWindow window, params TerminalTab[] tabs)
    {
        for (var i = 0; i < 60; i++)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            if (tabs.All(t => FindTabBorder(window, t).RenderTransform is null))
            {
                return;
            }
        }

        var left = string.Join(", ",
            tabs.Select(t => $"{t.Title}={FindTabBorder(window, t).RenderTransform}"));
        throw new Xunit.Sdk.XunitException($"落位后位移应清零，仍残留：{left}");
    }

    [AvaloniaFact]
    public async Task UI13_DraggedTabRaisesZIndexAndResetsOnRelease()
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

                // 拖动中被拖标签的容器节点 Z 序提升，其余保持 0。
                Assert.True(FindTabContainer(window, c.Tab).ZIndex > 0, "被拖标签应提升 Z 序");
                Assert.Equal(0, FindTabContainer(window, a.Tab).ZIndex);
                Assert.Equal(0, FindTabContainer(window, b.Tab).ZIndex);

                window.MouseUp(new Point((aCenter.X + bCenter.X) / 2, start.Y), MouseButton.Left);
                RunJobs(window);
                // 松手后 Z 序立即恢复（定时器只管位移，不管 Z 序）。
                Assert.Equal(0, FindTabContainer(window, c.Tab).ZIndex);
                Assert.Equal(0, FindTabContainer(window, a.Tab).ZIndex);
                Assert.Equal(0, FindTabContainer(window, b.Tab).ZIndex);
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
    public async Task UI13_PanelFlagsZOrderResortOnZIndexChange()
    {
        // 机制验证：子节点 ZIndex 变非零后，父 Panel 进入 Z 序排序状态
        // （Avalonia 12：Visual.ZIndex 变更 → 父节点 HasNonUniformZIndexChildren
        // + Renderer.RecalculateChildren，渲染层按 ZIndex 排序绘制顺序）——
        // 这是「被拖标签盖在重叠邻居之上」的框架前提。
        // headless 环境无像素级合成（RTB 不 rasterize 填充），故用状态级反射验证。
        using var env = new TestEnvironment();
        Window? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                var red = new Border { Background = Brushes.Red, Width = 40, Height = 40 };
                var blue = new Border { Background = Brushes.Blue, Width = 40, Height = 40 };
                var grid = new Grid { Width = 100, Height = 100, Children = { red, blue } };
                window = new Window { Content = grid, Width = 100, Height = 100 };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                grid.UpdateLayout();

                red.ZIndex = 100;
                Dispatcher.UIThread.RunJobs();

                var flag = (bool)typeof(Visual)
                    .GetProperty("HasNonUniformZIndexChildren",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(grid)!;
                Assert.True(flag, "子节点 ZIndex 非零后父 Panel 应进入 Z 序排序状态");
                Assert.Equal(100, red.ZIndex);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
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

    /// <summary>找指定 tab 在标签条面板中的直接子节点（Z 序设在该节点上）。</summary>
    private static Visual FindTabContainer(MainWindow window, TerminalTab tab)
    {
        var v = (Visual)FindTabBorder(window, tab);
        while (true)
        {
            var parent = v.GetVisualParent();
            if (parent is Panel)
            {
                return v;
            }
            v = parent!;
        }
    }

    private sealed record TestTerminal(TerminalTab Tab, TerminalControl Terminal, InMemoryPty Pty)
        : IDisposable
    {
        public void Dispose() => Pty.Dispose();
    }
}
