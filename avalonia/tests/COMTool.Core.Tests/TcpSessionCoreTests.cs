using COMTool.Core.Config;

namespace COMTool.Core.Tests;

public sealed class TcpSessionCoreTests
{
    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1", 80)]
    [InlineData("127.0.0.1:2345", "127.0.0.1", 2345)]
    [InlineData(" localhost：8080 ", "localhost", 8080)]
    [InlineData("http://example.com/", "example.com", 80)]
    [InlineData("https://example.com:443/", "example.com", 443)]
    [InlineData("example.com:", "example.com", 80)]
    public void NET03_TcpTargetParserMatchesPython(
        string input,
        string expectedHost,
        int expectedPort)
    {
        Assert.True(TcpTarget.TryParse(input, out var target));
        Assert.NotNull(target);
        Assert.Equal(expectedHost, target!.Host);
        Assert.Equal(expectedPort, target.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("host:0")]
    [InlineData("host:65536")]
    [InlineData("host:abc")]
    [InlineData("ftp://host")]
    [InlineData("host/path")]
    public void NET03_TcpTargetRejectsInvalidInput(string input)
    {
        Assert.False(TcpTarget.TryParse(input, out _));
    }

    [Fact]
    public void NET03_TcpStoreUpsertsClientAndServerSessions()
    {
        var store = new TcpSessionStore();

        var client = store.Upsert(TcpSessionRules.ClientMode, "Example.COM:2345", 80);
        var server = store.Upsert(TcpSessionRules.ServerMode, "", 2345);

        Assert.NotNull(client);
        Assert.Equal("tcp:client:example.com:2345", client!.Key);
        Assert.Equal("Example.COM:2345", client.Target);
        Assert.Equal(2345, client.Port);
        Assert.NotNull(server);
        Assert.Equal("tcp:server:2345", server!.Key);
        Assert.Equal("TCP server :2345", TcpSessionRules.DisplayName(server));
    }

    [Fact]
    public void NET03_TcpStoreDeduplicatesAndRemovesByKey()
    {
        var config = new ComtoolConfig
        {
            TcpSessions =
            {
                new TcpSession { Mode = "client", Target = "example.com:80" },
                new TcpSession { Mode = "client", Target = "EXAMPLE.com:80" },
                new TcpSession { Mode = "server", Port = 2345 },
            },
        };
        var store = new TcpSessionStore();
        store.Load(config);

        Assert.Equal(2, store.Sessions.Count);
        Assert.Equal("tcp:client:example.com:80", store.Sessions[0].Key);

        Assert.Equal("tcp:client:example.com:80", store.Remove("tcp:client:example.com:80"));
        Assert.Single(store.Sessions);
        Assert.Null(store.Remove("missing"));
    }

    [Fact]
    public void NET03_TcpStoreRejectsInvalidModeData()
    {
        var store = new TcpSessionStore();

        Assert.Null(store.Upsert(TcpSessionRules.ClientMode, "", 80));
        Assert.Null(store.Upsert(TcpSessionRules.ClientMode, "host:0", 80));
        Assert.Null(store.Upsert(TcpSessionRules.ServerMode, "", 0));
        Assert.Empty(store.Sessions);
    }

    [Fact]
    public void NET03_TcpStoreSaveWritesIndependentSnapshot()
    {
        var store = new TcpSessionStore();
        store.Upsert(TcpSessionRules.ClientMode, "example.com:80", 80);
        var config = new ComtoolConfig();
        store.Save(config);

        Assert.Single(config.TcpSessions);
        store.Sessions[0].Target = "changed:80";
        Assert.Equal("example.com:80", config.TcpSessions[0].Target);
    }

    [Fact]
    public void NET02_UdpStoreKeepsProtocolTargetAndLocalPort()
    {
        var store = new TcpSessionStore();

        var session = store.Upsert(
            TcpSessionRules.UdpProtocol,
            TcpSessionRules.ClientMode,
            "Example.COM:9000",
            2345);

        Assert.NotNull(session);
        Assert.Equal("udp", session!.Protocol);
        Assert.Equal("udp:client:2345:example.com:9000", session.Key);
        Assert.Equal("Example.COM:9000", session.Target);
        Assert.Equal(2345, session.Port);
        Assert.Equal("UDP Example.COM:9000", TcpSessionRules.DisplayName(session));
        Assert.Equal("Local :2345 -> Example.COM:9000", TcpSessionRules.Detail(session));
    }

    [Fact]
    public void NET02_UdpStoreRejectsInvalidLocalPortOrTarget()
    {
        var store = new TcpSessionStore();

        Assert.Null(store.Upsert(
            TcpSessionRules.UdpProtocol,
            TcpSessionRules.ClientMode,
            "example.com:9000",
            0));
        Assert.Null(store.Upsert(
            TcpSessionRules.UdpProtocol,
            TcpSessionRules.ServerMode,
            "not a target",
            2345));
        Assert.Empty(store.Sessions);
    }

    [Fact]
    public void NET04_TcpStorePersistsAutoReconnectOnlyForTcpClient()
    {
        var store = new TcpSessionStore();

        var client = store.Upsert(
            TcpSessionRules.TcpProtocol,
            TcpSessionRules.ClientMode,
            "127.0.0.1:2345",
            80,
            autoReconnect: true,
            reconnectInterval: 0.5);
        var server = store.Upsert(
            TcpSessionRules.TcpProtocol,
            TcpSessionRules.ServerMode,
            "",
            2345,
            autoReconnect: true,
            reconnectInterval: 0.5);
        var udp = store.Upsert(
            TcpSessionRules.UdpProtocol,
            TcpSessionRules.ClientMode,
            "127.0.0.1:2345",
            2346,
            autoReconnect: true,
            reconnectInterval: 0.5);

        Assert.True(client!.AutoReconnect);
        Assert.Equal(0.5, client.ReconnectInterval);
        Assert.False(server!.AutoReconnect);
        Assert.False(udp!.AutoReconnect);
    }

    [Fact]
    public void NET02_LegacyTcpSessionDefaultsToTcpProtocol()
    {
        var store = new TcpSessionStore();
        store.Load(new ComtoolConfig
        {
            TcpSessions =
            {
                new TcpSession { Mode = "client", Target = "127.0.0.1:2345", Protocol = "" },
            },
        });

        var session = Assert.Single(store.Sessions);
        Assert.Equal("tcp", session.Protocol);
        Assert.Equal("tcp:client:127.0.0.1:2345", session.Key);
    }
}
