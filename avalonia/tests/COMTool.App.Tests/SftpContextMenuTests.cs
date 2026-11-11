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

                var headers = menu!.Items.OfType<MenuItem>()
                    .Select(item => item.Header?.ToString())
                    .ToArray();
                Assert.Equal(new object?[]
                {
                    "打开",
                    "使用默认文本文编辑器打开",
                    "打开方式",
                    "使用默认程序打开",
                    "下载",
                    "删除",
                    "重命名",
                    "复制文件路径",
                    "将文件路径复制到终端（鼠标中键单击）",
                    "属性",
                    "权限",
                },
                headers);
                Assert.Equal(3, menu.Items.OfType<Separator>().Count());
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
}
