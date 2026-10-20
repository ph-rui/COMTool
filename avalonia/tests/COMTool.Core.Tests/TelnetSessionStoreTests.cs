using COMTool.Core.Config;

namespace COMTool.Core.Tests;

public sealed class TelnetSessionStoreTests
{
    [Fact]
    public void TELNET_Store_NormalizesKeyAndDeduplicates()
    {
        var config = new ComtoolConfig
        {
            TelnetSessions =
            {
                new TelnetSession
                {
                    Host = " Example.COM ",
                    Port = 0,
                    User = " alice ",
                    Alias = "old",
                    EncryptedPassword = "old-token",
                },
                new TelnetSession
                {
                    Host = "example.com",
                    Port = 23,
                    User = "alice",
                    Alias = "new",
                },
                new TelnetSession
                {
                    Host = "router.lan",
                    Port = 2323,
                    User = "",
                },
            },
        };

        var store = new TelnetSessionStore();
        store.Load(config);

        Assert.Equal(2, store.Sessions.Count);
        Assert.Equal("telnet:alice@example.com:23", store.Sessions[0].Key);
        Assert.Equal("new", store.Sessions[0].Alias);
        Assert.Equal("telnet:@router.lan:2323", store.Sessions[1].Key);
        Assert.Equal("router.lan:2323", TelnetSessionRules.Detail(store.Sessions[1]));
    }

    [Fact]
    public void TELNET_Upsert_PreservesRememberedPassword()
    {
        var store = new TelnetSessionStore();
        var created = store.Upsert(
            "example.com",
            23,
            "alice",
            "lab",
            "dpapi-token");
        Assert.NotNull(created);
        Assert.Equal("telnet:alice@example.com:23", created!.Key);

        var updated = store.Upsert("example.com", 23, "alice", "renamed");
        Assert.NotNull(updated);
        Assert.Equal("renamed", updated!.Alias);
        Assert.Equal("dpapi-token", updated.EncryptedPassword);

        var cleared = store.Upsert(
            "example.com",
            23,
            "alice",
            "renamed",
            "");
        Assert.Equal("", cleared!.EncryptedPassword);
    }

    [Fact]
    public void TELNET_SharePayload_RemovesPasswordAndUnknownFields()
    {
        var session = new TelnetSession
        {
            Transport = "telnet",
            Key = "telnet:alice@example.com:23",
            Host = "example.com",
            Port = 23,
            User = "alice",
            Alias = "lab",
            EncryptedPassword = "dpapi-token",
            Extra = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["futureSecret"] = System.Text.Json.JsonSerializer.SerializeToElement("secret"),
            },
        };

        var shared = TelnetSessionRules.ForShare(session);

        Assert.Equal(session.Key, shared.Key);
        Assert.Equal(session.Host, shared.Host);
        Assert.Equal("", shared.EncryptedPassword);
        Assert.Null(shared.Extra);
    }
}
