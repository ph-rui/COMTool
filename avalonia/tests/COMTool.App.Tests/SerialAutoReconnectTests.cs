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
