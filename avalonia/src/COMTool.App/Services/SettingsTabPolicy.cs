using COMTool.App.ViewModels;

namespace COMTool.App.Services;

/// <summary>
/// Decides which tab, if any, a settings save may replace.
/// </summary>
public static class SettingsTabPolicy
{
    /// <summary>
    /// Returns the edited tab only when it is still in the view model's tab
    /// list, matches the edited kind, and belongs to the saved session. A
    /// detached tab (or a tab for another session/kind) is never replaced.
    /// </summary>
    public static TerminalTab? TabToReplace(
        IReadOnlyList<TerminalTab> tabs,
        TerminalTab? editedTab,
        string sessionId,
        SessionKind kind)
    {
        if (editedTab is null
            || !tabs.Contains(editedTab)
            || editedTab.Kind != kind
            || !string.Equals(editedTab.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return editedTab;
    }
}
