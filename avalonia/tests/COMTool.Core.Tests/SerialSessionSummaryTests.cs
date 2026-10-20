using COMTool.Core.Config;

namespace COMTool.Core.Tests;

public sealed class SerialSessionSummaryTests
{
    [Fact]
    public void SER04_ConnectionSummaryFormatsBaudDataParityStopAndFlow()
    {
        Assert.Equal(
            "115200 8N1",
            SerialSessionRules.ConnectionSummary(new SerialConnectionConfig()));

        Assert.Equal(
            "9600 7E2",
            SerialSessionRules.ConnectionSummary(new SerialConnectionConfig
            {
                Baudrate = 9600,
                Bytesize = 7,
                Parity = "Even",
                Stopbits = "2",
            }));

        Assert.Equal(
            "115200 8O1",
            SerialSessionRules.ConnectionSummary(new SerialConnectionConfig
            {
                Parity = "Odd",
            }));

        Assert.Equal(
            "115200 8M1",
            SerialSessionRules.ConnectionSummary(new SerialConnectionConfig
            {
                Parity = "Mark",
            }));

        Assert.Equal(
            "115200 8S1",
            SerialSessionRules.ConnectionSummary(new SerialConnectionConfig
            {
                Parity = "Space",
            }));

        Assert.Equal(
            "115200 8N1",
            SerialSessionRules.ConnectionSummary(new SerialConnectionConfig
            {
                Flowcontrol = "None",
            }));

        Assert.Equal(
            "460800 8N1 · RTS/CTS",
            SerialSessionRules.ConnectionSummary(new SerialConnectionConfig
            {
                Baudrate = 460800,
                Flowcontrol = "RTS/CTS",
            }));
    }
}
