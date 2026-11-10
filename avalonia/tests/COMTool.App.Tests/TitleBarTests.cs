using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

public sealed class TitleBarTests
{
    [AvaloniaFact]
    public void UI01_TitleBarDoubleClickTogglesMaximizeOnce()
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

                var titleBar = window.FindControl<Border>("TitleBar");
                Assert.NotNull(titleBar);
                Assert.Equal(WindowState.Normal, window.WindowState);

                // The second press carries ClickCount=2. A pointer press alone
                // must not toggle the window state; the double-tap gesture
                // performs the single maximize/restore transition.
                var pointerPress = CreatePress(titleBar, window, clickCount: 2);
                titleBar.RaiseEvent(pointerPress);
                RunJobs(window);
                Assert.Equal(WindowState.Normal, window.WindowState);

                var firstDoubleTap = RaiseDoubleTap(titleBar, window, pointerPress);
                RunJobs(window);
                Assert.True(firstDoubleTap.Handled);
                Assert.Equal(WindowState.Maximized, window.WindowState);

                var restorePress = CreatePress(titleBar, window, clickCount: 2);
                titleBar.RaiseEvent(restorePress);
                RunJobs(window);
                Assert.Equal(WindowState.Maximized, window.WindowState);

                var secondDoubleTap = RaiseDoubleTap(titleBar, window, restorePress);
                RunJobs(window);
                Assert.True(secondDoubleTap.Handled);
                Assert.Equal(WindowState.Normal, window.WindowState);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI01_WindowMaximizedStateRestoredOnRestart()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                // 模拟"上次退出时是最大化"：config 里 window.maximized=true，
                // 新启动的窗口应直接以最大化状态出现（MobaXterm 行为）。
                env.SeedConfig(new ComtoolConfig
                {
                    Window = new WindowConfig
                    {
                        Width = 900,
                        Height = 600,
                        Maximized = true,
                    },
                });
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                RunJobs(window);
                Assert.Equal(WindowState.Maximized, window.WindowState);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI01_WindowMaximizedStatePersistedOnClose()
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
                Assert.Equal(WindowState.Normal, window.WindowState);

                // 用户点最大化后关闭：状态必须落盘（normal 尺寸也保留，
                // 下次启动按 normal 尺寸恢复后再最大化）。
                window.WindowState = WindowState.Maximized;
                RunJobs(window);
                Assert.Equal(WindowState.Maximized, window.WindowState);
                window.Close();
                RunJobs(window);
                window = null; // 已关闭，避免 finally 二次 Close
            });

            var loaded = ComtoolConfig.Load(
                Path.Combine(env.AvaloniaConfigDir, "config.json"));
            Assert.NotNull(loaded.Window);
            Assert.True(loaded.Window.Maximized);
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI01_TitleBarHasBottomDivider()
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

                var titleBar = window.FindControl<Border>("TitleBar");
                Assert.True(titleBar is not null, "TitleBar border not found");

                // r55: 用户反馈"顶部栏与下面内容分层不明显"——标题栏下沿加
                // 1px 分隔线（仅底边，其余三边保持 0，不改变 35px 高度布局）。
                Assert.Equal(1, titleBar.BorderThickness.Bottom);
                Assert.Equal(0, titleBar.BorderThickness.Top);
                Assert.Equal(0, titleBar.BorderThickness.Left);
                Assert.Equal(0, titleBar.BorderThickness.Right);

                // 线色走主题资源（浅色 #b4b4b4 / 深色 #3a3a3a），断言渲染刷
                // 与资源值一致，颜色调整时测试自动跟随。
                Assert.True(titleBar.TryFindResource("TitleBarBorderBrush", out var resource));
                var expected = Assert.IsAssignableFrom<ISolidColorBrush>(resource);
                var actual = Assert.IsAssignableFrom<ISolidColorBrush>(titleBar.BorderBrush);
                Assert.Equal(expected.Color, actual.Color);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI01_TitleBarHasNoSessionsButton()
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

                // r55: 用户要求去掉标题栏"会话"按钮（MobaXterm 标题栏无此
                // 按钮），折叠/展开侧栏改由侧栏 rail 的会话图标承担。
                Assert.Null(window.FindControl<Button>("SessionsButton"));
                var sidebar = window.FindControl<SessionSidebar>("Sidebar");
                Assert.True(sidebar is not null, "Sidebar not found");
                Assert.True(
                    sidebar.FindControl<Button>("RailSessionsButton") is not null,
                    "rail sessions icon is the replacement entry point");
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static PointerPressedEventArgs CreatePress(
        Border titleBar,
        Window window,
        int clickCount)
    {
        var position = titleBar.TranslatePoint(new Point(10, 10), window)
            ?? new Point(10, 10);
        var pointer = new Avalonia.Input.Pointer(0, PointerType.Mouse, isPrimary: true);
        return new PointerPressedEventArgs(
            titleBar,
            pointer,
            window,
            position,
            0,
            new PointerPointProperties(
                RawInputModifiers.None,
                PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            clickCount);
    }

    private static TappedEventArgs RaiseDoubleTap(
        Border titleBar,
        Window window,
        PointerPressedEventArgs press)
    {
        var args = new TappedEventArgs(InputElement.DoubleTappedEvent, press);
        titleBar.RaiseEvent(args);
        return args;
    }

    private static void RunJobs(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
