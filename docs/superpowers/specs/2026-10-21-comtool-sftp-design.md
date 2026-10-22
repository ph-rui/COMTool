# COMTool Avalonia SFTP Panel Design

## Goal

Add an embedded SFTP file browser to SSH session tabs so that COMTool can
manage remote files next to the terminal, at MobaXterm parity for the
features that MobaXterm actually ships.

The panel sits on the left of an SSH session tab, not as a standalone
session type. It offers a dual-pane (remote above, local below) browser,
button-driven transfers, and remote-side file management (delete, rename,
mkdir, chmod).

> **Revision 2026-10-22.** The panel moved from a bottom drawer that started
> expanded to a left vertical panel that starts folded away, matching what
> the user asked for after the first Windows build: the drawer took too much
> of the tab and MobaXterm keeps its file browser in a folded side panel. The
> remote list and the local list are now stacked instead of side by side,
> because the panel is narrow. The panel now asks for the SFTP subsystem when
> it is opened for the first time, not when the SSH tab is created. The rest
> of this document describes the panel as it ships after that revision; the
> sections that still name a "drawer" mean the same panel.

## Scope

Included:

- Embedded SFTP panel in SSH session tabs only. Serial, Telnet, TCP and local
  terminal tabs never show it.
- Left collapsible panel, folded away by default, toggled by a button in the
  tab strip next to the encoding combo.
- Per-session collapsed state and panel width (default 420px, draggable
  320..640), kept in memory for the life of the tab.
- Dual pane: remote (SFTP) above, local filesystem below.
- Transfers: upload (local to remote), download (remote to local), single file
  or whole directory, button driven.
- Remote file management: delete, rename, mkdir, chmod. Local pane is upload
  source and download target only.
- Conflict handling, recursive transfer, symlink policy, progress and
  cancellation, as specified under "File Operation Semantics".

Excluded from this phase:

- A standalone SFTP session type in the `+` menu and the sidebar.
- Drag and drop transfer (button driven only, by explicit request).
- Editing a remote file in an embedded editor, and remote file viewing.
- Transfer resume/append and "overwrite if newer" conflict policies.
- Creating symbolic links on the remote server.
- Local pane destructive operations (delete, rename, mkdir, chmod).
- Any change to the Python application, to `config.json`, or to the persisted
  session schema. The SFTP panel stores nothing on disk.
- Changing the new-session menu order (`Serial`, `SSH`, `Telnet`, `TCP`,
  `Local terminal`).

## Verified Platform Facts

These were checked against OpenSSH 9.x and SSH.NET 2026.0.0 on Linux before
writing this design, because several of them constrain the implementation.

| Fact | Consequence |
|---|---|
| `SftpClient` has no public or internal constructor that accepts an existing `SshClient` or session. `ChannelSession`, `SftpSession` and `SubsystemSession` are `internal`, so `SendSubsystemRequest` is unreachable. | The SFTP subsystem cannot share the terminal's SSH connection. A second SSH authentication is unavoidable (see "SshConnection Integration"). |
| A single `ConnectionInfo` instance can be used by two clients at the same time. The shell client and the SFTP client both authenticate with it and both work. | The SFTP client reuses the exact credentials of the terminal connection, with no second user prompt. |
| `SftpClient.WorkingDirectory` is the server-side home directory right after `Connect()`. | The remote pane opens at the login home directory. |
| `ListDirectory` returns absolute `FullName` values, and reports symlinks as `IsSymbolicLink = true` (lstat semantics). `GetAttributes` follows symlinks (stat semantics). | Symlinks are detected from the directory listing, never from `GetAttributes`. |
| `ListDirectory` on a symlink pointing to a directory returns the entries of the target directory, with `FullName` under the **resolved** path. | The engine must rebuild logical paths from the requested path, never from `FullName`. |
| `ListDirectory` on a symlink pointing to a file fails with `SftpPathNotFoundException`. | "Enter" on a symlink is only offered when the target is a directory. |
| `ChangePermissions(path, octal)` sets exactly the POSIX mode. Verified against the server's `stat -c %a` (`0640`, `0755`, `0600`, `0444`, `0700` all round-trip). | The permission dialog works in plain octal. |
| `SftpClient.SymbolicLink` succeeds for a relative target and fails with `SshException: Failure` for an absolute target. | The panel does not create remote symlinks in this phase. |
| A server without an `sftp` subsystem fails the SFTP connect with `SshException: Subsystem 'sftp' could not be executed.` while the shell keeps working. | The panel reports the failure inline; the terminal is never affected. |
| `internal-sftp` and the external `sftp-server` both support list, upload, download, mkdir, rename, delete and setstat. | The Linux E2E fixture can use `Subsystem sftp internal-sftp`. |

