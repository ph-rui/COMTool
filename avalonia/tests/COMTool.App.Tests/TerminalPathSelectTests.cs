using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;
using Xunit;

namespace COMTool.App.Tests;

/// <summary>
/// TERM-33：终端鼠标交互对齐 MobaXterm（r47 / 库 4.1.4）。
/// 1) 双击路径文本选中整条路径（而非仅一个词段）；
/// 2) 鼠标悬停在终端显示区域时光标为文本型（IBeam）而非箭头。
/// 库级详细行为见 Iciclecreek.Avalonia.Terminal.Tests 的 PathSelectCursorTests。
/// </summary>
public sealed class TerminalPathSelectTests
{
    private const string ProbePath = "/home/devuser/tftp_dir/patent"; // 28 列，0..27

    [AvaloniaFact]
    public void TERM33_DoubleClickPathSelectsWholePath()
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

                var terminal = CreateTerminalTab(window, "path-sel");
                terminal.Terminal.Write(ProbePath + "\r\n");
                RunJobs(window);

                var view = GetInnerView(terminal);
                // 双击路径中段（第 8 列 'g'，位于 devuser 词段内）
                var (x, y) = CellPoint(window, view, 8, 0.5);
                Press(view, window, x, y, clickCount: 1);
                Press(view, window, x, y, clickCount: 2);
                RunJobs(window);

                Assert.True(terminal.Terminal.Selection.HasSelection);
                Assert.Equal(ProbePath, terminal.Terminal.Selection.GetSelectionText());
            });
        }
        finally
        {
            window?.Close();
            RunJobs(window);
        }
    }

    [AvaloniaFact]
    public void TERM33_HoverOverTerminalShowsTextBeam()
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

                var terminal = CreateTerminalTab(window, "beam-check");
                terminal.Terminal.Write(ProbePath + "\r\n");
                RunJobs(window);

                var view = GetInnerView(terminal);
                var (x, y) = CellPoint(window, view, 5, 0.5);
                Move(view, window, x, y);
                RunJobs(window);

                Assert.Same(Iciclecreek.Terminal.TerminalView.IBeamCursor, view.Cursor);
            });
        }
        finally
        {
            window?.Close();
            RunJobs(window);
        }
    }

    // ─────────────────────────── 测试工具 ───────────────────────────

    private static Iciclecreek.Terminal.TerminalControl CreateTerminalTab(MainWindow window, string id)
    {
        var vm = (MainViewModel)window.DataContext!;
        var terminal = window.CreateTerminal();
        var pty = new InMemoryPty();
        var tab = new TerminalTab
        {
            Title = id,
            SessionId = id,
            Kind = SessionKind.Local,
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
        return terminal;
    }

    private static Iciclecreek.Terminal.TerminalView GetInnerView(Iciclecreek.Terminal.TerminalControl terminal)
    {
        var view = terminal.GetVisualDescendants()
            .OfType<Iciclecreek.Terminal.TerminalView>()
            .FirstOrDefault();
        Assert.NotNull(view);
        return view!;
    }

    /// <summary>
    /// 事件位置必须换算到 window（TopLevel）坐标系（r45 实机教训：直接给 view 局部坐标
    /// 会整体偏掉 view 在窗口中的偏移）。
    /// </summary>
    private static (double x, double y) CellPoint(MainWindow window, Iciclecreek.Terminal.TerminalView view, int col, double rowFrac)
    {
        var local = new Point(view.GutterWidth + view.CharWidth * (col + 0.5), view.CharHeight * rowFrac);
        var p = view.TranslatePoint(local, window) ?? local;
        return (p.X, p.Y);
    }

    private static void Press(Iciclecreek.Terminal.TerminalView view, MainWindow window, double x, double y, int clickCount)
    {
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var props = new PointerPointProperties(RawInputModifiers.LeftMouseButton,
                                               PointerUpdateKind.LeftButtonPressed);
        view.RaiseEvent(new PointerPressedEventArgs(view, pointer, window,
            new Point(x, y), 0, props, KeyModifiers.None, clickCount)
        {
            RoutedEvent = InputElement.PointerPressedEvent,
        });
        Dispatcher.UIThread.RunJobs();
    }

    private static void Move(Iciclecreek.Terminal.TerminalView view, MainWindow window, double x, double y)
    {
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        view.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, view, pointer, window,
            new Point(x, y), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None));
        Dispatcher.UIThread.RunJobs();
    }

    private static void RunJobs(MainWindow? window)
    {
        Dispatcher.UIThread.RunJobs();
        window?.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
