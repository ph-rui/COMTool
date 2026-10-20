namespace COMTool.TestGate.Tests;

public sealed class GateCheckerTests
{
    private const string ParityHeader =
        "| ID | 功能 | 优先级 | 状态 | 验收要点 |\n|---|---|---|---|---|";
    private const string MatrixHeader =
        "| Feature | Status | Automated | Phase | Platform | Command | Manual | Evidence |\n" +
        "|---|---|---|---|---|---|---|---|";
    private const string ManualHeader =
        "| ID | 覆盖功能 | 内容 |\n|---|---|---|";

    private static string Parity(string id, string status) =>
        ParityHeader + $"\n| {id} | X | P0 | {status} | x |";

    private static string Row(
        string id,
        string status,
        string automated = "-",
        string phase = "verify",
        string platform = "all",
        string manual = "-",
        string evidence = "-") =>
        MatrixHeader +
        $"\n| {id} | {status} | {automated} | {phase} | {platform} | dotnet test | {manual} | {evidence} |";

    private static GateResult Check(
        string parity,
        string matrix,
        string testList = "",
        string e2e = "",
        string package = "",
        string manual = ManualHeader + "\n| WIN-03 | SER-03 | 热插拔 |",
        string phase = "verify",
        string platform = "linux")
    {
        using var temp = new TempDirectory();
        return GateChecker.Check(new GateOptions(
            phase,
            platform,
            temp.Write("parity.md", parity),
            temp.Write("matrix.md", matrix),
            temp.Write("manual.md", manual),
            temp.Write("tests.txt", testList),
            temp.Write("e2e.log", e2e),
            temp.Write("package.log", package)));
    }

    [Fact]
    public void GC01_CompletedFeatureWithoutAutomatedTestFails()
    {
        var result = Check(Parity("UI-05", "完成"), Row("UI-05", "完成"));
        Assert.Contains(result.Errors, e => e.Contains("UI-05") && e.Contains("无自动化测试"));
    }

    [Fact]
    public void GC02_CompletedFeatureWithExistingTestPasses()
    {
        var result = Check(
            Parity("UI-05", "完成"),
            Row("UI-05", "完成", "UI05_TabSwitchKeepsBothTerminalsLive"),
            testList: "COMTool.App.Tests.UI05_TabSwitchKeepsBothTerminalsLive");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void GC03_PartialManualOnlyRequiresReason()
    {
        var result = Check(
            Parity("SER-03", "部分"),
            Row("SER-03", "部分", manual: "WIN-03"));
        Assert.Contains(result.Errors, e => e.Contains("WIN-03") && e.Contains("不能自动化"));
    }

    [Fact]
    public void GC04_PartialManualOnlyWithReasonPasses()
    {
        var result = Check(
            Parity("SER-03", "部分"),
            Row("SER-03", "部分", manual: "WIN-03", evidence: "真实 USB 热插拔无法在 CI 模拟"));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void GC05_UnknownTestIdFails()
    {
        var result = Check(
            Parity("UI-05", "完成"),
            Row("UI-05", "完成", "UI05_DoesNotExist"),
            testList: "COMTool.App.Tests.UI05_Other");
        Assert.Contains(result.Errors, e => e.Contains("UI05_DoesNotExist"));
    }

    [Fact]
    public void GC06_VerifyPhaseSkipsPackageRows()
    {
        var result = Check(
            Parity("PKG-01", "完成"),
            Row("PKG-01", "完成", "PKG01_CandidateSmoke", phase: "package"),
            phase: "verify");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void GC07_LinuxOnlyRowIsNotRequiredOnWindows()
    {
        var result = Check(
            Parity("UI-05", "完成"),
            Row("UI-05", "完成", "UI05_E2E", platform: "linux"),
            platform: "windows");
        Assert.True(result.IsSuccess);
    }
}
