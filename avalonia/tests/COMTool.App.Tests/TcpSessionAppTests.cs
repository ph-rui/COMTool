using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

public sealed class TcpSessionAppTests
{
    [Fact]
    public void NET03_LoadSessionsAddsTcpSectionAndMarksConnected()
    {
        using var env = new TestEnvironment();
        env.SeedConfig(new ComtoolConfig
        {
            TcpSessions =
            {
                new TcpSession { Mode = "client", Target = "127.0.0.1:2345" },
                new TcpSession { Mode = "server", Port = 2346 },
            },
        });

        var service = AppConfigService.Load();
        var viewModel = new MainViewModel();
        viewModel.LoadSessions(
            service,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(new[] { "tcp:server:2346" }, StringComparer.OrdinalIgnoreCase));

        var tcp = Assert.Single(viewModel.Sections, section => section.Title == "TCP");
        Assert.Equal(2, tcp.Items.Count);
        Assert.Contains(tcp.Items, item =>
            item.SessionId == "tcp:server:2346" && item.IsConnected);
    }

    [AvaloniaFact]
    public void NET03_TcpConnectionSettingsPanelIsAvailable()
    {
        using var env = new TestEnvironment();
        env.SeedConfig(new ComtoolConfig());
        var service = AppConfigService.Load();
        ConnectionSettingsWindow? window = null;
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
                    open,
                    _ => Task.CompletedTask);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var type = window.FindControl<ComboBox>("ConnectionTypeCombo")!;
                var tcpPanel = window.FindControl<Grid>("TcpPanel")!;
                Assert.False(tcpPanel.IsVisible);

                type.SelectedIndex = 2;
                Dispatcher.UIThread.RunJobs();
                Assert.True(tcpPanel.IsVisible);

                var protocol = window.FindControl<ComboBox>("TcpProtocolCombo")!;
                var port = window.FindControl<TextBox>("TcpPortBox")!;
                var clients = window.FindControl<ComboBox>("TcpClientsCombo")!;
                var autoReconnect = window.FindControl<CheckBox>("TcpAutoReconnectCheck")!;
                protocol.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                Assert.False(port.IsVisible);
                Assert.False(clients.IsVisible);
                Assert.False(autoReconnect.IsVisible);

                protocol.SelectedIndex = 0;
                var mode = window.FindControl<ComboBox>("TcpModeCombo")!;
                mode.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                Assert.True(port.IsVisible);
                Assert.True(clients.IsVisible);
                Assert.False(autoReconnect.IsVisible);

                mode.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs();
                Assert.True(autoReconnect.IsVisible);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void NET04_OpenTcpSettingsSavesAutoReconnect()
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
                    _ => Task.CompletedTask);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                window.FindControl<ComboBox>("ConnectionTypeCombo")!.SelectedIndex = 2;
                window.FindControl<ComboBox>("TcpProtocolCombo")!.SelectedIndex = 0;
                window.FindControl<ComboBox>("TcpModeCombo")!.SelectedIndex = 0;
                window.FindControl<TextBox>("TcpTargetBox")!.Text = "127.0.0.1:2345";
                window.FindControl<CheckBox>("TcpAutoReconnectCheck")!.IsChecked = true;
                window.FindControl<TextBox>("TcpReconnectIntervalBox")!.Text = "0.5";
                window.FindControl<Button>("TcpOpenButton")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal("tcp:client:127.0.0.1:2345", openedKey);
        var session = service.TcpSessions.Get(openedKey!);
        Assert.NotNull(session);
        Assert.True(session!.AutoReconnect);
        Assert.Equal(0.5, session.ReconnectInterval);
    }
}
