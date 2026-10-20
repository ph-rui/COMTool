using COMTool.App.Services;
using COMTool.App.ViewModels;

namespace COMTool.App.Tests;

public sealed class TerminalFocusPolicyTests
{
    [Fact]
    public void UI05_FocusPolicyRequiresSelectedLiveTerminal()
    {
        var viewModel = new MainViewModel();
        var selected = TestTab.Create(sessionId: "selected");
        var other = TestTab.Create(sessionId: "other");
        viewModel.AddTab(selected);
        viewModel.AddTab(other);
        viewModel.SelectedTab = selected;

        Assert.True(TerminalFocusPolicy.ShouldFocus(viewModel, selected));
    }

    [Fact]
    public void UI05_FocusPolicyRejectsDeadSelectedTerminal()
    {
        var viewModel = new MainViewModel();
        var selected = TestTab.Create(sessionId: "selected");
        viewModel.AddTab(selected);
        selected.IsAlive = false;

        Assert.False(TerminalFocusPolicy.ShouldFocus(viewModel, selected));
    }

    [Fact]
    public void UI05_FocusPolicyRejectsLiveUnselectedTerminal()
    {
        var viewModel = new MainViewModel();
        var selected = TestTab.Create(sessionId: "selected");
        var unselected = TestTab.Create(sessionId: "unselected");
        viewModel.AddTab(selected);
        viewModel.AddTab(unselected);
        viewModel.SelectedTab = selected;

        Assert.False(TerminalFocusPolicy.ShouldFocus(viewModel, unselected));
    }
}
