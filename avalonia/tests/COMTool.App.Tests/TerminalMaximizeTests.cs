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
    [AvaloniaFact]
    public void TERM15_DoubleClickUsesMaximizePath()
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

                var raised = RaiseDoubleTap(terminal, window);
                RunJobs(window);
                Assert.True(raised.Handled);
                Assert.NotEqual(before, window.WindowState);
                Assert.Equal(WindowState.Maximized, window.WindowState);

                // A second title-bar style double click restores the window.
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

                Assert.True(raised.Handled);
                Assert.Equal(WindowState.Maximized, window.WindowState);
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
