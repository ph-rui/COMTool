using COMTool.Connections;

namespace COMTool.Connections.Tests;

public sealed class PtyStreamConnectionTests
{
    [Fact]
    public void PTY01_ResizeAfterCloseDoesNotReachDisposedTransport()
    {
        var stream = new MemoryStream();
        var resizeCalls = 0;
        var closeCalls = 0;
        using var connection = new PtyStreamConnection(
            stream,
            stream,
            resize: (_, _) => resizeCalls++,
            close: () => closeCalls++,
            ownsStreams: false,
            interceptZmodem: false);

        connection.Resize(80, 24);
        Assert.Equal(1, resizeCalls);

        connection.Close();
        Assert.Equal(1, closeCalls);

        // A terminal layout pass can still request a resize after the tab has
        // disposed its transport. That must be a no-op, not an exception that
        // escapes through Avalonia's ArrangeOverride and kills the app.
        connection.Resize(100, 40);
        Assert.Equal(1, resizeCalls);
    }
}
