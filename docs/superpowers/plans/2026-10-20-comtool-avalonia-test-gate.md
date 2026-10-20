# COMTool Avalonia 全功能测试门禁实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立功能测试矩阵、Avalonia Headless UI 测试、Linux Xvfb 端到端测试和“自动化全绿才出候选包、人工签收才出正式包”的强制门禁。

**Architecture:** 新增一个跨平台 .NET 测试门禁检查器，关联 parity matrix、功能测试矩阵、测试列表和 E2E 日志；新增 App Headless 测试项目验证窗口生命周期、标签切换、焦点和策略逻辑；新增 Linux Xvfb 脚本验证真实 PTY、键盘焦点和 SSH 多连接；最后重构打包脚本为候选包/正式包两阶段。

**Tech Stack:** .NET SDK `10.0.402`, Avalonia `12.1.4`, Avalonia.Headless.XUnit `12.1.4`, xUnit `2.9.3`, Linux Xvfb, xdotool, OpenSSH, PowerShell 5.1+。

**Spec:** `docs/superpowers/specs/2026-10-20-comtool-avalonia-test-gate-design.md`

## Global Constraints

- 版本保持 `0.01`。
- 不修改 Python 应用、启动器、Python 3.10、ser2net 或 Python 配置。
- Avalonia 配置目录必须独立，禁止与 Python 共用 `config.json`。
- 只允许 Windows DPAPI 加密后的 `passwd_enc` 持久化，禁止保存明文密码。
- 保持 R2R 打包策略：`PublishReadyToRun=true`，`EnableCompressionInSingleFile=false`。
- 不做 Windows RDP；Windows 实机验证只通过 SSH 或用户本机操作。
- 测试和 E2E 必须使用临时配置目录，不读写真实用户配置。
- 正式包只能由 `promote` 从通过人工签收的候选包生成。
- 门禁失败时不得生成/覆盖候选包或正式包。
- 修复 bug 必须先写一个在旧代码上失败的回归测试，并在同一提交更新功能矩阵。
- 未实现的 P1 功能不得标为 `完成`，必须在矩阵中显式保留为 `未开始` 或带原因的 `部分`。
- 2 周 Beta 观察和默认版本切换不属于每次打包门禁，但仍保留为发布门槛。
- 未得到用户确认前不 push。

## Review Focus

这些是 spec 没有逐条展开、但最容易被实现遗漏的失败模式；对应测试必须出现在所属任务中：

1. 快速连续切换标签时，第一次 `AttachedToVisualTree` 回调可能晚于第二次切换，焦点不能落到旧标签。
2. 双击终端时必须与标题栏最大化路径一致，同时不能破坏终端自身的词选择/双击处理。
3. SSH 第一次连接尚未完成时再次点击同一会话，不能复用半开标签或阻塞第二次连接。
4. 旧候选包、旧签收文件或 SHA256 不匹配时，`promote` 必须拒绝，不能因为文件存在就提升。
5. 配置覆盖变量为空、相对路径或包含空格时，不能回退到真实用户配置，也不能串到 Python 配置。

---

## File Structure

新增：

- `docs/testing/feature-test-matrix.md`：功能到测试/人工验收的映射。
- `docs/testing/manual-acceptance.md`：人工验收项定义。
- `docs/testing/signoffs/README.md`：签收文件格式和命名规则。
- `avalonia/tools/COMTool.TestGate/`：矩阵、日志和签收检查器。
- `avalonia/tests/COMTool.TestGate.Tests/`：检查器单元测试。
- `avalonia/tests/COMTool.App.Tests/`：Avalonia Headless UI 测试。
- `avalonia/tests/e2e/linux/run-ui-e2e.sh`：Linux Xvfb 端到端入口。
- `avalonia/tests/e2e/linux/lib.sh`：进程、窗口、键盘和断言辅助函数。
- `avalonia/pack/verify.sh`、`avalonia/pack/verify.ps1`：统一自动化门禁。
- `avalonia/pack/promote.ps1`：候选包到正式包的人工签收提升。
- `avalonia/src/COMTool.App/Services/SessionActivationPolicy.cs`：SSH/串口会话点击策略。
- `avalonia/src/COMTool.App/Services/SettingsTabPolicy.cs`：设置页保存时替换哪个标签。
- `avalonia/tests/COMTool.App.Tests/InMemoryPty.cs`：Headless 测试用内存 PTY。

修改：

- `docs/parity-matrix.md`：补测试证据入口；无法自动化的项降为 `部分` 并绑定人工验收。
- `avalonia/src/COMTool.Core/Config/ConfigPaths.cs`：增加测试配置目录覆盖。
- `avalonia/src/COMTool.App/Views/MainWindow.axaml.cs`：最小化接入策略类和测试需要的可观察状态；不改变生产行为。
- `avalonia/pack/windows/pack.ps1`、`avalonia/pack/linux/pack.sh`：候选包输出和门禁调用。
- `.github/workflows/avalonia.yml`：使用统一门禁，上传候选包和测试证据。
- `avalonia/README.md`、`docs/dev.md`：记录测试、门禁和配置覆盖。

---

## Task 0: 建立工作分支和基线

**Files:**
- No file changes.

**Interfaces:**
- Consumes: clean `avalonia` branch at `60b9398`.
- Produces: branch `feature/avalonia-test-gate`; baseline test count. (`avalonia/feature-*` cannot coexist with the existing `avalonia` branch because Git treats `avalonia` as a ref directory conflict.)

- [ ] **Step 1: Create the feature branch**

Run:
```bash
git switch -c feature/avalonia-test-gate
git status --short --branch
```
Expected: branch `feature/avalonia-test-gate`, clean worktree.

- [ ] **Step 2: Run the existing suite and record the baseline**

Run:
```bash
cd /tmp/opencode/COMTool
/tmp/opencode/dotnet/dotnet test avalonia/COMTool.Avalonia.sln -c Release
```
Expected: all existing tests pass; record the exact counts in the task log. The current baseline is 87 tests (Core 13, Connections 31, Terminal 11, ZMODEM 32), but use the actual output.

- [ ] **Step 3: Confirm no Python files changed**

Run:
```bash
git status --short
git diff --name-only
```
Expected: no Python, launcher, ser2net or Python config files.

---

## Task 1: TestGate 项目和矩阵解析

**Files:**
- Create: `avalonia/tools/COMTool.TestGate/COMTool.TestGate.csproj`
- Create: `avalonia/tools/COMTool.TestGate/MatrixTable.cs`
- Create: `avalonia/tools/COMTool.TestGate/FeatureMatrix.cs`
- Create: `avalonia/tools/COMTool.TestGate/ParityMatrix.cs`
- Create: `avalonia/tools/COMTool.TestGate/Program.cs`
- Create: `avalonia/tests/COMTool.TestGate.Tests/COMTool.TestGate.Tests.csproj`
- Create: `avalonia/tests/COMTool.TestGate.Tests/MatrixTableTests.cs`
- Modify: `avalonia/COMTool.Avalonia.sln`

**Interfaces:**
- Consumes: Markdown tables in `docs/parity-matrix.md` and `docs/testing/feature-test-matrix.md`.
- Produces:
  - `MarkdownTable.Read(string path) -> IReadOnlyList<IReadOnlyDictionary<string, string>>`
  - `FeatureMatrix.Load(string path) -> IReadOnlyList<FeatureRow>`
  - `FeatureRow` fields: `Feature`, `Status`, `Automated[]`, `Phase`, `Platform`, `Command`, `Manual[]`, `Evidence`.
  - `ParityMatrix.Load(string path) -> IReadOnlyDictionary<string, string>` mapping feature ID to status.

- [ ] **Step 1: Write the failing parser tests**

Create `MatrixTableTests.cs` with tests named:

```csharp
[Fact]
public void MD01_ParsesHeaderAndRows()
{
    var path = TempFile("| Feature | Status |\n|---|---|\n| UI-05 | 完成 |\n");
    var rows = MarkdownTable.Read(path);
    Assert.Single(rows);
    Assert.Equal("UI-05", rows[0]["Feature"]);
    Assert.Equal("完成", rows[0]["Status"]);
}

[Fact]
public void MD02_IgnoresSeparatorAndBlankLines()
{
    var path = TempFile("| Feature | Status |\n| --- | --- |\n\n| UI-05 | 完成 |\n");
    Assert.Single(MarkdownTable.Read(path));
}

[Fact]
public void MD03_StripsWhitespaceAroundCells()
{
    var path = TempFile("|Feature|Status|\n|---|---|\n|  UI-05  |  完成  |\n");
    var row = MarkdownTable.Read(path).Single();
    Assert.Equal("UI-05", row["Feature"]);
    Assert.Equal("完成", row["Status"]);
}

[Fact]
public void FM01_ParsesCommaSeparatedIdsAndDash()
{
    var path = TempFile(
        "| Feature | Status | Automated | Phase | Platform | Command | Manual | Evidence |\n" +
        "|---|---|---|---|---|---|---|---|\n" +
        "| UI-05 | 完成 | UI05_A, UI05_B | verify | all | dotnet test | - | - |\n");
    var row = FeatureMatrix.Load(path).Single();
    Assert.Equal(new[] { "UI05_A", "UI05_B" }, row.Automated);
    Assert.Empty(row.Manual);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.TestGate.Tests/COMTool.TestGate.Tests.csproj -c Release
```
Expected: FAIL because `COMTool.TestGate` and `MarkdownTable` do not exist.

