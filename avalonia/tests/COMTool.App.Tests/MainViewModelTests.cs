using COMTool.App.ViewModels;

namespace COMTool.App.Tests;

public sealed class MainViewModelTests
{
    private static TerminalTab Tab(string sessionId) =>
        TestTab.Create(sessionId: sessionId, kind: SessionKind.Local);

    [Fact]
    public void UI05_CloseSelectedTabSelectsNeighbor()
    {
        var vm = new MainViewModel();
        var a = Tab("a");
        var b = Tab("b");
        var c = Tab("c");
        vm.AddTab(a);
        vm.AddTab(b);
        vm.AddTab(c);
        vm.SelectedTab = b;

        vm.CloseTab(b);

        Assert.Same(c, vm.SelectedTab);
    }

    [Fact]
    public void UI05_ClosingLastTabClearsSelection()
    {
        var vm = new MainViewModel();
        var a = Tab("a");
        vm.AddTab(a);

        vm.CloseTab(a);

        Assert.Null(vm.SelectedTab);
    }
}
