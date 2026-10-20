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
            Row("SER-03", "部分", manual: "WIN-03", evidence: "真实 USB 热插拔无法自动化，CI 无法模拟"));
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

    [Fact]
    public void GC08_GenericReasonRejected()
    {
        var result = Check(
            Parity("SER-03", "部分"),
            Row("SER-03", "部分", manual: "WIN-03", evidence: "不能确定"));
        Assert.Contains(result.Errors, e => e.Contains("不能自动化"));
    }

    [Fact]
    public void GC09_MissingManualIdFails()
    {
        var result = Check(
            Parity("SER-03", "部分"),
            Row("SER-03", "部分", manual: "WIN-99", evidence: "硬件原因无法自动化"));
        Assert.Contains(result.Errors, e => e.Contains("WIN-99"));
    }

    [Fact]
    public void GC10_PackageRowRequiredInPackagePhase()
    {
        var parity = Parity("PKG-01", "完成");
        var row = Row("PKG-01", "完成", "PKG01_CandidateSmoke", phase: "package");
        Assert.False(Check(parity, row, phase: "package").IsSuccess);
        Assert.True(Check(parity, row, phase: "package", package: "PASS PKG01_CandidateSmoke").IsSuccess);
    }

    [Fact]
    public void GC11_LinuxRowRequiredOnLinux()
    {
        var parity = Parity("UI-05", "完成");
        var row = Row("UI-05", "完成", "UI05_E2E", platform: "linux");
        Assert.False(Check(parity, row, platform: "linux").IsSuccess);
        Assert.True(Check(parity, row, platform: "linux", e2e: "PASS UI05_E2E").IsSuccess);
        Assert.True(Check(parity, row, platform: "windows").IsSuccess);
    }

    [Fact]
    public void GC12_MissingNotStartedRowFails()
    {
        var result = Check(
            Parity("UI-10", "未开始"),
            Row("UI-11", "部分", manual: "WIN-03", evidence: "硬件原因无法自动化"));
        Assert.Contains(result.Errors, e => e.Contains("UI-10"));
    }

    [Fact]
    public void GC13_UnknownStatusFails()
    {
        var result = Check(Parity("UI-05", "进行中"), Row("UI-05", "完成", "UI05_X"), testList: "UI05_X");
        Assert.Contains(result.Errors, e => e.Contains("进行中"));
    }

    [Fact]
    public void GC14_UnknownPlatformFails()
    {
        var result = Check(Parity("UI-05", "完成"), Row("UI-05", "完成", "UI05_X", platform: "linx"), testList: "UI05_X");
        Assert.Contains(result.Errors, e => e.Contains("linx"));
    }

    [Fact]
    public void GC15_DuplicateMatrixFeatureFails()
    {
        var matrix = MatrixHeader +
            "\n| UI-05 | 完成 | UI05_X | verify | all | dotnet test | - | - |" +
            "\n| UI-05 | 完成 | UI05_X | verify | all | dotnet test | - | - |";
        var result = Check(Parity("UI-05", "完成"), matrix, testList: "UI05_X");
        Assert.Contains(result.Errors, e => e.Contains("重复") || e.Contains("duplicate"));
    }

    [Fact]
    public void GC16_CompletedManualPhaseFails()
    {
        var result = Check(
            Parity("UI-05", "完成"),
            Row("UI-05", "完成", phase: "manual", manual: "WIN-03"));
        Assert.Contains(result.Errors, e => e.Contains("UI-05") && e.Contains("自动化"));
    }

    [Fact]
    public void GC17_FqnSubstringIsNotAMatch()
    {
        var result = Check(
            Parity("UI-05", "完成"),
            Row("UI-05", "完成", "COMTool.App.Tests.UI05"),
            testList: "COMTool.App.Tests.UI05_Extra");
        Assert.Contains(result.Errors, e => e.Contains("COMTool.App.Tests.UI05"));
    }
}