- [ ] **Step 3: Add the project files and solution entries**

`COMTool.TestGate.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
```

Test project:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\tools\COMTool.TestGate\COMTool.TestGate.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

Add both projects to `avalonia/COMTool.Avalonia.sln` using `dotnet sln add`.

- [ ] **Step 4: Implement `MarkdownTable` and `FeatureMatrix`**

`MarkdownTable.Read` must:
- read lines;
- ignore blank lines and the separator row (`---`);
- split on `|`, trim cells;
- use the first row as headers;
- throw `InvalidDataException` with the file path and line number if a row has a different cell count.

`FeatureMatrix.Load` must:
- call `MarkdownTable.Read`;
- require columns `Feature`, `Status`, `Automated`, `Phase`, `Platform`, `Command`, `Manual`, `Evidence`;
- split `Automated` and `Manual` on `,`, trim, and treat `-` as empty;
- return immutable rows.

`ParityMatrix.Load` must:
- call `MarkdownTable.Read`;
- use the `ID` and `状态` columns;
- ignore rows whose `ID` is empty or does not match `^[A-Z]+-\d+$`;
- normalize annotations such as `完成（Avalonia 增强）` to `完成`, `部分（...）` to `部分`, and so on;
- return a dictionary from feature ID to status, and fail on duplicate IDs.

Add this test helper in `MatrixTableTests.cs`:
```csharp
private static string TempFile(string content)
{
    var path = Path.Combine(Path.GetTempPath(), $"comtool-testgate-{Guid.NewGuid():N}.md");
    File.WriteAllText(path, content);
    return path;
}
```

- [ ] **Step 5: Run the parser tests**

Run:
```bash
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.TestGate.Tests/COMTool.TestGate.Tests.csproj -c Release
```
Expected: PASS for `MD01`-`FM01`.

- [ ] **Step 6: Commit**

```bash
git add avalonia/tools/COMTool.TestGate avalonia/tests/COMTool.TestGate.Tests avalonia/COMTool.Avalonia.sln
git commit -m "Add test gate matrix parser"
```

---

## Task 2: 验证矩阵覆盖规则和阶段/平台过滤

**Files:**
- Create: `avalonia/tools/COMTool.TestGate/GateChecker.cs`
- Create: `avalonia/tools/COMTool.TestGate/GateResult.cs`
- Create: `avalonia/tools/COMTool.TestGate/ManualAcceptance.cs`
- Create: `avalonia/tests/COMTool.TestGate.Tests/GateCheckerTests.cs`
- Create: `avalonia/tests/COMTool.TestGate.Tests/ManualAcceptanceTests.cs`
- Modify: `avalonia/tools/COMTool.TestGate/FeatureMatrix.cs`

**Interfaces:**
- Consumes: `FeatureMatrix.Load`, `ParityMatrix.Load`.
- Produces:
  - `GateOptions(string Phase, string Platform, string ParityPath, string MatrixPath, string ManualPath, string TestListPath, string E2eLogPath, string PackageLogPath)`
  - `GateChecker.Check(GateOptions options) -> GateResult`
  - `GateResult.Errors -> IReadOnlyList<string>`, `GateResult.IsSuccess -> bool`
  - `ManualAcceptance.Load(string path) -> IReadOnlySet<string>`

- [ ] **Step 1: Write failing coverage tests**

Tests:

```csharp
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

[Fact]
public void MA01_ReadsManualIds()
{
    using var temp = new TempDirectory();
    var path = temp.Write(
        "manual.md",
        "| ID | 覆盖功能 | 内容 |\n|---|---|---|\n| WIN-01 | UI-01 | 白条 |\n");
    Assert.Contains("WIN-01", ManualAcceptance.Load(path));
}
```

Add a test-only `TempDirectory` helper to the same project:
```csharp
internal sealed class TempDirectory : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), "comtool-testgate-tests", Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(_path);

    public string Write(string name, string content)
    {
        var file = Path.Combine(_path, name);
        File.WriteAllText(file, content);
        return file;
    }

    public void Dispose()
    {
        if (Directory.Exists(_path))
        {
            Directory.Delete(_path, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.TestGate.Tests/COMTool.TestGate.Tests.csproj -c Release --filter FullyQualifiedName~GateCheckerTests
```
Expected: FAIL because `GateChecker` does not exist.

- [ ] **Step 3: Implement `GateChecker`**

Rules, in order:
1. Build parity status map; ignore headings and non-feature rows.
2. Build matrix rows keyed by `Feature`.
3. For each parity status:
   - `完成`: matrix row exists; at least one `Automated`; every automated ID resolves against `TestListPath`, E2E log or package log according to phase.
   - `部分`: matrix row exists; at least one `Automated` or `Manual`; if no `Automated`, `Evidence` must be non-empty and must contain a reason word from `不能自动化|无法自动化|硬件|视觉|真实桌面`.
   - `未开始`/`不适用`: matrix row may exist with `-`, no test required.
4. Only enforce rows whose `Platform` is `all` or matches `options.Platform`.
5. Only enforce rows whose `Phase` matches `options.Phase` or is `manual`.
6. Manual IDs must appear in `docs/testing/manual-acceptance.md` table.
7. Unknown matrix feature IDs fail.
8. Duplicate feature IDs fail.

Test ID resolution:
- exact match;
- fully-qualified suffix match (`.UI05_...`);
- otherwise `Contains(id, StringComparison.Ordinal)` if the matrix value starts with `COMTool.` or contains `Tests.`.

- [ ] **Step 4: Confirm the parity parser test already exists**

Task 1 already added `PM01_ParsesParityStatusRows` in `MatrixTableTests.cs`. Do not duplicate it. Add gate-specific `ParityMatrix` assertions here only if the Task 1 test is insufficient; otherwise proceed.

- [ ] **Step 5: Run tests**

Run:
```bash
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.TestGate.Tests/COMTool.TestGate.Tests.csproj -c Release
```
Expected: all parser and checker tests pass.

- [ ] **Step 6: Commit**

```bash
git add avalonia/tools/COMTool.TestGate avalonia/tests/COMTool.TestGate.Tests
git commit -m "Enforce feature test matrix coverage rules"
```

---

## Task 3: TestGate 日志、CLI 和人工验收解析

**Files:**
- Create: `avalonia/tools/COMTool.TestGate/TestLog.cs`
- Create: `avalonia/tests/COMTool.TestGate.Tests/TestLogTests.cs`
- Modify: `avalonia/tools/COMTool.TestGate/Program.cs`

**Interfaces:**
- Consumes: `GateChecker`, `ManualAcceptance`.
- Produces:
  - `TestLog.ReadTests(string path) -> IReadOnlySet<string>`
  - `TestLog.ReadPassed(string path) -> IReadOnlySet<string>`
  - CLI: `--phase verify|package --platform linux|windows|macos --parity <path> --matrix <path> --manual <path> --tests <path> --e2e <path> --package <path>`

- [ ] **Step 1: Write failing log tests**

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run with `--filter FullyQualifiedName~TestLogTests`.
Expected: FAIL.

- [ ] **Step 3: Implement log parsers and CLI**

`Program.Main` must:
- parse `--key value` pairs;
- default `--phase verify`, `--platform` from `RuntimeInformation`;
- print each error as `ERROR: <message>`;
- print `RESULT: PASS` or `RESULT: FAIL (<count>)`;
- return 0/1.

- [ ] **Step 4: Run the CLI against a temporary fixture**

Run:
```bash
/tmp/opencode/dotnet/dotnet run --project avalonia/tools/COMTool.TestGate -- \
  --phase verify --platform linux \
  --parity /tmp/parity.md --matrix /tmp/matrix.md \
  --manual /tmp/manual.md --tests /tmp/tests.txt \
  --e2e /tmp/e2e.log
```
Expected: `RESULT: PASS` for a fixture with one completed feature mapped to one test.

- [ ] **Step 5: Commit**

```bash
git add avalonia/tools/COMTool.TestGate avalonia/tests/COMTool.TestGate.Tests
git commit -m "Add test gate CLI and log parsers"
```

---

## Task 4: 初始功能矩阵和人工验收清单

