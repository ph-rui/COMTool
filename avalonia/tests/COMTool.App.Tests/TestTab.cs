using Avalonia.Controls;
using COMTool.App.ViewModels;
using Iciclecreek.Terminal;

namespace COMTool.App.Tests;

internal static class TestTab
{
    public static TerminalTab Create(
        Control? content = null,
        string title = "test",
        string sessionId = "test",
        SessionKind kind = SessionKind.Local,
        bool connected = true,
        TerminalControl? terminal = null) =>
        new()
        {
            Title = title,
            SessionId = sessionId,
            Kind = kind,
            Content = content ?? new Border(),
            Terminal = terminal,
            IsTransportOpen = () => connected,
            CloseAction = () => { },
        };
}
