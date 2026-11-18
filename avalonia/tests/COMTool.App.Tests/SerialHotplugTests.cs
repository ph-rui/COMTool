using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.Connections;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

public sealed class SerialHotplugTests
{
    /// <summary>r67.7：配置文件损坏（2026-11-18 实机断电后 8.7KB 全 0x00）不能让应用
    /// 「无配置运行」——坏文件改名留证，按默认配置继续并立刻写回一份可用配置，
    /// 否则会话列表永远为空、后续设置也不落盘。</summary>
    [Fact]
    public void CFG01_CorruptConfigIsQuarantinedAndReset()
    {
        using var env = new TestEnvironment();
        Directory.CreateDirectory(env.AvaloniaConfigDir);
        var path = Path.Combine(env.AvaloniaConfigDir, "config.json");
        File.WriteAllBytes(path, new byte[256]);

        var service = AppConfigService.Load();

        Assert.Equal(path, service.ConfigPath);
        Assert.NotNull(service.Config);
        Assert.Equal(3, service.Config.Version);
        Assert.Empty(service.Config.SerialSessions);

        // 坏文件改名留证（含时间戳），原路径已是可解析的新配置。
        var quarantined = Directory.GetFiles(env.AvaloniaConfigDir, "config.json.corrupt.*");
        Assert.Single(quarantined);
        // 取证文件保持原字节（全 0x00），便于事后确认损坏形态。
        Assert.All(File.ReadAllBytes(quarantined[0]), b => Assert.Equal(0, b));

        var rewritten = ComtoolConfig.Load(path);
        Assert.Equal(3, rewritten.Version);
        Assert.Empty(rewritten.SerialSessions);
    }

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
            .Where(section => section.Title == "串口")
            .SelectMany(section => section.Items)
            .Any(item => string.Equals(
                item.SessionId,
                port,
                StringComparison.OrdinalIgnoreCase));
}
