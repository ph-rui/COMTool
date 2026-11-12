using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Core.Config;
using COMTool.Sftp;
using COMTool.Sftp.Testing;

namespace COMTool.App.Tests;

/// <summary>
/// r60: the MobaXterm-style right-click context menu of the SFTP browser,
/// the built-in text/image viewers, the properties window and the
/// middle-click "send path to the terminal" behaviour.
/// </summary>
public sealed class SftpContextMenuTests
{
    private const string RemoteHome = "/remote";
    private const string LocalHome = "/local";
    private const string CacheRoot = "/local-cache";

    /// <summary>
    /// Records the programs a launcher was asked to start.
    /// </summary>
    private sealed class FakeLauncher : ILocalFileLauncher
    {
        public readonly List<(string? Program, string Path)> Calls = new();

        public bool OpenWithDefaultProgram(string filePath)
        {
            Calls.Add((null, filePath));
            return true;
        }

        public bool OpenWithProgram(string programPath, string filePath)
        {
            Calls.Add((programPath, filePath));
            return true;
        }
    }

    /// <summary>
    /// A file picker with a fixed answer, so "Open with" is testable.
    /// </summary>
    private sealed class FixedPickers : ILocalPickers
    {
        private readonly string[] _files;

        public FixedPickers(string[] files) => _files = files;

        public Task<IReadOnlyList<string>> PickFilesAsync(string title)
            => Task.FromResult<IReadOnlyList<string>>(_files);

        public Task<string?> PickFolderAsync(string title)
            => Task.FromResult<string?>(null);
    }

    private static async Task<(MainWindow Window, SftpPaneViewModel Pane, InMemoryFileSystem Remote, InMemoryFileSystem Local, SftpPane View)> Setup(
        Action<InMemoryFileSystem> fillRemote)
    {
        var remote = new InMemoryFileSystem(RemoteHome);
        fillRemote(remote);
        var local = new InMemoryFileSystem(LocalHome);
        var pane = new SftpPaneViewModel(() => remote, local) { CacheRoot = CacheRoot };

        var window = new MainWindow { DataContext = new MainViewModel() };
        window.Show();
        var viewModel = (MainViewModel)window.DataContext;
        var tab = new TerminalTab
        {
            Title = "Ssh",
            SessionId = "ssh:test",
            Kind = SessionKind.Ssh,
            Content = new SshSessionView(),
            SftpPane = pane,
        };
        viewModel.AddTab(tab);
        window.UpdateConnectionStatesForTests();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var sidebar = window.FindControl<SessionSidebar>("Sidebar")!;
        sidebar.FindControl<Button>("RailSshBrowserButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var browser = sidebar.FindControl<SshBrowserPanel>("SshBrowserPanel")!;
        var view = browser.SftpPaneControl!;

        // The rail-click starts the initial load asynchronously; wait for it
        // to settle so tests never observe an empty list.
        await WaitUntil(
            () => pane.RemoteEntries.Count > 0 || pane.HasRemoteError,
            "load the remote listing");
        Assert.True(pane.RemoteEntries.Count > 0, "the remote listing did not load");

        return (window, pane, remote, local, view);
    }

    private static async Task WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Fail($"The {what} did not happen in time.");
    }

