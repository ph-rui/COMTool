using System.Text.Json;
using COMTool.Core.Config;

namespace COMTool.Core.Tests;

public sealed class ServerSettingsTests
{
    [Fact]
    public void ServerSettingsRoundTrip()
    {
        var config = new ComtoolConfig();
        config.Servers.Http.Port = 8080;
        config.Servers.Http.RootDirectory = @"C:\share";
        config.Servers.Http.AllowDirectoryListing = false;
        config.Servers.Tftp.Port = 1069;
        config.Servers.Tftp.ShowUploadInfo = false;

        var json = JsonSerializer.Serialize(config);
        var back = JsonSerializer.Deserialize<ComtoolConfig>(json)!;

        Assert.Equal(8080, back.Servers.Http.Port);
        Assert.Equal(@"C:\share", back.Servers.Http.RootDirectory);
        Assert.False(back.Servers.Http.AllowDirectoryListing);
        Assert.True(back.Servers.Http.EnableRuntimeLimit);
        Assert.Equal(3600, back.Servers.Http.RuntimeSeconds);
        Assert.Equal(1069, back.Servers.Tftp.Port);
        Assert.False(back.Servers.Tftp.ShowUploadInfo);
        Assert.True(back.Servers.Tftp.EnableRuntimeLimit);
        Assert.Equal(3600, back.Servers.Tftp.RuntimeSeconds);
    }

    [Fact]
    public void MissingServersSectionUsesDefaults()
    {
        var back = JsonSerializer.Deserialize<ComtoolConfig>("""
            { "version": 3, "skin": "light" }
            """)!;

        Assert.Equal(80, back.Servers.Http.Port);
        Assert.True(back.Servers.Http.AllowDirectoryListing);
        Assert.Equal(69, back.Servers.Tftp.Port);
        Assert.True(back.Servers.Tftp.ShowDownloadInfo);
        Assert.True(back.Servers.Tftp.ShowUploadInfo);
    }
}
