using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
}