## Architecture

### Modules

New kernel project `avalonia/src/COMTool.Sftp` (no Avalonia, no SSH.NET
dependency):

| Type | Responsibility |
|---|---|
| `ISftpFileSystem` | Remote side abstraction: list, read, write, mkdir, rename, delete, chmod, exists, home directory. |
| `ILocalFileSystem` | Local side abstraction with the same shape, implemented over `System.IO`. |
| `SftpEntry` | One remote or local item: name, logical path, directory flag, symlink flag, size, modified time, permission bits. |
| `SftpPermission` | The nine read/write/execute bits, convertible to and from octal and to `rwxr-xr-x` form. |
| `SftpPath` | Remote POSIX path helpers: combine, parent, root, name. Never mixes with local Windows paths. |
| `SftpConflictAction` / `SftpConflictDecision` | `Overwrite`, `Skip`, `Rename`, `Cancel`, plus the optional new name for `Rename`. |
| `SftpConflictContext` | What the UI needs to ask the user: source, target, existing size and timestamp, suggested rename. |
| `SftpTransferRequest` / `SftpTransferOptions` | Direction, source paths, target directory, symlink and depth policy, and the conflict resolver callback. |
| `SftpTransferProgress` / `SftpTransferResult` / `SftpTransferFailure` | Progress events and the per-transfer outcome. |
| `SftpTransferEngine` | Scans the source tree, then transfers it, applying the conflict policy, symlink policy, depth cap and cancellation. |
| `SystemLocalFileSystem` | `ILocalFileSystem` over `FileInfo` / `DirectoryInfo`. |

`avalonia/src/COMTool.Connections` gains:

- `SshSftpFileSystem`: `ISftpFileSystem` over SSH.NET's `SftpClient`.
- `SshConnection.CreateSftpFileSystem()`: lazily creates the SFTP client.

The dependency direction is `COMTool.App` to `COMTool.Connections` and
`COMTool.Sftp`, and `COMTool.Connections` to `COMTool.Sftp`. `COMTool.Sftp`
depends on neither, so the kernel stays free of Avalonia and of SSH.NET.

`avalonia/src/COMTool.App` gains:

- `Views/SftpPane.axaml` and `ViewModels/SftpPaneViewModel.cs`.
- `Views/SshSessionView.axaml`: the SSH tab content, a horizontal grid of the
  SFTP panel on the left and the terminal on the right.
- `TerminalTab.Terminal` (`TerminalControl?`) and `TerminalTab.SftpPane`
  (`SftpPaneViewModel?`), replacing the `tab.Content as TerminalControl` casts
  in `MainWindow`.
- The tab-strip toggle button and its command handler.

### ISftpFileSystem

```csharp
public interface ISftpFileSystem : IAsyncDisposable
{
    string HomeDirectory { get; }

    Task<IReadOnlyList<SftpEntry>> ListAsync(string path, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken);
    Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken);
    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken);
    Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken);
    Task DeleteFileAsync(string path, CancellationToken cancellationToken);
    Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken);
    Task SetPermissionsAsync(string path, int octalMode, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken);
}
```

`OpenWriteAsync` truncates an existing target, which is what the overwrite
policy needs.

