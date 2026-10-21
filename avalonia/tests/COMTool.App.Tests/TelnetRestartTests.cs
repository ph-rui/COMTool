using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;
using Iciclecreek.Terminal;

namespace COMTool.App.Tests;

/// <summary>
/// Covers the MobaXterm-style stopped Telnet session: the row reports the
/// drop, the terminal shows the stopped screen and R reconnects inside the
/// same tab.
/// </summary>
public sealed class TelnetRestartTests
{
    [AvaloniaFact]
    public void TELNET06_DisconnectedTelnetSessionRestartsInSameTab()
    {
        using var env = new TestEnvironment();
        using var server = new FakeTelnetServer();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig
                {
                    SessionSidebar = { Visible = true, Width = 220 },
                    TelnetSessions =
                    {
                        new TelnetSession
                        {
                            Host = "127.0.0.1",
                            Port = server.Port,
                            Alias = "fake-telnet",
                        },
                    },
                });

                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                window.StopSidebarHoverTimerForTests();
                RunJobs(window);

                var viewModel = Assert.IsType<MainViewModel>(window.DataContext);
                var item = viewModel.Sections
                    .SelectMany(section => section.Items)
                    .Single(session => session.Kind == SessionKind.Telnet);

                var open = window.OpenTelnetSessionForTests(item.SessionId);
                WaitUntil(() => open.IsCompleted, "Telnet connect");
                RunJobs(window);
                Assert.True(open.IsCompletedSuccessfully, "Telnet connect failed");

                var tab = Assert.Single(viewModel.Tabs);
                Assert.Equal(SessionKind.Telnet, tab.Kind);
                Assert.Equal(1, server.AcceptedCount);
                Assert.StartsWith("Connected", tab.StatusMessage, StringComparison.Ordinal);
                Assert.True(tab.IsAlive);
                Assert.False(tab.IsSessionStopped);

                // The server hangs up: the tab has to show the stopped screen.
                server.DropAll();
                WaitUntil(() => tab.IsSessionStopped, "stopped session state");
                RunJobs(window);

                Assert.False(tab.IsAlive);
                Assert.Equal("Session stopped", tab.StatusMessage);
                var terminal = Assert.IsType<TerminalControl>(tab.Content);
                Assert.Contains(
                    "Press R to restart session",
                    TerminalTranscript.Capture(terminal),
                    StringComparison.Ordinal);

                // R reconnects inside the same tab.
                Press(terminal, Key.R);
                WaitUntil(() => tab.IsAlive, "Telnet restart");
                RunJobs(window);

                Assert.Equal(2, server.AcceptedCount);
                Assert.False(tab.IsSessionStopped);
                Assert.StartsWith("Connected", tab.StatusMessage, StringComparison.Ordinal);
                Assert.Single(viewModel.Tabs);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TELNET06_FailedRestartKeepsStoppedSessionAndAllowsRetry()
    {
        using var env = new TestEnvironment();
        using var server = new FakeTelnetServer();
        MainWindow? window = null;
        try
        {
            env.Run(() =>
            {
                env.SeedConfig(new ComtoolConfig
                {
                    SessionSidebar = { Visible = true, Width = 220 },
                    TelnetSessions =
                    {
                        new TelnetSession
                        {
                            Host = "127.0.0.1",
                            Port = server.Port,
                            Alias = "fake-telnet",
                        },
                    },
                });

                window = new MainWindow { DataContext = new MainViewModel() };
                window.Show();
                window.StopPortTimerForTests();
                window.StopSidebarHoverTimerForTests();
                RunJobs(window);

                var viewModel = Assert.IsType<MainViewModel>(window.DataContext);
                var item = viewModel.Sections
                    .SelectMany(section => section.Items)
                    .Single(session => session.Kind == SessionKind.Telnet);

                var open = window.OpenTelnetSessionForTests(item.SessionId);
                WaitUntil(() => open.IsCompleted, "Telnet connect");
                RunJobs(window);

                var tab = Assert.Single(viewModel.Tabs);
                server.DropAll();
                server.Stop();
                WaitUntil(() => tab.IsSessionStopped, "stopped session state");
                RunJobs(window);

                var terminal = Assert.IsType<TerminalControl>(tab.Content);
                Press(terminal, Key.R);
                WaitUntil(
                    () => tab.StatusMessage.StartsWith("Restart failed", StringComparison.Ordinal),
                    "failed restart status");
                RunJobs(window);

                Assert.False(tab.IsAlive);
                Assert.True(tab.IsSessionStopped);

                // The stopped screen stays usable, so R can be pressed again.
                Press(terminal, Key.R);
                WaitUntil(
                    () => tab.StatusMessage.StartsWith("Restart failed", StringComparison.Ordinal),
                    "second failed restart status");
                RunJobs(window);
                Assert.True(tab.IsSessionStopped);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Press(TerminalControl terminal, Key key)
    {
        terminal.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            PhysicalKey = key == Key.R ? PhysicalKey.R : PhysicalKey.None,
        });
    }

    private static void RunJobs(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void WaitUntil(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
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

    /// <summary>
    /// A loopback TCP server that behaves like a Telnet peer for the parts
    /// that matter here: it accepts connections and can hang up on demand.
    /// </summary>
    private sealed class FakeTelnetServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly List<TcpClient> _clients = new();
        private int _accepted;
        private bool _stopped;

        public FakeTelnetServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }

        public int AcceptedCount => Volatile.Read(ref _accepted);

        public void DropAll()
        {
            lock (_clients)
            {
                foreach (var client in _clients)
                {
                    try
                    {
                        client.Close();
                    }
                    catch
                    {
                        // The peer is already gone.
                    }
                }

                _clients.Clear();
            }
        }

        public void Stop()
        {
            _stopped = true;
            DropAll();
            try
            {
                _listener.Stop();
            }
            catch
            {
                // The listener is already closed.
            }
        }

        public void Dispose() => Stop();

        private async Task AcceptLoopAsync()
        {
            while (!_stopped)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch
                {
                    return;
                }

                Interlocked.Increment(ref _accepted);
                lock (_clients)
                {
                    _clients.Add(client);
                }
            }
        }
    }
}
