using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;
using Iciclecreek.Terminal;

namespace COMTool.App.Tests;

public sealed class TerminalMaximizeTests
{
    // r40.3：用户实机确认 MobaXterm 只在最顶层标题栏有双击最大化/还原，
    // 终端黑色显示区内双击不改窗口大小。r15 旧实现的「终端区双击=ToggleMaximize」
    // 已移除；此测试改为断言窗口状态保持不变、焦点不丢失。
    [AvaloniaFact]
    public void TERM15_TerminalDoubleTapKeepsWindowState()
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

                // MainWindow only wires the terminal double-tap handler in its
                // own terminal factory, so use that factory for the tab.
                var terminal = CreateTerminal(window);
                var tab = TestTab.Create(terminal);
                var viewModel = (MainViewModel)window.DataContext!;
                viewModel.AddTab(tab);
                RunJobs(window);

                terminal.Focus();
                RunJobs(window);
                Assert.True(terminal.IsKeyboardFocusWithin);

                var before = window.WindowState;
                Assert.Equal(WindowState.Normal, before);

                RaiseDoubleTap(terminal, window);
                RunJobs(window);
                // 双击终端区后窗口状态必须保持 Normal（不再切换最大化）
                Assert.Equal(WindowState.Normal, window.WindowState);
                Assert.True(terminal.IsKeyboardFocusWithin);

                // 再次双击仍然不改变窗口状态
                RaiseDoubleTap(terminal, window);
                RunJobs(window);
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
    public void TERM15_DoubleClickDoesNotStealSelection()
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
                var tab = TestTab.Create(terminal);
                var viewModel = (MainViewModel)window.DataContext!;
                viewModel.AddTab(tab);
                RunJobs(window);

                terminal.Terminal.Write("selectable terminal content");
                terminal.Terminal.Selection.SelectAll();
                Assert.True(terminal.Terminal.Selection.HasSelection);
                var selectedText = terminal.Terminal.Selection.GetSelectionText();
                Assert.False(string.IsNullOrEmpty(selectedText));

                terminal.Focus();
                RunJobs(window);
                Assert.True(terminal.IsKeyboardFocusWithin);

                var raised = RaiseDoubleTap(terminal, window);
                RunJobs(window);

                // r40.3: 双击终端区不再切换窗口大小（对齐 MobaXterm，仅标题栏双击最大化）
                Assert.Equal(WindowState.Normal, window.WindowState);
                Assert.True(terminal.IsKeyboardFocusWithin);
                Assert.True(terminal.Terminal.Selection.HasSelection);
                Assert.Equal(selectedText, terminal.Terminal.Selection.GetSelectionText());
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

    private static TappedEventArgs RaiseDoubleTap(TerminalControl terminal, Window window)
    {
        var position = terminal.TranslatePoint(new Point(10, 10), window)
            ?? new Point(10, 10);
        var pointer = new Avalonia.Input.Pointer(0, PointerType.Mouse, isPrimary: true);
        var pointerArgs = new PointerPressedEventArgs(
            terminal,
            pointer,
            window,
            position,
            0,
            new PointerPointProperties(
                RawInputModifiers.None,
                PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            clickCount: 2);

        var args = new TappedEventArgs(
            InputElement.DoubleTappedEvent,
            pointerArgs);
        terminal.RaiseEvent(args);
        return args;
    }

    private static void RunJobs(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