**Files:**
- Create: `docs/testing/feature-test-matrix.md`
- Create: `docs/testing/manual-acceptance.md`
- Create: `docs/testing/signoffs/README.md`
- Modify: `docs/parity-matrix.md`

**Interfaces:**
- Consumes: all feature IDs from `docs/parity-matrix.md`.
- Produces: one matrix row per parity row, with explicit `-` where evidence does not exist yet.

- [ ] **Step 1: Generate matrix rows from the parity matrix**

For every parity row (all sections, including `NET-*` and `PLG-*`), add:

```markdown
| Feature | Status | Automated | Phase | Platform | Command | Manual | Evidence |
|---|---|---|---|---|---|---|---|
| UI-05 | 完成 | - | verify | all | - | - | 待补测试 |
```

Use the same normalized `Status` as the parity matrix (`完成（Avalonia 增强）` becomes `完成`). Do not mark a missing test as covered.
For `部分` rows with no automation, add the matching manual ID in `Manual` and write the reason in `Evidence`.
For `未开始`/`不适用`, leave `Automated` and `Manual` as `-`.

- [ ] **Step 2: Add the initial manual acceptance list**

Use the IDs from the spec plus at least:

```markdown
| ID | 覆盖功能 | 内容 | 证据要求 |
|---|---|---|---|
| WIN-01 | UI-01 | Windows 无原生白条，标题栏/缩放正常 | 截图 |
| WIN-02 | UI-14 | Windows 150%/200% DPI 无裁切 | 两张截图 |
| WIN-03 | SER-03 | 串口热插拔后列表刷新 | 日志/截图 |
| WIN-04 | SER-06 | 占用端口显示正确错误 | 截图 |
| WIN-05 | SER-07 | 串口回环收发 | 日志 |
| WIN-06 | SSH-04 | Windows DPAPI 记住密码并重连 | 日志 |
| WIN-07 | ZM-02/ZM-03 | 原生对话框完成 ZMODEM 收发 | 日志 |
| WIN-08 | PKG-04 | 安装/升级/卸载保留 Beta 配置 | 日志 |
| WIN-09 | TST-06 | 候选包 8 小时稳定性 | 资源曲线 |
| WIN-10 | UI-11 | About/帮助入口当前行为；插件帮助和更新提示未实现部分不计入完成 | 截图/说明 |
```

- [ ] **Step 3: Document signoff naming**

`docs/testing/signoffs/README.md` must state:
- file name `windows-0.01-<sha12>.json`;
- `candidateSha256` must be the full 64 characters;
- `items` must include every `WIN-*` ID used by the matrix;
- `soak.hours` must be at least 8;
- no `FAIL`, no empty `evidence`.

- [ ] **Step 4: Run TestGate to see the expected gaps**

Run:
```bash
/tmp/opencode/dotnet/dotnet run --project avalonia/tools/COMTool.TestGate -- \
  --phase verify --platform linux \
  --parity docs/parity-matrix.md \
  --matrix docs/testing/feature-test-matrix.md \
  --manual docs/testing/manual-acceptance.md \
  --tests /tmp/empty-tests.txt --e2e /tmp/empty-e2e.log
```
Expected: FAIL and list every `完成` feature with no automated test. This is intentional; do not proceed to packaging.

- [ ] **Step 5: Commit**

```bash
git add docs/testing docs/parity-matrix.md
git commit -m "Add feature test matrix and manual acceptance checklist"
```

---

## Task 5: 测试配置目录覆盖

**Files:**
- Modify: `avalonia/src/COMTool.Core/Config/ConfigPaths.cs`
- Create: `avalonia/tests/COMTool.Core.Tests/ConfigPathsTests.cs`
- Modify: `docs/dev.md`

**Interfaces:**
- Consumes: `ConfigPaths`.
- Produces:
  - `COMTOOL_AVALONIA_CONFIG_DIR` overrides only `AvaloniaConfigDirectory()`.
  - `COMTOOL_PYTHON_CONFIG_DIR` overrides only `PythonConfigDirectory()`.
  - Empty/whitespace values are ignored.
  - Relative values are converted with `Path.GetFullPath`.

- [ ] **Step 1: Write failing tests**

```csharp
[Collection("Environment")]
public sealed class ConfigPathsTests
{
    [Fact]
    public void CP01_AvaloniaOverrideWins()
    {
        using var env = new EnvScope("COMTOOL_AVALONIA_CONFIG_DIR", "/tmp/comtool-test-av");
        Assert.Equal("/tmp/comtool-test-av", Normalize(ConfigPaths.AvaloniaConfigDirectory()));
        Assert.NotEqual("/tmp/comtool-test-av", Normalize(ConfigPaths.PythonConfigDirectory()));
    }

    [Fact]
    public void CP02_PythonOverrideWins()
    {
        using var env = new EnvScope("COMTOOL_PYTHON_CONFIG_DIR", "/tmp/comtool-test-py");
        Assert.Equal("/tmp/comtool-test-py", Normalize(ConfigPaths.PythonConfigDirectory()));
    }

    [Fact]
    public void CP03_EmptyOverrideFallsBack()
    {
        using var env = new EnvScope("COMTOOL_AVALONIA_CONFIG_DIR", "   ");
        Assert.EndsWith("comtool-avalonia", Normalize(ConfigPaths.AvaloniaConfigDirectory()));
    }

    [Fact]
    public void CP04_RelativeOverrideIsAbsolute()
    {
        using var env = new EnvScope("COMTOOL_AVALONIA_CONFIG_DIR", "relative-config");
        Assert.True(Path.IsPathRooted(ConfigPaths.AvaloniaConfigDirectory()));
    }

    [Fact]
    public void CP05_DefaultDirectoriesStaySeparate()
    {
        Assert.NotEqual(
            Normalize(ConfigPaths.AvaloniaConfigDirectory()),
            Normalize(ConfigPaths.PythonConfigDirectory()));
    }

    [Fact]
    public void CP06_OverrideWithSpacesIsPreserved()
    {
        using var env = new EnvScope("COMTOOL_AVALONIA_CONFIG_DIR", "/tmp/comtool test config");
        Assert.Equal("/tmp/comtool test config", Normalize(ConfigPaths.AvaloniaConfigDirectory()));
    }
}
```

Use a collection definition with `DisableParallelization = true` because environment variables are process-global.

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.Core.Tests/COMTool.Core.Tests.csproj -c Release --filter FullyQualifiedName~ConfigPathsTests
```
Expected: FAIL for the override tests.

- [ ] **Step 3: Implement the override helpers**

In `ConfigPaths`:

```csharp
private static string? Override(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
}
```

Use it in `AvaloniaConfigDirectory()` and `PythonConfigDirectory()` only. Do not change `ApplicationName` or the default paths.

- [ ] **Step 4: Document the overrides**

Add to `docs/dev.md`:
- purpose: tests, portable smoke runs, E2E isolation;
- production does not set them;
- never point both at the same directory;
- no secrets in the override directory.

- [ ] **Step 5: Run tests**

Run:
```bash
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.Core.Tests/COMTool.Core.Tests.csproj -c Release
```
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add avalonia/src/COMTool.Core/Config/ConfigPaths.cs avalonia/tests/COMTool.Core.Tests/ConfigPathsTests.cs docs/dev.md
git commit -m "Add isolated config directory overrides"
```

---

## Task 6: App Headless 测试项目骨架

**Files:**
- Create: `avalonia/tests/COMTool.App.Tests/COMTool.App.Tests.csproj`
- Create: `avalonia/tests/COMTool.App.Tests/TestAppBuilder.cs`
- Create: `avalonia/tests/COMTool.App.Tests/TestEnvironment.cs`
- Create: `avalonia/tests/COMTool.App.Tests/AppSmokeTests.cs`
- Modify: `avalonia/COMTool.Avalonia.sln`

**Interfaces:**
- Consumes: `COMTool.App.App`, `COMTool.App.Views.MainWindow`, `COMTool.Core.Config.ComtoolConfig`.
- Produces:
  - `TestEnvironment` with `Root`, `AvaloniaConfigDir`, `PythonConfigDir`, `SeedConfig(ComtoolConfig)`, `Dispose()`.
  - xUnit assembly attribute `[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]`.
  - `TestAppBuilder.BuildAvaloniaApp() -> AppBuilder`.

- [ ] **Step 1: Add the test project and packages**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia.Headless.XUnit" Version="12.1.4" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\COMTool.App\COMTool.App.csproj" />
    <ProjectReference Include="..\..\src\COMTool.Core\COMTool.Core.csproj" />
    <ProjectReference Include="..\..\src\COMTool.Connections\COMTool.Connections.csproj" />
    <ProjectReference Include="..\..\src\COMTool.Terminal\COMTool.Terminal.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add the headless app builder**

