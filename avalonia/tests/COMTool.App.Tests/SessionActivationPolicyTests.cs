using COMTool.App.Services;
using COMTool.App.ViewModels;

namespace COMTool.App.Tests;

public sealed class SessionActivationPolicyTests
{
    private static TerminalTab Tab(string sessionId, bool connected = true) =>
        TestTab.Create(sessionId: sessionId, kind: SessionKind.Ssh, connected: connected);

    [Fact]
    public void SSH06_LiveConnectionOpensAnotherTab()
    {
        var live = Tab("ssh", connected: true);
        Assert.True(SessionActivationPolicy.ShouldOpenNewSsh(new[] { live }, "ssh", _ => true));
        Assert.Empty(SessionActivationPolicy.DeadSshTabsToClose(new[] { live }, "ssh", _ => true));
    }

    [Fact]
    public void SSH06_NoLiveConnectionClosesDeadTabs()
    {
        var dead = Tab("ssh", connected: false);
        Assert.True(SessionActivationPolicy.ShouldOpenNewSsh(new[] { dead }, "ssh", _ => false));
        Assert.Equal(new[] { dead }, SessionActivationPolicy.DeadSshTabsToClose(new[] { dead }, "ssh", _ => false));
    }

    [Fact]
    public void SSH06_ConnectingTabIsNotTreatedAsLive()
    {
        var connecting = Tab("ssh", connected: false);
        Assert.True(SessionActivationPolicy.ShouldOpenNewSsh(new[] { connecting }, "ssh", _ => false));
    }

    [Fact]
    public void SSH06_ActivationPolicy()
    {
        var live = Tab("ssh", connected: true);
        var dead = Tab("ssh", connected: false);
        Assert.True(SessionActivationPolicy.ShouldOpenNewSsh(new[] { live }, "ssh", _ => true));
        Assert.Empty(SessionActivationPolicy.DeadSshTabsToClose(new[] { live }, "ssh", _ => true));
        Assert.Equal(new[] { dead }, SessionActivationPolicy.DeadSshTabsToClose(new[] { dead }, "ssh", _ => false));
    }

    [Fact]
    public void SSH06_LiveConnectionKeepsDeadTabsOpen()
    {
        var live = Tab("ssh", connected: true);
        var dead = Tab("ssh", connected: false);
        Assert.Equal(
            Array.Empty<TerminalTab>(),
            SessionActivationPolicy.DeadSshTabsToClose(new[] { live, dead }, "ssh", tab => ReferenceEquals(tab, live)));
    }

    [Fact]
    public void SSH06_OnlyMatchingSshTabsAreConsidered()
    {
        var otherSession = Tab("other", connected: false);
        var serial = TestTab.Create(sessionId: "ssh", kind: SessionKind.Serial, connected: false);
        Assert.Empty(SessionActivationPolicy.DeadSshTabsToClose(new[] { otherSession, serial }, "ssh", _ => false));
    }

    [Fact]
    public void NET01_TcpClientDeadTabsAreCleanedUp()
    {
        var dead = TestTab.Create(
            sessionId: "tcp:client:127.0.0.1:2345",
            kind: SessionKind.Tcp,
            connected: false);
        var serial = TestTab.Create(
            sessionId: "tcp:client:127.0.0.1:2345",
            kind: SessionKind.Serial,
            connected: false);

        var deadTabs = SessionActivationPolicy.DeadSessionTabsToClose(
            new[] { dead, serial },
            "tcp:client:127.0.0.1:2345",
            SessionKind.Tcp,
            _ => false);

        Assert.Equal(new[] { dead }, deadTabs);
    }
}
