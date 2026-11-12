using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Sftp;
using COMTool.Sftp.Testing;

namespace COMTool.App.Tests;

public sealed class SftpFileManagementTests
{
    private const string RemoteHome = "/remote";
    private const string LocalHome = "/local";

    [AvaloniaFact]
    public void PermissionsWindowKeepsTheCheckboxGridAndTheOctalFieldInSync()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var window = new SftpPermissionsWindow { Permission = SftpPermission.FromOctalText("640") };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("640", window.OctalText);
            Assert.True(window.OwnerReadBoxControl.IsChecked == true);
            Assert.True(window.OwnerWriteBoxControl.IsChecked == true);
            Assert.False(window.OwnerExecuteBoxControl.IsChecked == true);
            Assert.True(window.GroupReadBoxControl.IsChecked == true);
            Assert.False(window.GroupWriteBoxControl.IsChecked == true);
            Assert.False(window.GroupExecuteBoxControl.IsChecked == true);
            Assert.False(window.OthersReadBoxControl.IsChecked == true);
            Assert.False(window.OthersWriteBoxControl.IsChecked == true);
            Assert.False(window.OthersExecuteBoxControl.IsChecked == true);

            // A ticked box changes the field.
            window.OwnerExecuteBoxControl.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("740", window.OctalText);
            Assert.Equal(SftpPermission.FromOctalText("740"), window.Permission);

            // A mode that is typed changes the boxes.
            window.OctalBoxControl.Text = "755";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("755", window.OctalText);
            Assert.True(window.OwnerExecuteBoxControl.IsChecked == true);
            Assert.True(window.GroupExecuteBoxControl.IsChecked == true);
            Assert.True(window.OthersExecuteBoxControl.IsChecked == true);
            Assert.True(window.OthersReadBoxControl.IsChecked == true);
            Assert.False(window.OthersWriteBoxControl.IsChecked == true);

