using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

public sealed class TelnetSessionAppTests
{
    [Fact]
    public void TELNET_App_LoadSessionsAddsTelnetSectionAndMarksConnected()
    {
        using var env = new TestEnvironment();
        env.SeedConfig(new ComtoolConfig
        {
            TelnetSessions =
            {
                new TelnetSession
                {
                    Key = "telnet:alice@example.com:23",
                    Host = "example.com",
                    Port = 23,
                    User = "alice",
                    Alias = "lab",
                },
                new TelnetSession
                {
                    Key = "telnet:@router.lan:2323",
                    Host = "router.lan",
                    Port = 2323,
                    User = "",
                },
            },
        });

        var service = AppConfigService.Load();
        var viewModel = new MainViewModel();
        viewModel.LoadSessions(
            service,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(new[] { "telnet:alice@example.com:23" }, StringComparer.OrdinalIgnoreCase));

        var telnet = Assert.Single(viewModel.Sections, section => section.Title == "Telnet");
        Assert.Equal(2, telnet.Items.Count);
        var lab = Assert.Single(telnet.Items, item => item.SessionId == "telnet:alice@example.com:23");
        Assert.Equal("lab", lab.Name);
        Assert.Equal("alice@example.com:23", lab.Detail);
        Assert.True(lab.IsConnected);
        var router = Assert.Single(telnet.Items, item => item.SessionId == "telnet:@router.lan:2323");
        Assert.Equal("router.lan:2323", router.Detail);
        Assert.False(router.IsConnected);
    }

    [AvaloniaFact]
    public void TELNET_App_SettingsPanelSavesCredentialsAndRoutes()
    {
        using var env = new TestEnvironment();
        env.SeedConfig(new ComtoolConfig());
        var service = AppConfigService.Load();
        ConnectionSettingsWindow? window = null;
        string? openedKey = null;
        try
        {
            env.Run(() =>
            {
                Func<string, TerminalTab?, Task> open = (_, _) => Task.CompletedTask;
                window = new ConnectionSettingsWindow(
                    service,
                    null,
                    open,
                    open,
                    (key, _) =>
                    {
                        openedKey = key;
                        return Task.CompletedTask;
                    },
                    open,
                    _ => Task.CompletedTask);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                window.FindControl<ComboBox>("ConnectionTypeCombo")!.SelectedIndex = 2;
                Dispatcher.UIThread.RunJobs();
                Assert.True(window.FindControl<Grid>("TelnetPanel")!.IsVisible);

                window.FindControl<TextBox>("TelnetHostBox")!.Text = "example.com";
                window.FindControl<TextBox>("TelnetPortBox")!.Text = "2323";
                window.FindControl<TextBox>("TelnetUserBox")!.Text = "alice";
                window.FindControl<TextBox>("TelnetAliasBox")!.Text = "lab";
                window.FindControl<TextBox>("TelnetPasswordBox")!.Text = "secret";
                window.FindControl<CheckBox>("TelnetRememberCheck")!.IsChecked = true;
                window.FindControl<Button>("TelnetOpenButton")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal("telnet:alice@example.com:2323", openedKey);
        var session = service.TelnetSessions.Get(openedKey!);
        Assert.NotNull(session);
        Assert.Equal("lab", session!.Alias);
        Assert.Equal("alice", session.User);
        Assert.Equal("secret", service.PasswordCache.Get(openedKey!));
    }
}
