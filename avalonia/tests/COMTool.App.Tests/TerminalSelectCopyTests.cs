using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;
using Xunit;

namespace COMTool.App.Tests;

/// <summary>
/// TERM-32: mouse select-to-copy (MobaXterm parity, r45 / lib 4.1.3).
/// Verifies that the AutoCopyOnSelection property is correctly wired through
/// the TerminalControl template to the inner TerminalView, and that the
/// default value is true (matching MobaXterm's default behavior).
///
/// The actual copy-on-release behavior is covered by the library-level
/// AutoCopySelectionTests (Iciclecreek.Avalonia.Terminal.Tests), which
/// simulate full press/move/release pointer sequences and assert the
/// headless clipboard contents.
/// </summary>
public sealed class TerminalSelectCopyTests
{
    [AvaloniaFact]
    public void TERM32_AutoCopyOnSelectionDefaultsToTrue()
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

                var terminal = CreateTerminalTab(window, "selcopy-default");
                var view = GetInnerView(terminal);

                // The template binding AutoCopyOnSelection="{TemplateBinding AutoCopyOnSelection}"
                // must propagate the control-level default (true) to the inner view.
                Assert.True(view.AutoCopyOnSelection,
                    "AutoCopyOnSelection should default to true (MobaXterm parity)");
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TERM32_AutoCopyOnSelectionCanBeDisabled()
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

                var terminal = CreateTerminalTab(window, "selcopy-off");
                terminal.AutoCopyOnSelection = false;
                RunJobs(window);

                var view = GetInnerView(terminal);
                Assert.False(view.AutoCopyOnSelection,
                    "Setting AutoCopyOnSelection=false on TerminalControl should propagate to inner view");
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // ─────────────────────────── test helpers ───────────────────────────

    private static Iciclecreek.Terminal.TerminalControl CreateTerminalTab(
        MainWindow window, string id)
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

    private static Iciclecreek.Terminal.TerminalView GetInnerView(
        Iciclecreek.Terminal.TerminalControl terminal)
    {
        var view = terminal.GetVisualDescendants()
            .OfType<Iciclecreek.Terminal.TerminalView>()
            .FirstOrDefault();
        Assert.NotNull(view);
        return view!;
    }

    private static void RunJobs(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
