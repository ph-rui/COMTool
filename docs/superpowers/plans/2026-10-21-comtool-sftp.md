# COMTool Avalonia SFTP Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an embedded SFTP file browser to Avalonia SSH session tabs: a bottom drawer with a remote pane and a local pane, button-driven uploads and downloads, remote file management, and a conflict policy that asks the user on every collision.

**Architecture:** A new dependency-free kernel project `COMTool.Sftp` owns the file-system abstractions (`ISftpFileSystem`, `ILocalFileSystem`), the transfer engine and the conflict policy. `COMTool.Connections` implements `ISftpFileSystem` over SSH.NET's `SftpClient` and exposes it from `SshConnection.CreateSftpFileSystem()`, which authenticates a second SSH connection with the same `ConnectionInfo` (SSH.NET cannot share a channel with the shell). `COMTool.App` adds `SftpPaneViewModel`, `SftpPane.axaml` and `SshSessionView.axaml`; the SSH tab content becomes `SshSessionView`, which stacks the terminal over the drawer.

**Tech Stack:** .NET 10, C# latest, Avalonia 12.1.4 (headless testable), CommunityToolkit.Mvvm 8.4.2, SSH.NET 2026.0.0, xunit 2.9.3, bash + Xvfb + xdotool for E2E.

**Spec:** `docs/superpowers/specs/2026-10-21-comtool-sftp-design.md` — read it before starting; this plan implements it and does not restate it.

> **As built, 2026-10-22.** The plan below was executed in full. Afterwards the user asked for the MobaXterm arrangement, in two passes. First the panel became a **left vertical panel that starts folded away**, width 420 (320..640) replacing the drawer height, with the SFTP subsystem requested the first time the panel is opened instead of when the tab is created. Then the user asked to "完全模仿 MobaXterm 的风格": the panel is now a **single remote list** (no local pane at all), with an **icon-only toolbar whose hover tooltips are Chinese** (返回上级目录 / 刷新当前目录 / 新建文件夹 / 重命名 / 删除 / 修改权限 / 上传到当前文件夹 / 下载到本地 / 跟随符号链接 / 显示隐藏文件), **Chinese sortable column headers** (名称 / 大小 (KB) / 最后修改 / 所有权限, directories always first), an **inline prompt row** for new-folder/rename, a **two-step red-icon delete confirm**, and **picker-driven transfers** (system file dialog for upload, folder dialog for download, behind `ILocalPickers`). The e2e answers those pickers from `COMTOOL_E2E_PICK_FILES` / `COMTOOL_E2E_PICK_FOLDER`, and its path-box click waits for the focus ring before typing, because a click followed immediately by keystrokes can race the app's focus change while the panel's initial load is busy and the path leaks into the terminal. The live documents (`docs/parity-matrix.md`, `docs/testing/feature-test-matrix.md`, the design spec) describe the shipped behaviour; the task texts below still speak of a bottom drawer with two panes.

## Global Constraints

- Product version stays `0.01`; the Windows executable stays `COMTool-Avalonia.exe`; the R2R publish command in `docs/packaging/avalonia-windows-beta.md` is unchanged.
- The Avalonia app and the Python app keep separate configs. `config.json` is never shared, and only the DPAPI-encrypted `passwd_enc` is persisted.
- The SFTP panel persists nothing. No new configuration key, no new session field, nothing written to disk.
- The new-session menu order stays `Serial`, `SSH`, `Telnet`, `TCP`, `Local terminal`. The SFTP browser is not a new session type.
- The Python application is not modified by this plan.
- Every feature gets an automated test. A UI behaviour is never proven by a screenshot or a manual click alone.
- The unified gate (`avalonia/pack/verify.sh`) must pass before anything is packaged or deployed. The gate is never bypassed.
- Nothing is pushed or published without an explicit user confirmation.
- On Windows the executable is overwritten in place. Never create a `COMTool-Avalonia.exe.bak-*` file.
- Dependency direction: `COMTool.App` -> `COMTool.Connections`, `COMTool.Sftp`; `COMTool.Connections` -> `COMTool.Sftp`; `COMTool.Sftp` -> nothing (no Avalonia, no SSH.NET).
- The remote pane is on the **left**, the local pane on the **right**.
- Destructive operations (`mkdir`, `rename`, `delete`, `chmod`) act on the **remote** pane only. The local pane is upload source and download target.
- The drawer is expanded by default. The collapsed state and the drawer height are per session and live in memory only.
- SSH.NET stays at `2026.0.0`. Do not upgrade it in this phase.
- The `SshConnection` owns the SFTP client and disposes it. The panel never disposes the remote file system.
- Verified platform facts (probed against OpenSSH + SSH.NET 2026.0.0 before this plan): `SftpClient.WorkingDirectory` is the login home directory; `ListDirectory` returns absolute `FullName` values and reports symlinks with `IsSymbolicLink = true`; `ListDirectory` on a symlink-to-directory follows the link and returns entries under the **resolved** path, so logical paths must be rebuilt from the requested path; `GetAttributes` follows symlinks; `ChangePermissions(path, octal)` matches the server's `stat -c %a` exactly; a server without an `sftp` subsystem fails with `SshException: Subsystem 'sftp' could not be executed.`

## Review Focus

- A remote path that disappears between listing and transfer. Expected: that file is reported as failed, the rest of the queue still runs, the summary shows the failure.
- A file name with a space, a non-ASCII character or a leading dash. Expected: the name is preserved byte-for-byte, because no shell quoting is involved anywhere in the SFTP path.
- The target directory already exists on the other side. Expected: the transfer merges into it instead of failing.
- The user closes the tab while a transfer is running. Expected: the transfer is cancelled, the partially written file is removed, and nothing escapes as an unhandled exception.
- A zero-byte remote file. Expected: it transfers as a valid empty file and the progress total stays correct.
- One file in the middle of a directory tree is not permitted by the server. Expected: the other files still transfer and the summary reports exactly one failure.

---

### Task 1: SFTP kernel project, path and permission primitives

**Files:**
- Create: `avalonia/src/COMTool.Sftp/COMTool.Sftp.csproj`
- Create: `avalonia/src/COMTool.Sftp/SftpPath.cs`
- Create: `avalonia/src/COMTool.Sftp/SftpPermission.cs`
- Create: `avalonia/tests/COMTool.Sftp.Tests/COMTool.Sftp.Tests.csproj`
- Create: `avalonia/tests/COMTool.Sftp.Tests/SftpPathTests.cs`
- Create: `avalonia/tests/COMTool.Sftp.Tests/SftpPermissionTests.cs`
- Modify: `avalonia/COMTool.Avalonia.sln` (two new projects)
- Modify: `avalonia/pack/verify.sh` (new test project in `unit_projects`)

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `namespace COMTool.Sftp` -> `public static class SftpPath` with `const char Separator = '/'`, `const string Root = "/"`, `static string Combine(string basePath, string name)`, `static string GetParent(string path)`, `static string GetFileName(string path)`, `static bool IsRoot(string path)`, `static string Normalize(string path)`.
  - `namespace COMTool.Sftp` -> `public readonly record struct SftpPermission(bool OwnerRead, bool OwnerWrite, bool OwnerExecute, bool GroupRead, bool GroupWrite, bool GroupExecute, bool OthersRead, bool OthersWrite, bool OthersExecute)` with `static SftpPermission FromOctal(int mode)`, `int ToOctal()`, `string ToRwxString()`, `static SftpPermission FromRwxString(string text)`, `static SftpPermission FromOctalText(string octalText)`, `string ToOctalText()`.

- [ ] **Step 1: Create the kernel project and the test project**

`avalonia/src/COMTool.Sftp/COMTool.Sftp.csproj` (no package references, no Avalonia, no SSH.NET):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