`ILocalFileSystem` mirrors this shape so the transfer engine is symmetric and
fully testable with an in-memory fake.

### Transfer Model

```csharp
public sealed record SftpTransferRequest(
    SftpTransferDirection Direction,
    IReadOnlyList<string> SourcePaths,
    string TargetDirectory,
    SftpTransferOptions Options);

public sealed record SftpTransferOptions(
    bool FollowSymbolicLinks = true,
    int MaxDepth = 16,
    int MaxFiles = 100_000,
    Func<SftpConflictContext, Task<SftpConflictDecision>>? ConflictResolver = null);
```

- `Direction` is `Upload` (local to remote) or `Download` (remote to local).
  `SourcePaths` are absolute paths on the source side, `TargetDirectory` is an
  absolute path on the target side.
- `ConflictResolver` is how the panel asks the user what to do about an
  existing target. It is optional; when it is missing, an existing target is
  **skipped** and reported in the summary. The panel always supplies one, so
  the prompt is the normal path.
- The resolver runs on the UI thread, because it shows a prompt. The engine
  awaits it between two files, never in the middle of a byte copy.
- The `Apply to all` and `Never ask again` states live in the panel's
  resolver closure, not in the engine. The engine only sees the decision for
  the conflict in front of it.

### SshConnection Integration

`SshConnection.Connect` already builds a `ConnectionInfo`. The class keeps it
and exposes:

```csharp
public ISftpFileSystem CreateSftpFileSystem();
```

- The SFTP client is created **lazily**: the shell connect never waits for
  it and neither does opening the tab. The panel starts folded away, so an
  SSH session that never opens the panel keeps a single connection. The
  first remote listing is requested when the panel is opened, in the
  background, and the SFTP client is created at that moment. Folding the
  panel again does not close the client, so opening it a second time asks
  for nothing new.
- The SFTP client authenticates with the **same** `ConnectionInfo`, so the
  same key file or the same password is used. The user is never prompted a
  second time. The password is not written to disk and no new configuration
  key is introduced.
- The SFTP client is disposed together with the `SshConnection`, so closing
  the tab releases it.
- If the connect fails (for example a server without an `sftp` subsystem),
  the panel shows the reason inline and offers a `Retry` button; folding and
  opening the panel tries again as well. The terminal keeps working, and the
  panel can be used again after a successful retry.
- Restarting a stopped session with `R` creates a new `SshConnection`. The
  panel then gets a new SFTP client and reloads the remote listing, while the
  collapsed state and the panel width stay as they were.

This is a deliberate deviation from MobaXterm, WinSCP and FileZilla, which
multiplex the SFTP channel onto the terminal connection. SSH.NET's public API
does not allow it, and reflection into its internals would break on every
library upgrade.

## Panel UI

### Layout

The SSH tab content becomes `SshSessionView`:

```
+-----------------------------+----------------------------+
| SFTP panel                  | terminal                   |
| width = PaneWidth           | column 2, star             |
| column 0, 0 when folded     |                            |
+-----------------------------+----------------------------+
      column 1: grid splitter, 4px, only when expanded
```

The panel itself is a grid:

```
+---------------------------------------------------------+
| toolbar: up | refresh | new folder | delete | rename |   |
|          chmod | upload | download | follow symlinks |    |
|          show hidden                                      |
+---------------------------------------------------------+
| remote pane: path box + list                             |
+---------------------------------------------------------+
| local pane: path box + list                              |
+---------------------------------------------------------+
| status / progress / conflict prompt                      |
+---------------------------------------------------------+
```

The remote pane is the upper side and the local pane the lower side: the
panel is narrow, so the two sides share the height. The layout keeps the
MobaXterm idea of a file browser next to the terminal.

Each pane shows a path box and a list with the columns `Name`, `Size`,
`Modified` and `Permissions`. Remote permissions use the `rwxr-xr-x` form.
Local entries carry the Windows read-only attribute as their permission
(read and write when writable, read only otherwise), because the local pane
never exposes `chmod`.

