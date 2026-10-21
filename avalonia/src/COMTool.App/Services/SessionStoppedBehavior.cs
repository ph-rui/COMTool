using Avalonia.Input;
using Avalonia.Interactivity;
using COMTool.App.ViewModels;
using Iciclecreek.Terminal;

namespace COMTool.App.Services;

/// <summary>
/// Presents the MobaXterm-style stopped-session screen for SSH and Telnet
/// tabs and maps its three actions to the host window. The terminal library
/// keeps writing its own "Process exited" line unless a WritingMessage
/// handler replaces it, so the text has to be rewritten rather than appended
/// afterwards.
/// </summary>
internal sealed class SessionStoppedBehavior
{
    internal const string PromptText =
        "\r\nSession stopped\r\n"
        + "Press <return> to exit tab\r\n"
        + "Press R to restart session\r\n"
        + "Press S to save terminal output to file\r\n";

    /// <summary>
    /// Session kinds that get the stopped screen. TCP and serial keep the
    /// default "Process exited" text of the terminal library.
    /// </summary>
    internal static bool SupportsStoppedScreen(SessionKind kind) =>
        kind is SessionKind.Ssh or SessionKind.Telnet;

    private readonly TerminalControl _terminal;
    private readonly TerminalTab _tab;
    private readonly Action _closeTab;
    private readonly Func<Task> _restartSession;
    private readonly Func<Task> _saveOutput;
    private bool _attached;

    public SessionStoppedBehavior(
        TerminalControl terminal,
        TerminalTab tab,
        Action closeTab,
        Func<Task> restartSession,
        Func<Task> saveOutput)
    {
        _terminal = terminal;
        _tab = tab;
        _closeTab = closeTab;
        _restartSession = restartSession;
        _saveOutput = saveOutput;
    }

    public void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        _terminal.WritingMessage += OnWritingMessage;
        // Use the tunnel route and handledEventsToo because the terminal view
        // marks most keys handled even after its PTY has exited. Without this
        // the stopped-screen shortcuts would never reach the host.
        _terminal.AddHandler(
            InputElement.KeyDownEvent,
            OnKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void OnWritingMessage(object? sender, TerminalMessageEventArgs e)
    {
        if (!SupportsStoppedScreen(_tab.Kind)
            || e.Kind != TerminalMessageKind.ProcessExited)
        {
            return;
        }

        e.Text = PromptText;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_tab.IsSessionStopped)
        {
            return;
        }

        // Shift is allowed for uppercase R/S; all other modifiers belong to
        // the terminal or the application shortcut layer.
        if ((e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                try
                {
                    _closeTab();
                }
                catch (Exception ex)
                {
                    AppLog.Error("Failed to close stopped session tab", ex);
                }

                break;

            case Key.R:
                e.Handled = true;
                await RunAsync(_restartSession, "restart");
                break;

            case Key.S:
                e.Handled = true;
                await RunAsync(_saveOutput, "save");
                break;
        }
    }

    private static async Task RunAsync(Func<Task> action, string actionName)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AppLog.Error($"Stopped-session {actionName} action failed", ex);
        }
    }
}
