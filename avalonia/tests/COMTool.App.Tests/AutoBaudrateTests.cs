using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Connections;
using COMTool.Core.Config;
using Xunit;

namespace COMTool.App.Tests;

/// <summary>
/// r67.10: "Auto" entry in the serial baudrate combo. Selecting it scans the
/// port through <see cref="SerialBaudScanner"/> and saves the detected numeric
/// rate; the dialog stays open and reports an error when no candidate yields
/// readable text.
/// </summary>
public sealed class AutoBaudrateTests
{
    private static (ConnectionSettingsWindow Window, AppConfigService Config, List<string> Opened,
        List<string> Errors) OpenDialog(TestEnvironment env)
    {
        env.SeedConfig(new ComtoolConfig());
        var service = AppConfigService.Load();
        var opened = new List<string>();
        var errors = new List<string>();
        Func<string, TerminalTab?, Task> open = (id, _) =>
        {
            opened.Add(id);
            return Task.CompletedTask;
        };
        var window = new ConnectionSettingsWindow(
            service, null, open, open, open, open,
            message =>
            {
                errors.Add(message);
                return Task.CompletedTask;
            });
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, service, opened, errors);
    }

    private static void SelectPort(ConnectionSettingsWindow window, string device)
    {
        var portCombo = window.FindControl<ComboBox>("PortCombo")!;
        portCombo.ItemsSource = new List<SerialPortInfo>
        {
            new(device, "USB-SERIAL CH340"),
        };
        portCombo.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
    }

    private static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), "condition not reached within 5s");
    }

    [AvaloniaFact]
    public void UI18_BaudrateComboOffersAutoAsFirstEntry()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var (window, _, _, _) = OpenDialog(env);
            try
            {
                var combo = window.FindControl<ComboBox>("BaudrateCombo")!;
                var items = (combo.ItemsSource as IEnumerable<string>)!.ToList();
                Assert.Equal("Auto", items[0]);
                Assert.Contains("115200", items);
                Assert.Equal("115200", combo.Text);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [AvaloniaFact]
    public async Task UI18_AutoDetectsAndSavesNumericBaudrate()
    {
        using var env = new TestEnvironment();
        await env.RunAsync(async () =>
        {
            var release = new TaskCompletionSource<BaudScanOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var probed = new List<int>();
            ConnectionSettingsWindow.ScanForTests = (config, progress, ct) =>
            {
                Assert.Equal("COM7", config.Port);
                progress?.Invoke(9600);
                probed.Add(9600);
                return release.Task;
            };

            var (window, service, opened, errors) = OpenDialog(env);
            try
            {
                SelectPort(window, "COM7");
                var combo = window.FindControl<ComboBox>("BaudrateCombo")!;
                combo.Text = "Auto";
                var button = window.FindControl<Button>("SerialOpenButton")!;

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                // 扫描期间按钮禁用并显示当前探测速率。
                Assert.False(button.IsEnabled);
                Assert.Equal("Detecting 9600...", button.Content);

                release.SetResult(new BaudScanOutcome(
                    new BaudScanResult(19200, 0.99, 320),
                    new[] { 9600, 19200 }));
                WaitUntil(() => opened.Count > 0);

                Assert.Empty(errors);
                Assert.Equal(new[] { "COM7" }, opened);
                Assert.Equal("19200", combo.Text);
                Assert.True(button.IsEnabled);
                Assert.Equal("OPEN", button.Content);
                Assert.Equal(
                    19200,
                    service.SerialSessions.Get("COM7")!.Serial.Baudrate);
                Assert.False(window.IsVisible);
            }
            finally
            {
                ConnectionSettingsWindow.ScanForTests = null;
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [AvaloniaFact]
    public async Task UI18_AutoFailureKeepsDialogOpenAndReportsTriedRates()
    {
        using var env = new TestEnvironment();
        await env.RunAsync(async () =>
        {
            ConnectionSettingsWindow.ScanForTests = (_, _, _) =>
                Task.FromResult(new BaudScanOutcome(
                    null,
                    new[] { 115200, 9600 }));

            var (window, service, opened, errors) = OpenDialog(env);
            try
            {
                SelectPort(window, "COM7");
                window.FindControl<ComboBox>("BaudrateCombo")!.Text = "Auto";
                var button = window.FindControl<Button>("SerialOpenButton")!;

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitUntil(() => errors.Count > 0);

                Assert.Empty(opened);
                Assert.Null(service.SerialSessions.Get("COM7"));
                Assert.True(window.IsVisible);
                Assert.True(button.IsEnabled);
                Assert.Equal("OPEN", button.Content);
                Assert.Contains("115200", errors[0]);
                Assert.Contains("9600", errors[0]);
                Assert.Contains("readable text", errors[0]);
            }
            finally
            {
                ConnectionSettingsWindow.ScanForTests = null;
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [AvaloniaFact]
    public void UI18_OtherBaudratePathsUnaffected()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            ConnectionSettingsWindow.ScanForTests = (_, _, _) =>
            {
                throw new InvalidOperationException("Auto scan must not run for numeric rates");
            };

            var (window, service, opened, errors) = OpenDialog(env);
            try
            {
                SelectPort(window, "COM7");
                window.FindControl<ComboBox>("BaudrateCombo")!.Text = "57600";
                window.FindControl<Button>("SerialOpenButton")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitUntil(() => opened.Count > 0);

                Assert.Empty(errors);
                Assert.Equal(
                    57600,
                    service.SerialSessions.Get("COM7")!.Serial.Baudrate);
            }
            finally
            {
                ConnectionSettingsWindow.ScanForTests = null;
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }
}
