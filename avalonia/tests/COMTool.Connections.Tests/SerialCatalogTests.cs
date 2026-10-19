using COMTool.Connections;
using COMTool.Core.Config;

namespace COMTool.Connections.Tests;

public sealed class SerialCatalogTests
{
    [Fact]
    public void NormalizeSortsCom2BeforeCom10()
    {
        var ports = SerialPortCatalog.Normalize(new[]
        {
            new SerialPortInfo("COM10"),
            new SerialPortInfo("COM2"),
            new SerialPortInfo("com1"),
        });

        Assert.Equal(new[] { "com1", "COM2", "COM10" }, ports.Select(p => p.Device));
    }

    [Fact]
    public void NormalizeSortsLinuxPortsNaturally()
    {
        var ports = SerialPortCatalog.Normalize(new[]
        {
            new SerialPortInfo("/dev/ttyUSB10"),
            new SerialPortInfo("/dev/ttyUSB2"),
            new SerialPortInfo("/dev/ttyACM1"),
        });

        Assert.Equal(
            new[] { "/dev/ttyACM1", "/dev/ttyUSB2", "/dev/ttyUSB10" },
            ports.Select(p => p.Device));
    }

    [Fact]
    public void NormalizeDeduplicatesAndPrefersMacCallout()
    {
        var ports = SerialPortCatalog.Normalize(new[]
        {
            new SerialPortInfo("/dev/tty.usbserial-1"),
            new SerialPortInfo("/dev/cu.usbserial-1"),
            new SerialPortInfo("/dev/cu.usbserial-1"),
            new SerialPortInfo("/dev/cu.Bluetooth-Incoming-Port"),
        });

        Assert.Single(ports);
        Assert.Equal("/dev/cu.usbserial-1", ports[0].Device);
    }

    [Fact]
    public void DisplayLabelStripsDuplicateWindowsDevice()
    {
        var port = new SerialPortInfo("COM5", "USB-SERIAL CH340 (COM5)");

        Assert.Equal("USB-SERIAL CH340", port.DisplayLabel);
        Assert.Equal("COM5  USB-SERIAL CH340", port.DisplayText);
    }

    [Fact]
    public void ScanFailureIsDistinguishableFromEmptyScan()
    {
        var failed = SerialPortScanResult.Failed("access denied");
        var empty = new SerialPortScanResult(true, Array.Empty<SerialPortInfo>());

        Assert.False(failed.Success);
        Assert.True(empty.Success);
        Assert.Empty(empty.Ports);
    }

    [Fact]
    public void SessionStoreUsesNaturalPortOrder()
    {
        var store = new SerialSessionStore();
        store.Load(new ComtoolConfig
        {
            SerialSessions =
            {
                new SerialSession { Port = "COM10" },
                new SerialSession { Port = "COM2" },
                new SerialSession { Port = "COM1" },
            },
        });

        Assert.Equal(
            new[] { "COM1", "COM2", "COM10" },
            store.SortedSessions().Select(s => s.Port));
    }

    [Fact]
    public void SettingsMapToSystemIoPortsEnums()
    {
        Assert.Equal(System.IO.Ports.Parity.Even, SerialConnection.ParseParity("Even"));
        Assert.Equal(System.IO.Ports.Parity.None, SerialConnection.ParseParity("bogus"));
        Assert.Equal(System.IO.Ports.StopBits.Two, SerialConnection.ParseStopBits("2"));
        Assert.Equal(
            System.IO.Ports.Handshake.RequestToSend,
            SerialConnection.ParseHandshake("RTS/CTS"));
        Assert.Equal(
            System.IO.Ports.Handshake.XOnXOff,
            SerialConnection.ParseHandshake("XON/XOFF"));
        // System.IO.Ports does not expose DSR/DTR flow control. It must not
        // fall through to the unrelated RTS/CTS + XON/XOFF mode.
        Assert.Equal(
            System.IO.Ports.Handshake.None,
            SerialConnection.ParseHandshake("DSR/DTR"));
    }
}
