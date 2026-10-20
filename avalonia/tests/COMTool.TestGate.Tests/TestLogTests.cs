namespace COMTool.TestGate.Tests;

public sealed class TestLogTests
{
    [Fact]
    public void TL01_ReadsDottedTestNames()
    {
        using var temp = new TempDirectory();
        var path = temp.Write(
            "tests.txt",
            "The following Tests are available:\n    COMTool.Core.Tests.ConfigTests.Loads\n    COMTool.App.Tests.UI05_TabSwitch\n");
        var names = TestLog.ReadTests(path);
        Assert.Contains("COMTool.Core.Tests.ConfigTests.Loads", names);
        Assert.Contains("COMTool.App.Tests.UI05_TabSwitch", names);
        Assert.DoesNotContain("The following Tests are available:", names);
    }

    [Fact]
    public void TL02_ReadsOnlyPassLines()
    {
        using var temp = new TempDirectory();
        var path = temp.Write("e2e.log", "PASS UI05_TabSwitch\nFAIL SSH06_Multiple\nnoise\n");
        var passed = TestLog.ReadPassed(path);
        Assert.Contains("UI05_TabSwitch", passed);
        Assert.DoesNotContain("SSH06_Multiple", passed);
        Assert.DoesNotContain("noise", passed);
    }

    [Fact]
    public void TL03_MissingLogFileIsEmpty()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "comtool-testgate-tests",
            Guid.NewGuid().ToString("N"),
            "missing.log");

        Assert.Empty(TestLog.ReadTests(path));
        Assert.Empty(TestLog.ReadPassed(path));
    }
}
