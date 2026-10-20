using COMTool.App.ViewModels;

namespace COMTool.App.Services;

/// <summary>
/// Decides whether a terminal that asked for focus still belongs to the
/// current selection.
/// </summary>
public static class TerminalFocusPolicy
{
    public static bool ShouldFocus(MainViewModel viewModel, TerminalTab tab) =>
        tab.IsAlive && ReferenceEquals(viewModel.SelectedTab, tab);
}