`TestAppBuilder.cs`:

```csharp
using Avalonia;
using Avalonia.Headless;
using COMTool.App;

[assembly: AvaloniaTestApplication(typeof(COMTool.App.Tests.TestAppBuilder))]

namespace COMTool.App.Tests;

public static class TestAppBuilder
{
    private static readonly string Root =
        Path.Combine(Path.GetTempPath(), "comtool-app-tests", Guid.NewGuid().ToString("N"));

    static TestAppBuilder()
    {
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable(
            "COMTOOL_AVALONIA_CONFIG_DIR",
            Path.Combine(Root, "avalonia"));
        Environment.SetEnvironmentVariable(
            "COMTOOL_PYTHON_CONFIG_DIR",
            Path.Combine(Root, "python"));
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
```

The static constructor creates a process-wide temp root and sets both environment variables before `App` initializes, so `AppLog` does not write to the real config.

- [ ] **Step 3: Add `TestEnvironment`**

Responsibilities:
- create a unique root under the process-wide temp root;
- set both environment variables for each test;
- create `AvaloniaConfigDir`;
- write `config.json` from a `ComtoolConfig` with `Version = 3`;
- restore previous environment variables on dispose;
- delete the root on success; keep it on failure for diagnosis.

- [ ] **Step 4: Write the smoke test**

```csharp
[AvaloniaFact]
public void APP01_MainWindowShows()
{
    using var env = new TestEnvironment();
    env.SeedConfig(new ComtoolConfig());
    var window = new MainWindow { DataContext = new MainViewModel() };
    window.Show();
    Dispatcher.UIThread.RunJobs();
    Assert.True(window.IsVisible);
    Assert.NotNull(window.FindControl<SessionSidebar>("Sidebar"));
    window.Close();
}
```

- [ ] **Step 5: Run the test**

Run:
```bash
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.App.Tests/COMTool.App.Tests.csproj -c Release
```
Expected: PASS. If the app tries to read the real config, fix `TestAppBuilder` before continuing.

- [ ] **Step 6: Commit**

```bash
git add avalonia/tests/COMTool.App.Tests avalonia/COMTool.Avalonia.sln
git commit -m "Add Avalonia headless test project"
```

---

## Task 7: 视图模型和会话策略测试

**Files:**
- Create: `avalonia/src/COMTool.App/Services/SessionActivationPolicy.cs`
- Create: `avalonia/src/COMTool.App/Services/SettingsTabPolicy.cs`
- Create: `avalonia/tests/COMTool.App.Tests/MainViewModelTests.cs`
- Create: `avalonia/tests/COMTool.App.Tests/SessionActivationPolicyTests.cs`
- Create: `avalonia/tests/COMTool.App.Tests/SettingsTabPolicyTests.cs`
- Modify: `avalonia/src/COMTool.App/Views/MainWindow.axaml.cs`

**Interfaces:**
- Consumes: `MainViewModel`, `TerminalTab`, `SessionKind`.
- Produces:
  - `SessionActivationPolicy.ShouldOpenNewSsh(IReadOnlyList<TerminalTab> tabs, string sessionId, Func<TerminalTab, bool> isConnected) -> bool`
  - `SessionActivationPolicy.DeadSshTabsToClose(...) -> IReadOnlyList<TerminalTab>`
  - `SettingsTabPolicy.TabToReplace(IReadOnlyList<TerminalTab> tabs, TerminalTab? editedTab, string sessionId, SessionKind kind) -> TerminalTab?`
- `MainWindow.OnSessionActivated` and `OpenSshFromSettingsAsync` must call these policies; behavior must not change.

- [ ] **Step 1: Write failing policy tests**

```csharp
[Fact]
public void SSH06_LiveConnectionOpensAnotherTab()
{
    var live = Tab("ssh", connected: true);
    Assert.True(SessionActivationPolicy.ShouldOpenNewSsh(new[] { live }, "ssh", _ => true));
    Assert.Empty(SessionActivationPolicy.DeadSshTabsToClose(new[] { live }, "ssh", _ => true));
}

[Fact]
public void SSH06_NoLiveConnectionClosesDeadTabs()
{
    var dead = Tab("ssh", connected: false);
    Assert.True(SessionActivationPolicy.ShouldOpenNewSsh(new[] { dead }, "ssh", _ => false));
    Assert.Equal(new[] { dead }, SessionActivationPolicy.DeadSshTabsToClose(new[] { dead }, "ssh", _ => false));
}

[Fact]
public void SSH06_ConnectingTabIsNotTreatedAsLive()
{
    var connecting = Tab("ssh", connected: false);
    Assert.True(SessionActivationPolicy.ShouldOpenNewSsh(new[] { connecting }, "ssh", _ => false));
}

[Fact]
public void SSH06_ActivationPolicy()
{
    var live = Tab("ssh", connected: true);
    var dead = Tab("ssh", connected: false);
    Assert.True(SessionActivationPolicy.ShouldOpenNewSsh(new[] { live }, "ssh", _ => true));
    Assert.Empty(SessionActivationPolicy.DeadSshTabsToClose(new[] { live }, "ssh", _ => true));
    Assert.Equal(new[] { dead }, SessionActivationPolicy.DeadSshTabsToClose(new[] { dead }, "ssh", _ => false));
}

[Fact]
public void UI08_SettingsReplacesEditedTabOnly()
{
    var first = Tab("ssh", connected: true);
    var second = Tab("ssh", connected: true);
    Assert.Same(first, SettingsTabPolicy.TabToReplace(new[] { first, second }, first, "ssh", SessionKind.Ssh));
    Assert.Null(SettingsTabPolicy.TabToReplace(new[] { first, second }, second, "other", SessionKind.Ssh));
}

[Fact]
public void UI08_DetachedEditedTabIsNotReplaced()
{
    var other = Tab("ssh", connected: true);
    var edited = Tab("ssh", connected: true);
    Assert.Null(SettingsTabPolicy.TabToReplace(new[] { other }, edited, "ssh", SessionKind.Ssh));
}

[Fact]
public void UI05_CloseSelectedTabSelectsNeighbor()
{
    var vm = new MainViewModel();
    var a = Tab("a"); var b = Tab("b"); var c = Tab("c");
    vm.AddTab(a); vm.AddTab(b); vm.AddTab(c);
    vm.SelectedTab = b;
    vm.CloseTab(b);
    Assert.Same(c, vm.SelectedTab);
}

[Fact]
public void UI05_ClosingLastTabClearsSelection()
{
    var vm = new MainViewModel();
    var a = Tab("a");
    vm.AddTab(a);
    vm.CloseTab(a);
    Assert.Null(vm.SelectedTab);
}
```

The `Tab` helper creates a `TerminalTab` with a `Border` content and `IsTransportOpen` returning the supplied value.

- [ ] **Step 2: Run tests to verify they fail**

Run with `--filter "SessionActivationPolicyTests|SettingsTabPolicyTests|MainViewModelTests"`.
Expected: FAIL because the policy classes do not exist.

- [ ] **Step 3: Implement the policies and wire `MainWindow`**

`SessionActivationPolicy` is pure logic: filter by session ID and `isConnected`.
`SettingsTabPolicy` returns `editedTab` only when `kind` and `SessionId` match; otherwise `null`.
Replace the inline SSH list filtering and settings replacement conditions in `MainWindow.axaml.cs` with calls to these methods. Do not change what happens after the decision.

- [ ] **Step 4: Run all App tests**

Run:
```bash
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.App.Tests/COMTool.App.Tests.csproj -c Release
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add avalonia/src/COMTool.App/Services/SessionActivationPolicy.cs \
  avalonia/src/COMTool.App/Services/SettingsTabPolicy.cs \
  avalonia/src/COMTool.App/Views/MainWindow.axaml.cs \
  avalonia/tests/COMTool.App.Tests
git commit -m "Extract and test session activation policies"
```

---

## Task 8: Headless 侧栏和标签切换测试

**Files:**
- Create: `avalonia/tests/COMTool.App.Tests/InMemoryPty.cs`
- Create: `avalonia/tests/COMTool.App.Tests/TestTab.cs`
- Create: `avalonia/tests/COMTool.App.Tests/MainWindowHeadlessTests.cs`
- Modify: `avalonia/tests/COMTool.App.Tests/TestEnvironment.cs`

**Interfaces:**
- Consumes: `MainWindow`, `MainViewModel`, `TerminalTab`, `PtyStreamConnection`.
- Produces: `InMemoryPty` with `WriterText`, `Send(string)`, and an `IPtyConnection` exposing a readable/writable in-memory stream pair.

- [ ] **Step 1: Implement the in-memory PTY helper**

`InMemoryPty` must:
- implement `IPtyConnection`;
- expose `ReaderStream` and `WriterStream` backed by a duplex queue;
- record every byte written by the terminal;
- return `IsInputCompleted = false` until `Close()`;
- raise `ProcessExited` on `Close()`;
- ignore `Resize`.

