using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using Iciclecreek.Terminal;

namespace COMTool.App.Tests;

public sealed class SessionStoppedTests
{
    [AvaloniaFact]
    public void SSH11_StoppedMessageMatchesMobaxterm()
    {
        var terminal = new TerminalControl { Process = string.Empty };
        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = terminal,
        };
        var pty = new InMemoryPty();
        var tab = TestTab.Create(terminal, kind: SessionKind.Ssh);
        try
        {
            window.Show();
            terminal.ApplyTemplate();
            var behavior = new SessionStoppedBehavior(
                terminal,
                tab,
                () => { },
                () => Task.CompletedTask,
                () => Task.CompletedTask);
            behavior.Attach();

            var exited = false;
            terminal.ProcessExited += (_, _) => exited = true;
            terminal.AttachConnection(pty);
            pty.Close();

            WaitUntil(() => exited, "terminal process exit");
            Dispatcher.UIThread.RunJobs();

            var text = TerminalTranscript.Capture(terminal);
            Assert.Contains("Session stopped", text, StringComparison.Ordinal);
            Assert.Contains("Press <return> to exit tab", text, StringComparison.Ordinal);
            Assert.Contains("Press R to restart session", text, StringComparison.Ordinal);
            Assert.Contains("Press S to save terminal output to file", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Process exited with code", text, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
            pty.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void SSH11_NonSshProcessExitKeepsDefaultMessage()
    {
        var terminal = new TerminalControl { Process = string.Empty };
        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = terminal,
        };
        var pty = new InMemoryPty();
        var tab = TestTab.Create(terminal, kind: SessionKind.Tcp);
        try
        {
            window.Show();
            terminal.ApplyTemplate();
            var behavior = new SessionStoppedBehavior(
                terminal,
                tab,
                () => { },
                () => Task.CompletedTask,
                () => Task.CompletedTask);
            behavior.Attach();

            var exited = false;
            terminal.ProcessExited += (_, _) => exited = true;
            terminal.AttachConnection(pty);
            pty.Close();

            WaitUntil(() => exited, "TCP process exit");
            Dispatcher.UIThread.RunJobs();

            var text = TerminalTranscript.Capture(terminal);
            Assert.Contains("Process exited", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Session stopped", text, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
            pty.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void SSH11_StoppedKeysInvokeMobaxtermActions()
    {
        var terminal = new TerminalControl { Process = string.Empty };
        var tab = TestTab.Create(terminal, kind: SessionKind.Ssh);
        var closed = 0;
        var restarted = 0;
        var saved = 0;
        var behavior = new SessionStoppedBehavior(
            terminal,
            tab,
            () => closed++,
            () =>
            {
                restarted++;
                return Task.CompletedTask;
            },
            () =>
            {
                saved++;
                return Task.CompletedTask;
            });
        behavior.Attach();

        Press(terminal, Key.R);
        Press(terminal, Key.S);
        Press(terminal, Key.Enter);
        Assert.Equal(0, restarted);
        Assert.Equal(0, saved);
        Assert.Equal(0, closed);

        tab.IsSessionStopped = true;
        Press(terminal, Key.R);
        Press(terminal, Key.S);
        Press(terminal, Key.Enter);
        Assert.Equal(1, restarted);
        Assert.Equal(1, saved);
        Assert.Equal(1, closed);

        // Ctrl+R is a terminal shortcut, not the stopped-session action.
        Press(terminal, Key.R, KeyModifiers.Control);
        Assert.Equal(1, restarted);
    }

    [AvaloniaFact]
    public void SSH11_TranscriptSavesFullScrollback()
    {
        var terminal = new TerminalControl { Process = string.Empty };
        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = terminal,
        };
        try
        {
            window.Show();
            terminal.ApplyTemplate();
            // More lines than the 24-row viewport proves scrollback, not just
            // the visible screen, ends up in the saved transcript.
            for (var i = 0; i < 100; i++)
            {
                terminal.Terminal.Write($"scrollback line {i}\r\n");
            }

            terminal.Terminal.Write("\r\n");

            var text = TerminalTranscript.Capture(terminal);
            Assert.Contains("scrollback line 0", text, StringComparison.Ordinal);
            Assert.Contains("scrollback line 99", text, StringComparison.Ordinal);
            Assert.EndsWith("scrollback line 99\n", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\n\n", text, StringComparison.Ordinal);

            using var stream = new MemoryStream();
            TerminalTranscript.SaveAsync(terminal, stream).GetAwaiter().GetResult();
            var saved = Encoding.UTF8.GetString(stream.ToArray());
            Assert.Equal(text, saved);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Press(
        TerminalControl terminal,
        Key key,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        terminal.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            PhysicalKey = key switch
            {
                Key.R => PhysicalKey.R,
                Key.S => PhysicalKey.S,
                Key.Enter => PhysicalKey.Enter,
                _ => PhysicalKey.None,
            },
        });
    }

    private static void WaitUntil(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"timed out waiting for {because}");
            }

            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }
}
