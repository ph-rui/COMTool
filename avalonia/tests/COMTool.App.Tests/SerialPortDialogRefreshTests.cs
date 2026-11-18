using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Connections;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

/// <summary>
/// r67.6: the connection dialog must follow serial hot-plug while it is open.
/// The original live-refresh design (2026-10-17, G1/G2) requires a device
/// inserted while the port list is visible to show up within a second; the
/// Avalonia port only enumerated once at construction, so a device plugged in
/// after opening the dialog was invisible until it was closed and reopened.
/// </summary>
public sealed class SerialPortDialogRefreshTests
{
    private static ConnectionSettingsWindow OpenDialog(AppConfigService service)
    {
        Func<string, TerminalTab?, Task> open = (_, _) => Task.CompletedTask;
        var window = new ConnectionSettingsWindow(
            service, null, open, open, open, open, _ => Task.CompletedTask);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void SER03_PortComboPicksUpDeviceInsertedWhileOpen()
    {
        using var env = new TestEnvironment();
        env.SeedConfig(new ComtoolConfig());
        env.Run(() =>
        {
            var service = AppConfigService.Load(() => new SerialPortScanResult(
                true,
                Array.Empty<SerialPortInfo>()));
            var window = OpenDialog(service);
            try
            {
                var combo = window.FindControl<ComboBox>("PortCombo");
                Assert.NotNull(combo);
                Assert.Empty(combo!.ItemsSource as IList<SerialPortInfo>
                    ?? Array.Empty<SerialPortInfo>());
                Assert.Null(combo.SelectedItem);

                // Device inserted while the dialog is open: the shared scan
                // result changes, the dialog picks it up on its next tick.
                service.RefreshPorts(new SerialPortScanResult(
                    true,
                    new[] { new SerialPortInfo("COM777", "USB-SERIAL CH340") }));
                window.RefreshPortList();
                Dispatcher.UIThread.RunJobs();

                var items = Assert.IsAssignableFrom<IList<SerialPortInfo>>(combo.ItemsSource);
                var item = Assert.Single(items);
                Assert.Equal("COM777", item.Device);
                Assert.Equal("USB-SERIAL CH340", item.DisplayLabel);
                // Auto-selected so OPEN works right away.
                Assert.Equal("COM777", (combo.SelectedItem as SerialPortInfo)?.Device);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [AvaloniaFact]
    public void SER03_PortComboKeepsSelectionAndDropsRemovedPorts()
    {
        using var env = new TestEnvironment();
        env.SeedConfig(new ComtoolConfig());
        env.Run(() =>
        {
            var service = AppConfigService.Load(() => new SerialPortScanResult(
                true,
                new[]
                {
                    new SerialPortInfo("COM777", "USB-SERIAL CH340"),
                    new SerialPortInfo("COM778", "XR21V1410 USB UART"),
                }));
            var window = OpenDialog(service);
            try
            {
                var combo = window.FindControl<ComboBox>("PortCombo");
                Assert.NotNull(combo);
                var first = Assert.IsAssignableFrom<IList<SerialPortInfo>>(combo!.ItemsSource);
                Assert.Equal(2, first.Count);
                combo.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();

                // COM778 unplugged, COM779 appeared: the selection follows the
                // same device when it is still present and never jumps on its
                // own, but a removed port disappears from the list.
                service.RefreshPorts(new SerialPortScanResult(
                    true,
                    new[]
                    {
                        new SerialPortInfo("COM777", "USB-SERIAL CH340"),
                        new SerialPortInfo("COM779", "CP2102 USB to UART"),
                    }));
                window.RefreshPortList();
                Dispatcher.UIThread.RunJobs();

                var items = Assert.IsAssignableFrom<IList<SerialPortInfo>>(combo.ItemsSource);
                Assert.Equal(2, items.Count);
                Assert.DoesNotContain(items, port => port.Device == "COM778");
                Assert.Contains(items, port => port.Device == "COM779");
                // The previously selected (now gone) port is not silently
                // carried over; the first available port is selected.
                Assert.Equal("COM777", (combo.SelectedItem as SerialPortInfo)?.Device);

                // A refresh with no changes must not rebuild the list.
                var same = combo.ItemsSource;
                window.RefreshPortList();
                Assert.Same(same, combo.ItemsSource);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }
}