Keep it in the test project; do not add a production test hook.

Add `TestTab.cs`:
```csharp
internal static class TestTab
{
    public static TerminalTab Create(
        Control content,
        string title = "test",
        string sessionId = "test",
        SessionKind kind = SessionKind.Local) =>
        new()
        {
            Title = title,
            SessionId = sessionId,
            Kind = kind,
            Content = content,
            IsTransportOpen = () => true,
            CloseAction = () => { },
        };
}
```

- [ ] **Step 2: Write failing sidebar test**

```csharp
[AvaloniaFact]
public void UI02_SidebarExpandsOnLaunch()
{
    using var env = new TestEnvironment();
    var config = new ComtoolConfig();
    config.SessionSidebar.Visible = false;
    config.SessionSidebar.Width = 240;
    env.SeedConfig(config);

    var window = new MainWindow { DataContext = new MainViewModel() };
    window.Show();
    Dispatcher.UIThread.RunJobs();

    var sidebar = window.FindControl<SessionSidebar>("Sidebar")!;
    Assert.True(sidebar.IsVisible);
    Assert.True(sidebar.Bounds.Width >= 200);
    window.Close();
}
```

Do not assert the private `_sidebarVisible` field.

- [ ] **Step 3: Write failing tab-switch tests**

Test `UI05_TabSwitchReparentOrder`:
- create two `TerminalControl` tabs with `Process = string.Empty`;
- select tab A, attach `InMemoryPty`;
- click tab B, attach `InMemoryPty`;
- click tab A;
- assert A is selected, A `IsFocused`, and `InMemoryPty` for A still accepts input;
- assert B's PTY was not closed by the switch.

Test `UI05_TabSwitchFocusesIncomingTerminal`:
- after clicking tab A, call `window.KeyTextInput("A")`;
- assert A's `WriterText` contains `"A"` and B's does not.

Test `UI05_RapidSwitchFocus`:
- click B then immediately click A before running dispatcher jobs;
- run jobs;
- assert A is selected and focused, and `"R"` typed afterward reaches A only.

Use the actual tab `Border` controls found by `Tag` and click their center with `window.MouseDown`/`MouseUp`; do not call private handlers.

- [ ] **Step 4: Run tests to verify they fail**

Run:
```bash
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.App.Tests/COMTool.App.Tests.csproj -c Release --filter "FullyQualifiedName~UI02|FullyQualifiedName~UI05"
```
Expected: FAIL until the helper and focus setup are correct. If the terminal library does not attach in Headless, keep the `InMemoryPty` liveness test and record that real focus is covered by the E2E task; do not mark UI-05 complete on Headless alone.

- [ ] **Step 5: Run all App tests**

Run:
```bash
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.App.Tests/COMTool.App.Tests.csproj -c Release
```
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add avalonia/tests/COMTool.App.Tests
git commit -m "Test tab reparenting and sidebar startup behavior"
```

---

## Task 9: Headless 双击最大化测试

**Files:**
- Create: `avalonia/tests/COMTool.App.Tests/TerminalMaximizeTests.cs`

**Interfaces:**
- Consumes: `MainWindow.ToggleMaximize`, terminal `DoubleTapped` handler.
- Produces: `TERM15_DoubleClickUsesMaximizePath` and `TERM15_DoubleClickDoesNotStealSelection`.

- [ ] **Step 1: Write failing tests**

```csharp
[AvaloniaFact]
public void TERM15_DoubleClickUsesMaximizePath()
{
    using var env = new TestEnvironment();
    env.SeedConfig(new ComtoolConfig());
    var window = new MainWindow { DataContext = new MainViewModel() };
    window.Show();
    Dispatcher.UIThread.RunJobs();

    var terminal = new TerminalControl { Process = string.Empty };
    var tab = TestTab.Create(terminal);
    var vm = (MainViewModel)window.DataContext!;
    vm.AddTab(tab);
    Dispatcher.UIThread.RunJobs();

    var before = window.WindowState;
    terminal.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, new PointerPressedEventArgs()));
    Assert.NotEqual(before, window.WindowState);
    Assert.Equal(WindowState.Maximized, window.WindowState);
    window.Close();
}
```

If `TappedEventArgs` cannot be constructed directly in Avalonia 12, use `window.MouseDown`/`MouseUp` twice with the platform's double-click interval, or the supported headless double-click API; keep the assertion that `WindowState` becomes `Maximized`.

Test `TERM15_DoubleClickDoesNotStealSelection`:
- set a terminal selection;
- raise double tap;
- assert the window still maximizes and the terminal remains focused.

- [ ] **Step 2: Run tests to verify they fail**

Run with `--filter FullyQualifiedName~TERM15`.
Expected: FAIL until the test can raise the event correctly.

- [ ] **Step 3: Add the smallest production seam if needed**

If raising `DoubleTapped` is not possible without a real pointer, add an internal method `internal void HandleTerminalDoubleTapForTest()` that calls `ToggleMaximize()` and have the event handler call it. Mark it `internal` and add `InternalsVisibleTo("COMTool.App.Tests")` in `COMTool.App.csproj`. Do not change the event handler's behavior.

- [ ] **Step 4: Run all App tests**

Run:
```bash
/tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.App.Tests/COMTool.App.Tests.csproj -c Release
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add avalonia/tests/COMTool.App.Tests avalonia/src/COMTool.App
git commit -m "Test terminal double-click maximize behavior"
```

---

## Task 10: Linux Xvfb E2E 骨架和启动烟测

**Files:**
- Create: `avalonia/tests/e2e/linux/lib.sh`
- Create: `avalonia/tests/e2e/linux/run-ui-e2e.sh`

**Interfaces:**
- Consumes: published Linux app path, local OpenSSH, Xvfb, xdotool.
- Produces: `PASS <TestId>` / `FAIL <TestId>: <reason>` output and exit code 0/1.
- Produces test IDs: `APP01_SmokeLaunchE2E`.

- [ ] **Step 1: Implement `lib.sh`**

Functions:
- `require_commands` checks `Xvfb`, `xdotool`, `openbox`, `sshd`, `ssh-keygen`, `lrzsz`/`rz`/`sz`, `nc`; on Linux it exits with the distro install command instead of skipping.
- `find_free_display` picks `:99` or the next free display.
- `start_xvfb` writes the PID and waits for `xdpyinfo`; `start_wm` launches `openbox --sm-disable` on that display so maximize/restore has a real window manager.
- `start_app <config-dir>` launches the app with `HOME`, `COMTOOL_AVALONIA_CONFIG_DIR`, `COMTOOL_PYTHON_CONFIG_DIR`, and `DISPLAY`.
- `wait_for_window <title>` uses `timeout 20 xdotool search --sync --name`; timeout is a failure, not a hang.
- `activate_window` uses `xdotool windowactivate --sync`.
- `click_window <x> <y>` uses `xdotool mousemove --window <id> <x> <y> click 1`.
- `type_text` uses `xdotool type --clearmodifiers --delay 20`.
- `press_enter` uses `xdotool key Return`.
- `wait_for_file <path> <timeout>` polls every 100 ms.
- `pass <id>` and `fail <id> <reason>`.
- `capture_failure` saves screenshot and `run.log`.
- `cleanup` kills app, sshd, openbox and Xvfb; only on success removes the temp root.

All functions use `set -euo pipefail` and quote paths.

- [ ] **Step 2: Add a smoke test**

`run-ui-e2e.sh`:
- parse `--app <path>`, `--keep`, `--filter <regex>`;
- create temp root;
- start Xvfb and `openbox`;
- seed an empty version 3 config;
- start the app;
- wait for the `COMTool v0.01` window;
- assert the process is alive;
- close the window by clicking the close button at `(<width>-17, 17)`;
- wait for the process to exit;
- print `PASS APP01_SmokeLaunchE2E`;
- cleanup.

- [ ] **Step 3: Run the smoke test**

Run:
```bash
bash -n avalonia/tests/e2e/linux/lib.sh
bash -n avalonia/tests/e2e/linux/run-ui-e2e.sh
DISPLAY=:99 bash avalonia/tests/e2e/linux/run-ui-e2e.sh \
  --app avalonia/publish/linux-x64/COMTool.App
