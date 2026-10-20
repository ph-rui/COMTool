using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Connections;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

public sealed class SerialSidebarDetailsTests
{
    [AvaloniaFact]
    public void SER04_SerialSidebarShowsConnectionSummaryOnThirdLine()
    {
        using var env = new TestEnvironment();
        Window? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig
                {
                    SerialSessions =
                    {
                        new SerialSession
                        {
                            Port = "COM99",
                            Name = "USB-Enhanced-SERIAL-A CH344",
                            Serial = new SerialConnectionConfig
                            {
                                Port = "COM99",
                                Baudrate = 115200,
                                Bytesize = 8,
                                Parity = "None",
                                Stopbits = "1",
                            },
                        },
                    },
                });
                var scan = new SerialPortScanResult(
                    true,
                    new[] { new SerialPortInfo("COM99", "USB-Enhanced-SERIAL-A CH344") });
                var service = AppConfigService.Load(() => scan);
                var viewModel = new MainViewModel();
                var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                viewModel.LoadSessions(service, connected, connected, connected);

                var sidebar = new SessionSidebar { DataContext = viewModel };
                window = new Window
                {
                    Content = sidebar,
                    Width = 240,
                    Height = 300,
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var summary = sidebar.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(text => text.Text == "115200 8N1");
                Assert.True(summary.IsVisible);
                Assert.True(summary.Bounds.Height > 0);

                var row = summary.FindAncestorOfType<Grid>();
                Assert.NotNull(row);
                Assert.True(
                    row!.Bounds.Height >= 40,
                    $"serial row is below the minimum height: {row.Bounds.Height}");
                Assert.True(
                    summary.Bounds.Bottom <= row.Bounds.Bottom + 0.5,
                    $"summary clipped: bottom={summary.Bounds.Bottom}, row={row.Bounds.Bottom}");
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
