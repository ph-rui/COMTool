using System.Text.Json;
using COMTool.Core.Config;

namespace COMTool.Core.Tests;

public sealed class ConfigTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "comtool-avalonia-tests",
        Guid.NewGuid().ToString("N"));

    public ConfigTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsPythonConfig()
    {
        var path = Path.Combine(_directory, "config.json");
        var config = new ComtoolConfig
        {
            Skin = "dark",
            Locale = "zh_CN",
            Encoding = "GBK",
        };
        config.SerialSessions.Add(new SerialSession
        {
            Port = "COM99",
            Name = "COM99  USB-SERIAL CH340",
            Serial = new SerialConnectionConfig
            {
                Port = "COM99",
                Baudrate = 921600,
                Parity = "Even",
                Rts = true,
            },
        });
        config.SshSessions.Add(new SshSession
        {
            Key = "root@example.com:22",
            Host = "Example.com",
            Port = 22,
            User = "root",
            Alias = "lab",
            EncryptedPassword = "dpapi-token",
        });
        config.TelnetSessions.Add(new TelnetSession
        {
            Key = "telnet:alice@example.com:23",
            Host = "example.com",
            Port = 23,
            User = "alice",
            Alias = "telnet-lab",
            EncryptedPassword = "telnet-token",
        });

        config.Save(path);
        var loaded = ComtoolConfig.Load(path);

        Assert.Equal("dark", loaded.Skin);
        Assert.Equal("GBK", loaded.Encoding);
        Assert.Single(loaded.SerialSessions);
        Assert.Equal(921600, loaded.SerialSessions[0].Serial.Baudrate);
        Assert.Equal("dpapi-token", loaded.SshSessions[0].EncryptedPassword);
        Assert.Equal("telnet-token", loaded.TelnetSessions[0].EncryptedPassword);
        Assert.Equal("telnet", loaded.TelnetSessions[0].Transport);
    }

    [Fact]
    public void Load_OldVersion_BacksUpAndReturnsDefaults()
    {
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, "{\"version\":2,\"skin\":\"dark\"}");

        var config = ComtoolConfig.Load(path);

        Assert.Equal(3, config.Version);
        Assert.Equal("light", config.Skin);
        Assert.Single(Directory.GetFiles(_directory, "config.json.bak.*.json"));
    }

    [Fact]
    public void UnknownFields_ArePreservedOnSave()
    {
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, """
        {
          "version": 3,
          "skin": "light",
          "futureField": { "enabled": true },
          "sessionSidebar": { "visible": false, "width": 240, "extra": 1 }
        }
        """);

        var config = ComtoolConfig.Load(path);
        config.Save(path);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(document.RootElement.TryGetProperty("futureField", out var future));
        Assert.True(future.GetProperty("enabled").GetBoolean());
        Assert.Equal(240, document.RootElement
            .GetProperty("sessionSidebar")
            .GetProperty("width")
            .GetInt32());
    }

    [Fact]
    public void SaveAndLoad_RoundTripsSessionSidebarAndTheme()
    {
        var path = Path.Combine(_directory, "config.json");
        var config = new ComtoolConfig
        {
            Skin = "dark",
            SessionSidebar = new SessionSidebarConfig
            {
                Visible = false,
                Width = 287,
            },
        };

        config.Save(path);
        var loaded = ComtoolConfig.Load(path);

        Assert.Equal("dark", loaded.Skin);
        Assert.False(loaded.SessionSidebar.Visible);
        Assert.Equal(287, loaded.SessionSidebar.Width);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsWindowSize()
    {
        var path = Path.Combine(_directory, "config.json");
        var config = new ComtoolConfig
        {
            Window = new WindowConfig
            {
                Width = 1000,
                Height = 650,
            },
        };

        config.Save(path);
        var loaded = ComtoolConfig.Load(path);

        Assert.Equal(1000, loaded.Window.Width);
        Assert.Equal(650, loaded.Window.Height);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsNetworkScanRange()
    {
        var path = Path.Combine(_directory, "config.json");
        var config = new ComtoolConfig
        {
            NetworkScan = new NetworkScanConfig
            {
                Octet1 = "10",
                Octet2 = "13",
                Octet3 = "1",
                From = "20",
                To = "40",
            },
        };

        config.Save(path);
        var loaded = ComtoolConfig.Load(path);

        Assert.Equal("10", loaded.NetworkScan.Octet1);
        Assert.Equal("13", loaded.NetworkScan.Octet2);
        Assert.Equal("1", loaded.NetworkScan.Octet3);
        Assert.Equal("20", loaded.NetworkScan.From);
        Assert.Equal("40", loaded.NetworkScan.To);
    }

    [Fact]
    public void Load_ConfigWithoutNetworkScan_UsesDefaults()
    {
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, """{"version":3,"skin":"dark"}""");

        var loaded = ComtoolConfig.Load(path);

        Assert.Equal("192", loaded.NetworkScan.Octet1);
        Assert.Equal("168", loaded.NetworkScan.Octet2);
        Assert.Equal("1", loaded.NetworkScan.Octet3);
        Assert.Equal("1", loaded.NetworkScan.From);
        Assert.Equal("254", loaded.NetworkScan.To);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsPortScanSettings()
    {
        var path = Path.Combine(_directory, "config.json");
        var config = new ComtoolConfig
        {
            PortScan = new PortScanConfig
            {
                Ip = "192.0.2.251",
                From = "1000",
                To = "2000",
            },
        };

        config.Save(path);
        var loaded = ComtoolConfig.Load(path);

        Assert.Equal("192.0.2.251", loaded.PortScan.Ip);
        Assert.Equal("1000", loaded.PortScan.From);
        Assert.Equal("2000", loaded.PortScan.To);
    }

    [Fact]
    public void Load_ConfigWithoutPortScan_UsesDefaults()
    {
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, """{"version":3,"skin":"dark"}""");

        var loaded = ComtoolConfig.Load(path);

        Assert.Equal("127.0.0.1", loaded.PortScan.Ip);
        Assert.Equal("1", loaded.PortScan.From);
        Assert.Equal("65535", loaded.PortScan.To);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsRemoteMonitorOptions()
    {
        var path = Path.Combine(_directory, "config.json");
        var config = new ComtoolConfig
        {
            RemoteMonitor = new RemoteMonitorConfig
            {
                ShowHost = false,
                ShowCpu = true,
                ShowRam = false,
                ShowNetUp = true,
                ShowNetDown = false,
                ShowUptime = true,
                ShowPartitions = false,
            },
        };

        config.Save(path);
        var loaded = ComtoolConfig.Load(path);

        Assert.False(loaded.RemoteMonitor.ShowHost);
        Assert.True(loaded.RemoteMonitor.ShowCpu);
        Assert.False(loaded.RemoteMonitor.ShowRam);
        Assert.True(loaded.RemoteMonitor.ShowNetUp);
        Assert.False(loaded.RemoteMonitor.ShowNetDown);
        Assert.True(loaded.RemoteMonitor.ShowUptime);
        Assert.False(loaded.RemoteMonitor.ShowPartitions);
    }

    [Fact]
    public void Load_ConfigWithoutRemoteMonitor_UsesMobaXtermDefaults()
    {
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, """{"version":3,"skin":"dark"}""");

        var loaded = ComtoolConfig.Load(path);

        Assert.True(loaded.RemoteMonitor.ShowHost);
        Assert.True(loaded.RemoteMonitor.ShowCpu);
        Assert.True(loaded.RemoteMonitor.ShowRam);
        Assert.True(loaded.RemoteMonitor.ShowNetUp);
        Assert.True(loaded.RemoteMonitor.ShowNetDown);
        Assert.True(loaded.RemoteMonitor.ShowUptime);
        Assert.True(loaded.RemoteMonitor.ShowPartitions);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsMacros()
    {
        var path = Path.Combine(_directory, "config.json");
        var config = new ComtoolConfig();
        config.Macros.Add(new MacroConfig
        {
            Id = "macro-1",
            Name = "reboot",
            Text = "reboot now",
            AppendEnter = true,
        });
        config.Macros.Add(new MacroConfig
        {
            Id = "macro-2",
            Name = "hostname",
            Text = "hostname",
            AppendEnter = false,
        });
        config.Save(path);
        var loaded = ComtoolConfig.Load(path);

        Assert.Equal(2, loaded.Macros.Count);
        Assert.Equal("macro-1", loaded.Macros[0].Id);
        Assert.Equal("reboot", loaded.Macros[0].Name);
        Assert.Equal("reboot now", loaded.Macros[0].Text);
        Assert.True(loaded.Macros[0].AppendEnter);
        Assert.Equal("macro-2", loaded.Macros[1].Id);
        Assert.False(loaded.Macros[1].AppendEnter);
    }

    [Fact]
    public void Load_OldConfigWithoutMacros_LoadsEmpty()
    {
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, """
        {
          "version": 3,
          "skin": "light"
        }
        """);

        var config = ComtoolConfig.Load(path);

        Assert.Empty(config.Macros);
    }

    [Fact]
    public void AvaloniaConfigDirectory_IsSeparateFromPython()
    {
        Assert.NotEqual(
            Path.GetFullPath(ConfigPaths.PythonConfigDirectory()),
            Path.GetFullPath(ConfigPaths.AvaloniaConfigDirectory()));
    }

    [Fact]
    public void RM03_SshSessionMonitorFlagRoundTripsAndPersistsThroughStore()
    {
        // monitor=true 随配置保存/加载往返
        var path = Path.Combine(_directory, "cfg-monitor.json");
        var config = new ComtoolConfig();
        config.SshSessions.Add(new SshSession
        {
            Key = "root@192.168.1.10:22",
            Host = "192.168.1.10",
            Port = 22,
            User = "root",
            Monitor = true,
        });
        config.Save(path);
        var loaded = ComtoolConfig.Load(path);
        Assert.True(loaded.SshSessions[0].Monitor);

        // 旧配置（无 monitor 键）向后兼容 -> false
        var legacyPath = Path.Combine(_directory, "cfg-monitor-legacy.json");
        File.WriteAllText(legacyPath,
            "{ \"version\": 3, \"sshSessions\": [ { \"key\": \"a@b:22\", \"host\": \"b\", \"port\": 22, \"user\": \"a\" } ] }");
        var legacy = ComtoolConfig.Load(legacyPath);
        Assert.False(legacy.SshSessions[0].Monitor);

        // Store.SetMonitor 更新；编辑会话（Upsert）不丢已记忆的开关
        var store = new SshSessionStore();
        var seed = new ComtoolConfig();
        seed.SshSessions.Add(new SshSession
        {
            Key = "u@h:22",
            Host = "h",
            Port = 22,
            User = "u",
            Monitor = true,
        });
        store.Load(seed);
        store.SetMonitor("u@h:22", true);
        store.Upsert("h", 22, "u", alias: "alias-x");
        var outConfig = new ComtoolConfig();
        store.Save(outConfig);
        Assert.True(outConfig.SshSessions[0].Monitor);
        Assert.Equal("alias-x", outConfig.SshSessions[0].Alias);

        store.SetMonitor("u@h:22", false);
        var outConfig2 = new ComtoolConfig();
        store.Save(outConfig2);
        Assert.False(outConfig2.SshSessions[0].Monitor);
    }
}