### Toggle Button

A single button in the tab strip, immediately left of the encoding combo, in
the same horizontal stack (`Grid.Column="1"` of the 36px tab-strip row). It is
visible only when the selected tab is an SSH session.

- Icon: the FontAwesome folder glyph, switching to the open-folder glyph when
  the drawer is expanded.
- Tooltip: `SFTP browser` when collapsed, `Hide SFTP browser` when expanded.
- Clicking it toggles `TerminalTab.SftpPane.IsExpanded`.

### Per-Session State

`SftpPaneViewModel` holds:

- `IsExpanded` (default `false`, so the terminal owns the tab until the
  button is pressed).
- `PaneWidth` (default 420). The view model clamps the value to 320..640;
  the grid splitter writes the dragged width back into it.
- `HasRemoteError`, which shows the `Retry` button after a failed connect.
- The current remote and local paths.
- The remote and local entry collections, the selection, the transfer state
  and the conflict prompt state.

The state lives on the tab, so switching tabs and coming back restores the
same collapsed state and height. Nothing is persisted to disk.

### Pane Behaviour

- The remote path starts at `ISftpFileSystem.HomeDirectory`.
- The local path starts at the user profile directory and is remembered in
  memory for the life of the tab.
- Double clicking a remote directory enters it; double clicking a remote
  symlink enters it only when the target is a directory.
- `Up` moves to the parent; the root is `/` for the remote pane and the drive
  root for the local pane.
- Hidden files (names starting with `.`) are shown by default. A `Show
  hidden` checkbox in the toolbar toggles them.
- The remote list refreshes automatically after entering a directory, after a
  transfer, and after delete, rename, mkdir or chmod. A `Refresh` button
  forces it.

## File Operation Semantics

The behaviour below was chosen to match WinSCP and FileZilla, the two mature
open-source SFTP clients, and to stay compatible with the simpler MobaXterm
confirmation prompt.

### Transfer Triggers and Queue

- `Upload` transfers the selected local entries into the current remote
  directory. `Download` transfers the selected remote entries into the
  current local directory.
- Selecting a directory transfers it recursively.
- Each session runs **one transfer at a time**. While a transfer runs, the
  upload and download buttons are disabled; the other buttons stay usable.
- A transfer runs on a background thread and never blocks the terminal.

### Conflict Policy

When the target file already exists, the panel shows an inline prompt strip
(not a modal dialog, so the terminal stays usable) with the actions
`Overwrite`, `Skip`, `Rename` and `Cancel`.

| Action | Behaviour |
|---|---|
| `Overwrite` | Truncate the target and write the source over it. |
| `Skip` | Keep the target, skip this file only, continue the queue. |
| `Rename` | Open a name field pre-filled with the suggested name, then write to that name. |
| `Cancel` | Abort the whole queue, including the files not started yet. |

Two checkboxes accompany the prompt:

- `Apply to all`: the chosen action is reused for every remaining conflict in
  this transfer.
- `Never ask again`: the chosen action becomes the session-wide default and
  the prompt no longer appears for this tab.

The suggested name is `name (1).ext`, incremented until it is free
(`a.txt` to `a (1).txt` to `a (2).txt`). A name without an extension becomes
`a (1)`.

Asking on every conflict is the default, which is what WinSCP and FileZilla
do. MobaXterm only asks for a yes/no confirmation; the richer set is a
superset of that.

### Recursive Transfers and Symbolic Links

- Uploading a directory creates the same directory on the remote side (an
  existing directory is reused, not an error) and copies the tree.
- Downloading a directory creates the same directory locally and copies the
  tree.
- Remote symbolic links are shown with an `@` marker in the name column.
- Symbolic links are **followed** by default: their target content is
  transferred. The panel cannot create a Windows symlink without elevated
  privileges, and creating a remote symlink would produce dangling links.
