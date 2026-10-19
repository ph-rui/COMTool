using COMTool.Connections;

namespace COMTool.Connections.Tests;

public sealed class SerialPortCatalogTests
{
    [Theory]
    [InlineData("USB-SERIAL CH340 (COM99)", "COM99")]
    [InlineData("Communications Port (COM10)", "COM10")]
    [InlineData("Prolific USB-to-Serial Comm Port (COM3)", "COM3")]
    [InlineData("Standard Serial over Bluetooth link (COM7)", "COM7")]
    [InlineData("com12 device", "COM12")]
    public void FindComPortExtractsDevice(string friendlyName, string expected) =>
        Assert.Equal(expected, SerialPortCatalog.FindComPort(friendlyName));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("USB Serial Device")]
    public void FindComPortRejectsMissingDevice(string? friendlyName) =>
        Assert.Null(SerialPortCatalog.FindComPort(friendlyName));

    [Fact]
    public void DisplayTextRemovesDuplicateDeviceSuffix()
    {
        var info = new SerialPortInfo(
            "COM99",
            Description: "USB-SERIAL CH340 (COM99)",
            Name: "USB-SERIAL CH340");
        Assert.Equal("USB-SERIAL CH340", info.DisplayLabel);
        Assert.Equal("COM99  USB-SERIAL CH340", info.DisplayText);
    }

    [Fact]
    public void NormalizeSortsNaturallyAndDeduplicates()
    {
        var ports = SerialPortCatalog.Normalize(new[]
        {
            new SerialPortInfo("COM10"),
            new SerialPortInfo("COM2"),
            new SerialPortInfo("COM1"),
            new SerialPortInfo("COM2"),
        });

        Assert.Equal(new[] { "COM1", "COM2", "COM10" }, ports.Select(port => port.Device));
    }
}
