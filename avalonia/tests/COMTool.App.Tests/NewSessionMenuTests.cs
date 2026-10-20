using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

public sealed class NewSessionMenuTests
{
    [AvaloniaFact]
    public void UI06_NewSessionMenuReplacesPluginEntries()
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

                AssertSessionMenu(window.FindControl<Button>("EmptyAddSessionButton")!);
                AssertSessionMenu(window.FindControl<Button>("ToolbarAddSessionButton")!);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI06_NewSessionSettingsPreselectsRequestedTransport()
    {
        using var env = new TestEnvironment();
        env.SeedConfig(new ComtoolConfig());
        var service = AppConfigService.Load();

        env.Run(() =>
        {
            var cases = new (SessionKind Kind, int Index, string Panel)[]
            {
                (SessionKind.Serial, 0, "SerialPanel"),
                (SessionKind.Ssh, 1, "SshPanel"),
                (SessionKind.Telnet, 2, "TelnetPanel"),
                (SessionKind.Tcp, 3, "TcpPanel"),
            };

            foreach (var (kind, index, panelName) in cases)
            {
                ConnectionSettingsWindow? window = null;
                try
                {
                    Func<string, TerminalTab?, Task> open = (_, _) => Task.CompletedTask;
                    window = new ConnectionSettingsWindow(
                        service,
                        null,
                        open,
                        open,
                        open,
                        open,
                        _ => Task.CompletedTask,
                        kind);
                    window.Show();
                    Dispatcher.UIThread.RunJobs();

                    Assert.Equal(
                        index,
                        window.FindControl<ComboBox>("ConnectionTypeCombo")!.SelectedIndex);
                    Assert.True(window.FindControl<Grid>(panelName)!.IsVisible);
                    Assert.Equal("New session", window.Title);
                }
                finally
                {
                    window?.Close();
                    Dispatcher.UIThread.RunJobs();
                }
            }
        });
    }

    [AvaloniaFact]
    public void UI06_LocalTerminalEntryAddsLocalTab()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                var viewModel = new MainViewModel();
                window = new MainWindow { DataContext = viewModel };

                var button = window.FindControl<Button>("EmptyAddSessionButton")!;
                var local = ((MenuFlyout)button.Flyout!).Items
                    .OfType<MenuItem>()
                    .Single(item => item.Tag?.ToString() == "Local");

                local.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                var tab = Assert.Single(viewModel.Tabs);
                Assert.Equal(SessionKind.Local, tab.Kind);
                Assert.StartsWith("terminal:", tab.SessionId, StringComparison.Ordinal);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI07_EmptyStatePointsToNewSession()
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

                var hint = window.FindControl<TextBlock>("EmptyStateAddHint")!;
                Assert.Equal("or click + to add a session", hint.Text);
                Assert.Equal(
                    "New session",
                    ToolTip.GetTip(window.FindControl<Button>("EmptyAddSessionButton")!)?.ToString());
                Assert.Equal(
                    "New session",
                    ToolTip.GetTip(window.FindControl<Button>("ToolbarAddSessionButton")!)?.ToString());
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void AssertSessionMenu(Button button)
    {
        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        var items = flyout.Items.OfType<MenuItem>().ToList();

        Assert.Equal(
            new[] { "Serial", "SSH", "Telnet", "TCP", "Local terminal" },
            items.Select(item => item.Header?.ToString()).ToArray());
        Assert.Equal(
            new[] { "Serial", "SSH", "Telnet", "TCP", "Local" },
            items.Select(item => item.Tag?.ToString()).ToArray());
        Assert.DoesNotContain(
            items,
            item => item.Header?.ToString()?.Contains("plugin", StringComparison.OrdinalIgnoreCase) == true);
    }
}
