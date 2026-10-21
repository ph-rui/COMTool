using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

public sealed class SessionEditTests
{
    [AvaloniaFact]
    public void UI04_SessionContextMenuIncludesEditSession()
    {
        var sidebar = new SessionSidebar { DataContext = new MainViewModel() };
        var item = new SessionItem
        {
            Name = "router",
            SessionId = "telnet:root@192.168.1.12:23",
            Kind = SessionKind.Telnet,
        };
        SessionItem? edited = null;
        sidebar.EditRequested += (_, requested) => edited = requested;

        var menu = sidebar.CreateSessionContextMenu(item);
        Assert.Equal(
            new[] { "Open", "Edit session", "Remove session" },
            menu.Items.OfType<MenuItem>().Select(entry => entry.Header?.ToString()).ToArray());

        var edit = menu.Items.OfType<MenuItem>().Single(
            entry => entry.Header?.ToString() == "Edit session");
        edit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Same(item, edited);
    }

    [AvaloniaFact]
    public void UI04_SessionEditMenuOpensPopulatedSavedSettings()
    {
        using var env = new TestEnvironment();
        var seeded = new ComtoolConfig();
        env.SeedConfig(seeded);
        var service = AppConfigService.Load();
        var saved = service.TelnetSessions.Upsert(
            "192.168.1.12",
            23,
            "root",
            alias: "");
        Assert.NotNull(saved);
        service.Save();

        MainWindow? window = null;
        ConnectionSettingsWindow? settings = null;
        try
        {
            env.Run(() =>
            {
                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.StopPortTimerForTests();
                window.StopSidebarHoverTimerForTests();

                var viewModel = (MainViewModel)window.DataContext!;
                var item = viewModel.Sections
                    .SelectMany(section => section.Items)
                    .Single(session => session.Kind == SessionKind.Telnet
                        && session.SessionId == saved.Key);
                var menu = window.FindControl<SessionSidebar>("Sidebar")!
                    .CreateSessionContextMenu(item);
                var edit = menu.Items.OfType<MenuItem>().Single(
                    entry => entry.Header?.ToString() == "Edit session");
                edit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                settings = window.SettingsWindowForTests;
                Assert.NotNull(settings);
                Assert.Equal("192.168.1.12 - Connection settings", settings.Title);
                Assert.Equal(
                    2,
                    settings.FindControl<ComboBox>("ConnectionTypeCombo")!.SelectedIndex);
                Assert.True(settings.FindControl<Grid>("TelnetPanel")!.IsVisible);
                Assert.Equal(
                    "192.168.1.12",
                    settings.FindControl<TextBox>("TelnetHostBox")!.Text);
                Assert.Equal("23", settings.FindControl<TextBox>("TelnetPortBox")!.Text);
                Assert.Equal("root", settings.FindControl<TextBox>("TelnetUserBox")!.Text);
            });
        }
        finally
        {
            settings?.Close();
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void UI08_TabSettingsGearTargetsSelectedTab()
    {
        using var env = new TestEnvironment();
        env.SeedConfig(new ComtoolConfig());
        MainWindow? window = null;
        ConnectionSettingsWindow? settings = null;
        try
        {
            env.Run(() =>
            {
                var first = new TerminalTab
                {
                    Title = "First tab",
                    SessionId = "first.example",
                    Kind = SessionKind.Ssh,
                    Content = new Border(),
                };
                var second = new TerminalTab
                {
                    Title = "Second tab",
                    SessionId = "second.example",
                    Kind = SessionKind.Ssh,
                    Content = new Border(),
                };
                var viewModel = new MainViewModel();
                window = new MainWindow { DataContext = viewModel };
                window.Show();
                window.StopPortTimerForTests();
                window.StopSidebarHoverTimerForTests();
                viewModel.AddTab(first);
                viewModel.AddTab(second);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var gears = window.GetVisualDescendants()
                    .OfType<Button>()
                    .Where(button => button.Name == "TabSettingsButton")
                    .ToList();
                Assert.Equal(2, gears.Count);
                var firstGear = gears.Single(button =>
                    ReferenceEquals(button.DataContext, first));
                var secondGear = gears.Single(button =>
                    ReferenceEquals(button.DataContext, second));
                Assert.False(firstGear.IsVisible);
                Assert.False(secondGear.IsVisible);
                Assert.Contains(
                    "settings",
                    ToolTip.GetTip(secondGear)?.ToString(),
                    StringComparison.OrdinalIgnoreCase);

                // Hovering a tab does not reveal settings until that tab is selected.
                first.IsTabHovered = true;
                Dispatcher.UIThread.RunJobs();
                Assert.False(firstGear.IsVisible);

                viewModel.SelectedTab = first;
                Dispatcher.UIThread.RunJobs();
                Assert.True(firstGear.IsVisible);
                Assert.False(secondGear.IsVisible);

                viewModel.SelectedTab = second;
                Dispatcher.UIThread.RunJobs();
                Assert.False(firstGear.IsVisible);
                Assert.False(secondGear.IsVisible);

                // The selected tab reveals its settings gear while hovered.
                second.IsTabHovered = true;
                Dispatcher.UIThread.RunJobs();
                Assert.True(secondGear.IsVisible);

                secondGear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                settings = window.SettingsWindowForTests;
                Assert.NotNull(settings);
                Assert.Equal("Second tab - Connection settings", settings.Title);
            });
        }
        finally
        {
            settings?.Close();
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