`avalonia/tests/COMTool.Sftp.Tests/COMTool.Sftp.Tests.csproj` — copy the shape of `avalonia/tests/COMTool.Core.Tests/COMTool.Core.Tests.csproj` (`coverlet.collector` 6.0.4, `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4, `<Using Include="Xunit" />`) and reference `..\..\src\COMTool.Sftp\COMTool.Sftp.csproj`.

- [ ] **Step 2: Register both projects in the solution and in the gate**

```bash
cd /tmp/opencode/COMTool/avalonia
dotnet sln COMTool.Avalonia.sln add src/COMTool.Sftp/COMTool.Sftp.csproj tests/COMTool.Sftp.Tests/COMTool.Sftp.Tests.csproj
```

Then add to the `unit_projects` array in `avalonia/pack/verify.sh`, directly after the `COMTool.Core.Tests` entry:

```bash
    "COMTool.Sftp.Tests:$repo_root/avalonia/tests/COMTool.Sftp.Tests/COMTool.Sftp.Tests.csproj"
```

- [ ] **Step 3: Write the failing path tests**

`avalonia/tests/COMTool.Sftp.Tests/SftpPathTests.cs`, class `SftpPathTests`, namespace `COMTool.Sftp.Tests`:

```csharp
[Fact]
public void CombineJoinsWithASingleSeparator()
{
    Assert.Equal("/a/b/c", SftpPath.Combine("/a/b", "c"));
    Assert.Equal("/c", SftpPath.Combine("/", "c"));
    Assert.Equal("/a/b/c", SftpPath.Combine("/a/b/", "c"));
}

[Fact]
public void GetParentWalksUpAndStopsAtRoot()
{
    Assert.Equal("/a", SftpPath.GetParent("/a/b"));
    Assert.Equal("/", SftpPath.GetParent("/a"));
    Assert.Equal("/", SftpPath.GetParent("/"));
}

[Fact]
public void GetFileNameReturnsTheLastSegment()
{
    Assert.Equal("b", SftpPath.GetFileName("/a/b"));
    Assert.Equal(string.Empty, SftpPath.GetFileName("/"));
}

[Fact]
public void NormalizeCollapsesAndResolvesDotSegments()
{
    Assert.Equal("/a/c", SftpPath.Normalize("/a/./b/../c"));
    Assert.Equal("/a/b", SftpPath.Normalize("//a///b"));
    Assert.Equal("/", SftpPath.Normalize("/"));
}

[Fact]
public void NormalizeCannotEscapeTheRoot()
{
    Assert.Equal("/x", SftpPath.Normalize("/../x"));
    Assert.Equal("/", SftpPath.Normalize("/a/../.."));
}

[Fact]
public void IsRootOnlyMatchesTheRootPath()
{
    Assert.True(SftpPath.IsRoot("/"));
    Assert.False(SftpPath.IsRoot("/a"));
}
```

- [ ] **Step 4: Run the path tests to verify they fail**

Run: `DOTNET_ROOT=/tmp/opencode/dotnet /tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.Sftp.Tests/COMTool.Sftp.Tests.csproj -c Release --filter "FullyQualifiedName~SftpPathTests"`

Expected: build failure with `CS0103: The name 'SftpPath' does not exist`.

- [ ] **Step 5: Implement `SftpPath`**

`avalonia/src/COMTool.Sftp/SftpPath.cs`. `Normalize` splits on `/`, drops empty and `.` segments, pops the previous segment on `..` but never below the root, and re-joins with a leading `/` (`"/"` when nothing remains). `Combine` trims a trailing `/` from the base (except for the root) and appends `/` + name. `GetParent` returns `/` for a single-segment path. `GetFileName` returns the text after the last `/`, empty for `/`.

- [ ] **Step 6: Run the path tests to verify they pass**

Run: same command as Step 4. Expected: 6 tests pass.

- [ ] **Step 7: Write the failing permission tests**

`avalonia/tests/COMTool.Sftp.Tests/SftpPermissionTests.cs`, class `SftpPermissionTests`:

```csharp
[Fact]
public void OctalRoundTrips()
{
    foreach (var digits in new[] { "000", "644", "755", "600", "777" })
    {
        var mode = Mode(digits);
        Assert.Equal(mode, SftpPermission.FromOctal(mode).ToOctal());
    }
}

[Fact]
public void RwxStringMatchesTheOctalMode()
{
    Assert.Equal("rw-r--r--", SftpPermission.FromOctal(Mode("644")).ToRwxString());
    Assert.Equal("rwxr-xr-x", SftpPermission.FromOctal(Mode("755")).ToRwxString());
}

[Fact]
public void ExecuteIsIndependentOfRead()
{
    // 0111 is execute only: the read bit is not implied. The three
    // characters of a group are always read, write, execute.
    Assert.Equal("--x--x--x", SftpPermission.FromOctal(Mode("111")).ToRwxString());
    Assert.Equal(Mode("111"), SftpPermission.FromOctal(Mode("111")).ToOctal());
    Assert.True(SftpPermission.FromOctal(Mode("111")).OwnerExecute);
    Assert.False(SftpPermission.FromOctal(Mode("111")).OwnerRead);
}

[Fact]
public void RwxStringRoundTrips()
{
    Assert.Equal(Mode("644"), SftpPermission.FromRwxString("rw-r--r--").ToOctal());
    Assert.Equal(Mode("111"), SftpPermission.FromRwxString("--x--x--x").ToOctal());
}

[Fact]
public void OctalTextRoundTrips()
{
    Assert.Equal("644", SftpPermission.FromOctal(Mode("644")).ToOctalText());
    Assert.Equal("000", SftpPermission.FromOctal(Mode("000")).ToOctalText());
    Assert.Equal(Mode("640"), SftpPermission.FromOctalText("640").ToOctal());
    Assert.Equal("640", SftpPermission.FromOctalText("640").ToOctalText());
}
```

C# has no octal integer literals, and a leading `0` does not make one: `0644` is the decimal number 644. Write the modes the way they appear on the shell and convert them, either with a test helper

```csharp
private static int Mode(string octalDigits) => Convert.ToInt32(octalDigits, 8);
```

or with the `SftpPermission.FromOctalText` / `ToOctalText` helpers.

- [ ] **Step 8: Run the permission tests to verify they fail**

Run: `DOTNET_ROOT=/tmp/opencode/dotnet /tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.Sftp.Tests/COMTool.Sftp.Tests.csproj -c Release --filter "FullyQualifiedName~SftpPermissionTests"`

Expected: build failure with `CS0103: The name 'SftpPermission' does not exist`.

- [ ] **Step 9: Implement `SftpPermission`**

`avalonia/src/COMTool.Sftp/SftpPermission.cs`. `ToOctal` returns `Owner* << 6 | Group* << 3 | Others*` where each group is `read(4) + write(2) + execute(1)`. `FromOctal` masks the mode with `0o777` and extracts the same bits. `ToRwxString` emits three characters per group in the order read, write, execute, using `-` for a clear bit, so an execute-only group is `--x`. `FromRwxString` parses the same three-by-three layout and ignores any character other than `r`, `w` and `x`. `FromOctalText` parses an octal mode written as digits and `ToOctalText` renders the mode as three octal digits; they exist because C# has no octal literals.

- [ ] **Step 10: Run all kernel tests to verify they pass**

Run: `DOTNET_ROOT=/tmp/opencode/dotnet /tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.Sftp.Tests/COMTool.Sftp.Tests.csproj -c Release`

Expected: 11 tests pass.

- [ ] **Step 11: Commit**

```bash
cd /tmp/opencode/COMTool
git add avalonia/src/COMTool.Sftp avalonia/tests/COMTool.Sftp.Tests avalonia/COMTool.Avalonia.sln avalonia/pack/verify.sh docs/superpowers/plans/2026-10-21-comtool-sftp.md
git commit -m "feat(avalonia): add the SFTP kernel project with path and permission primitives"
```

---

### Task 2: File system abstractions and the local implementation

**Files:**
- Create: `avalonia/src/COMTool.Sftp/SftpEntry.cs`
- Create: `avalonia/src/COMTool.Sftp/ISftpFileSystem.cs`
- Create: `avalonia/src/COMTool.Sftp/ILocalFileSystem.cs`
- Create: `avalonia/src/COMTool.Sftp/SystemLocalFileSystem.cs`
- Create: `avalonia/tests/COMTool.Sftp.Tests/SystemLocalFileSystemTests.cs`

**Interfaces:**
- Consumes: `SftpEntry`, `SftpPermission` from Task 1.
- Produces:
  - `public sealed record SftpEntry(string Name, string Path, bool IsDirectory, bool IsSymbolicLink, long Size, DateTime LastWriteTimeUtc, SftpPermission Permission)`.
  - `public interface IFileSystem : IAsyncDisposable` with `string HomeDirectory { get; }`, `Task<IReadOnlyList<SftpEntry>> ListAsync(string path, CancellationToken cancellationToken)`, `Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)`, `Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken)`, `Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)`, `Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken)`, `Task DeleteFileAsync(string path, CancellationToken cancellationToken)`, `Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken)`, `Task SetPermissionsAsync(string path, int octalMode, CancellationToken cancellationToken)`, `Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)`. The two side interfaces derive from it, so the transfer engine can walk both with the same code.
  - `public interface ISftpFileSystem : IFileSystem`, the remote side.
  - `public interface ILocalFileSystem : IFileSystem`, the local side.
  - `public sealed class SystemLocalFileSystem : ILocalFileSystem` with a parameterless constructor; `HomeDirectory` returns `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)`.

- [ ] **Step 1: Write the failing local file system tests**

`avalonia/tests/COMTool.Sftp.Tests/SystemLocalFileSystemTests.cs`, class `SystemLocalFileSystemTests`. Each test creates a unique directory under `Path.GetTempPath()` and deletes it in a `finally` block.

```csharp
[Fact]
public async Task ListsFilesAndDirectoriesWithTheirSize()
{
    var root = NewRoot();
    try
    {
        await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        var fs = new SystemLocalFileSystem();
        var entries = await fs.ListAsync(root, CancellationToken.None);
        var file = entries.Single(entry => entry.Name == "a.txt");
        Assert.False(file.IsDirectory);
        Assert.Equal(5, file.Size);
        Assert.Equal(Path.Combine(root, "a.txt"), file.Path);
        Assert.True(entries.Single(entry => entry.Name == "sub").IsDirectory);
    }
    finally { Directory.Delete(root, recursive: true); }
}

[Fact]
public async Task CreatesRenamesAndDeletes()
{
    // CreateDirectoryAsync, RenameAsync, DeleteFileAsync, DeleteDirectoryAsync
    // (recursive) and ExistsAsync behave as their names promise.
}

[Fact]
public async Task RoundTripsFileContent()
{
    // OpenWriteAsync truncates an existing file, OpenReadAsync returns the
    // bytes that were written.
}

[Fact]
public async Task PermissionMapsToTheReadOnlyAttribute()
{
    // SetPermissionsAsync(path, 0o444) makes the file read only;
    // SetPermissionsAsync(path, 0o644) makes it writable again.
}
```

Each body must assert exactly the values named in its comment and nothing weaker: the file size, the entry path, `ExistsAsync` before and after each mutation, the read-back bytes, and `File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly)` after each `SetPermissionsAsync` call.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `DOTNET_ROOT=/tmp/opencode/dotnet /tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.Sftp.Tests/COMTool.Sftp.Tests.csproj -c Release --filter "FullyQualifiedName~SystemLocalFileSystemTests"`

Expected: build failure with `CS0103: The name 'SystemLocalFileSystem' does not exist`.

- [ ] **Step 3: Implement the abstractions**

`avalonia/src/COMTool.Sftp/SftpEntry.cs`, `IFileSystem.cs`, `ISftpFileSystem.cs`, `ILocalFileSystem.cs` with exactly the members listed in the Interfaces block. `ILocalFileSystem` is a separate interface with the same member list, so a test double can implement either side independently and the App can depend on the interface instead of `System.IO`. Both sides derive from `IFileSystem`, which the transfer engine uses to walk the remote and the local side with the same code.

- [ ] **Step 4: Implement `SystemLocalFileSystem`**

`avalonia/src/COMTool.Sftp/SystemLocalFileSystem.cs`.

- `ListAsync` uses `new DirectoryInfo(path).EnumerateFileSystemInfos()`, maps each item to an `SftpEntry` with `Name = item.Name`, `Path = item.FullName`, `IsDirectory = item is DirectoryInfo`, `Size = file.Length` for files and `0` for directories, `LastWriteTimeUtc = item.LastWriteTimeUtc`, and a permission derived from the read-only attribute: `SftpPermission.FromOctalText(readOnly ? "444" : "644")`.
- `OpenReadAsync` returns `new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)`.
- `OpenWriteAsync` returns `new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)` — `FileMode.Create` truncates an existing file, which is what the overwrite policy needs.
- `CreateDirectoryAsync` uses `Directory.CreateDirectory` (an existing directory is reused, not an error).
- `RenameAsync` uses `File.Move` for files and `Directory.Move` for directories.
- `DeleteFileAsync` uses `File.Delete`; a missing file is not an error.
- `DeleteDirectoryAsync(path, recursive)` uses `Directory.Delete(path, recursive)`.
- `SetPermissionsAsync(path, octalMode)` sets or clears `FileAttributes.ReadOnly` from the owner-write bit of the octal mode; other bits are ignored because Windows has no POSIX mode.
- `ExistsAsync` returns `File.Exists(path) || Directory.Exists(path)`.
- `DisposeAsync` returns `default` and touches nothing.

- [ ] **Step 5: Run the tests to verify they pass**

Run: same command as Step 2. Expected: 4 tests pass.

- [ ] **Step 6: Commit**

```bash
cd /tmp/opencode/COMTool
git add avalonia/src/COMTool.Sftp avalonia/tests/COMTool.Sftp.Tests
git commit -m "feat(avalonia): add the SFTP file system abstractions and the local implementation"
```

---

### Task 3: Transfer model, conflict naming and the in-memory test double

**Files:**
- Create: `avalonia/src/COMTool.Sftp/SftpConflict.cs`
- Create: `avalonia/src/COMTool.Sftp/SftpTransfer.cs`
- Create: `avalonia/src/COMTool.Sftp/Testing/InMemoryFileSystem.cs`
- Create: `avalonia/tests/COMTool.Sftp.Tests/SftpConflictNamingTests.cs`
- Create: `avalonia/tests/COMTool.Sftp.Tests/InMemoryFileSystemTests.cs`

**Interfaces:**
- Consumes: `ISftpFileSystem`, `ILocalFileSystem`, `SftpEntry` from Task 2.
- Produces:
  - `public enum SftpConflictAction { Overwrite, Skip, Rename, Cancel }`.
  - `public sealed record SftpConflictDecision(SftpConflictAction Action, string? NewName = null)`.
  - `public sealed record SftpConflictContext(string SourcePath, string TargetPath, long TargetSize, DateTime TargetLastWriteUtc, string SuggestedRename)`.
  - `public static class SftpConflictNaming` with `static string Suggest(string fileName, int index)` -> `"a.txt"` + index 1 -> `"a (1).txt"`, `"a"` -> `"a (1)"`, `"a.tar.gz"` -> `"a.tar (1).gz"`.
  - `public enum SftpTransferDirection { Upload, Download }`.
  - `public enum SftpTransferStage { Scanning, Transferring, Completed, Cancelled, Failed }`.
  - `public sealed record SftpTransferOptions(bool FollowSymbolicLinks = true, int MaxDepth = 16, int MaxFiles = 100_000, Func<SftpConflictContext, Task<SftpConflictDecision>>? ConflictResolver = null)`.
  - `public sealed record SftpTransferRequest(SftpTransferDirection Direction, IReadOnlyList<string> SourcePaths, string TargetDirectory, SftpTransferOptions Options)`.
  - `public sealed record SftpTransferProgress(SftpTransferStage Stage, string FileName, int CompletedFiles, int TotalFiles, long CompletedBytes, long TotalBytes)`.
  - `public sealed record SftpTransferFailure(string Path, string Reason)`.
  - `public sealed record SftpTransferResult(int Succeeded, int Skipped, int Failed, bool Cancelled, long BytesTransferred, IReadOnlyList<SftpTransferFailure> Failures, IReadOnlyList<string> SkippedPaths)`.
  - `namespace COMTool.Sftp.Testing` -> `public sealed class InMemoryFileSystem : ISftpFileSystem, ILocalFileSystem` with `public InMemoryFileSystem(string homeDirectory)`, `public string HomeDirectory { get; }`, the ten interface members, and helpers `public void AddFile(string path, string content)`, `public void AddDirectory(string path)`, `public void AddSymlink(string path, string targetPath)`, `public string? ReadFile(string path)`, `public bool HasEntry(string path)`. The double lives in the shipping assembly under the `COMTool.Sftp.Testing` namespace so the kernel tests and the headless app tests share one implementation. It references no test framework.

- [ ] **Step 1: Write the failing conflict naming tests**

`avalonia/tests/COMTool.Sftp.Tests/SftpConflictNamingTests.cs`, class `SftpConflictNamingTests`:

```csharp
[Fact]
public void SuggestsTheParenthesisedIndexBeforeTheExtension()
{
    Assert.Equal("a (1).txt", SftpConflictNaming.Suggest("a.txt", 1));
    Assert.Equal("a (2).txt", SftpConflictNaming.Suggest("a.txt", 2));
}