```
Expected: `PASS APP01_SmokeLaunchE2E`, no orphan `COMTool.App`, `Xvfb`, `openbox` or `sshd` process.

- [ ] **Step 4: Commit**

```bash
git add avalonia/tests/e2e/linux
git commit -m "Add Linux Xvfb E2E harness"
```

---

## Task 11: E2E SSH fixture、会话点击和标签切换

**Files:**
- Modify: `avalonia/tests/e2e/linux/lib.sh`
- Modify: `avalonia/tests/e2e/linux/run-ui-e2e.sh`

**Interfaces:**
- Consumes: `lib.sh` helpers.
- Produces:
  - `UI02_SidebarExpandsOnLaunchE2E`
  - `E2E01_OpenSshSession`
  - `SSH06_MultipleConnectionsE2E`
  - `UI05_TabSwitchTypingBothDirections`

- [ ] **Step 1: Add the sshd fixture**

`lib.sh` must:
- generate an ephemeral ed25519 user key and host key;
- create `authorized_keys` with mode `0600`;
- write an `sshd_config` with a free port, `ListenAddress 127.0.0.1`, `PasswordAuthentication no`, `PermitRootLogin no`, `UsePAM no`, `StrictModes no`, `PidFile` under the temp root;
- start `sshd` with `sudo` if not already running on the chosen port;
- wait for the port with `nc -z` or a bash `/dev/tcp` loop;
- stop only the sshd PID it started.

- [ ] **Step 2: Seed one SSH session**

Write a helper `seed_ssh_config <config-dir> <port> <key-file>` that creates:

```json
{
  "version": 3,
  "skin": "light",
  "encoding": "UTF-8",
  "serialSessions": [],
  "sshSessions": [
    {
      "key": "<user>@127.0.0.1:<port>",
      "host": "127.0.0.1",
      "port": <port>,
      "user": "<user>",
      "alias": "e2e",
      "ssh_key": "<key-file>",
      "passwd_enc": ""
    }
  ],
  "sessionSidebar": { "visible": false, "width": 220 },
  "window": { "width": 1000, "height": 650 }
}
```

Use the current Unix user. Do not write a plaintext password.

- [ ] **Step 3: Add the session click test**

- start the app; the seeded config has `sessionSidebar.visible=false`, so a visible sidebar proves startup expansion;
- click the sidebar row at `(60, 116)` (calibrate once against the screenshot; keep the coordinates in `lib.sh` as named constants);
- wait for the first tab to appear; this proves the sidebar row is visible and clickable;
- print `PASS UI02_SidebarExpandsOnLaunchE2E`;
- type `printf E2E01_READY > <temp>/e2e01`;
- press Enter;
- wait for the marker file;
- print `PASS E2E01_OpenSshSession`.

- [ ] **Step 4: Add the multi-connection test**

- in the first tab, type `echo $$ > <temp>/shell-a.pid`; press Enter; wait for the file;
- click the same sidebar row again;
- wait for a second tab;
- type `echo $$ > <temp>/shell-b.pid`; press Enter; wait for the file;
- assert both files exist and their contents differ; this proves the second click opened an independent shell rather than reusing the first;
- print `PASS SSH06_MultipleConnectionsE2E`.

- [ ] **Step 5: Add the bidirectional tab-switch test**

- with two live tabs, set unique working directories:
  - click first tab, type `cd <temp>/dir-a`; press Enter;
  - click second tab, type `cd <temp>/dir-b`; press Enter;
- click first tab at `(80, 53)` and type `touch <temp>/dir-a/after-switch-1`; press Enter; wait for file;
- click second tab at `(190, 53)` and type `touch <temp>/dir-b/after-switch-2`; press Enter; wait for file;
- click first tab again and type `touch <temp>/dir-a/after-switch-3`; wait for file;
- assert each file is in the expected directory and no cross-directory file exists;
- do not click inside the terminal area after switching tabs;
- print `PASS UI05_TabSwitchTypingBothDirections`.

- [ ] **Step 6: Run the tests and calibrate coordinates**

Run:
```bash
DISPLAY=:99 bash avalonia/tests/e2e/linux/run-ui-e2e.sh \
  --app avalonia/publish/linux-x64/COMTool.App
```
Expected: `PASS UI02_SidebarExpandsOnLaunchE2E`, `PASS E2E01_OpenSshSession`, `PASS SSH06_MultipleConnectionsE2E`, `PASS UI05_TabSwitchTypingBothDirections`.

If a click lands on the wrong control, capture `screenshots/failure.png`, adjust only the named coordinate constants, and rerun. Do not replace the keyboard test with screenshot assertions.

- [ ] **Step 7: Commit**

```bash
git add avalonia/tests/e2e/linux
git commit -m "Add SSH multi-tab E2E regression"
```

---

## Task 12: E2E 双击最大化和失败清理

**Files:**
- Modify: `avalonia/tests/e2e/linux/run-ui-e2e.sh`
- Modify: `avalonia/tests/e2e/linux/lib.sh`

**Interfaces:**
- Produces: `TERM15_DoubleClickMaximizeE2E`; cleanup evidence on failure.

- [ ] **Step 1: Add maximize/restore test**

- record `xdotool getwindowgeometry --shell` before;
- double-click the terminal center `(600, 300)` with two click pairs 100 ms apart;
- wait 500 ms;
- assert width and height increased;
- double-click again;
- assert the original size is restored;
- print `PASS TERM15_DoubleClickMaximizeE2E`.

If the Xvfb session does not honor `WindowState=Maximized`, fix the `openbox` startup or geometry assertion; do not silently skip or downgrade this E2E.

- [ ] **Step 2: Add failure cleanup test**

Run the script with an intentionally invalid app path:
```bash
bash avalonia/tests/e2e/linux/run-ui-e2e.sh --app /nonexistent
```
Expected: non-zero exit, no orphan Xvfb/openbox/sshd/app, and a printed temp root path for diagnosis.

- [ ] **Step 3: Run the full E2E suite**

Run:
```bash
DISPLAY=:99 bash avalonia/tests/e2e/linux/run-ui-e2e.sh \
  --app avalonia/publish/linux-x64/COMTool.App
```
Expected: all E2E IDs PASS except any explicitly approved platform limitation.

- [ ] **Step 4: Commit**

```bash
git add avalonia/tests/e2e/linux
git commit -m "Add terminal maximize E2E and cleanup checks"
```

---

## Task 13: 统一 verify 脚本

**Files:**
- Create: `avalonia/pack/verify.sh`
- Create: `avalonia/pack/verify.ps1`

**Interfaces:**
- Consumes: `COMTool.Avalonia.sln`, `run-ui-e2e.sh`, `COMTool.TestGate`.
- Produces:
  - `verify.sh [--app <linux-app>] [--results <dir>]`
  - `verify.ps1 [-App <windows-app>] [-Results <dir>]`
  - exit 0 only when all automated checks pass.

- [ ] **Step 1: Implement `verify.sh`**

Order:
1. resolve repo root and `dotnet`;
2. create timestamped results dir;
3. `dotnet restore avalonia/COMTool.Avalonia.sln`;
4. `dotnet build -c Release --no-restore`;
5. `dotnet test -c Release --no-build --logger "trx;LogFileName=unit-tests.trx"` and a second run for `COMTool.App.Tests` with `app-tests.trx`
6. `dotnet test ... --list-tests --no-build > test-list.txt`;
7. on Linux, if `--app` is not supplied, use the just-built `avalonia/src/COMTool.App/bin/Release/net10.0/COMTool.App`; on other OS write an explicit `E2E not applicable on <os>` line;
8. on Linux run `run-ui-e2e.sh --app <app>`; on other OS write an explicit `E2E not applicable on <os>` line;
9. run `COMTool.TestGate --phase verify --platform <os> ...`;
10. write `summary.txt` and `publish/test-results/latest.txt` containing the results directory path;
11. exit non-zero on any failure.

- [ ] **Step 2: Implement `verify.ps1`**

Same order with PowerShell. Use `$LASTEXITCODE` checks after each native command. Do not use `Start-Process -Wait` without checking `ExitCode`.

- [ ] **Step 3: Run on Linux**

Run without `--app` so E2E uses the just-built binary, not a stale package:
```bash
bash avalonia/pack/verify.sh
```
Expected: all tests pass and `RESULT: PASS`; results directory contains TRX, E2E log and summary.

- [ ] **Step 4: Run syntax checks**

Run:
```bash
bash -n avalonia/pack/verify.sh
pwsh -NoProfile -Command '$null = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path "avalonia/pack/verify.ps1"), [ref]$null, [ref]$null)'
```
Expected: no syntax errors.

- [ ] **Step 5: Commit**

```bash
git add avalonia/pack/verify.sh avalonia/pack/verify.ps1
git commit -m "Add unified Avalonia verification gate"
```

---

## Task 14: 候选包、candidate.json 和候选包烟测

**Files:**
- Create: `avalonia/tools/COMTool.TestGate/CandidateInfo.cs`
- Modify: `avalonia/pack/windows/pack.ps1`
- Modify: `avalonia/pack/linux/pack.sh`
- Create: `avalonia/pack/promote.ps1`

**Interfaces:**
- Consumes: `verify.sh`/`verify.ps1`, published app.
- Produces:
  - `avalonia/publish/candidate/` only after verify and smoke pass;
  - `candidate.json` with `version`, `platform`, `sha256`, `builtAt`, `commit`, `resultsDir`, `tests`.
  - `PKG01_CandidateSmoke` log line.

- [ ] **Step 1: Change Windows pack script**

- add `-CandidateDir` defaulting to `../../publish/candidate`;
- call `verify.ps1` first with no skip parameter and read the results path from `publish/test-results/latest.txt`;
- publish to a unique staging directory under `publish/staging/` with `PublishReadyToRun=true` and `EnableCompressionInSingleFile=false`;
- start the staged `COMTool.App.exe` with `--demo`, wait for the window/process, close it via `CloseMainWindow()`, and fail if it exited with a non-zero code before close;
- write `PASS PKG01_CandidateSmoke` to `<results>/candidate-smoke.log`;
- create the zip and folder in a temporary `publish/staging/candidate-<id>/` directory;
- write `candidate.json` and `SHA256SUMS.txt` there;
- run `COMTool.TestGate --phase package --platform windows --tests <results>/test-list.txt --package <results>/candidate-smoke.log ...`;
- only after the package gate passes, atomically replace `publish/candidate/` with the staged directory;
- on any failure delete the staging directory and leave the previous `publish/candidate/` untouched.

Do not provide a `-SkipTests` switch.

- [ ] **Step 2: Change Linux pack script**

Same behavior with `COMTool.App`, using the same R2R/compression settings, `timeout` and a close click under Xvfb for smoke. If `DISPLAY` is unset, start its own Xvfb through `run-ui-e2e.sh` utilities or fail with a clear message; do not skip smoke silently. Run the same `--phase package` gate before committing the candidate.

- [ ] **Step 3: Add `candidate.json` model**

```csharp
public sealed record CandidateInfo(
    string Version,
    string Platform,
    string Sha256,
    string BuiltAt,
    string Commit,
    string ResultsDir,
    int Passed,
    int Failed,
    int Skipped);
