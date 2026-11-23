using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.Core.Config;
using Xunit;

namespace COMTool.App.Tests;

/// <summary>
/// r67.9: remove-session confirmation wording. The old dialog concatenated
/// "Remove this {kind} session from memory?" and the session label into one
/// TextBlock; the prompt now separates question / name / endpoint so the
/// dialog can render three levels and drop duplicated text.
/// </summary>
public sealed class SessionRemovalPromptTests
{
    private static AppConfigService Load(TestEnvironment env, ComtoolConfig config)
    {
        env.SeedConfig(config);
        return AppConfigService.Load();
    }

    [Fact]
    public void UI17_RemovalPromptSplitsTelnetNameAndEndpoint()
    {
        using var env = new TestEnvironment();
        var telnet = new TelnetSession
        {
            Host = "192.168.1.12",
            Port = 23,
            User = "root",
        };
        var config = Load(env, new ComtoolConfig { TelnetSessions = { telnet } });

        var prompt = SessionRemovalPrompt.Build(
            SessionKind.Telnet, TelnetSessionRules.Key("192.168.1.12", 23, "root"), config);

        Assert.Equal("Remove session", prompt.Title);
        Assert.Equal("Remove this Telnet session?", prompt.Question);
        Assert.Equal("192.168.1.12", prompt.Name);
        Assert.Equal("root@192.168.1.12:23", prompt.Detail);
        Assert.Equal("", prompt.Note);
    }

    [Fact]
    public void UI17_RemovalPromptUsesAliasAndDropsDuplicateDetail()
    {
        using var env = new TestEnvironment();
        var aliased = new SshSession
        {
            Host = "192.0.2.48",
            Port = 22,
            User = "devuser",
            Alias = "devboard",
        };
        // DisplayName == Detail for this one: alias repeats "user@host:port".
        var duplicated = new SshSession
        {
            Host = "192.0.2.49",
            Port = 22,
            User = "devuser",
            Alias = "devuser@192.0.2.49:22",
        };
        var config = Load(env, new ComtoolConfig { SshSessions = { aliased, duplicated } });

        var ssh = SessionRemovalPrompt.Build(
            SessionKind.Ssh, SshSessionRules.Key("192.0.2.48", 22, "devuser"), config);
        Assert.Equal("Remove this SSH session?", ssh.Question);
        Assert.Equal("devboard", ssh.Name);
        Assert.Equal("devuser@192.0.2.48:22", ssh.Detail);

        var same = SessionRemovalPrompt.Build(
            SessionKind.Ssh, SshSessionRules.Key("192.0.2.49", 22, "devuser"), config);
        Assert.Equal("devuser@192.0.2.49:22", same.Name);
        Assert.Equal("", same.Detail);
        Assert.Equal("", same.Note);
    }

    [Fact]
    public void UI17_RemovalPromptHandlesTcpAndSerial()
    {
        using var env = new TestEnvironment();
        var tcp = new TcpSession
        {
            Protocol = "tcp",
            Mode = "client",
            Target = "192.168.1.50:9000",
            Port = 2345,
        };
        var config = Load(env, new ComtoolConfig
        {
            TcpSessions = { tcp },
            SerialSessions = { new SerialSession { Port = "COM99", Name = "COM99" } },
        });

        var tcpPrompt = SessionRemovalPrompt.Build(
            SessionKind.Tcp, TcpSessionRules.Key("tcp", "client", "192.168.1.50:9000", 2345), config);
        Assert.Equal("Remove this TCP session?", tcpPrompt.Question);
        Assert.Equal("192.168.1.50:9000", tcpPrompt.Name);
        Assert.Equal("TCP client", tcpPrompt.Detail);

        var serialPrompt = SessionRemovalPrompt.Build(SessionKind.Serial, "COM99", config);
        Assert.Equal("Remove this serial session?", serialPrompt.Question);
        Assert.Equal("COM99", serialPrompt.Name);
        Assert.Equal("", serialPrompt.Detail);
        Assert.Equal("", serialPrompt.Note);
    }

    [Fact]
    public void UI17_RemovalPromptFallsBackToIdForMissingSession()
    {
        using var env = new TestEnvironment();
        var config = Load(env, new ComtoolConfig());

        var prompt = SessionRemovalPrompt.Build(SessionKind.Telnet, "root@192.168.1.12:23", config);

        Assert.Equal("root@192.168.1.12:23", prompt.Name);
        Assert.Equal("", prompt.Detail);
        Assert.Equal("", prompt.Note);
    }

    [Fact]
    public void UI17_RemovalPromptWarnsAboutCachedPassword()
    {
        using var env = new TestEnvironment();
        var telnet = new TelnetSession
        {
            Host = "192.168.1.12",
            Port = 23,
            User = "root",
        };
        var config = Load(env, new ComtoolConfig { TelnetSessions = { telnet } });
        var key = TelnetSessionRules.Key("192.168.1.12", 23, "root");

        // 无缓存密码时不显示提示行。
        Assert.Equal("", SessionRemovalPrompt.Build(SessionKind.Telnet, key, config).Note);

        config.PasswordCache.Set(key, "secret");
        Assert.Equal(
            "The saved password will also be deleted.",
            SessionRemovalPrompt.Build(SessionKind.Telnet, key, config).Note);
    }
}
