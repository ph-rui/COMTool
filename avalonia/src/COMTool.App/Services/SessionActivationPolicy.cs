using COMTool.App.ViewModels;

namespace COMTool.App.Services;

/// <summary>
/// Pure decisions for what should happen when a saved session is activated.
/// </summary>
public static class SessionActivationPolicy
{
    /// <summary>
    /// An SSH session is a saved endpoint rather than a single connection, so
    /// every activation opens another independent connection. A live matching
    /// tab never blocks a new one.
    /// </summary>
    public static bool ShouldOpenNewSsh(
        IReadOnlyList<TerminalTab> tabs,
        string sessionId,
        Func<TerminalTab, bool> isConnected)
    {
        _ = tabs;
        _ = sessionId;
        _ = isConnected;
        return true;
    }

    /// <summary>
    /// Returns the dead tabs to close before reconnecting. If any matching SSH
    /// tab is live, the click opens a parallel connection and no dead tab is
    /// discarded.
    /// </summary>
    public static IReadOnlyList<TerminalTab> DeadSshTabsToClose(
        IReadOnlyList<TerminalTab> tabs,
        string sessionId,
        Func<TerminalTab, bool> isConnected)
    {
        var matching = tabs
            .Where(tab => tab.Kind == SessionKind.Ssh
                && string.Equals(tab.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matching.Any(isConnected))
        {
            return Array.Empty<TerminalTab>();
        }

        return matching.Where(tab => !isConnected(tab)).ToList();
    }
}
