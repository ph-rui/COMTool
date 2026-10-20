namespace COMTool.TestGate.Tests;

public sealed class ManualAcceptanceTests
{
    [Fact]
    public void MA01_ReadsManualIds()
    {
        using var temp = new TempDirectory();
        var path = temp.Write(
            "manual.md",
            "| ID | 覆盖功能 | 内容 |\n|---|---|---|\n| WIN-01 | UI-01 | 白条 |\n");
        Assert.Contains("WIN-01", ManualAcceptance.Load(path));
    }
}
