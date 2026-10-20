using Avalonia.Controls;
using COMTool.App.ViewModels;

namespace COMTool.App.Tests;

internal static class TestTab
{
    public static TerminalTab Create(
        Control? content = null,
        string title = "test",
        string sessionId = "test",
        SessionKind kind = SessionKind.Local,
        bool connected = true) =>
        new()
        {
            Title = title,
            SessionId = sessionId,
            Kind = kind,
            Content = content ?? new Border(),
            IsTransportOpen = () => connected,
            CloseAction = () => { },
        };
}