- A `Follow symbolic links` checkbox in the toolbar switches this off. Links
  are then skipped and listed in the transfer summary. The checkbox applies
  to the transfers started after it is toggled.
- A cycle guard limits the recursion to **16 levels** and the transfer to
  100 000 files. When the depth limit is hit, the remaining branches are
  skipped and the summary says so.
- File names are used exactly as the server reports them. No case folding is
  applied.

### Permissions (chmod)

- `chmod` applies to the remote pane only.
- The dialog has a three by three grid of checkboxes (owner, group, others by
  read, write, execute) and a read-only octal preview such as `644`. The two
  representations stay in sync in both directions.
- The execute bit is **independent** of the read bit, which is the POSIX
  behaviour. `r--` plus `--x` is a valid `111`.
- Applying the change refreshes the remote list and shows the new mode.
- If the server refuses the change, the panel reports `chmod failed:
  <reason>` and stays usable.

### Cancellation and Progress

- The status area shows `Uploading 3/12 - 1.2 MB / 8.4 MB (14%)` with a
  progress bar and a `Cancel` button.
- The engine runs a scan phase first, so the total file count and total size
  are known before the first byte moves. This is the same behaviour as
  WinSCP's "calculate total size" option.
- `Cancel` stops the queue immediately and removes the partially written
  file, so no truncated file is left behind. The status area shows
  `Transfer cancelled`.
- Closing the tab cancels the running transfer the same way and disposes the
  SFTP client.
- When the queue ends, the status area shows a summary such as
  `12 files uploaded (8.4 MB)` or `10 uploaded, 2 failed`. Failed entries are
  listed with their reason in a scrollable area inside the panel.

### Errors

- Every failure is reported with the file name and the reason, for example
  `Upload failed: boot.log - Permission denied`.
- A failure of one file does not stop the queue. The summary reports how many
  succeeded, were skipped and failed.
- A failure of the SFTP connection itself (for example no `sftp` subsystem on
  the server) is shown in the panel with a `Retry` button and never as a
  modal error, and never disturbs the terminal.

## Testing Strategy

Every feature has an automated test. Screenshots and manual clicks are
evidence only, never the proof.

### Layers

| Layer | Location | Needs a real server |
|---|---|---|
| L1 kernel unit tests | new `avalonia/tests/COMTool.Sftp.Tests` | no |
| L2 protocol integration | `avalonia/tests/COMTool.Connections.Tests/SftpIntegrationTests.cs` | yes, on Linux |
| L3 headless app tests | `avalonia/tests/COMTool.App.Tests/SftpPaneTests.cs` | no |
| L4 end-to-end | `avalonia/tests/e2e/linux/run-ui-e2e.sh` | yes, on Linux |

### L1: kernel unit tests

Against an in-memory `ISftpFileSystem` and `ILocalFileSystem` fake.

- `SftpPathTests`: remote combine, parent, root, name; remote paths always
  use `/` and are never mixed with local Windows paths; `..` cannot escape
  the root.
- `SftpPermissionTests`: nine bit to octal round trip (`755` to
  `rwxr-xr-x`); the execute bit is independent of the read bit; checkbox and
  numeric edits stay in sync.
- `SftpConflictTests`: the conflict state machine. Missing target transfers
  directly. An existing target asks. `Skip` skips only the current file and
  the queue continues. `Cancel` aborts the whole queue. `Apply to all`
  suppresses later prompts. `Never ask again` is remembered for the session.
  The suggested name is `a (1).txt`, then `a (2).txt`, and `a (1)` for a name
  without an extension.
- `SftpTransferEngineTests`: upload and download are byte identical; a
  directory upload creates the remote directory; a directory download creates
  the local directory; a three level tree arrives completely; the three
  conflict actions behave as specified; progress events increase monotonically
  and report the correct totals; cancelling stops the queue and removes the
  partial file; one failing file does not stop the queue and the summary
  reports it; a symlink is followed when the option is on and skipped with a
  note when it is off; a twenty level cycle stops at the depth limit of 16.