            window.Close();
        });
    }

    [AvaloniaFact]
    public void PermissionsWindowKeepsTheExecuteBitIndependentOfTheReadBit()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var window = new SftpPermissionsWindow { Permission = SftpPermission.FromOctalText("111") };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("111", window.OctalText);
            Assert.True(window.OwnerExecuteBoxControl.IsChecked == true);
            Assert.True(window.GroupExecuteBoxControl.IsChecked == true);
            Assert.True(window.OthersExecuteBoxControl.IsChecked == true);
            Assert.False(window.OwnerReadBoxControl.IsChecked == true);
            Assert.False(window.OwnerWriteBoxControl.IsChecked == true);
            Assert.False(window.GroupReadBoxControl.IsChecked == true);
            Assert.False(window.OthersReadBoxControl.IsChecked == true);

            window.Close();
        });
    }

    // ───────────────── r62：权限窗对齐 MobaXterm（rwx 字符串 + 标题 + 取消按钮） ─────────────────

    [AvaloniaFact]
    public void SFTP12_PermissionsWindowSyncsRwxStringWithBoxesAndOctal()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var window = new SftpPermissionsWindow
            {
                Permission = SftpPermission.FromOctalText("644"),
                TargetName = "a.txt",
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("rw-r--r--", window.RwxText);
            Assert.Equal("644", window.OctalText);

            // 输入完整 rwx 串 → 复选框与八进制跟随
            window.RwxBoxControl.Text = "rwxr-xr-x";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("755", window.OctalText);
            Assert.True(window.OwnerExecuteBoxControl.IsChecked == true);
            Assert.True(window.GroupExecuteBoxControl.IsChecked == true);
            Assert.True(window.OthersExecuteBoxControl.IsChecked == true);

            // 取消勾选 → rwx 串与八进制跟随
            window.GroupExecuteBoxControl.IsChecked = false;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("rwxr--r-x", window.RwxText);
            Assert.Equal("745", window.OctalText);

            window.Close();
        });
    }

    [AvaloniaFact]
    public void SFTP12_PermissionsWindowTitleShowsTheEntryName()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var window = new SftpPermissionsWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Permissions", window.Title);

            window.TargetName = "screen.jpg";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Permissions for \"screen.jpg\"", window.Title);

            window.Close();
        });
    }

    [AvaloniaFact]
    public void SFTP12_PermissionsWindowCancelClosesWithoutApplying()
    {
        using var env = new TestEnvironment();
        var applied = 0;
        env.Run(() =>
        {
            var window = new SftpPermissionsWindow
            {
                Permission = SftpPermission.FromOctalText("644"),
            };
            // r63: 事件签名改为 Func<SftpPermission, Task>（窗口 await 期间显示瞬态标题）
            window.PermissionApplied += _ =>
            {
                applied++;
                return Task.CompletedTask;
            };
            var closed = false;
            window.Closed += (_, _) => closed = true;
            window.Show();

            window.CancelButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.True(closed);
        });
        Assert.Equal(0, applied);
    }

    [AvaloniaFact]
    public void SFTP12_PermissionsWindowIgnoresInvalidRwxText()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var window = new SftpPermissionsWindow
            {
                Permission = SftpPermission.FromOctalText("644"),
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.RwxBoxControl.Text = "abc";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SftpPermission.FromOctalText("644"), window.Permission);
            Assert.Equal("644", window.OctalText);

            window.RwxBoxControl.Text = "rw-r--r-";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SftpPermission.FromOctalText("644"), window.Permission);

            window.Close();
        });
    }

    // ───────────── r63：权限窗版式对齐 MobaXterm（抬头/右对齐标签/居中按钮/瞬态标题） ─────────────

    [AvaloniaFact]
    public void SFTP13_PermissionsWindowShowsCenteredBoldHeading()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var window = new SftpPermissionsWindow { TargetName = "Image" };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // MobaXterm：窗口体内有居中粗体抬头 Permissions for "name"
            Assert.Equal("Permissions for \"Image\"", window.HeadingText);
            Assert.Equal(FontWeight.Bold, window.HeadingControl.FontWeight);
            Assert.Equal(HorizontalAlignment.Center, window.HeadingControl.HorizontalAlignment);
            // 标题栏保持 r62 行为
            Assert.Equal("Permissions for \"Image\"", window.Title);

            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [AvaloniaFact]
    public void SFTP13_PermissionsWindowLayoutUsesMobaXtermLabels()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var window = new SftpPermissionsWindow { TargetName = "a.txt" };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("权限:", window.RwxLabelControl.Text);
            Assert.Equal("用户:", window.OwnerLabelControl.Text);
            Assert.Equal("群组:", window.GroupLabelControl.Text);
            Assert.Equal("其他:", window.OthersLabelControl.Text);
            Assert.Equal("八进制模式:", window.OctalLabelControl.Text);
            Assert.Equal(FontWeight.Bold, window.OwnerLabelControl.FontWeight);
            Assert.Equal(FontWeight.Bold, window.GroupLabelControl.FontWeight);
            Assert.Equal(FontWeight.Bold, window.OthersLabelControl.FontWeight);
            Assert.Equal(HorizontalAlignment.Right, window.RwxLabelControl.HorizontalAlignment);
            Assert.Equal(HorizontalAlignment.Center, window.ButtonRowControl.HorizontalAlignment);
            Assert.True(window.Width >= 400, "r63: 窗口应比 r62 的 320px 更宽");

            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [AvaloniaFact]
    public async Task SFTP13_PermissionsWindowApplyShowsTransientTitleThenCloses()
    {
        using var env = new TestEnvironment();
        SftpPermission? applied = null;
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await env.RunAsync(async () =>
        {
            var window = new SftpPermissionsWindow
            {
                Permission = SftpPermission.FromOctalText("644"),
                TargetName = "Image",
            };
            window.PermissionApplied = permission =>
            {
                applied = permission;
                started.TrySetResult(true);
                return Task.Delay(250);
            };
            var closed = false;
            window.Closed += (_, _) => closed = true;
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.ApplyButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // 应用期间：标题变「正在修改权限...」，按钮禁用，窗口保持打开
            Assert.Equal("正在修改权限...", window.Title);
            Assert.False(window.ApplyButtonControl.IsEnabled);
            Assert.False(window.CancelButtonControl.IsEnabled);

            await started.Task;
            await Task.Delay(400);
            Dispatcher.UIThread.RunJobs();
            Assert.True(closed);
        });
        Assert.Equal("644", applied?.ToOctalText());
    }

    [AvaloniaFact]
    public async Task SFTP13_PermissionsWindowApplyClosesEvenOnFailure()
    {
        using var env = new TestEnvironment();
        await env.RunAsync(async () =>
        {
            var window = new SftpPermissionsWindow { TargetName = "x" };
            window.PermissionApplied = _ => Task.FromException(new InvalidOperationException("chmod failed"));
            var closed = false;
            window.Closed += (_, _) => closed = true;
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.ApplyButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(200);
            Dispatcher.UIThread.RunJobs();

            // 写回失败不崩溃：窗口照常关闭（错误已由 VM 写入 SFTP 状态栏）
            Assert.True(closed);
        });
    }

    [AvaloniaFact]
    public async Task NewFolderButtonCreatesTheRemoteDirectory()
    {
        using var env = new TestEnvironment();
        var remote = new InMemoryFileSystem(RemoteHome);
        var viewModel = new SftpPaneViewModel(() => remote, new InMemoryFileSystem(LocalHome));
        await viewModel.LoadAsync(CancellationToken.None);

        var (pane, window) = NewDrawer(viewModel);
        try
        {
            Assert.False(pane.PromptRowControl.IsVisible);

            pane.NewFolderButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // The icon button opens the inline prompt row asking for the
            // new folder name.
            Assert.True(pane.PromptRowControl.IsVisible);
            Assert.Equal("新建文件夹", pane.PromptLabelControl.Text);

            pane.PromptBoxControl.Text = "folder";
            pane.PromptOkButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.True(remote.HasEntry("/remote/folder"));
            Assert.Equal("Created folder", viewModel.StatusMessage);
            Assert.False(pane.PromptRowControl.IsVisible);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task RenameButtonRenamesTheSelectedRemoteEntry()
    {
        using var env = new TestEnvironment();
        var remote = new InMemoryFileSystem(RemoteHome);
        remote.AddFile("/remote/a.txt", "a");
        var viewModel = new SftpPaneViewModel(() => remote, new InMemoryFileSystem(LocalHome));
        await viewModel.LoadAsync(CancellationToken.None);

        var (pane, window) = NewDrawer(viewModel);
        try
        {
            viewModel.SelectedRemoteEntry = viewModel.RemoteEntries.Single(entry => entry.Name == "a.txt");
            Dispatcher.UIThread.RunJobs();

            Assert.True(pane.RenameButtonControl.IsEnabled);

            pane.RenameButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // The prompt is prefilled with the current name.
            Assert.True(pane.PromptRowControl.IsVisible);
            Assert.Equal("重命名", pane.PromptLabelControl.Text);
            Assert.Equal("a.txt", pane.PromptBoxControl.Text);

            pane.PromptBoxControl.Text = "b.txt";
            pane.PromptOkButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.True(remote.HasEntry("/remote/b.txt"));
            Assert.False(remote.HasEntry("/remote/a.txt"));
            Assert.Equal("Renamed to b.txt", viewModel.StatusMessage);
            Assert.False(pane.PromptRowControl.IsVisible);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task DeleteButtonAsksBeforeItDeletesTheSelectedRemoteEntry()
    {
        using var env = new TestEnvironment();
        var remote = new InMemoryFileSystem(RemoteHome);
        remote.AddFile("/remote/a.txt", "a");
        var viewModel = new SftpPaneViewModel(() => remote, new InMemoryFileSystem(LocalHome));
        await viewModel.LoadAsync(CancellationToken.None);

        var (pane, window) = NewDrawer(viewModel);
        try
        {
            viewModel.SelectedRemoteEntry = viewModel.RemoteEntries.Single(entry => entry.Name == "a.txt");
            Dispatcher.UIThread.RunJobs();

            pane.DeleteButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // The first click only arms, the second one deletes.
            Assert.True(remote.HasEntry("/remote/a.txt"));
            Assert.True(pane.IsDeleteArmed);

            pane.DeleteButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.False(remote.HasEntry("/remote/a.txt"));
            Assert.Equal("Deleted a.txt", viewModel.StatusMessage);
            Assert.False(pane.IsDeleteArmed);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void PanelHasNoLocalPane()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var viewModel = new SftpPaneViewModel(
                () => new InMemoryFileSystem(RemoteHome),
                new InMemoryFileSystem(LocalHome));
            var (pane, window) = NewDrawer(viewModel);
            try
            {
                // The redesign is a single remote list: no local list, no
                // local path box, no second pane.
                Assert.Null(pane.FindControl<Control>("LocalList"));
                Assert.Null(pane.FindControl<Control>("LocalPathBox"));
                Assert.Null(pane.FindControl<Control>("LocalPaneGrid"));
                Assert.NotNull(pane.FindControl<Control>("RemoteList"));
                Assert.NotNull(pane.FindControl<Control>("RemotePathBox"));
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [AvaloniaFact]
    public async Task ChmodFailureIsReportedInline()
    {
        using var env = new TestEnvironment();
        var remote = new InMemoryFileSystem(RemoteHome);
        remote.AddFile("/remote/a.txt", "a");
        var viewModel = new SftpPaneViewModel(
            () => new RefusingRemoteFileSystem(remote),
            new InMemoryFileSystem(LocalHome));
        await viewModel.LoadAsync(CancellationToken.None);

        var (pane, window) = NewDrawer(viewModel);
        try
        {
            viewModel.SelectedRemoteEntry = viewModel.RemoteEntries.Single(entry => entry.Name == "a.txt");
            Dispatcher.UIThread.RunJobs();

            pane.ChmodButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var permissions = pane.PermissionsWindow;
            Assert.NotNull(permissions);

            permissions!.OctalBoxControl.Text = "600";
            Dispatcher.UIThread.RunJobs();
            permissions.ApplyButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("chmod failed", viewModel.StatusMessage);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static (SftpPane Pane, Window Window) NewDrawer(SftpPaneViewModel viewModel)
    {
        var pane = new SftpPane { ViewModel = viewModel };
        var window = new Window { Content = pane, Width = 900, Height = 500 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (pane, window);
    }

    /// <summary>
    /// A remote that refuses to change the permissions, so that the
    /// message of the failure can be seen.
    /// </summary>
    private sealed class RefusingRemoteFileSystem : ISftpFileSystem
    {
        private readonly ISftpFileSystem _inner;

        public RefusingRemoteFileSystem(ISftpFileSystem inner)
        {
            _inner = inner;
        }

        public string HomeDirectory => _inner.HomeDirectory;

        public Task<IReadOnlyList<SftpEntry>> ListAsync(string path, CancellationToken cancellationToken)
            => _inner.ListAsync(path, cancellationToken);

        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
            => _inner.OpenReadAsync(path, cancellationToken);

        public Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken)
            => _inner.OpenWriteAsync(path, cancellationToken);

        public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
            => _inner.CreateDirectoryAsync(path, cancellationToken);

        public Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken)
            => _inner.RenameAsync(oldPath, newPath, cancellationToken);

        public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
            => _inner.DeleteFileAsync(path, cancellationToken);

        public Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken)
            => _inner.DeleteDirectoryAsync(path, recursive, cancellationToken);

        public Task SetPermissionsAsync(string path, int octalMode, CancellationToken cancellationToken)
            => throw new InvalidOperationException("the server refused the mode");

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
            => _inner.ExistsAsync(path, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