```

Use `System.Text.Json` with camelCase property names.

- [ ] **Step 4: Test candidate metadata serialization**

Add `CI01_SerializesCandidateInfo` in `COMTool.TestGate.Tests`; assert `sha256`, `builtAt` and `commit` round-trip.

- [ ] **Step 5: Run the Linux pack script**

Run:
```bash
SKIP_PUBLISH=0 bash avalonia/pack/linux/pack.sh
```
Expected: `verify` passes, `PASS PKG01_CandidateSmoke`, candidate tar and `candidate.json` exist, no `publish/package` files changed.

- [ ] **Step 6: Commit**

```bash
git add avalonia/pack avalonia/tools/COMTool.TestGate docs/testing/signoffs
git commit -m "Build gated candidate packages"
```

---

## Task 15: 签收校验和正式包提升

**Files:**
- Create: `avalonia/tools/COMTool.TestGate/SignoffChecker.cs`
- Create: `avalonia/tests/COMTool.TestGate.Tests/SignoffCheckerTests.cs`
- Modify: `avalonia/pack/promote.ps1`
- Modify: `docs/testing/signoffs/README.md`

**Interfaces:**
- Consumes: `candidate.json`, signoff JSON, `manual-acceptance.md`, feature matrix.
- Produces:
  - `Signoff(string Product, string Version, string Platform, string CandidateSha256, string Tester, string Date, string WindowsVersion, IReadOnlyList<string> Hardware, IReadOnlyList<SignoffItem> Items, Soak Soak)`
  - `SignoffItem(string Id, string Result, string Evidence)`
  - `Soak(int Hours, string Result, string Evidence)`
  - `SignoffChecker.Validate(CandidateInfo candidate, Signoff signoff, IReadOnlySet<string> requiredManualIds) -> IReadOnlyList<string>`
  - CLI `--validate-signoff --candidate <json> --signoff <json> --manual <path> --matrix <path>`.

- [ ] **Step 1: Write failing signoff tests**

```csharp
[Fact]
public void SO01_RejectsShaMismatch()
{
    var errors = Validate(candidateSha: "aaaa", signoffSha: "bbbb", items: AllPass(), soak: Pass());
    Assert.Contains(errors, e => e.Contains("SHA256"));
}

[Fact]
public void SO02_RejectsMissingManualItem()
{
    var errors = Validate(items: new[] { Item("WIN-01") }, required: new[] { "WIN-01", "WIN-03" });
    Assert.Contains(errors, e => e.Contains("WIN-03"));
}

[Fact]
public void SO03_RejectsFailResult()
{
    var errors = Validate(items: new[] { Item("WIN-01", "FAIL") }, required: new[] { "WIN-01" });
    Assert.Contains(errors, e => e.Contains("FAIL"));
}

[Fact]
public void SO04_RejectsEmptyEvidence()
{
    var errors = Validate(items: new[] { Item("WIN-01", "PASS", "") });
    Assert.Contains(errors, e => e.Contains("evidence"));
}

[Fact]
public void SO05_RejectsShortSoak()
{
    var errors = Validate(soak: new Soak(4, "PASS", "x"));
    Assert.Contains(errors, e => e.Contains("8"));
}

[Fact]
public void SO06_AcceptsCompleteSignoff()
{
    var sha = new string('a', 64);
    Assert.Empty(Validate(candidateSha: sha, signoffSha: sha, items: AllPass(), soak: Pass()));
}

private static IReadOnlyList<string> Validate(
    string candidateSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    string signoffSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    IReadOnlyList<SignoffItem>? items = null,
    Soak? soak = null,
    IReadOnlyList<string>? required = null) =>
    SignoffChecker.Validate(
        new CandidateInfo("0.01", "windows-x64", candidateSha, "2026-10-20T00:00:00Z", "abc123", "/tmp/results", 100, 0, 0),
        new Signoff("COMTool-Avalonia", "0.01", "windows-x64", signoffSha, "tester", "2026-10-20", "Windows 11", Array.Empty<string>(), items ?? AllPass(), soak ?? Pass()),
        (required ?? new[] { "WIN-01" }).ToHashSet());
```

- [ ] **Step 2: Run tests to verify they fail**

Run with `--filter FullyQualifiedName~SignoffCheckerTests`.
Expected: FAIL.

- [ ] **Step 3: Implement `SignoffChecker`**

Validate:
- product equals `COMTool-Avalonia`;
- version equals candidate version;
- platform equals candidate platform;
- full SHA256 equality, case-insensitive;
- every required manual ID from the matrix exists exactly once;
- all item results are `PASS`;
- every evidence is non-empty and exists on disk or is a URL-like `docs/` path (record a warning if the file is missing; fail for empty);
- soak hours >= 8 and soak result `PASS`;
- no `FAIL`.

- [ ] **Step 4: Implement `promote.ps1`**

- take `-CandidateDir`, optional `-Signoff`, `-OutputDir` (default `publish/package`);
- read `candidate.json`;
- when `-Signoff` is omitted, look for exactly `docs/testing/signoffs/<os>-<version>-<sha12>.json` where `<os>` is `windows` or `linux` from the candidate platform prefix; fail if zero or multiple matches;
- call the C# signoff validator;
- copy the candidate zip/tar and checksum to a temp output;
- write `release.json` with candidate SHA256, release SHA256, signoff path, promotion time;
- atomically replace `publish/package` contents only after all checks pass;
- no `-Force` bypass.

- [ ] **Step 5: Test rejection with a real fixture**

Run:
```bash
pwsh -NoProfile -File avalonia/pack/promote.ps1 \
  -CandidateDir avalonia/publish/candidate \
  -Signoff /tmp/missing.json
