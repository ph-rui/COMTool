using COMTool.App.Services;
using COMTool.App.ViewModels;

namespace COMTool.App.Tests;

public sealed class SettingsTabPolicyTests
{
    private static TerminalTab Tab(string sessionId) =>
        TestTab.Create(sessionId: sessionId, kind: SessionKind.Ssh);

    [Fact]
    public void UI08_SettingsReplacesEditedTabOnly()
    {
        var first = Tab("ssh");
        var second = Tab("ssh");
        Assert.Same(
            first,
            SettingsTabPolicy.TabToReplace(new[] { first, second }, first, "ssh", SessionKind.Ssh));
        Assert.Null(
            SettingsTabPolicy.TabToReplace(new[] { first, second }, second, "other", SessionKind.Ssh));
    }

    [Fact]
    public void UI08_DetachedEditedTabIsNotReplaced()
    {
        var other = Tab("ssh");
        var edited = Tab("ssh");
        Assert.Null(SettingsTabPolicy.TabToReplace(new[] { other }, edited, "ssh", SessionKind.Ssh));
    }

    [Fact]
    public void UI08_SessionIdComparisonIsCaseInsensitive()
    {
        var edited = Tab("SSH");
        Assert.Same(
            edited,
            SettingsTabPolicy.TabToReplace(new[] { edited }, edited, "ssh", SessionKind.Ssh));
    }

    [Fact]
    public void UI08_WrongKindIsNotReplaced()
    {
        var edited = Tab("ssh");
        Assert.Null(
            SettingsTabPolicy.TabToReplace(new[] { edited }, edited, "ssh", SessionKind.Serial));
    }

    [Fact]
    public void UI08_NullEditedTabIsNotReplaced()
    {
        Assert.Null(
            SettingsTabPolicy.TabToReplace(Array.Empty<TerminalTab>(), null, "ssh", SessionKind.Ssh));
    }
}