[Fact]
public void SuggestsAParenthesisedNameWhenThereIsNoExtension()
{
    Assert.Equal("a (1)", SftpConflictNaming.Suggest("a", 1));
}

[Fact]
public void UsesOnlyTheLastExtension()
{
    Assert.Equal("a.tar (1).gz", SftpConflictNaming.Suggest("a.tar.gz", 1));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `DOTNET_ROOT=/tmp/opencode/dotnet /tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.Sftp.Tests/COMTool.Sftp.Tests.csproj -c Release --filter "FullyQualifiedName~SftpConflictNamingTests"`

Expected: build failure with `CS0103: The name 'SftpConflictNaming' does not exist`.

- [ ] **Step 3: Implement the transfer model and the conflict naming helper**

`avalonia/src/COMTool.Sftp/SftpConflict.cs` holds `SftpConflictAction`, `SftpConflictDecision`, `SftpConflictContext` and `SftpConflictNaming`. `Suggest` splits the name at the last `.` that is not at position 0, so `"a.tar.gz"` becomes `"a.tar"` + `".gz"`, and inserts `" ({index})"` before the extension.

`avalonia/src/COMTool.Sftp/SftpTransfer.cs` holds `SftpTransferDirection`, `SftpTransferStage`, `SftpTransferOptions`, `SftpTransferRequest`, `SftpTransferProgress`, `SftpTransferFailure` and `SftpTransferResult` with exactly the members listed above.

- [ ] **Step 4: Run the tests to verify they pass**

Run: same command as Step 2. Expected: 3 tests pass.

- [ ] **Step 5: Write the failing in-memory file system tests**

`avalonia/tests/COMTool.Sftp.Tests/InMemoryFileSystemTests.cs`, class `InMemoryFileSystemTests`:

```csharp
[Fact]
public async Task ListsEntriesWithTheirLogicalPaths()
{
    var fs = new InMemoryFileSystem("/home/test");
    fs.AddFile("/home/test/a.txt", "hello");
    fs.AddDirectory("/home/test/sub");
    var entries = await fs.ListAsync("/home/test", CancellationToken.None);
    Assert.Equal(2, entries.Count);
    Assert.Equal("/home/test/a.txt", entries.Single(e => e.Name == "a.txt").Path);
}

[Fact]
public async Task FollowsSymlinkedDirectoriesButKeepsTheRequestedPath()
{
    // A symlink to a directory lists the target's children, and every
    // returned path is rebuilt from the requested path, not from the
    // resolved target path.
}

[Fact]
public async Task DeletesDirectoriesRecursively()
{
    // DeleteDirectoryAsync(path, recursive: true) removes the children too.
}
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `DOTNET_ROOT=/tmp/opencode/dotnet /tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.Sftp.Tests/COMTool.Sftp.Tests.csproj -c Release --filter "FullyQualifiedName~InMemoryFileSystemTests"`

Expected: build failure with `CS0103: The name 'InMemoryFileSystem' does not exist`.

- [ ] **Step 7: Implement the in-memory file system**

`avalonia/src/COMTool.Sftp/Testing/InMemoryFileSystem.cs`, namespace `COMTool.Sftp.Testing`. One instance owns one tree of nodes; a node is a file (content bytes), a directory, or a symlink (a target path).

- Paths are stored in a `Dictionary<string, Node>` keyed by the normalised path, plus a per-directory child map so `ListAsync` returns the direct children only.
- `ListAsync(path)` returns the direct children of `path`. When `path` is a symlink to a directory, it lists the **target's** children, but every returned `SftpEntry.Path` is rebuilt as `SftpPath.Combine(requestedPath, childName)` — never the resolved target path. This mirrors the verified SSH.NET behaviour and is the case the transfer engine must survive.
- `ListAsync` on a symlink to a file throws `FileNotFoundException`, mirroring `SftpPathNotFoundException` on the real server.
- `OpenReadAsync` returns a `MemoryStream` over the stored bytes; `OpenWriteAsync` returns a writable `MemoryStream` whose content is committed back to the node when it is disposed.
- `CreateDirectoryAsync` creates the node and its parents; an existing directory is reused.
- `RenameAsync` moves the node and its descendants, rewriting the descendant keys.
- `DeleteFileAsync` removes a file node; `DeleteDirectoryAsync(path, recursive)` removes the subtree when `recursive` is true, and throws `InvalidOperationException` when it is false and the directory is not empty.
- `SetPermissionsAsync(path, octalMode)` stores the mode on the node.
- `ExistsAsync` returns whether a node exists.
- `HomeDirectory` returns the constructor argument.
- `DisposeAsync` returns `default` and keeps the data, so a test can inspect the tree after a transfer.
- `AddSymlink(path, targetPath)` registers a symlink node.

- [ ] **Step 8: Run the tests to verify they pass**

Run: same command as Step 6. Expected: 3 tests pass.

- [ ] **Step 9: Commit**

```bash
cd /tmp/opencode/COMTool
git add avalonia/src/COMTool.Sftp avalonia/tests/COMTool.Sftp.Tests
git commit -m "feat(avalonia): add the SFTP transfer model, conflict naming and the in-memory file system"
```

---

### Task 4: The transfer engine

**Files:**
- Create: `avalonia/src/COMTool.Sftp/SftpTransferEngine.cs`
- Create: `avalonia/tests/COMTool.Sftp.Tests/SftpTransferEngineTests.cs`

**Interfaces:**
- Consumes: everything from Task 3, plus `ISftpFileSystem` / `ILocalFileSystem` from Task 2.
- Produces:
  - `public sealed class SftpTransferEngine` with `public SftpTransferEngine(ISftpFileSystem remote, ILocalFileSystem local)` and `public Task<SftpTransferResult> RunAsync(SftpTransferRequest request, IProgress<SftpTransferProgress>? progress, CancellationToken cancellationToken)`.

- [ ] **Step 1: Write the failing engine tests**

`avalonia/tests/COMTool.Sftp.Tests/SftpTransferEngineTests.cs`, class `SftpTransferEngineTests`. Every test builds a remote and a local `InMemoryFileSystem`, runs the engine and asserts on the tree plus the returned `SftpTransferResult`.

```csharp
[Fact]
public async Task UploadsASingleFileByteIdentically()
{
    // local "/local/a.txt" = "hello" -> remote "/remote/a.txt" = "hello"
    // result.Succeeded == 1, result.BytesTransferred == 5
}

[Fact]
public async Task DownloadsASingleFileByteIdentically()
{
    // remote "/remote/a.txt" = "hello" -> local "/local/a.txt"
}

[Fact]
public async Task UploadsADirectoryTreeAndCreatesTheDirectories()
{
    // local "/local/tree/sub/b.txt" -> remote "/remote/tree/sub/b.txt"
}

[Fact]
public async Task MergesIntoAnExistingTargetDirectory()
{
    // The remote already has "/remote/tree" with another file; the upload
    // adds to it instead of failing.
}

[Fact]
public async Task AsksTheResolverWhenTheTargetExists()
{
    // The resolver is called exactly once, and the decision is applied.
}

[Fact]
public async Task SkipContinuesWithTheRemainingFiles()
{
    // Two files, the first one conflicts; Skip leaves the first target
    // untouched, the second file is still transferred, result.Skipped == 1.
}

[Fact]
public async Task CancelAbortsTheWholeQueue()
{
    // Two files, the first one conflicts; Cancel stops the queue, the
    // second file is not transferred, result.Cancelled is true.
}

[Fact]
public async Task RenameWritesToTheSuggestedName()
{
    // The conflict is resolved with Rename and no explicit name, so the
    // target "/remote/a (1).txt" is written and the original is untouched.
}

[Fact]
public async Task ReportsProgressWithTheTotalFileCountAndSize()
{
    // The progress events report TotalFiles == 2 and TotalBytes == the sum
    // of the source sizes, and CompletedFiles increases monotonically.
}

[Fact]
public async Task CancellingRemovesThePartiallyWrittenFile()
{
    // A cancellation during the byte copy leaves no partial file on the
    // target side.
}

[Fact]
public async Task OneFailingFileDoesNotStopTheQueue()
{
    // The remote rejects one file, and a second file that vanished between
    // the scan and the transfer fails the same way: the other file is still
    // transferred, result.Failed == 2 and both reasons are in
    // result.Failures.
}

[Fact]
public async Task FollowsSymbolicLinksWhenTheOptionIsOn()
{
    // A remote symlink to a directory is downloaded as its content.
}

[Fact]
public async Task SkipsSymbolicLinksWhenTheOptionIsOff()
{
    // The link is listed in result.SkippedPaths and not transferred.
}

[Fact]
public async Task StopsAtTheMaximumRecursionDepth()
{
    // A 20 level tree with MaxDepth 16 transfers 16 levels and reports the
    // rest as skipped.
}

[Fact]
public async Task PreservesUnusualFileNames()
{
    // A name with a space, a non-ASCII character and a leading dash arrives
    // unchanged.
}

[Fact]
public async Task TransfersAZeroByteFile()
{
    // The empty file arrives as an empty file and the byte total stays 0.
}
```

Each body must assert exactly the values named in its comment and nothing weaker: the resulting tree contents on both sides, and the `SftpTransferResult` fields it names.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `DOTNET_ROOT=/tmp/opencode/dotnet /tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.Sftp.Tests/COMTool.Sftp.Tests.csproj -c Release --filter "FullyQualifiedName~SftpTransferEngineTests"`

Expected: build failure with `CS0103: The name 'SftpTransferEngine' does not exist`.

- [ ] **Step 3: Implement the engine**

`avalonia/src/COMTool.Sftp/SftpTransferEngine.cs`.

The engine runs in two phases and reports progress for both.

**Scan phase.** For every path in `request.SourcePaths`, walk the source side. Directories become a list of directories to create on the target side, in top-down order, and are recursed into. The recursion depth counts from 1 for the top-level entry. A directory that already exists on the target side is reused, not an error. Symbolic links are followed when `Options.FollowSymbolicLinks` is true (the link is treated as the file or directory it points at) and are skipped otherwise, in which case the link path is added to `SkippedPaths`. The walk stops when the depth would exceed `Options.MaxDepth`; the remaining branches are added to `SkippedPaths` as well. The walk stops when the file count would exceed `Options.MaxFiles`; the remaining branches are skipped the same way. The phase reports `SftpTransferStage.Scanning` progress events and computes `TotalFiles` and `TotalBytes`.

**Transfer phase.** For every file in the scan result, in order:

1. If the target already exists (`ExistsAsync` on the target side), ask the resolver. When `Options.ConflictResolver` is null, the file is skipped and reported in `SkippedPaths`. The resolver receives an `SftpConflictContext` whose `SuggestedRename` is the first free name of the form `name (n).ext`, found by trying `n = 1, 2, 3, ...` and checking `ExistsAsync` on the target side. A `Rename` decision without an explicit `NewName` uses `SuggestedRename`; with an explicit `NewName` it uses that name.
2. `Overwrite` writes to the target path. `Skip` records the file as skipped and moves on. `Cancel` sets `Cancelled` and stops the queue immediately, including the files that were not started. `Rename` writes to the renamed path.
3. Copy the bytes with a 64 KiB buffer, counting the copied bytes into `CompletedBytes` and reporting `SftpTransferStage.Transferring` progress. The copy checks `cancellationToken` on every buffer and, when it is cancelled, deletes the partially written target file and stops the queue with `Cancelled` set.
4. A failure of one file is caught, recorded in `Failures` with the exception message as the reason, and the queue continues.

The phase ends with `SftpTransferStage.Completed` (or `Cancelled` / `Failed`) and returns the `SftpTransferResult`. `Succeeded` counts the files that were written, `Skipped` the files that were skipped, `Failed` the files that failed, and `BytesTransferred` the number of bytes actually written.

- [ ] **Step 4: Run the engine tests to verify they pass**

Run: same command as Step 2. Expected: 16 tests pass.

- [ ] **Step 5: Mutation-check the engine**

Break the implementation on purpose in three ways and confirm the matching test fails each time: (a) make `Skip` stop the queue instead of continuing, (b) make the recursion limit `MaxDepth + 1` instead of `MaxDepth`, (c) make `Cancel` continue with the next file. Revert each break before moving on.

- [ ] **Step 6: Commit**

```bash
cd /tmp/opencode/COMTool
git add avalonia/src/COMTool.Sftp avalonia/tests/COMTool.Sftp.Tests docs/superpowers/plans/2026-10-21-comtool-sftp.md
git commit -m "feat(avalonia): add the SFTP transfer engine with conflict handling and recursion limits"
```

---

### Task 5: The SSH.NET implementation and the second connection

**Files:**
- Create: `avalonia/src/COMTool.Connections/SshSftpFileSystem.cs`
- Modify: `avalonia/src/COMTool.Connections/SshConnection.cs`
- Modify: `avalonia/src/COMTool.Connections/COMTool.Connections.csproj`
- Create: `avalonia/tests/COMTool.Connections.Tests/SftpIntegrationTests.cs`

**Interfaces:**
- Consumes: `ISftpFileSystem`, `SftpEntry`, `SftpPermission`, `SftpPath` from `COMTool.Sftp`.
- Produces:
  - `namespace COMTool.Connections` -> `public sealed class SshSftpFileSystem : ISftpFileSystem` with `public SshSftpFileSystem(ConnectionInfo connectionInfo)` — the constructor connects the `SftpClient` and throws on failure — and `public string HomeDirectory { get; }`.
  - `namespace COMTool.Connections` -> `SshConnection.CreateSftpFileSystem()` returning `ISftpFileSystem`; it is cached, so the second call returns the same instance, and the instance is disposed together with the connection.

- [ ] **Step 1: Add the project reference**

In `avalonia/src/COMTool.Connections/COMTool.Connections.csproj`, add to the existing `ProjectReference` group:

```xml
    <ProjectReference Include="..\COMTool.Sftp\COMTool.Sftp.csproj" />
```

- [ ] **Step 2: Write the failing integration test**

`avalonia/tests/COMTool.Connections.Tests/SftpIntegrationTests.cs`, class `SftpIntegrationTests`. Copy the fixture pattern of `avalonia/tests/COMTool.Connections.Tests/SshIntegrationTests.cs` (an ephemeral `/usr/sbin/sshd` on a free port, an ed25519 key pair, `AuthorizedKeysFile`, `StrictModes no`, `UsePAM no`, `AllowUsers {Environment.UserName}`) and add one line to the generated `sshd_config`:

```
Subsystem sftp internal-sftp
```

Every test returns early when the host is not Linux or `/usr/sbin/sshd` is missing, exactly like the existing SSH test.

```csharp
[Fact]
public async Task ListsTheHomeDirectory()
{
    // The remote listing contains the server side home path and at least
    // one entry.
}

[Fact]
public async Task UploadsAndDownloadsTheSameBytes()
{
    // A one megabyte random buffer is uploaded, the server side sha256sum
    // matches, the file is downloaded again and the bytes match.
}

[Fact]
public async Task CreatesRenamesAndDeletesOnTheServer()
{
    // mkdir, rename and delete are visible in a second listing.
}

[Fact]
public async Task SetsPermissionsThatTheServerAgreesWith()
{
    // ChangePermissions(path, 0o640) is confirmed by the server side
    // "stat -c %a" output.
}

[Fact]
public async Task ReportsASymbolicLinkAsASymbolicLink()
{
    // A symlink created on the server with "ln -s" is listed with
    // IsSymbolicLink == true.
}

[Fact]
public async Task FailsWhenTheServerHasNoSftpSubsystem()
{
    // A second sshd without the Subsystem line makes
    // CreateSftpFileSystem() throw, while the shell connection still works.
}

[Fact]
public async Task DisposingTheConnectionReleasesTheSftpClient()
{
    // After the connection is disposed, the SFTP file system throws
    // ObjectDisposedException on the next call.
}
```

Each body must assert exactly the values named in its comment and nothing weaker: the listed entry names, the server side checksum, the server side `stat -c %a` output, the exception type of the failure, and the exception type after the connection is disposed.

- [ ] **Step 3: Run the integration tests to verify they fail**

Run: `DOTNET_ROOT=/tmp/opencode/dotnet /tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.Connections.Tests/COMTool.Connections.Tests.csproj -c Release --filter "FullyQualifiedName~SftpIntegrationTests"`

Expected: build failure with `CS1061: 'SshConnection' does not contain a definition for 'CreateSftpFileSystem'`.

- [ ] **Step 4: Implement `SshSftpFileSystem`**

`avalonia/src/COMTool.Connections/SshSftpFileSystem.cs`.

- The constructor takes the `ConnectionInfo`, creates `new SftpClient(connectionInfo)`, calls `Connect()`, and stores `HomeDirectory = client.WorkingDirectory` (the server side home directory).
- `ListAsync(path)` calls `client.ListDirectory(path)`. The two entries of the directory itself, `.` and `..`, are left out: the server of OpenSSH sends them and they are not part of the content of the directory. For every `ISftpFile` it builds an `SftpEntry` with `Name = file.Name`, `Path = SftpPath.Combine(requestedPath, file.Name)`, `IsDirectory = file.IsDirectory`, `IsSymbolicLink = file.IsSymbolicLink`, `Size = file.Length`, `LastWriteTimeUtc = file.LastWriteTimeUtc` and the permission bits from `file.OwnerCanRead` and friends. The entry path is always rebuilt from the requested path, never taken from `file.FullName`, because following a symlink makes the server report the resolved path.
- `OpenReadAsync(path)` returns `client.Open(path)`.
- `OpenWriteAsync(path)` returns `client.Open(path, FileMode.Create)`, which truncates an existing target.
- `CreateDirectoryAsync(path)` calls `client.CreateDirectory(path)`.
- `RenameAsync(oldPath, newPath)` calls `client.RenameFile(oldPath, newPath)`.
- `DeleteFileAsync(path)` calls `client.DeleteFile(path)`.
- `DeleteDirectoryAsync(path, recursive)` calls `client.DeleteDirectory(path)` when `recursive` is false. When `recursive` is true it empties the tree first: it lists the children, removes the files and the links, removes the directories deepest first, and finally calls `client.DeleteDirectory(path)`. The server refuses to remove a directory that is not empty, so the files below it have to go as well.
- `SetPermissionsAsync(path, octalMode)` calls `client.ChangePermissions(path, mode)`. The protocol carries the mode as the decimal number whose digits are the octal digits, so the mode 0644 goes over the wire as 644 and not as 420. The kernel keeps the numeric mode, the SSH layer converts it.
- `DeleteFileAsync(path)` and `RenameAsync(oldPath, newPath)` remove or rename a symbolic link itself. The library resolves a link to its target before it sends the request, so its public API would delete the target of the link and leave the link behind. The request of the protocol is sent directly for a link. The library keeps its session for itself, so it is reached by reflection; when it is not there, the caller gets a clear error instead of a deleted target.
- `ExistsAsync(path)` calls `client.Exists(path)`.
- Every SSH.NET call is synchronous, so each member runs its body on `Task.Run` and passes the `CancellationToken` down to the SSH.NET overload that accepts one where one exists.
- `DisposeAsync` disposes the client exactly once and is safe to call twice.

- [ ] **Step 5: Expose the SFTP client from `SshConnection`**

In `avalonia/src/COMTool.Connections/SshConnection.cs`:

- Add a `private readonly ConnectionInfo _connectionInfo;` field and a `private SshSftpFileSystem? _sftp;` field.
- `Connect` builds the `ConnectionInfo` as it does today and passes it to the constructor, which stores it.
- Add:

```csharp
/// <summary>
/// Creates the SFTP subsystem client. SSH.NET cannot share the shell
/// channel, so this authenticates a second SSH connection with the same
/// credentials. The client is created once and disposed with the connection.
/// </summary>
public ISftpFileSystem CreateSftpFileSystem()
```

  The method returns the cached instance when it exists. Otherwise it creates `new SshSftpFileSystem(_connectionInfo)`, stores it and returns it. A failure disposes the half-built client and rethrows, so the caller can show the reason.
- `Dispose` disposes `_sftp` before `_client`.

- [ ] **Step 6: Run the integration tests to verify they pass**

Run: same command as Step 3. Expected: 7 tests pass.

- [ ] **Step 7: Run the whole connections test project**

Run: `DOTNET_ROOT=/tmp/opencode/dotnet /tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.Connections.Tests/COMTool.Connections.Tests.csproj -c Release`

Expected: the 7 new tests plus the existing SSH integration test pass.

- [ ] **Step 8: Commit**

```bash
cd /tmp/opencode/COMTool
git add avalonia/src/COMTool.Connections avalonia/tests/COMTool.Connections.Tests avalonia/src/COMTool.Sftp avalonia/tests/COMTool.Sftp.Tests docs/superpowers/plans/2026-10-21-comtool-sftp.md
git commit -m "feat(avalonia): expose the SFTP file system over a second SSH connection"
```

---

### Task 6: The panel view model

**Files:**
- Create: `avalonia/src/COMTool.App/ViewModels/SftpPaneViewModel.cs`
- Modify: `avalonia/src/COMTool.App/COMTool.App.csproj`
- Create: `avalonia/tests/COMTool.App.Tests/SftpPaneViewModelTests.cs`

**Interfaces:**
- Consumes: `ISftpFileSystem`, `ILocalFileSystem`, `SystemLocalFileSystem`, `SftpEntry`, `SftpTransferEngine`, `SftpTransferRequest`, `SftpTransferOptions`, `SftpConflictAction`, `SftpConflictDecision` from `COMTool.Sftp`.
- Produces:
  - `namespace COMTool.App.ViewModels` -> `public sealed partial class SftpPaneViewModel : ObservableObject` with:
    - `public SftpPaneViewModel(Func<ISftpFileSystem> remoteFactory, ILocalFileSystem? local = null)`
    - `public void SetRemoteFactory(Func<ISftpFileSystem> remoteFactory)`
    - `public ObservableCollection<SftpEntry> RemoteEntries { get; }`, `public ObservableCollection<SftpEntry> LocalEntries { get; }`
    - `public string RemotePath { get; set; }`, `public string LocalPath { get; set; }`
    - `public bool IsExpanded { get; set; }` (default `true`), `public double PaneHeight { get; set; }` (default `260`, clamped to 120..600)
    - `public SftpEntry? SelectedRemoteEntry { get; set; }`, `public SftpEntry? SelectedLocalEntry { get; set; }`
    - `public bool FollowSymbolicLinks { get; set; }` (default `true`), `public bool ShowHidden { get; set; }` (default `true`)
    - `public bool IsBusy { get; }`, `public bool IsTransferring { get; }`, `public bool CanTransfer { get; }`
    - `public string StatusMessage { get; }`, `public double TransferProgress { get; }`
    - `public bool IsConflictVisible { get; }`, `public string ConflictMessage { get; }`, `public string ConflictRenameText { get; set; }`, `public bool ApplyToAll { get; set; }`, `public bool NeverAskAgain { get; set; }`
    - `public Task LoadAsync(CancellationToken cancellationToken)`, `public Task RetryAsync()`, `public Task RefreshRemoteAsync()`, `public Task RefreshLocalAsync()`, `public Task EnterRemoteAsync(SftpEntry entry)`, `public Task EnterLocalAsync(SftpEntry entry)`, `public Task GoUpRemoteAsync()`, `public Task GoUpLocalAsync()`, `public Task NewRemoteFolderAsync(string name)`, `public Task RenameRemoteAsync(string newName)`, `public Task DeleteRemoteAsync()`, `public Task SetRemotePermissionsAsync(int octalMode)`, `public Task UploadAsync()`, `public Task DownloadAsync()`, `public void CancelTransfer()`, `public void ResolveConflict(SftpConflictAction action)`.

- [ ] **Step 1: Add the project reference**

In `avalonia/src/COMTool.App/COMTool.App.csproj`, add to the existing `ProjectReference` group:

```xml
    <ProjectReference Include="..\COMTool.Sftp\COMTool.Sftp.csproj" />
```

- [ ] **Step 2: Write the failing view model tests**

`avalonia/tests/COMTool.App.Tests/SftpPaneViewModelTests.cs`, class `SftpPaneViewModelTests`. The tests use `COMTool.Sftp.Testing.InMemoryFileSystem` for the remote and the local side; the project reference to `COMTool.Sftp` arrives transitively through `COMTool.App`, so the file only needs `using COMTool.Sftp;` and `using COMTool.Sftp.Testing;`.

```csharp
[Fact]
public async Task StartsExpandedWithTheDefaultHeight()
{
    var vm = NewViewModel();
    Assert.True(vm.IsExpanded);
    Assert.Equal(260, vm.PaneHeight);
}

[Fact]
public async Task ClampsThePaneHeight()
{
    var vm = NewViewModel();
    vm.PaneHeight = 10;
    Assert.Equal(120, vm.PaneHeight);
    vm.PaneHeight = 5000;
    Assert.Equal(600, vm.PaneHeight);
}

[Fact]
public async Task LoadsTheRemoteHomeDirectory()
{
    // RemoteEntries contains the files of the home directory and
    // RemotePath equals the home directory.
}

[Fact]
public async Task ReportsAnUnavailableSftpSubsystem()
{
    // A factory that throws makes StatusMessage contain
    // "SFTP unavailable" and the reason, and the terminal keeps working.
}

[Fact]
public async Task RetriesAfterAFailure()
{
    // A factory that fails once and then succeeds makes RetryAsync load the
    // listing.
}

[Fact]
public async Task EntersAndLeavesRemoteDirectories()
{
    // EnterRemoteAsync on a directory changes RemotePath and reloads;
    // GoUpRemoteAsync returns to the parent.
}

[Fact]
public async Task HidesHiddenFilesWhenTheToggleIsOff()
{
    // A dot file disappears from RemoteEntries when ShowHidden is false.
}

[Fact]
public async Task CreatesRenamesAndDeletesOnTheRemote()
{
    // NewRemoteFolderAsync, RenameRemoteAsync and DeleteRemoteAsync change
    // the remote tree and the listing.
}

[Fact]
public async Task RefusesToDeleteALocalEntry()
{
    // The local pane exposes no delete path: the view model has no local
    // delete method, so the remote-only rule holds by construction.
}

[Fact]
public async Task AppliesPermissionsToTheRemoteEntry()
{
    // SetRemotePermissionsAsync(0o640) changes the mode of the remote entry
    // and the listing shows it.
}

[Fact]
public async Task UploadsTheSelectedLocalEntries()
{
    // The remote receives the file and the status message reports the count.
}

[Fact]
public async Task DownloadsTheSelectedRemoteEntries()
{
    // The local side receives the file.
}

[Fact]
public async Task DisablesTheTransferButtonsWithoutASelection()
{
    // CanTransfer is false without a selection and true with one.
}

[Fact]
public async Task ShowsAConflictPromptAndAppliesTheDecision()
{
    // Uploading onto an existing remote file shows the conflict prompt with
    // the suggested name "a (1).txt", and ResolveConflict(Overwrite)
    // replaces the target.
}

[Fact]
public async Task AppliesTheDecisionToAllRemainingConflicts()
{
    // With ApplyToAll set, the second conflict does not prompt again.
}

[Fact]
public async Task RemembersTheNeverAskAgainDecision()
{
    // With NeverAskAgain set, the next transfer does not prompt.
}

[Fact]
public async Task ReportsAFailedTransferInTheStatusMessage()
{
    // A failing file makes the status message contain "failed".
}

[Fact]
public async Task CancellingTheTransferStopsTheQueueAndRemovesThePartialFile()
{
    // CancelTransfer makes the transfer report Cancelled and leaves no
    // partially written file on the target side.
}

[Fact]
public async Task SwapsTheRemoteFactoryAfterASessionRestart()
{
    // SetRemoteFactory replaces the remote and reloads the listing, while
    // IsExpanded and PaneHeight keep their values.
}
```

Each body must assert exactly the values named in its comment and nothing weaker: the collection contents, the path values, the status message text, and the property values.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `DOTNET_ROOT=/tmp/opencode/dotnet /tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.App.Tests/COMTool.App.Tests.csproj -c Release --filter "FullyQualifiedName~SftpPaneViewModelTests"`

Expected: build failure with `CS0246: The type or namespace name 'SftpPaneViewModel' could not be found`.

- [ ] **Step 4: Implement the view model**

`avalonia/src/COMTool.App/ViewModels/SftpPaneViewModel.cs`, namespace `COMTool.App.ViewModels`. Use `CommunityToolkit.Mvvm` (`ObservableObject`, `[ObservableProperty]`, `partial void On...Changed`) like the other view models in this project.

- The constructor stores the factory and the local file system, creates a `SystemLocalFileSystem` when the caller passes none, and does not touch the network.
- `LoadAsync` runs the factory on a background thread (`await Task.Run(remoteFactory, cancellationToken)`), stores the resulting `ISftpFileSystem`, sets `RemotePath` to `HomeDirectory`, sets `LocalPath` to `local.HomeDirectory` (the user profile directory) and loads both listings. A factory failure sets `StatusMessage` to `$"SFTP unavailable: {ex.Message}"` and leaves the panel usable. The panel never disposes the remote file system; the `SshConnection` owns it.
- `RefreshRemoteAsync` and `RefreshLocalAsync` reload their listing into the observable collections, sorted with directories first and then by name, and filtered by `ShowHidden` (a name starting with `.` is hidden when the toggle is off). A listing failure sets `StatusMessage` to `$"Listing failed: {ex.Message}"`.
- `EnterRemoteAsync(entry)` changes `RemotePath` to the entry path when the entry is a directory or a symlink to a directory, and reloads. It does nothing for a file.
- `GoUpRemoteAsync` sets `RemotePath` to `SftpPath.GetParent(RemotePath)` and reloads; the root is not left.
- `GoUpLocalAsync` moves the local path to its parent directory.
- `NewRemoteFolderAsync(name)` calls `CreateDirectoryAsync(SftpPath.Combine(RemotePath, name))`, reloads and reports `$"Created {name}"`; an empty name is ignored.
- `RenameRemoteAsync(newName)` renames `SelectedRemoteEntry.Path` to `SftpPath.Combine(RemotePath, newName)`, reloads and reports `$"Renamed to {newName}"`.
- `DeleteRemoteAsync` deletes the selected remote entry, with `recursive: true` for a directory, then reloads and reports `$"Deleted {name}"`.
- `SetRemotePermissionsAsync(octalMode)` calls `SetPermissionsAsync` on the selected remote entry, reloads and reports `$"Permissions set to {octalMode:000}"`; the mode is shown as the octal digits `640` and not as the number `416`; a failure reports `$"chmod failed: {ex.Message}"`.
- `UploadAsync` and `DownloadAsync` build an `SftpTransferRequest` from the selected entries and the current target directory, create an `SftpTransferEngine`, set `IsTransferring` and `IsBusy`, run the transfer on a background thread with a `Progress<SftpTransferProgress>` that updates `TransferProgress` and `StatusMessage`, and finally report the summary (`$"{result.Succeeded} uploaded"` for a transfer to the server, `$"{result.Succeeded} downloaded"` for one to the local side, and `", {result.Failed} failed"` when a file failed). A failure during the run sets `StatusMessage` to `$"Transfer failed: {ex.Message}"`.
- The conflict resolver passed to the engine asks the view model for a decision. The engine runs on a background thread, so the resolver marshals to the UI thread (`await Dispatcher.UIThread.InvokeAsync(...)`) before it touches `IsConflictVisible`, `ConflictMessage` and `ConflictRenameText`, and it returns a `TaskCompletionSource<SftpConflictDecision>` that `ResolveConflict` completes. When `NeverAskAgain` is set and a default decision is stored, that decision is returned without prompting. `ApplyToAll` stores the chosen decision for the rest of the transfer; `NeverAskAgain` stores it for the rest of the session.
- `ResolveConflict(action)` completes the pending resolver with `new SftpConflictDecision(action, action == SftpConflictAction.Rename ? ConflictRenameText : null)` and hides the prompt. `Cancel` completes it with `new SftpConflictDecision(SftpConflictAction.Cancel)`.
- `CancelTransfer` cancels the transfer token, which makes the engine remove the partially written file and return with `Cancelled` set.
- `SetRemoteFactory(factory)` replaces the factory, clears the remote entries and the remote path, and reloads. `IsExpanded` and `PaneHeight` are untouched.
- `PaneHeight` is clamped to 120..600 in its setter.

- [ ] **Step 5: Run the tests to verify they pass**

Run: same command as Step 3. Expected: 18 tests pass.

- [ ] **Step 6: Commit**

```bash
cd /tmp/opencode/COMTool
git add avalonia/src/COMTool.App avalonia/tests/COMTool.App.Tests
git commit -m "feat(avalonia): add the SFTP panel view model"
```

---

### Task 7: The SSH session view and the tab wiring

**Files:**
- Create: `avalonia/src/COMTool.App/Views/SshSessionView.axaml`
- Create: `avalonia/src/COMTool.App/Views/SshSessionView.axaml.cs`
- Modify: `avalonia/src/COMTool.App/ViewModels/MainViewModel.cs`
- Modify: `avalonia/src/COMTool.App/Views/MainWindow.axaml.cs`
- Create: `avalonia/tests/COMTool.App.Tests/SshSessionViewTests.cs`

**Interfaces:**
- Consumes: `SftpPaneViewModel` from Task 6, `TerminalControl` from `COMTool.Terminal`.
- Produces:
  - `namespace COMTool.App.Views` -> `public sealed partial class SshSessionView : UserControl` with `public TerminalControl? Terminal { get; set; }` and `public SftpPaneViewModel? SftpPane { get; set; }`.
  - `TerminalTab.Terminal` (`TerminalControl?`), `TerminalTab.SftpPane` (`SftpPaneViewModel?`) and `TerminalTab.IsSshTab` (`bool`).

- [ ] **Step 1: Write the failing session view tests**

`avalonia/tests/COMTool.App.Tests/SshSessionViewTests.cs`, class `SshSessionViewTests`. Follow the headless pattern of `avalonia/tests/COMTool.App.Tests/SessionEditTests.cs` (`[AvaloniaFact]`, `new TestEnvironment()`, `env.Run(() => { ... })`, `Dispatcher.UIThread.RunJobs()`).

```csharp
[AvaloniaFact]
public void SshSessionViewStacksTheTerminalOverTheDrawer()
{
    // The view has a terminal host and an SFTP pane; the pane row is below
    // the terminal row and its height follows SftpPane.PaneHeight.
}

[AvaloniaFact]
public void SshSessionViewHidesTheDrawerWhenCollapsed()
{
    // SftpPane.IsExpanded = false makes the drawer and the splitter
    // invisible, and the terminal takes the whole height.
}

[AvaloniaFact]
public void TerminalTabExposesTheTerminalAndTheSftpPane()
{
    // A TerminalTab with a SshSessionView reports the same terminal and
    // pane through the new properties, and IsSshTab is true only for
    // SessionKind.Ssh.
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `DOTNET_ROOT=/tmp/opencode/dotnet /tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.App.Tests/COMTool.App.Tests.csproj -c Release --filter "FullyQualifiedName~SshSessionViewTests"`

Expected: build failure with `CS0246: The type or namespace name 'SshSessionView' could not be found`.

- [ ] **Step 3: Add the `TerminalTab` properties**

In `avalonia/src/COMTool.App/ViewModels/MainViewModel.cs`, inside `TerminalTab`:

```csharp
public TerminalControl? Terminal { get; init; }

public SftpPaneViewModel? SftpPane { get; init; }

public bool IsSshTab => Kind == SessionKind.Ssh;
```

Add the `using` for `COMTool.Terminal` if the file does not already have it.

- [ ] **Step 4: Create `SshSessionView`**

`avalonia/src/COMTool.App/Views/SshSessionView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="COMTool.App.Views.SshSessionView">
  <Grid Name="SessionGrid" RowDefinitions="*,4,*">
    <ContentControl Name="TerminalHost" Grid.Row="0" />
    <GridSplitter Name="SftpSplitter"
                  Grid.Row="1"
                  Height="4"
                  ResizeDirection="Rows" />
    <ContentControl Name="SftpHost" Grid.Row="2" />
  </Grid>
</UserControl>
```

`SshSessionView.axaml.cs` holds a `SftpPane` property and a `Terminal` property. Setting `Terminal` adds the control to `TerminalHost`'s content; setting `SftpPane` creates the drawer view (from Task 8, so until then the host stays empty) and adds it to `SftpHost`. For this task the `SftpHost` content is a plain `Border` placeholder, and Task 8 replaces it with the real `SftpPane` view.

The drawer height and the drawer visibility are **not** bound in XAML. A `ContentControl` that hosts a `Control` keeps the inherited `DataContext`, so a `{Binding SftpPane.IsExpanded}` inside `SshSessionView.axaml` would resolve against the window view model, not against the view. Implement them in the code-behind instead:

- `SshSessionView` subscribes to `SftpPane.PropertyChanged` and sets the height of the row of the drawer to `SftpPane.PaneHeight`, `SftpHost.IsVisible` to `SftpPane.IsExpanded` and `SftpSplitter.IsVisible` to `SftpPane.IsExpanded`. The height belongs to the row and not to the control of the drawer: a row of the size "*" does not obey the height of its child, and the handle of the splitter resizes the row.
- The `GridSplitter` writes the dragged height back into `SftpPane.PaneHeight`.
- Setting a new `SftpPane` unsubscribes from the old one and subscribes to the new one, so the view does not leak a handler.

- [ ] **Step 5: Use the session view as the SSH tab content**

In `avalonia/src/COMTool.App/Views/MainWindow.axaml.cs`:

- In `OpenSshSessionAsync`, create the panel view model and the session view instead of putting the terminal straight into the tab:

```csharp
var terminal = CreateTerminal();
var pane = new SftpPaneViewModel(
    () => connection.CreateSftpFileSystem(),
    new SystemLocalFileSystem());
var sessionView = new SshSessionView { Terminal = terminal, SftpPane = pane };
var tab = new TerminalTab
{
    Title = SshSessionRules.DisplayName(session),
    SessionId = session.Key,
    Kind = SessionKind.Ssh,
    Content = sessionView,
    Terminal = terminal,
    SftpPane = pane,
    StatusMessage = $"Connected {SshSessionRules.Detail(session)}",
};
```

  Then start the first listing in the background without blocking the connect:

```csharp
_ = pane.LoadAsync(CancellationToken.None);
```

  The drawer is asked for its remote side through `() => connection.CreateSftpFileSystem()`. The connection owns the client of the sftp subsystem and disposes it with the tab.

  Add `using COMTool.Sftp;` to `MainWindow.axaml.cs` for `SystemLocalFileSystem`.

- Replace every `tab.Content as TerminalControl` with `tab.Terminal`. There are ten such sites in `MainWindow.axaml.cs`; `tab.Content` is now a `SshSessionView` for SSH tabs, so the casts would silently return null and break the terminal focus, the detach/reattach flow and the double-click detach window. Every tab that holds a terminal names it in `TerminalTab.Terminal`, not only the tabs of an SSH session.
- The two places that put the focus back on the tab after the tab was detached or attached again focus the terminal when there is one, so that the terminal of an SSH session gets the keyboard.
- In `RestartSshSessionAsync`, use `tab.Terminal` instead of `tab.Content as TerminalControl`, and after the new connection is attached call `tab.SftpPane?.SetRemoteFactory(() => connection.CreateSftpFileSystem())`. The method loads the listing itself, so a second load would read the directory twice. The collapsed state and the drawer height stay as they were.
- In `AttachSshTransport`, extend the tab `CloseAction` with `tab.SftpPane?.CancelTransfer()` before the connection is disposed, so closing the tab during a transfer cancels the queue and removes the partially written file instead of leaving a background task running against a dead connection.

- [ ] **Step 6: Run the tests to verify they pass**

Run: same command as Step 2. Expected: 3 tests pass.

- [ ] **Step 7: Run the whole app test project**

Run: `DOTNET_ROOT=/tmp/opencode/dotnet /tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.App.Tests/COMTool.App.Tests.csproj -c Release`

Expected: the 3 new tests plus the existing 60 app tests pass. A regression here means a `tab.Content as TerminalControl` site was missed.

- [ ] **Step 8: Commit**

```bash
cd /tmp/opencode/COMTool
git add avalonia/src/COMTool.App avalonia/tests/COMTool.App.Tests
git commit -m "feat(avalonia): host the SFTP drawer inside the SSH session view"
```

---

### Task 8: The drawer view and the tab strip toggle

**Files:**
- Create: `avalonia/src/COMTool.App/Views/SftpPane.axaml`
- Create: `avalonia/src/COMTool.App/Views/SftpPane.axaml.cs`
- Modify: `avalonia/src/COMTool.App/Views/SshSessionView.axaml.cs`
- Modify: `avalonia/src/COMTool.App/Views/MainWindow.axaml`
- Modify: `avalonia/src/COMTool.App/Views/MainWindow.axaml.cs`
- Create: `avalonia/tests/COMTool.App.Tests/SftpPaneLayoutTests.cs`

**Interfaces:**
- Consumes: `SftpPaneViewModel` from Task 6, `SshSessionView` from Task 7.
- Produces:
  - `namespace COMTool.App.Views` -> `public sealed partial class SftpPane : UserControl` with `public SftpPaneViewModel? ViewModel { get; set; }`.
  - The tab strip button `SftpToggleButton` in `MainWindow.axaml`, visible only when the selected tab is an SSH tab.

- [ ] **Step 1: Write the failing layout tests**

`avalonia/tests/COMTool.App.Tests/SftpPaneLayoutTests.cs`, class `SftpPaneLayoutTests`, using the headless pattern.

```csharp
[AvaloniaFact]
public void SftpPaneShowsTheRemotePaneOnTheLeftAndTheLocalPaneOnTheRight()
{
    // The remote list is to the left of the local list, measured from the
    // real Bounds of both controls.
}

[AvaloniaFact]
public void DrawerIsExpandedByDefault()
{
    // The pane row of the session view has the drawer height and the drawer
    // is visible.
}

[AvaloniaFact]
public void ToggleButtonCollapsesAndRestoresTheDrawer()
{
    // Clicking SftpToggleButton toggles SftpPane.IsExpanded and the drawer
    // visibility.
}

[AvaloniaFact]
public void ToggleButtonSitsLeftOfTheEncodingCombo()
{
    // The right edge of SftpToggleButton is left of the left edge of
    // EncodingCombo, measured from the real Bounds.
}

[AvaloniaFact]
public void ToggleButtonIsHiddenForNonSshTabs()
{
    // With a serial, TCP or local terminal tab selected the button is
    // invisible; with an SSH tab it is visible.
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `DOTNET_ROOT=/tmp/opencode/dotnet /tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.App.Tests/COMTool.App.Tests.csproj -c Release --filter "FullyQualifiedName~SftpPaneLayoutTests"`

Expected: build failure with `CS0246: The type or namespace name 'SftpPane' could not be found`.

- [ ] **Step 3: Create the drawer view**

`avalonia/src/COMTool.App/Views/SftpPane.axaml`, a `UserControl` with a `Grid` of three rows:

- Row 0: the toolbar, with buttons `Up`, `Refresh`, `New folder`, `Delete`, `Rename`, `chmod`, `Upload`, `Download` and the checkboxes `Follow symbolic links` and `Show hidden`.
- Row 1: a two-column grid, column 0 the remote pane, column 1 the local pane. Each pane is a `Grid` with a path `TextBox` on top and a `ListBox` below. The remote `ListBox` shows `Name`, `Size`, `Modified` and `Permissions` in four columns through an `ItemTemplate` with a `Grid`; the local `ListBox` shows the same four columns, the permission of a local entry being its read-only attribute.
- Row 2: the status area, with a `TextBlock` bound to `StatusMessage`, a `ProgressBar` bound to `TransferProgress` and a `Cancel` button that is visible only while `IsTransferring` is true.

Name the two lists `RemoteList` and `LocalList`, the two path boxes `RemotePathBox` and `LocalPathBox`, and the progress bar `TransferProgressBar`, so the tests can find them.

The remote name column shows a `@` marker for a symbolic link and the permissions column shows `Permission.ToRwxString()`. Both are small `IValueConverter` types that live in `avalonia/src/COMTool.App/Views/SftpConverters.cs`: the name, the size, the time, the permissions and the tip of the toggle button. The local list shows the same four columns; the permission of a local entry is the read-only attribute of the file.

`SftpPane.axaml.cs` holds the `ViewModel` property; setting it assigns the `DataContext`, so every binding inside the drawer resolves against the view model. The drawer also handles the double click on the remote list: it calls `ViewModel.EnterRemoteAsync(entry)` for a directory and for a symlink to a directory, and does nothing for a plain file. The double click on the local list calls `ViewModel.EnterLocalAsync(entry)`, and the path boxes read the path that was typed when the enter key is pressed.

The `Up` button of the toolbar moves the pane that was worked on last: the local pane when only a local entry is marked, otherwise the remote pane. The remote side is the side that is worked on, and the local side has to be marked for the local pane to move.

- [ ] **Step 4: Put the drawer into the session view**

In `avalonia/src/COMTool.App/Views/SshSessionView.axaml.cs`, replace the `SftpHost` placeholder with a real `SftpPane` whose `ViewModel` is the `SftpPane` property. The name of the view is the same as the name of the property, so the view is named with its whole path (`new COMTool.App.Views.SftpPane { ... }`). The row height follows `SftpPane.PaneHeight` and the visibility follows `SftpPane.IsExpanded`; the splitter writes the dragged height back into `SftpPane.PaneHeight`.

- [ ] **Step 5: Add the tab strip toggle button**

In `avalonia/src/COMTool.App/Views/MainWindow.axaml`, inside the `StackPanel Grid.Column="1"` that holds the encoding combo, add the button **before** the `EncodingCombo`:

```xml
<Button Name="SftpToggleButton"
        Width="28" Height="31"
        MinWidth="0" MinHeight="0"
        Padding="0"
        Margin="0,0,4,0"
        VerticalAlignment="Top"
        IsVisible="{Binding SelectedTab.IsSshTab}"
        Click="OnSftpToggleClick"
        ToolTip.Tip="SFTP browser">
  <Panel>
    <TextBlock Text="&#xf07b;"
               FontFamily="{DynamicResource IconFontAwesome}"
               FontSize="13"
               Foreground="White"
               IsVisible="{Binding !SelectedTab.SftpPane.IsExpanded}" />
    <TextBlock Text="&#xf07c;"
               FontFamily="{DynamicResource IconFontAwesome}"
               FontSize="13"
               Foreground="White"
               IsVisible="{Binding SelectedTab.SftpPane.IsExpanded}" />
  </Panel>
</Button>
```

`&#xf07b;` is the closed folder glyph and `&#xf07c;` the open folder glyph. The tooltip is `SFTP browser` when the drawer is collapsed and `Hide SFTP browser` when it is expanded; bind it through a converter or set it in the click handler.

- [ ] **Step 6: Handle the toggle click**

In `avalonia/src/COMTool.App/Views/MainWindow.axaml.cs`:

```csharp
private void OnSftpToggleClick(object? sender, RoutedEventArgs e)
{
    var pane = (sender as Button)?.DataContext is MainViewModel viewModel
        ? viewModel.SelectedTab?.SftpPane
        : null;
    pane?.IsExpanded = !(pane?.IsExpanded ?? false);
}
```

Because the button lives in the tab strip, its `DataContext` is the `MainViewModel`, not the tab. Read the pane from `SelectedTab`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: same command as Step 2. Expected: 5 tests pass.

- [ ] **Step 8: Commit**

```bash
cd /tmp/opencode/COMTool
git add avalonia/src/COMTool.App avalonia/tests/COMTool.App.Tests
git commit -m "feat(avalonia): add the SFTP drawer view and the tab strip toggle"
```

---

### Task 9: The file management commands in the drawer

**Files:**
- Create: `avalonia/src/COMTool.App/Views/SftpPermissionsWindow.axaml`
- Create: `avalonia/src/COMTool.App/Views/SftpPermissionsWindow.axaml.cs`
- Modify: `avalonia/src/COMTool.App/Views/SftpPane.axaml`
- Modify: `avalonia/src/COMTool.App/Views/SftpPane.axaml.cs`
- Create: `avalonia/tests/COMTool.App.Tests/SftpFileManagementTests.cs`

**Interfaces:**
- Consumes: `SftpPaneViewModel` from Task 6, `SftpPermission` from Task 1.
- Produces:
  - `namespace COMTool.App.Views` -> `public sealed partial class SftpPermissionsWindow : Window` with `public SftpPermission Permission { get; set; }`, `public string OctalText { get; set; }` and `public event Action<SftpPermission>? PermissionApplied;`. The window is opened non-modally so the terminal stays usable.

- [ ] **Step 1: Write the failing file management tests**

`avalonia/tests/COMTool.App.Tests/SftpFileManagementTests.cs`, class `SftpFileManagementTests`, using the headless pattern.

```csharp
[AvaloniaFact]
public void PermissionsWindowKeepsTheCheckboxGridAndTheOctalFieldInSync()
{
    // Toggling a checkbox updates OctalText, and typing an octal value
    // updates the checkboxes. 0640 -> owner read+write, group read.
}

[AvaloniaFact]
public void PermissionsWindowKeepsExecuteIndependentOfRead()
{
    // The octal value 0111 checks the three execute boxes and leaves the
    // read boxes clear.
}

[AvaloniaFact]
public void NewFolderButtonCreatesTheRemoteDirectory()
{
    // The button is enabled only with a name typed into the new folder
    // field, and the remote tree gains the directory.
}

[AvaloniaFact]
public void RenameButtonRenamesTheSelectedRemoteEntry()
{
    // The rename field is pre-filled with the selected name, and applying it
    // renames the remote entry.
}

[AvaloniaFact]
public void DeleteButtonDeletesTheSelectedRemoteEntry()
{
    // The remote tree loses the entry and the listing is refreshed.
}

[AvaloniaFact]
public void LocalPaneExposesNoDestructiveCommands()
{
    // The local pane has no new folder, rename, delete or chmod button.
}

[AvaloniaFact]
public void ChmodFailureIsReportedInline()
{
    // A remote that rejects the mode change leaves the status message
    // containing "chmod failed".
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `DOTNET_ROOT=/tmp/opencode/dotnet /tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.App.Tests/COMTool.App.Tests.csproj -c Release --filter "FullyQualifiedName~SftpFileManagementTests"`

Expected: build failure with `CS0246: The type or namespace name 'SftpPermissionsWindow' could not be found`.

- [ ] **Step 3: Create the permissions window**

`avalonia/src/COMTool.App/Views/SftpPermissionsWindow.axaml`: a `Window` with `Title="Permissions"`, `SizeToContent="Height"`, a three-by-three grid of checkboxes (rows: owner, group, others; columns: read, write, execute) named `OwnerReadBox`, `OwnerWriteBox`, `OwnerExecuteBox`, `GroupReadBox`, `GroupWriteBox`, `GroupExecuteBox`, `OthersReadBox`, `OthersWriteBox`, `OthersExecuteBox`, an editable `TextBox` named `OctalBox`, and an `Apply` button named `ApplyButton`. Avalonia 12 has one event for the state of a `CheckBox`, `IsCheckedChanged`, so every box listens to it.

`SftpPermissionsWindow.axaml.cs` keeps the two representations in sync: any checkbox change recomputes `Permission` and refreshes `OctalText` as three octal digits; a change of `OctalText` to a valid three-digit octal value recomputes the checkboxes. A value that is not written in octal is ignored, so the boxes keep the mode they had. `Apply` raises `PermissionApplied` with the current `Permission` and closes the window. `Permission` and `OctalText` are plain properties; the markup of the window does not bind to them, so the window needs no change notification.

- [ ] **Step 4: Add the file management controls to the drawer**

In `avalonia/src/COMTool.App/Views/SftpPane.axaml`:

- Add a `TextBox` named `NewFolderNameBox` and a `TextBox` named `RenameNameBox` to the toolbar area, each next to its button.
- Add buttons named `NewFolderButton`, `RenameButton`, `DeleteButton` and `ChmodButton`. They are enabled only when `SelectedRemoteEntry` is not null, except `NewFolderButton`, which is enabled when `NewFolderNameBox` has text.
- The delete button asks for a confirmation through a two-step inline confirm: the first click changes the button content to `Confirm delete`, the second click deletes. This keeps the panel free of modal dialogs.

In `avalonia/src/COMTool.App/Views/SftpPane.axaml.cs`, wire the buttons to the view model:

- `NewFolderButton` calls `ViewModel.NewRemoteFolderAsync(NewFolderNameBox.Text)` and clears the box.
- `RenameButton` calls `ViewModel.RenameRemoteAsync(RenameNameBox.Text)`. The field is filled with the name of the marked entry; a name that is the same as the old one only puts the cursor into the field.
- `DeleteButton` calls `ViewModel.DeleteRemoteAsync()`.
- `ChmodButton` opens a `SftpPermissionsWindow` with the selected entry's permission, and on `PermissionApplied` calls `ViewModel.SetRemotePermissionsAsync(permission.ToOctal())`. The window is shown with `Show()` (not `ShowDialog`) so the terminal stays usable. The drawer keeps the window in `PermissionsWindow` while it is open, so a test can reach it.

- [ ] **Step 5: Run the tests to verify they pass**

Run: same command as Step 2. Expected: 7 tests pass.

- [ ] **Step 6: Commit**

```bash
cd /tmp/opencode/COMTool
git add avalonia/src/COMTool.App avalonia/tests/COMTool.App.Tests
git commit -m "feat(avalonia): add the remote file management commands to the SFTP drawer"
```

---

### Task 10: The transfer controls and the conflict prompt

**Files:**
- Modify: `avalonia/src/COMTool.App/Views/SftpPane.axaml`
- Modify: `avalonia/src/COMTool.App/Views/SftpPane.axaml.cs`
- Create: `avalonia/tests/COMTool.App.Tests/SftpTransferUiTests.cs`

**Interfaces:**
- Consumes: `SftpPaneViewModel` from Task 6.
- Produces: the `UploadButton`, `DownloadButton`, `CancelButton`, `TransferProgressBar`, `ConflictPanel`, `ConflictOverwriteButton`, `ConflictSkipButton`, `ConflictRenameButton`, `ConflictCancelButton`, `ConflictRenameBox`, `ApplyToAllBox` and `NeverAskAgainBox` controls in `SftpPane.axaml`.

- [ ] **Step 1: Write the failing transfer UI tests**

`avalonia/tests/COMTool.App.Tests/SftpTransferUiTests.cs`, class `SftpTransferUiTests`, using the headless pattern.

```csharp
[AvaloniaFact]
public void UploadAndDownloadAreDisabledWithoutASelection()
{
    // Both buttons are disabled until a local or remote entry is selected.
}

[AvaloniaFact]
public void CancelButtonAppearsOnlyDuringATransfer()
{
    // The cancel button is invisible while idle and visible while the
    // transfer runs.
}

[AvaloniaFact]
public void ProgressBarReflectsTheTransferProgress()
{
    // The progress bar value follows the view model.
}

[AvaloniaFact]
public void ConflictPanelIsHiddenUntilAConflictHappens()
{
    // The panel is invisible while idle.
}

[AvaloniaFact]
public void ConflictPanelOffersTheFourDecisions()
{
    // The panel has Overwrite, Skip, Rename and Cancel buttons plus the
    // Apply to all and Never ask again checkboxes.
}

[AvaloniaFact]
public void ConflictRenameBoxIsPrefilledWithTheSuggestedName()
{
    // The rename box holds "a (1).txt" for a target named "a.txt".
}

[AvaloniaFact]
public void ConflictPanelIsNotAModalDialog()
{
    // The panel lives inside the drawer, so the terminal control is still
    // visible and enabled while the prompt is shown.
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `DOTNET_ROOT=/tmp/opencode/dotnet /tmp/opencode/dotnet/dotnet test avalonia/tests/COMTool.App.Tests/COMTool.App.Tests.csproj -c Release --filter "FullyQualifiedName~SftpTransferUiTests"`

Expected: the tests fail because the named controls do not exist in the drawer yet.

- [ ] **Step 3: Add the transfer controls to the drawer**

In `avalonia/src/COMTool.App/Views/SftpPane.axaml`:

- `UploadButton` and `DownloadButton` in the toolbar. The upload is enabled by `CanUpload`, which asks for a marked local entry, and the download by `CanDownload`, which asks for a marked remote entry.
- `CancelButton` in the status area, visible only when `IsTransferring` is true.
- `TransferProgressBar` in the status area, `Minimum 0`, `Maximum 100`, bound to `TransferProgress`.
- A `Border` named `ConflictPanel` in the status area, visible only when `IsConflictVisible` is true. It holds a `TextBlock` bound to `ConflictMessage`, a `TextBox` named `ConflictRenameBox` bound to `ConflictRenameText`, the four buttons and the two checkboxes. It sits on a row of its own below the status line, so that the four buttons and the two checkboxes have a line to themselves.

In `avalonia/src/COMTool.App/Views/SftpPane.axaml.cs`, wire the buttons to the view model:

- `UploadButton` -> `ViewModel.UploadAsync()`, `DownloadButton` -> `ViewModel.DownloadAsync()`, `CancelButton` -> `ViewModel.CancelTransfer()`.
- `ConflictOverwriteButton` -> `ViewModel.ResolveConflict(SftpConflictAction.Overwrite)`, `ConflictSkipButton` -> `Skip`, `ConflictRenameButton` -> `Rename`, `ConflictCancelButton` -> `Cancel`.
- The `ApplyToAllBox` and `NeverAskAgainBox` are two-way bound to the view model properties.

- [ ] **Step 4: Run the tests to verify they pass**

Run: same command as Step 2. Expected: 7 tests pass.

- [ ] **Step 5: Commit**

```bash
cd /tmp/opencode/COMTool
git add avalonia/src/COMTool.App avalonia/tests/COMTool.App.Tests
git commit -m "feat(avalonia): add the SFTP transfer controls and the conflict prompt"
```

---

### Task 11: The end-to-end SFTP test

**Files:**
- Modify: `avalonia/tests/e2e/linux/lib.sh`
- Modify: `avalonia/tests/e2e/linux/run-ui-e2e.sh`
- Modify: `avalonia/tests/e2e/linux/lib.sh` (screenshot helper, if needed)

**Interfaces:**
- Consumes: the drawer, the toggle button and the transfer buttons from Tasks 8 and 10.
- Produces: the E2E test id `SFTP01_BrowserTransferE2E`.

- [ ] **Step 1: Enable the SFTP subsystem in the E2E sshd fixture**

In `avalonia/tests/e2e/linux/lib.sh`, in the heredoc that writes `$SSH_FIXTURE_DIR/sshd_config`, add the line:

```
Subsystem sftp internal-sftp
```

- [ ] **Step 2: Write the E2E test**

In `avalonia/tests/e2e/linux/run-ui-e2e.sh`:

- Add `SFTP01_BrowserTransferE2E` to the `E2E_TESTS` array and to the `SSH_TEST_IDS` array.
- Add a `run_sftp01_browser_transfer` function that:

  1. Calls `ensure_first_shell "$test_id"` to open the SSH tab.
  2. Finds the `SftpToggleButton` in the window with `xdotool search --name` or a coordinate click, and verifies the drawer is open by checking that the remote pane lists a file. The remote pane is in the lower half of the content area, below the terminal.
  3. Writes a local file under `$TMP_ROOT` with a known marker.
  4. Selects it in the local pane, clicks `Upload`, waits for the status text to report the upload, and verifies on the server side that the file exists with the same content (`cat` through the SSH shell and compare with the marker).
  5. Creates a server side file with different content, downloads it, and verifies the local file content changed.
  6. Uploads the same local file again so the target exists, verifies the conflict prompt appears, clicks `Overwrite`, and verifies the server side content is the local content.
  7. Takes a screenshot into `docs/fixtures/phase1/sftp-drawer-1000x650.png`.

  Use the existing helpers in `lib.sh` for window lookup, clicking and waiting; follow the pattern of `run_ssh11_restart_stopped_session` and `run_e2e01_open_ssh`. Add a small helper that clicks a control by its on-screen position, because the Avalonia headless tree is not reachable from the shell.

- [ ] **Step 3: Run the E2E test**

Run: `PATH=/tmp/opencode/e2e-deps/usr/bin:$PATH XVFB_BIN=/tmp/opencode/rdp-tools/root/usr/bin/Xvfb WM_BIN=/usr/bin/mutter DOTNET_ROOT=/tmp/opencode/dotnet bash avalonia/tests/e2e/linux/run-ui-e2e.sh --app avalonia/src/COMTool.App/bin/Release/net10.0/COMTool.App --filter SFTP01`

Expected: `PASS SFTP01_BrowserTransferE2E` and a screenshot at `docs/fixtures/phase1/sftp-drawer-1000x650.png`.

If the run fails with `Bind to port ... failed: Address already in use`, the sshd fixture port collided with a leftover process; kill it and run the command again.

- [ ] **Step 4: Commit**

```bash
cd /tmp/opencode/COMTool
git add avalonia/tests/e2e docs/fixtures/phase1/sftp-drawer-1000x650.png
git commit -m "test(avalonia): add the SFTP drawer end-to-end test"
```

**As built (deviations from the plan):**

- The drawer opens expanded, so the test does not click the toggle button. It waits for `subsystem 'sftp'` in the fixture log, which proves that the drawer asked for the subsystem on its own.
- The panes are reached through the coordinate constants `SFTP_REMOTE_PATH_X/Y`, `SFTP_LOCAL_PATH_X/Y`, `SFTP_REMOTE_ITEM_X/Y`, `SFTP_LOCAL_ITEM_X/Y`, `SFTP_UPLOAD_X`, `SFTP_DOWNLOAD_X` and `SFTP_TOOLBAR_Y`, measured on the 1000x650 fixture window with the sidebar collapsed.
- The conflict prompt is recognized by the colour of its strip (`SFTP_CONFLICT_COLOR`, `#fff4ce`) through `wait_for_window_color`, and `Overwrite` is clicked by coordinate. The shell of the test cannot read the Avalonia tree.
- `ssh_connection_count` counts the interactive shells of the fixture instead of the sockets of its port: the drawer's sftp channel made one session look like two, so SSH06 never opened its second connection.
- The sidebar probe of `collapse_sidebar_for_tabs` averages a 24x24 square (`window_region_is_dark`) instead of reading one pixel, because a pixel of the dark terminal can land on a light glyph.
- `run_pid_command_until_pid_file` types the PID command again until the file holds a number; the terminal can drop the first keystrokes while the shell prints its login banner, and the redirections still create the files.
- The screenshot is taken into the root of the run. `docs/fixtures/phase1/sftp-drawer-1000x650.png` is produced in Task 12 together with its README row.
- Two defects were fixed on the way: `SshSftpFileSystem.ListAsync` translates the `SftpException` of SSH.NET into `DirectoryNotFoundException` (the engine probes a path by listing it, so every real download failed), and every focus site resolves the terminal through `TerminalTab.Terminal` because the content of an SSH tab is the session view.
- `avalonia/pack/verify.sh` lists the tests of each project separately; the solution-wide list merged the output of parallel projects and glued names together, which made TestGate reject tests that exist.

---

### Task 12: Documentation, the parity matrix and the full gate

**Files:**
- Modify: `docs/parity-matrix.md`
- Modify: `docs/testing/feature-test-matrix.md`
- Modify: `docs/testing/manual-acceptance.md`
- Modify: `docs/fixtures/phase1/README.md`
- Modify: `docs/packaging/avalonia-windows-beta.md` (only after the Windows deploy of a later task)

**Interfaces:**
- Consumes: every test id created in Tasks 1 to 11.
- Produces: the `SFTP-01` to `SFTP-08` parity rows and their matching matrix rows.

- [ ] **Step 1: Add the parity rows**

In `docs/parity-matrix.md`, insert a new section `## 6. SFTP` right after `## 5. Telnet`, and renumber the sections that follow it: `## 6. TCP/UDP` becomes `## 7`, `## 7. ZMODEM` becomes `## 8`, `## 8. 插件与协议` becomes `## 9`, `## 9. 配置、会话与平台` becomes `## 10`, `## 10. 打包与发布` becomes `## 11` and `## 11. 测试与验收` becomes `## 12`. No other document references these section numbers. Add these rows:

| ID | Feature | Priority | Status | Acceptance notes |
|---|---|---|---|---|
| SFTP-01 | SFTP drawer embedded in the SSH tab | P1 | 完成 | Bottom drawer in the SSH tab only, expanded by default, toggled by the button next to the encoding combo, per-session collapsed state and height, not a new session type; the drawer does not affect serial, Telnet, TCP or local terminal tabs |
| SFTP-02 | Dual pane browsing | P1 | 完成 | Remote pane on the left, local pane on the right, path box, navigation, hidden file toggle, symlinks marked with `@` |
| SFTP-03 | Upload and download | P1 | 完成 | File and recursive directory transfers in both directions, button driven, byte identical; the totals are computed before the transfer starts |
| SFTP-04 | Conflict handling | P1 | 完成 | Ask on every conflict, Overwrite / Skip / Rename / Cancel, suggested name `name (1).ext`, `Apply to all` and `Never ask again` |
| SFTP-05 | Remote file management | P1 | 完成 | New folder, rename, delete and chmod on the remote pane only; the local pane has no destructive commands |
| SFTP-06 | Symbolic links and recursion limits | P2 | 完成 | Links are followed by default and can be switched off; the recursion stops at 16 levels and 100000 files |
| SFTP-07 | Progress, cancellation and errors | P1 | 完成 | Progress with the file count and the byte total, cancel removes the partial file, one failure does not stop the queue |
| SFTP-08 | Windows real server interaction and visuals | P2 | 部分 | See WIN-23 |

- [ ] **Step 2: Add the manual acceptance item**

In `docs/testing/manual-acceptance.md`, add:

```
| WIN-23 | SFTP-08 | Windows real SSH server: open the SFTP drawer, upload and download a file, resolve a conflict, run chmod, check the panel layout and the font rendering on a real desktop | 截图/日志 |
```

- [ ] **Step 3: Add the feature test matrix rows**

In `docs/testing/feature-test-matrix.md`, add one row per parity id. The `Automated` column lists the test ids created by this plan, the `Phase` is `verify`, the `Platform` is `all` for the rows covered by the unit and headless tests and `linux` for the rows that also need the sshd integration, the `Command` is `dotnet test`, and the `Manual` column is `-` except for `SFTP-08`, which uses `WIN-23`.

The `SFTP-08` row has status `部分`, an empty `Automated` column and a manual reference, and its `Evidence` cell must contain one of the phrases the gate accepts for a manual-only row: `不能自动化`, `无法自动化`, `硬件`, `视觉` or `真实桌面`. Use `真实桌面`.

The `SFTP-01` row lists the headless ids `SftpPaneLayoutTests.SftpPaneShowsTheRemotePaneOnTheLeftAndTheLocalPaneOnTheRight`, `SftpPaneLayoutTests.DrawerIsExpandedByDefault`, `SftpPaneLayoutTests.ToggleButtonCollapsesAndRestoresTheDrawer`, `SftpPaneLayoutTests.ToggleButtonSitsLeftOfTheEncodingCombo`, `SftpPaneLayoutTests.ToggleButtonIsHiddenForNonSshTabs` and `SshSessionViewTests.SshSessionViewStacksTheTerminalOverTheDrawer`.

The `SFTP-03`, `SFTP-04`, `SFTP-06` and `SFTP-07` rows list the engine ids `SftpTransferEngineTests.*` and the view model ids `SftpPaneViewModelTests.*`. The `SFTP-02` and `SFTP-05` rows list the view model ids and the file management ids `SftpFileManagementTests.*`.

Every id in the `Automated` column must exist in `dotnet test --list-tests` output, otherwise the gate fails with `不存在于测试列表`.

- [ ] **Step 4: Update the fixture README**

In `docs/fixtures/phase1/README.md`, add a row for `sftp-drawer-1000x650.png` describing what the screenshot proves.

- [ ] **Step 5: Run the full gate**

```bash
cd /tmp/opencode/COMTool
PATH=/tmp/opencode/e2e-deps/usr/bin:$PATH \
XVFB_BIN=/tmp/opencode/rdp-tools/root/usr/bin/Xvfb \
WM_BIN=/usr/bin/mutter \
DOTNET_ROOT=/tmp/opencode/dotnet \
DOTNET=/tmp/opencode/dotnet/dotnet \
bash avalonia/pack/verify.sh --results /tmp/opencode/verify-sftp
```

Expected: `RESULT: PASS`, `testgate.log` contains `RESULT: PASS`, and `summary.txt` reports the new test count. The first run can fail with a spurious sshd port collision; run it again in that case.

- [ ] **Step 6: Update the test counts in the docs**

Take the total from `summary.txt` and update the counts in `docs/testing/feature-test-matrix.md` and `docs/fixtures/phase1/README.md`, plus the test count in `docs/packaging/avalonia-windows-beta.md`.

- [ ] **Step 7: Commit**

```bash
cd /tmp/opencode/COMTool
git add docs
git commit -m "docs: record the SFTP drawer feature, tests and evidence"
```