```
Expected: non-zero exit, no files written to `publish/package`.

- [ ] **Step 6: Commit**

```bash
git add avalonia/tools/COMTool.TestGate avalonia/tests/COMTool.TestGate.Tests avalonia/pack/promote.ps1 docs/testing/signoffs
git commit -m "Require signed candidate before release promotion"
```

---

## Task 16: CI 使用统一门禁

**Files:**
- Modify: `.github/workflows/avalonia.yml`

**Interfaces:**
- Consumes: `verify.sh`, `verify.ps1`, `pack.ps1`, `pack.sh`.
- Produces: green verify matrix; candidate artifacts only.

- [ ] **Step 1: Replace build/test steps with verify**

For Ubuntu/Windows/macOS:
- checkout;
- setup-dotnet `10.0.x`;
- Linux only: install `xvfb xdotool openbox openssh-server lrzsz`;
- run `bash avalonia/pack/verify.sh` on Linux/macOS;
- run `pwsh -File avalonia/pack/verify.ps1` on Windows;
- upload `avalonia/publish/test-results/**` with `if: always()`.

- [ ] **Step 2: Make publish jobs depend on verify**

- `publish-windows` and `publish-linux` use `needs: verify`;
- call `pack.ps1` / `pack.sh`, which re-run their platform verify;
- upload only `publish/candidate/**`;
- do not create or upload `publish/package` in CI.

- [ ] **Step 3: Validate the workflow structure**

Run:
```bash
python3 - <<'PY'
import yaml, pathlib
data = yaml.safe_load(pathlib.Path('.github/workflows/avalonia.yml').read_text())
assert 'jobs' in data
print(sorted(data['jobs']))
PY
```
Expected: jobs include `verify`, `publish-windows`, `publish-linux`.

If PyYAML is unavailable, use `ruby -e 'require "yaml"; YAML.load_file(...)'` or inspect manually; do not skip syntax validation.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/avalonia.yml
git commit -m "Run packaging through the test gate"
```

---

## Task 17: 完成功能矩阵映射和缺口审计

**Files:**
- Modify: `docs/testing/feature-test-matrix.md`
- Modify: `docs/testing/manual-acceptance.md`
- Modify: `docs/parity-matrix.md`
- Modify: `docs/fixtures/phase1/README.md`
- Modify: `docs/superpowers/plans/2026-10-19-comtool-avalonia-phase1-progress.md`
- Add tests under `avalonia/tests/**` as gaps are found.

**Interfaces:**
- Consumes: all existing test names, E2E IDs, manual IDs.
- Produces: a matrix that passes `TestGate --phase verify` and `--phase package`.

- [ ] **Step 1: Map existing tests by section**

For each parity section, fill the matrix:

- UI: `MainWindowHeadlessTests`, `TerminalMaximizeTests`, `AppSmokeTests`, E2E IDs; UI-14 uses `WIN-02`, UI-11 uses `WIN-10`; UI-10 and unimplemented plugin/help parts stay `未开始`/`部分`.
- TERM: existing Terminal and ZMODEM tests; add `TERM15` tests.
- SER: `SerialCatalogTests`, `SerialPortCatalogTests`, `SerialPtyIntegrationTests`; Windows-only items use `WIN-*`.
- SSH: `SshIntegrationTests`, policy tests, E2E IDs; DPAPI uses `WIN-06`.
- NET/PLG: leave `未开始`; no test requirement.
- ZM: existing ZMODEM tests plus real lrzsz tests.
- CFG: `ConfigTests`, `SessionStoreTests`, `ConfigPathsTests`.
- PKG: pack smoke, CI packaging, manual `WIN-08`.
- TST: this gate, E2E, manual `WIN-09`.

- [ ] **Step 2: Downgrade unsupported “完成” claims**

For any `完成` row with no automated test after the audit:
- change it to `部分`;
- add the correct `WIN-*` or new `MAN-*` manual ID;
- write the specific reason in `Evidence`;
- update `docs/parity-matrix.md` to the same status.

Do not leave a manual-only feature marked `完成`.

- [ ] **Step 3: Add missing automated tests only where feasible**

Prioritize:
1. UI-05 tab switching/focus (already in Tasks 8-11).
2. SSH-06 multi-connection (Tasks 7, 11).
3. TERM-15 double-click (Tasks 9, 12).
4. UI-02 sidebar startup (Tasks 8, 11).
5. UI-08 settings replacement (Task 7).

Do not invent hardware automation. Convert those to manual IDs.

- [ ] **Step 4: Run TestGate verify phase**

Run:
```bash
/tmp/opencode/dotnet/dotnet run --project avalonia/tools/COMTool.TestGate -- \
  --phase verify --platform linux \
  --parity docs/parity-matrix.md \
  --matrix docs/testing/feature-test-matrix.md \
  --manual docs/testing/manual-acceptance.md \
  --tests avalonia/publish/test-results/latest/test-list.txt \
  --e2e avalonia/publish/test-results/latest/e2e.log
```
Expected: `RESULT: PASS`; no completed feature lacks automated coverage.

- [ ] **Step 5: Run TestGate package phase**

Run with the candidate smoke log:
```bash
/tmp/opencode/dotnet/dotnet run --project avalonia/tools/COMTool.TestGate -- \
  --phase package --platform linux \
  --parity docs/parity-matrix.md \
  --matrix docs/testing/feature-test-matrix.md \
  --manual docs/testing/manual-acceptance.md \
  --tests avalonia/publish/test-results/latest/test-list.txt \
  --package avalonia/publish/test-results/latest/candidate-smoke.log
```
Expected: `RESULT: PASS`.

- [ ] **Step 6: Commit**

```bash
git add docs avalonia/tests
git commit -m "Map completed features to automated and manual tests"
```

---

## Task 18: 完整验证、候选包和人工签收交接

**Files:**
- Modify: `docs/packaging/avalonia-windows-beta.md`
- Modify: `docs/packaging/avalonia-linux-beta.md`
- Modify: `docs/fixtures/phase1/README.md`
- Modify: `docs/superpowers/plans/2026-10-19-comtool-avalonia-phase1-progress.md`

**Interfaces:**
- Consumes: all tasks above.
- Produces: Linux/Widows candidates, test evidence, and a request for user signoff. No formal package until signoff.

- [ ] **Step 1: Run the full Linux gate**

Run without `--app` so E2E uses the just-built binary:
```bash
bash avalonia/pack/verify.sh
```
Expected: all unit/integration/Headless/E2E tests pass; `RESULT: PASS`.

- [ ] **Step 2: Build the Linux candidate**

Run:
```bash
bash avalonia/pack/linux/pack.sh
```
Expected: candidate tar, candidate folder, `candidate.json`, `SHA256SUMS.txt`; `publish/package` unchanged.

- [ ] **Step 3: Build the Windows candidate over SSH**

Use the existing Windows SSH path without RDP:
```bash
DISPLAY=:99 SSH_ASKPASS=/tmp/opencode/askpass-win.sh setsid -w \
  ssh zhangrui@172.16.66.163 \
  'cd C:\Users\zhangrui\COMTool && powershell -NoProfile -ExecutionPolicy Bypass -File avalonia\pack\windows\pack.ps1'
```
Expected: verify passes, candidate zip/folder and `candidate.json` exist on the Windows host.

If the Windows host path differs, discover it first with `ssh ... 'cd ... && pwd'`; do not guess or overwrite the stable app.

- [ ] **Step 4: Copy the candidate to the user's test paths**

Only after the candidate is generated:
```bash
# Example; use the actual candidate path returned by the Windows build.
scp zhangrui@172.16.66.163:'C:/Users/zhangrui/COMTool/avalonia/publish/candidate/<zip>' /tmp/
```
Record the candidate SHA256. Do not overwrite `COMTool-Avalonia.exe` yet; the candidate is for testing, not release.

- [ ] **Step 5: Update packaging docs and progress log**

Record:
- candidate SHA256 and size;
- test counts;
- E2E test IDs and results;
- known manual items still pending;
- exact Windows candidate path;
- confirmation that `publish/package` is unchanged.

- [ ] **Step 6: Commit the validation record**

```bash
git add docs
git commit -m "Record test gate candidate validation"
```

- [ ] **Step 7: Stop and request user signoff**

Ask the user to run the Windows candidate and complete `WIN-01` through `WIN-10`. Do not create the formal package, do not switch the default app, and do not push. After the user provides a signoff JSON with the correct candidate SHA256, run `promote.ps1` and then update `release.json` and packaging docs. The remote CI run is also pending push approval. The 2-week Beta observation, default-version switch and rollback drill remain separate release gates and are not claimed by this plan.

---

## Self-Review Results

- [x] Spec coverage: goals/non-goals and constraints (Global Constraints), matrix (Tasks 1-4), checker (Tasks 2-3), Headless (Tasks 6-9), config isolation (Task 5), E2E (Tasks 10-12), verify/pack (Tasks 13-14), candidate/release (Tasks 14-15), CI (Task 16), coverage audit (Task 17), validation/signoff (Task 18).
- [x] Step scan: each task has failing-test, pass-test, verification and commit steps; no task asks the implementer to invent behavior without an interface or assertion.
- [x] Type consistency: `FeatureRow`, `GateOptions`, `GateResult`, `TestEnvironment`, `SessionActivationPolicy`, `SettingsTabPolicy`, `CandidateInfo`, `Signoff`, `SignoffItem`, `Soak`, `SignoffChecker` are introduced before later use.
- [x] Review Focus: rapid switch (Task 8), double-click selection (Task 9), SSH connecting state (Task 7), stale/mismatched signoff (Task 15), config override edges (Task 5).
- [x] Proportion: code blocks are 43% of the document and are limited to tests, interfaces and fixed copy; task bodies remain implementation instructions rather than full program transcripts.
- [x] No task requires Python changes, plaintext passwords, RDP, or a formal package before signoff. Unimplemented P1 work remains explicitly marked.
