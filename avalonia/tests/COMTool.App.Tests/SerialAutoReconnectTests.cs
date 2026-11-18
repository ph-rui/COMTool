using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Connections;
using COMTool.Core.Config;

namespace COMTool.App.Tests;

public sealed class SerialAutoReconnectTests
{
    [AvaloniaFact]
    public void SER08_ReplugReopensOpenSerialTab()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.SeedConfig(new ComtoolConfig
            {
                SerialSessions =
                {
                    new SerialSession
                    {
                        Port = "COM999",
                        Name = "USB-SERIAL CH340",
                        Serial = new SerialConnectionConfig { Port = "COM999" },
                    },
                },
            });

            env.Run(() =>
            {
                var viewModel = new MainViewModel();
                var factory = new FakeSerialConnectionFactory();
                window = new MainWindow
                {
                    DataContext = viewModel,
                    SerialConnectionFactory = factory.Create,
                };
                window.Show();
                window.StopPortTimerForTests();
                RunJobs(window);

                var present = new SerialPortScanResult(
                    true,
                    new[] { new SerialPortInfo("COM999", "USB-SERIAL CH340") });
                window.ApplyPortScanForTests(present);
                RunJobs(window);
                window.OpenSerialSessionForTests("COM999");
                RunJobs(window);

                var tab = Assert.Single(viewModel.Tabs);
                Assert.True(tab.IsAlive);
                Assert.Single(factory.Created);
                Assert.True(factory.Created[0].IsOpen);

                window.ApplyPortScanForTests(new SerialPortScanResult(
                    true,
                    Array.Empty<SerialPortInfo>()));
                RunJobs(window);

                Assert.Single(viewModel.Tabs);
                Assert.Same(tab, viewModel.Tabs[0]);
                Assert.False(tab.IsAlive);
                Assert.False(factory.Created[0].IsOpen);

                window.ApplyPortScanForTests(present);
                RunJobs(window);

                Assert.Single(viewModel.Tabs);
                Assert.Same(tab, viewModel.Tabs[0]);
                Assert.True(tab.IsAlive);
                Assert.Equal(2, factory.Created.Count);
                Assert.True(factory.Created[1].IsOpen);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// r67.6: a real unplug ends the serial read stream (EOF) without the pty
    /// raising ProcessExited, so the reconnect state machine was never armed and
    /// the tab stayed "Connection lose" forever. The liveness check must arm the
    /// retry path, and the next scan must reopen the same COM port.
    /// </summary>
    [AvaloniaFact]
    public void SER08_StreamEndWithoutProcessExitedStillReconnects()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.SeedConfig(new ComtoolConfig
            {
                SerialSessions =
                {
                    new SerialSession
                    {
                        Port = "COM999",
                        Name = "USB-SERIAL CH340",
                        Serial = new SerialConnectionConfig { Port = "COM999" },
                    },
                },
            });

            env.Run(() =>
            {
                var viewModel = new MainViewModel();
                var factory = new FakeSerialConnectionFactory();
                window = new MainWindow
                {
                    DataContext = viewModel,
                    SerialConnectionFactory = factory.Create,
                };
                window.Show();
                window.StopPortTimerForTests();
                RunJobs(window);

                var present = new SerialPortScanResult(
                    true,
                    new[] { new SerialPortInfo("COM999", "USB-SERIAL CH340") });
                window.ApplyPortScanForTests(present);
                RunJobs(window);
                window.OpenSerialSessionForTests("COM999");
                RunJobs(window);

                var tab = Assert.Single(viewModel.Tabs);
                Assert.True(tab.IsAlive);
                var first = factory.Created[0];

                // Device vanishes at stream level only: EOF, no ProcessExited.
                first.SimulateInputStreamEnd();
                Assert.True(
                    SpinWait.SpinUntil(() => first.Pty.IsInputCompleted, 3000),
                    "the serial read pump should reach EOF");

                window.UpdateConnectionStatesForTests();
                RunJobs(window);

                Assert.False(tab.IsAlive);
                Assert.Equal("Connection lose", tab.StatusMessage);

                // The port is still enumerated: the next scan must reopen it.
                window.ApplyPortScanForTests(present);
                RunJobs(window);

                Assert.Single(viewModel.Tabs);
                Assert.Same(tab, viewModel.Tabs[0]);
                Assert.True(tab.IsAlive);
                Assert.Equal(2, factory.Created.Count);
                Assert.Contains("Connected COM999", tab.StatusMessage);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void SER08_ReconnectFailureIsVisibleAndRetried()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.SeedConfig(new ComtoolConfig
            {
                SerialSessions =
                {
                    new SerialSession
                    {
                        Port = "COM999",
                        Name = "USB-SERIAL CH340",
                        Serial = new SerialConnectionConfig { Port = "COM999" },
                    },
                },
            });

            env.Run(() =>
            {
                var viewModel = new MainViewModel();
                var factory = new FakeSerialConnectionFactory();
                window = new MainWindow
                {
                    DataContext = viewModel,
                    SerialConnectionFactory = factory.Create,
                };
                window.Show();
                window.StopPortTimerForTests();
                RunJobs(window);

                var present = new SerialPortScanResult(
                    true,
                    new[] { new SerialPortInfo("COM999", "USB-SERIAL CH340") });
                window.ApplyPortScanForTests(present);
                RunJobs(window);
                window.OpenSerialSessionForTests("COM999");
                RunJobs(window);
                var tab = Assert.Single(viewModel.Tabs);

                // The device is enumerated but cannot be opened (ghost port).
                window.SerialConnectionFactory =
                    _ => throw new IOException("Access to the path 'COM999' is denied.");
                factory.Created[0].SimulateInputStreamEnd();
                Assert.True(
                    SpinWait.SpinUntil(() => factory.Created[0].Pty.IsInputCompleted, 3000));
                window.UpdateConnectionStatesForTests();
                RunJobs(window);

                window.ApplyPortScanForTests(present);
                RunJobs(window);

                Assert.False(tab.IsAlive);
                Assert.Equal("Reconnecting COM999 (attempt 1)...", tab.StatusMessage);

                // The next scan inside the 2s backoff window must not hammer
                // the port; the retry stays armed and visible.
                window.ApplyPortScanForTests(present);
                RunJobs(window);
                Assert.Equal(1, factory.Created.Count);
                Assert.False(tab.IsAlive);
                Assert.Equal("Reconnecting COM999 (attempt 1)...", tab.StatusMessage);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void SER08_ManualReconnectIgnoresBackoff()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            env.SeedConfig(new ComtoolConfig
            {
                SerialSessions =
                {
                    new SerialSession
                    {
                        Port = "COM999",
                        Name = "USB-SERIAL CH340",
                        Serial = new SerialConnectionConfig { Port = "COM999" },
                    },
                },
            });

            env.Run(() =>
            {
                var viewModel = new MainViewModel();
                var factory = new FakeSerialConnectionFactory();
                window = new MainWindow
                {
                    DataContext = viewModel,
                    SerialConnectionFactory = factory.Create,
                };
                window.Show();
                window.StopPortTimerForTests();
                RunJobs(window);

                var present = new SerialPortScanResult(
                    true,
                    new[] { new SerialPortInfo("COM999", "USB-SERIAL CH340") });
                window.ApplyPortScanForTests(present);
                RunJobs(window);
                window.OpenSerialSessionForTests("COM999");
                RunJobs(window);
                var tab = Assert.Single(viewModel.Tabs);

                factory.Created[0].SimulateInputStreamEnd();
                Assert.True(
                    SpinWait.SpinUntil(() => factory.Created[0].Pty.IsInputCompleted, 3000));
                window.UpdateConnectionStatesForTests();
                RunJobs(window);
                Assert.False(tab.IsAlive);

                window.ReconnectSerialNowForTests(tab);
                RunJobs(window);

                Assert.True(tab.IsAlive);
                Assert.Equal(2, factory.Created.Count);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void RunJobs(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class FakeSerialConnectionFactory
    {
        public List<FakeSerialConnection> Created { get; } = new();

        public ISerialConnection Create(SerialConnectionConfig config)
        {
            var connection = new FakeSerialConnection(config.Port);
            Created.Add(connection);
            return connection;
        }
    }

    private sealed class FakeSerialConnection : ISerialConnection
    {
        private readonly InMemoryPty _ptySource = new();
        private bool _open = true;

        public FakeSerialConnection(string portName)
        {
            PortName = portName;
            Pty = new PtyStreamConnection(
                _ptySource.ReaderStream,
                _ptySource.WriterStream,
                close: () => _ptySource.Close(),
                ownsStreams: false);
        }

        /// <summary>
        /// Simulates the device vanishing at the stream level (EOF) without the
        /// pty raising ProcessExited — the real-world unplug path.
        /// </summary>
        public void SimulateInputStreamEnd() => _ptySource.EndInputWithoutExit();

        public PtyStreamConnection Pty { get; }

        public string PortName { get; }

        public bool IsOpen => _open && !Pty.IsInputCompleted;

        public void Dispose()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            Pty.Dispose();
        }
    }
}
