using COMTool.Core.Config;

namespace COMTool.Core.Tests;

public sealed class SessionStoreTests
{
    [Fact]
    public void SerialStore_DeduplicatesByNormalizedPort_LastWins()
    {
        var config = new ComtoolConfig
        {
            SerialSessions =
            {
                new SerialSession
                {
                    Port = "COM3",
                    Name = "old",
                    Serial = new SerialConnectionConfig { Baudrate = 9600 },
                },
                new SerialSession
                {
                    Port = " com3 ",
                    Name = "new",
                    Serial = new SerialConnectionConfig { Baudrate = 115200 },
                },
                new SerialSession
                {
                    Port = "COM10",
                    Name = "ten",
                },
            },
        };

        var store = new SerialSessionStore();
        store.Load(config);

        Assert.Equal(2, store.Sessions.Count);
        Assert.Equal("new", store.Sessions[0].Name);
        Assert.Equal(115200, store.Sessions[0].Serial.Baudrate);
        Assert.Equal("COM10", store.Sessions[1].Port);
    }

    [Fact]
    public void SerialStore_Prune_OnlyRemovesAfterSuccessfulScan()
    {
        var store = new SerialSessionStore();
        store.Load(new ComtoolConfig
        {
            SerialSessions =
            {
                new SerialSession { Port = "COM1" },
                new SerialSession { Port = "COM2" },
            },
        });

        var removed = store.Prune(new[] { "com1" });

        Assert.Equal(new[] { "COM2" }, removed);
        Assert.Single(store.Sessions);
        Assert.Equal("COM1", store.Sessions[0].Port);
    }

    [Fact]
    public void SerialStore_Upsert_NormalizesPortAndKeepsPosition()
    {
        var store = new SerialSessionStore();
        store.Load(new ComtoolConfig
        {
            SerialSessions =
            {
                new SerialSession { Port = "COM1" },
                new SerialSession { Port = "COM2" },
            },
        });

        store.Upsert(" com2 ", new SerialConnectionConfig { Port = " com2 ", Baudrate = 460800 });

        Assert.Equal("COM1", store.Sessions[0].Port);
        Assert.Equal("com2", store.Sessions[1].Port);
        Assert.Equal(460800, store.Sessions[1].Serial.Baudrate);
        Assert.Equal("com2", store.Sessions[1].Serial.Port);
    }

    [Fact]
    public void SshStore_BuildsStableKeyAndKeepsPasswordWhenMetadataChanges()
    {
        var store = new SshSessionStore();
        var created = store.Upsert(
            "Example.COM",
            22,
            " Root ",
            alias: "lab",
            encryptedPassword: "token");

        Assert.NotNull(created);
        Assert.Equal("Root@example.com:22", created!.Key);
        Assert.Equal("token", created.EncryptedPassword);

        var updated = store.Upsert(
            "example.com",
            22,
            "Root",
            alias: "renamed");

        Assert.NotNull(updated);
        Assert.Equal("token", updated!.EncryptedPassword);
        Assert.Equal("renamed", updated.Alias);
        Assert.Single(store.Sessions);
    }

    [Fact]
    public void SshStore_EmptyPasswordArgument_ForgetsPassword()
    {
        var store = new SshSessionStore();
        store.Upsert("host", 22, "user", encryptedPassword: "token");
        var updated = store.Upsert("host", 22, "user", encryptedPassword: "");

        Assert.Equal("", updated!.EncryptedPassword);
    }

    [Fact]
    public void SshStore_GetReturnsIndependentCopy()
    {
        var store = new SshSessionStore();
        store.Upsert("host", 22, "user", encryptedPassword: "token");

        var session = store.Get("user@host:22");
        Assert.NotNull(session);
        session!.EncryptedPassword = "";

        Assert.Equal("token", store.Sessions[0].EncryptedPassword);
    }

    [Fact]
    public void SshStore_InvalidPort_FallsBackTo22()
    {
        var store = new SshSessionStore();
        var session = store.Upsert("host", 70000, "user");

        Assert.NotNull(session);
        Assert.Equal(22, session!.Port);
        Assert.Equal("user@host:22", session.Key);
    }
}
