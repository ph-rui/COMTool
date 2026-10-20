using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.Connections;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

public sealed class SerialHotplugTests
{
    [Fact]
    public void SER03_MissingPortDoesNotDeleteRememberedSerialSession()
    {
        using var env = new TestEnvironment();
        env.SeedConfig(new ComtoolConfig
        {
            SerialSessions =
            {
                new SerialSession
                {
                    Port = "COM999",
                    Name = "Missing test port",
                    Serial = new SerialConnectionConfig { Port = "COM999" },
                },
            },
        });

        var service = AppConfigService.Load();

        Assert.NotNull(service.SerialSessions.Get("COM999"));
    }

    [Fact]
    public void SER03_RepluggedPortRestoresSidebarSession()
    {
        using var env = new TestEnvironment();
        env.SeedConfig(new ComtoolConfig
        {
            SerialSessions =
            {
                new SerialSession
                {
                    Port = "COM999",
                    Name = "Missing test port",
                    Serial = new SerialConnectionConfig { Port = "COM999" },
                },
            },
        });

        var present = new SerialPortScanResult(
            true,
            new[] { new SerialPortInfo("COM999", "USB-SERIAL CH340") });
        var service = AppConfigService.Load(() => present);
        var viewModel = new MainViewModel();
        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        viewModel.LoadSessions(service, connected, connected, connected);
        Assert.True(HasSerial(viewModel, "COM999"));

        // Unplug: the sidebar hides the unavailable port, but the remembered
        // session is retained.
        service.RefreshPorts(new SerialPortScanResult(
            true,
            Array.Empty<SerialPortInfo>()));
        viewModel.LoadSessions(service, connected, connected, connected);
        Assert.False(HasSerial(viewModel, "COM999"));
        Assert.NotNull(service.SerialSessions.Get("COM999"));

        // Replug the same COM port: it returns without re-adding the session.
        service.RefreshPorts(present);
        viewModel.LoadSessions(service, connected, connected, connected);
        Assert.True(HasSerial(viewModel, "COM999"));
    }

    private static bool HasSerial(MainViewModel viewModel, string port) =>
        viewModel.Sections
            .Where(section => section.Title == "Serial")
            .SelectMany(section => section.Items)
            .Any(item => string.Equals(
                item.SessionId,
                port,
                StringComparison.OrdinalIgnoreCase));
}
