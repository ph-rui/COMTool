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
    public void SaveAndLoad_RoundTripsMacrosAndRecentSessions()
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
        config.RecentSessions.Add(new RecentSessionConfig
        {
            Kind = "Ssh",
            Id = "root@192.168.1.10:22",
            At = 1761234567890L,
        });
        config.RecentSessions.Add(new RecentSessionConfig
        {
            Kind = "Serial",
            Id = "COM99",
            At = 1761234567000L,
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

        Assert.Equal(2, loaded.RecentSessions.Count);
        Assert.Equal("Ssh", loaded.RecentSessions[0].Kind);
        Assert.Equal("root@192.168.1.10:22", loaded.RecentSessions[0].Id);
        Assert.Equal(1761234567890L, loaded.RecentSessions[0].At);
        Assert.Equal("Serial", loaded.RecentSessions[1].Kind);
        Assert.Equal("COM99", loaded.RecentSessions[1].Id);
    }

    [Fact]
    public void Load_OldConfigWithoutMacrosOrHistory_LoadsEmpty()
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
        Assert.Empty(config.RecentSessions);
    }

    [Fact]
    public void AvaloniaConfigDirectory_IsSeparateFromPython()
    {
        Assert.NotEqual(
            Path.GetFullPath(ConfigPaths.PythonConfigDirectory()),
            Path.GetFullPath(ConfigPaths.AvaloniaConfigDirectory()));
    }
}