    private static void RaiseDoubleTap(Control control, Window window)
    {
        var position = control.TranslatePoint(new Point(10, 10), window) ?? new Point(10, 10);
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var press = new PointerPressedEventArgs(
            control,
            pointer,
            window,
            position,
            0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            clickCount: 2);
        control.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, press));
    }

    [AvaloniaFact]
    public async Task SFTP10_RemoteListContextMenuHasTheMobaXtermItems()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, _, _, var view) = await Setup(remote =>
                {
                    remote.AddDirectory("/remote/dir");
                    remote.AddFile("/remote/a.txt", "a");
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);

                var menu = view.RemoteContextMenuControl;
                Assert.NotNull(menu);

                // r62: 16 items = 5 blank-area (directory) items + 11 entry items
                // (MobaXterm order), 3 separators in the entry part.
                var items = menu!.Items.OfType<MenuItem>().ToList();
                Assert.Equal(16, items.Count);
                Assert.Equal(3, menu.Items.OfType<Separator>().Count());

                var headers = items.Select(item => item.Header?.ToString()).ToArray();
                Assert.Equal(new object?[]
                {
                    "上一级目录", "新建文件夹", "新建文件", "刷新当前文件夹列表", "上传到当前文件夹",
                    "打开",
                    "使用默认文本文编辑器打开",
                    "打开方式...",
                    "使用默认程序打开...",
                    "下载",
                    "删除",
                    "重命名",
                    "复制文件路径",
                    "将文件路径复制到终端（鼠标中键单击）",
                    "属性",
                    "权限",
                },
                headers);

                // No selection: the blank-area menu is shown, entry menu hidden.
                foreach (var item in items.Take(5))
                {
                    Assert.True(item.IsVisible);
                }

                foreach (var item in items.Skip(5))
                {
                    Assert.False(item.IsVisible);
                }

                // Mark a file: the entry menu is shown, blank-area menu hidden.
                pane.SelectedRemoteEntry = pane.RemoteEntries.Single(entry => entry.Name == "a.txt");
                Dispatcher.UIThread.RunJobs();

                foreach (var item in items.Take(5))
                {
                    Assert.False(item.IsVisible);
                }

                foreach (var item in items.Skip(5))
                {
                    Assert.True(item.IsVisible);
                }
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task SFTP10_FileOnlyMenuItemsAreHiddenForADirectory()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, _, _, var view) = await Setup(remote =>
                {
                    remote.AddDirectory("/remote/dir");
                    remote.AddFile("/remote/a.txt", "a");
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);

                pane.SelectedRemoteEntry = pane.RemoteEntries.Single(entry => entry.Name == "dir");
                Dispatcher.UIThread.RunJobs();

                // r62: entry menu active -> blank-area items hidden as well
                Assert.False(view.MenuBlankNewFileControl.IsVisible);
                Assert.False(view.MenuBlankRefreshControl.IsVisible);

                Assert.False(view.MenuOpenTextEditorControl.IsVisible);
                Assert.False(view.MenuOpenWithControl.IsVisible);
                Assert.False(view.MenuOpenSystemControl.IsVisible);
                Assert.True(view.MenuOpenControl.IsVisible);
                Assert.True(view.MenuDownloadControl.IsVisible);
                Assert.True(view.MenuDeleteControl.IsVisible);
                Assert.True(view.MenuRenameControl.IsVisible);
                Assert.True(view.MenuCopyPathControl.IsVisible);
                Assert.True(view.MenuPropertiesControl.IsVisible);
                Assert.True(view.MenuPermissionsControl.IsVisible);

                pane.SelectedRemoteEntry = pane.RemoteEntries.Single(entry => entry.Name == "a.txt");
                Dispatcher.UIThread.RunJobs();

                Assert.True(view.MenuOpenTextEditorControl.IsVisible);
                Assert.True(view.MenuOpenWithControl.IsVisible);
                Assert.True(view.MenuOpenSystemControl.IsVisible);
                Assert.False(view.MenuBlankNewFileControl.IsVisible);
                Assert.True(view.MenuOpenControl.IsVisible);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task SFTP10_DoubleClickAFileOpensTheBuiltinTextViewer()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, _, var local, var view) = await Setup(remote =>
                {
                    remote.AddFile("/remote/a.txt", "hello r60\nsecond line");
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);
                var launcher = new FakeLauncher();
                view.FileLauncher = launcher;

                pane.SelectedRemoteEntry = pane.RemoteEntries.Single(entry => entry.Name == "a.txt");
                RaiseDoubleTap(view.RemoteListControl, window);

                await WaitUntil(() => view.TextViewerWindowControl is not null, "open the text viewer");

                var textWindow = view.TextViewerWindowControl!;
                Assert.Contains("hello r60", textWindow.TextHostControl.Text);
                Assert.True(textWindow.TextHostControl.IsReadOnly);
                // The built-in viewer reads the bytes in memory: no external
                // program may start and no cache file is written.
                Assert.Empty(launcher.Calls);
                Assert.Null(local.ReadFile(System.IO.Path.Combine(CacheRoot, "a.txt")));
                textWindow.Close();
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task SFTP10_DoubleClickADirectoryStillEntersIt()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, _, _, var view) = await Setup(remote =>
                {
                    remote.AddDirectory("/remote/dir");
                    remote.AddFile("/remote/dir/inner.txt", "x");
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);

                pane.SelectedRemoteEntry = pane.RemoteEntries.Single(entry => entry.Name == "dir");
                RaiseDoubleTap(view.RemoteListControl, window);

                await WaitUntil(() => pane.RemotePath == "/remote/dir", "enter the directory");
                Assert.Contains(pane.RemoteEntries, entry => entry.Name == "inner.txt");
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task SFTP10_RightClickSelectsTheRowUnderThePointer()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, _, _, var view) = await Setup(remote =>
                {
                    remote.AddDirectory("/remote/dir");
                    remote.AddFile("/remote/a.txt", "a");
                    remote.AddFile("/remote/b.txt", "b");
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);

                Assert.Null(pane.SelectedRemoteEntry);
                var container = view.RemoteListControl.ContainerFromIndex(2);
                Assert.NotNull(container);
                var pt = container!.TranslatePoint(
                    new Point(container.Bounds.Width / 2, container.Bounds.Height / 2), window);
                Assert.NotNull(pt);
                window.MouseDown(pt!.Value, MouseButton.Right);
                window.MouseUp(pt.Value, MouseButton.Right);
                Dispatcher.UIThread.RunJobs();

                Assert.NotNull(pane.SelectedRemoteEntry);
                Assert.Equal("b.txt", pane.SelectedRemoteEntry!.Name);
                // File-only menu items are visible for the freshly marked file.
                Assert.True(view.MenuOpenTextEditorControl.IsVisible);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task SFTP10_MiddleClickSendsTheMarkedPathToTheTerminal()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, _, _, var view) = await Setup(remote =>
                {
                    remote.AddFile("/remote/a.txt", "a");
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);

                var sent = new List<string>();
                pane.PathToTerminal = sent.Add;
                pane.SelectedRemoteEntry = pane.RemoteEntries.Single(entry => entry.Name == "a.txt");

                var list = view.RemoteListControl;
                var center = list.TranslatePoint(
                    new Point(list.Bounds.Width / 2, list.Bounds.Height / 2), window);
                Assert.NotNull(center);
                window.MouseDown(center!.Value, MouseButton.Middle);
                window.MouseUp(center.Value, MouseButton.Middle);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("/remote/a.txt", sent.Single());
                Assert.Contains("/remote/a.txt", pane.StatusMessage);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task SFTP10_MenuOpenWithDefaultProgramCachesAndLaunches()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, _, var local, var view) = await Setup(remote =>
                {
                    remote.AddFile("/remote/firmware.bin", "BIN");
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);
                var launcher = new FakeLauncher();
                view.FileLauncher = launcher;

                pane.SelectedRemoteEntry = pane.RemoteEntries.Single(entry => entry.Name == "firmware.bin");
                view.MenuOpenSystemControl.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                await WaitUntil(() => launcher.Calls.Count == 1, "launch the default program");

                var (program, path) = launcher.Calls[0];
                Assert.Null(program);
                Assert.Equal(System.IO.Path.Combine(CacheRoot, "firmware.bin"), path);
                Assert.Equal("BIN", local.ReadFile(path));
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task SFTP10_MenuOpenWithTextEditorUsesTheTextEditorProgram()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, _, _, var view) = await Setup(remote =>
                {
                    remote.AddFile("/remote/a.txt", "a");
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);
                var launcher = new FakeLauncher();
                view.FileLauncher = launcher;

                pane.SelectedRemoteEntry = pane.RemoteEntries.Single(entry => entry.Name == "a.txt");
                view.MenuOpenTextEditorControl.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                await WaitUntil(() => launcher.Calls.Count == 1, "launch the text editor");

                var (program, path) = launcher.Calls[0];
                Assert.Equal(SystemLocalFileLauncher.TextEditorProgram, program);
                Assert.Equal(System.IO.Path.Combine(CacheRoot, "a.txt"), path);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task SFTP10_MenuOpenWithPicksTheProgramAndLaunchesIt()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, _, _, var view) = await Setup(remote =>
                {
                    remote.AddFile("/remote/firmware.bin", "B");
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);
                var launcher = new FakeLauncher();
                view.FileLauncher = launcher;
                view.Pickers = new FixedPickers(new[] { "C:\\tools\\diff.exe" });

                pane.SelectedRemoteEntry = pane.RemoteEntries.Single(entry => entry.Name == "firmware.bin");
                view.MenuOpenWithControl.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                await WaitUntil(() => launcher.Calls.Count == 1, "launch the picked program");

                Assert.Equal("C:\\tools\\diff.exe", launcher.Calls[0].Program);
                Assert.Equal(System.IO.Path.Combine(CacheRoot, "firmware.bin"), launcher.Calls[0].Path);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task SFTP10_MenuCopyPathCopiesTheRemotePath()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, _, _, var view) = await Setup(remote =>
                {
                    remote.AddFile("/remote/a.txt", "a");
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);

                pane.SelectedRemoteEntry = pane.RemoteEntries.Single(entry => entry.Name == "a.txt");
                view.MenuCopyPathControl.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("/remote/a.txt", view.LastCopiedPath);
                Assert.Contains("/remote/a.txt", pane.StatusMessage);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task SFTP10_MenuSendPathInvokesTheTerminalCallback()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, _, _, var view) = await Setup(remote =>
                {
                    remote.AddFile("/remote/a.txt", "a");
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);

                var sent = new List<string>();
                pane.PathToTerminal = sent.Add;
                pane.SelectedRemoteEntry = pane.RemoteEntries.Single(entry => entry.Name == "a.txt");
                view.MenuSendPathControl.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("/remote/a.txt", sent.Single());
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task SFTP10_MenuPropertiesShowsTheEntryInfo()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, _, _, var view) = await Setup(fs =>
                {
                    fs.AddFile("/remote/a.txt", "hello");
                    fs.Owner = "devuser";
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);

                pane.SelectedRemoteEntry = pane.RemoteEntries.Single(entry => entry.Name == "a.txt");
                view.MenuPropertiesControl.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                await WaitUntil(() => view.PropertiesWindow is not null, "open the properties window");

                var props = view.PropertiesWindow!;
                Assert.Equal("a.txt", props.NameTextControl.Text);
                Assert.Equal("/remote/a.txt", props.PathTextControl.Text);
                Assert.Equal("文件", props.TypeTextControl.Text);
                Assert.Equal("5 B", props.SizeTextControl.Text);
                Assert.Contains("644", props.PermissionTextControl.Text);
                Assert.Equal("devuser", props.OwnerTextControl.Text);
                props.Close();
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void SFTP10_TextViewerShowsTheContentReadOnly()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var window = new SftpTextViewerWindow();
            window.Show();
            window.SetContent("a.txt", System.Text.Encoding.UTF8.GetBytes("hello r60"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("hello r60", window.TextHostControl.Text);
            Assert.True(window.TextHostControl.IsReadOnly);
            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [AvaloniaFact]
    public void SFTP10_ImageViewerShowsABitmapAndReportsBadData()
    {
        using var env = new TestEnvironment();
        env.Run(() =>
        {
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
            var window = new SftpImageViewerWindow();
            window.Show();

            window.SetImage("x.png", new Bitmap(new MemoryStream(png)), png.Length);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.ImageHostControl.Source);
            Assert.Contains("x.png", window.StatusTextControl.Text);

            window.SetError("y.png", "无法解码图像，文件可能不是有效的图像");
            Assert.Contains("无法解码", window.StatusTextControl.Text);
            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
    }

    [AvaloniaFact]
    public async Task SFTP12_BlankAreaRightClickShowsTheDirectoryMenu()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, _, _, var view) = await Setup(remote =>
                {
                    remote.AddFile("/remote/a.txt", "a");
                    remote.AddFile("/remote/b.txt", "b");
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);

                // 模拟空白区右键：直接向 ListBox 本身发出 PointerPressed
                // （事件源是列表而非 ListBoxItem，FindAncestor 找不到条目，
                // 走"空白处"分支）。比按几何坐标找空白更确定。
                var list = view.RemoteListControl;
                var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
                var windowPoint = list.TranslatePoint(
                    new Point(list.Bounds.Width / 2, list.Bounds.Height / 2), window) ?? new Point(10, 10);
                list.RaiseEvent(new PointerPressedEventArgs(
                    list,
                    pointer,
                    window,
                    windowPoint,
                    0,
                    new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.Other),
                    KeyModifiers.None,
                    clickCount: 1));
                Dispatcher.UIThread.RunJobs();

                // Nothing got selected and the directory (blank-area) menu is shown.
                Assert.Null(pane.SelectedRemoteEntry);
                Assert.True(view.MenuBlankUpControl.IsVisible);
                Assert.True(view.MenuBlankNewFolderControl.IsVisible);
                Assert.True(view.MenuBlankNewFileControl.IsVisible);
                Assert.True(view.MenuBlankRefreshControl.IsVisible);
                Assert.True(view.MenuBlankUploadControl.IsVisible);
                Assert.False(view.MenuOpenControl.IsVisible);
                Assert.False(view.MenuDeleteControl.IsVisible);
                Assert.False(view.MenuPermissionsControl.IsVisible);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task SFTP14_BlankAreaLeftClickClearsSelection()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, _, _, var view) = await Setup(remote =>
                {
                    remote.AddDirectory("/remote/dir");
                    remote.AddFile("/remote/a.txt", "a");
                    remote.AddFile("/remote/b.txt", "b");
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);

                // 先选中一个文件
                pane.SelectedRemoteEntry = pane.RemoteEntries.Single(entry => entry.Name == "a.txt");
                Dispatcher.UIThread.RunJobs();
                Assert.True(view.RemoteListControl.SelectedIndex >= 0);

                // 模拟空白区左键单击：直接向 ListBox 本身发 PointerPressed
                // （事件源是列表而非 ListBoxItem，走"空白处"分支）。
                var list = view.RemoteListControl;
                var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
                var windowPoint = list.TranslatePoint(
                    new Point(list.Bounds.Width / 2, list.Bounds.Height / 2), window) ?? new Point(10, 10);
                list.RaiseEvent(new PointerPressedEventArgs(
                    list,
                    pointer,
                    window,
                    windowPoint,
                    0,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
                    KeyModifiers.None,
                    clickCount: 1));
                Dispatcher.UIThread.RunJobs();

                // r64: 左键单击空白区 → 清除选中（MobaXterm 行为）
                Assert.Equal(-1, view.RemoteListControl.SelectedIndex);
                Assert.Null(pane.SelectedRemoteEntry);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task SFTP14_RowLeftClickStillSelectsTheRow()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, _, _, var view) = await Setup(remote =>
                {
                    remote.AddDirectory("/remote/dir");
                    remote.AddFile("/remote/a.txt", "a");
                    remote.AddFile("/remote/b.txt", "b");
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);

                var list = view.RemoteListControl;
                var target = pane.RemoteEntries.Single(entry => entry.Name == "b.txt");
                var index = pane.RemoteEntries.ToList().IndexOf(target);
                var container = (Control)list.ContainerFromIndex(index)!;

                // 模拟对该行的真实左键单击（完整输入管线：命中→隧道→选中→冒泡→Tap）
                var windowPoint = container.TranslatePoint(
                    new Point(container.Bounds.Width / 2, container.Bounds.Height / 2), window);
                Assert.NotNull(windowPoint);
                window.MouseDown(windowPoint!.Value, MouseButton.Left);
                window.MouseUp(windowPoint.Value, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();

                // r64: 行上的左键单击仍正常选中该行，不受空白区逻辑影响
                Assert.NotNull(pane.SelectedRemoteEntry);
                Assert.Equal("b.txt", pane.SelectedRemoteEntry!.Name);
                Assert.True(view.MenuOpenControl.IsVisible);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task SFTP14_RowClickDoesNotClearSelection()
    {
        // r64 守卫：行上的左键按下（合成事件，源在行容器内）不得清除已有选中。
        // 这是 --selftest-r64 ② 在同一模拟方式下的可移植版本（App 工程不引用
        // Avalonia.Headless，实机自检无法用 MouseDown 扩展方法）。
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, _, _, var view) = await Setup(remote =>
                {
                    remote.AddDirectory("/remote/dir");
                    remote.AddFile("/remote/a.txt", "a");
                    remote.AddFile("/remote/b.txt", "b");
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);

                // 先选中 b.txt（经 VM，双向绑定同步到 ListBox）
                pane.SelectedRemoteEntry = pane.RemoteEntries.Single(entry => entry.Name == "b.txt");
                Dispatcher.UIThread.RunJobs();
                var list = view.RemoteListControl;
                Assert.True(list.SelectedIndex >= 0);

                var index = pane.RemoteEntries.ToList().IndexOf(pane.RemoteEntries.Single(e => e.Name == "b.txt"));
                var container = (Control)list.ContainerFromIndex(index)!;
                var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
                var windowPoint = container.TranslatePoint(
                    new Point(container.Bounds.Width / 2, container.Bounds.Height / 2), window) ?? new Point(10, 10);
                container.RaiseEvent(new PointerPressedEventArgs(
                    container,
                    pointer,
                    window,
                    windowPoint,
                    0,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
                    KeyModifiers.None,
                    clickCount: 1));
                Dispatcher.UIThread.RunJobs();

                // 行上的按下不清除选中（空白区分支的 onItem 守卫生效）
                Assert.NotNull(pane.SelectedRemoteEntry);
                Assert.Equal("b.txt", pane.SelectedRemoteEntry!.Name);
                Assert.True(list.SelectedIndex >= 0);
                Assert.True(view.MenuOpenControl.IsVisible);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task SFTP12_NewFilePromptCreatesAnEmptyRemoteFile()
    {
        using var env = new TestEnvironment();
        MainWindow? window = null;
        try
        {
            await env.RunAsync(async () =>
            {
                env.SeedConfig(new ComtoolConfig());
                (window, var pane, var remote, _, var view) = await Setup(fs =>
                {
                    fs.AddFile("/remote/a.txt", "a");
                });
                await pane.EnsureLoadedAsync(CancellationToken.None);

                // No selection -> the blank-area menu is active.
                view.MenuBlankNewFileControl.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                Assert.True(view.PromptRowControl.IsVisible);
                Assert.Equal("新建文件", view.PromptLabelControl.Text);

                view.PromptBoxControl.Text = "new.txt";
                view.PromptOkButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => remote.HasEntry("/remote/new.txt"), "create the empty file");

                var entry = pane.RemoteEntries.Single(e => e.Name == "new.txt");
                Assert.Equal(0, entry.Size);
                Assert.False(view.PromptRowControl.IsVisible);
                Assert.Contains("new.txt", pane.StatusMessage);
            });
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