### L2: protocol integration tests

`SftpIntegrationTests` starts an ephemeral sshd, exactly like the existing
`SshIntegrationTests` does, and skips on non-Linux hosts. The sshd config
adds `Subsystem sftp internal-sftp`.

- The SFTP client lists the home directory.
- A one megabyte random file is uploaded and the server side `sha256sum`
  matches.
- The file is downloaded again and the bytes match.
- `mkdir`, `rename` and `delete` are visible on the server.
- `chmod 0640` is confirmed by the server side `stat -c %a`.
- A symlink created on the server is reported as `IsSymbolicLink = true`.
- A server without an `sftp` subsystem makes `CreateSftpFileSystem()` fail
  with a recognisable error while the shell connection keeps working.
- Disposing the `SshConnection` releases the SFTP client.

### L3: headless app tests

Avalonia headless, asserting the real layout and the view model.

- The panel is folded away by default (the terminal gets the full width),
  the toggle button opens it, a second click folds it away again.
- The collapsed state and the width survive a tab switch.
- A folded panel never asks for the remote file system; the first opening
  asks once, and folding and opening again asks nothing new.
- The remote list is populated after loading and reloaded by `Refresh`.
- The local list shows the local directory.
- `Upload` and `Download` are disabled without a selection and enabled with
  one.
- `Delete`, `Rename`, `New folder` and `chmod` are enabled in the remote pane
  and disabled in the local pane.
- During a transfer the upload and download buttons are disabled and `Cancel`
  is enabled.
- A failure produces a status message that contains `failed`.
- The toggle button sits to the left of the encoding combo, asserted from the
  real `Bounds`, in the same style as the existing tab icon spacing test.
- The drawer exists in the SSH session view and the toggle button is absent
  for serial, TCP and local terminal tabs.

### L4: end-to-end test

One new case, `SFTP01`, on the existing Xvfb harness. The sshd fixture gains
`Subsystem sftp internal-sftp`.

1. The app logs in with the fixture key.
2. The SFTP button opens the panel, the server logs the `sftp` subsystem
   request, and the remote pane lists the home directory.
3. A local file is uploaded and the server side checksum matches.
4. A server side file is downloaded and the local content matches.
5. A conflicting upload raises the prompt, `Overwrite` is chosen and the
   target content is replaced.
6. A screenshot is stored under `docs/fixtures/phase1/`.

### Mutation checks

Each L1, L2 and L3 test is mutation checked while it is written: the
implementation is broken on purpose and the test has to fail. A test that
cannot fail is not accepted.

## Documentation and Gate

- `avalonia/src/COMTool.Sftp` and `avalonia/tests/COMTool.Sftp.Tests` are
  added to `avalonia/pack/verify.sh`, so the unified gate covers them.
- `docs/testing/feature-test-matrix.md` and `docs/fixtures/phase1/README.md`
  get the new test rows and the updated test count.
- `docs/parity-matrix.md` gets the SFTP rows.

## Risks and Known Deviations

- **A second SSH connection once the panel is opened.** Caused by the
  SSH.NET API, not by the design. The cost is one extra handshake and one
  extra connection on the server, started in the background the first time
  the panel is opened. A session whose panel stays folded keeps one
  connection. The terminal is usable while it happens.
- **Symbolic links are followed, not preserved.** A consequence of the
  Windows target and of the missing `readlink` API in SSH.NET. The
  `Follow symbolic links` checkbox and the depth limit keep it predictable.
- **No remote symlink creation.** SSH.NET's `SymbolicLink` fails for absolute
  targets on OpenSSH, and dangling links are a poor default.
- **No transfer resume.** SFTP resume needs offset handling and a partially
  written file policy, which is out of scope for this phase.
- **The conflict prompt needs the user.** The drawer cannot resolve conflicts
  on its own, so an unattended transfer can stall on a prompt. The status area
  shows which file is waiting.
