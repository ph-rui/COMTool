using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.App.Views;
using COMTool.Sftp;
using COMTool.Sftp.Testing;
using Iciclecreek.Terminal;

namespace COMTool.App.Tests;

public sealed class SftpTransferUiTests
{
    private const string RemoteHome = "/remote";
    private const string LocalHome = "/local";

    private static (SftpPaneViewModel ViewModel, InMemoryFileSystem Remote, InMemoryFileSystem Local) NewViewModel()
    {
        var remote = new InMemoryFileSystem(RemoteHome);
        var local = new InMemoryFileSystem(LocalHome);
        var viewModel = new SftpPaneViewModel(() => remote, local);
        return (viewModel, remote, local);
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
        }

        Assert.Fail($"The panel did not {what} in time.");
    }

    [AvaloniaFact]
    public async Task UploadIsAlwaysEnabledDownloadNeedsASelection()
    {
        using var env = new TestEnvironment();
        var (viewModel, remote, local) = NewViewModel();
        remote.AddFile("/remote/a.txt", "a");
        local.AddFile("/local/a.txt", "a");
        await viewModel.LoadAsync(CancellationToken.None);

        var (pane, window) = NewDrawer(viewModel);
        try
        {
            // The upload files come from the system dialog, so the upload
            // button does not need any selection; the download needs the
            // marked remote entry.
            Assert.True(pane.UploadButtonControl.IsEnabled);
            Assert.False(pane.DownloadButtonControl.IsEnabled);

            viewModel.SelectedRemoteEntry = viewModel.RemoteEntries.Single(entry => entry.Name == "a.txt");
            Dispatcher.UIThread.RunJobs();

            Assert.True(pane.UploadButtonControl.IsEnabled);
            Assert.True(pane.DownloadButtonControl.IsEnabled);

            viewModel.SelectedRemoteEntry = null;
            Dispatcher.UIThread.RunJobs();

            Assert.False(pane.DownloadButtonControl.IsEnabled);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task UploadDownloadUseTheSystemPickerDialogs()
    {
        using var env = new TestEnvironment();
        var (viewModel, remote, local) = NewViewModel();
        local.AddFile("/local/a.txt", "hello");
        remote.AddFile("/remote/b.txt", "world");
        await viewModel.LoadAsync(CancellationToken.None);

        var (pane, window) = NewDrawer(viewModel);
        try
        {
            // The pane hands the pickers the dialog answers: the upload
            // sends the picked files, the download takes the marked entry
            // into the picked folder.
            pane.Pickers = new FakePickers(new[] { "/local/a.txt" }, "/local");
            pane.UploadButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => remote.HasEntry("/remote/a.txt"), "upload through the picker");
            Assert.Equal("hello", remote.ReadFile("/remote/a.txt"));

            viewModel.SelectedRemoteEntry = viewModel.RemoteEntries.Single(entry => entry.Name == "b.txt");
            Dispatcher.UIThread.RunJobs();
            pane.DownloadButtonControl.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(() => local.HasEntry("/local/b.txt"), "download through the picker");
            Assert.Equal("world", local.ReadFile("/local/b.txt"));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class FakePickers : ILocalPickers
    {
        private readonly string[] _files;
        private readonly string _folder;

        public FakePickers(string[] files, string folder)
        {
            _files = files;
            _folder = folder;
        }

        public Task<IReadOnlyList<string>> PickFilesAsync(string title)
            => Task.FromResult<IReadOnlyList<string>>(_files);

        public Task<string?> PickFolderAsync(string title)
            => Task.FromResult<string?>(_folder);
    }

    [AvaloniaFact]
    public async Task CancelButtonAppearsOnlyDuringATransfer()
    {
        using var env = new TestEnvironment();
        var (viewModel, _, _) = NewViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        var (pane, window) = NewDrawer(viewModel);
        try
        {
            Assert.False(pane.CancelButtonControl.IsVisible);

            viewModel.IsTransferring = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(pane.CancelButtonControl.IsVisible);

            viewModel.IsTransferring = false;
            Dispatcher.UIThread.RunJobs();

            Assert.False(pane.CancelButtonControl.IsVisible);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task ProgressBarReflectsTheTransferProgress()
    {
        using var env = new TestEnvironment();
        var (viewModel, _, _) = NewViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        var (pane, window) = NewDrawer(viewModel);
        try
        {
            Assert.Equal(0, pane.TransferProgressBarControl.Value);
            Assert.Equal(100, pane.TransferProgressBarControl.Maximum);

            viewModel.TransferProgress = 42;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(42, pane.TransferProgressBarControl.Value);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task ConflictPanelIsHiddenUntilAConflictHappens()
    {
        using var env = new TestEnvironment();
        var (viewModel, _, _) = NewViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        var (pane, window) = NewDrawer(viewModel);
        try
        {
            Assert.False(pane.ConflictPanelControl.IsVisible);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task ConflictPanelOffersTheFourDecisions()
    {
        using var env = new TestEnvironment();
        var (viewModel, _, _) = NewViewModel();
        await viewModel.LoadAsync(CancellationToken.None);

        var (pane, window) = NewDrawer(viewModel);
        try
        {
            var panel = pane.ConflictPanelControl;

            Assert.NotNull(panel.FindControl<Button>("ConflictOverwriteButton"));
            Assert.NotNull(panel.FindControl<Button>("ConflictSkipButton"));
            Assert.NotNull(panel.FindControl<Button>("ConflictRenameButton"));
            Assert.NotNull(panel.FindControl<Button>("ConflictCancelButton"));
            Assert.NotNull(panel.FindControl<CheckBox>("ApplyToAllBox"));
            Assert.NotNull(panel.FindControl<CheckBox>("NeverAskAgainBox"));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task ConflictRenameBoxIsPrefilledWithTheSuggestedName()
    {
        using var env = new TestEnvironment();
        var (viewModel, remote, local) = NewViewModel();
        remote.AddFile("/remote/a.txt", "old");
        local.AddFile("/local/a.txt", "new");
        await viewModel.LoadAsync(CancellationToken.None);

        var (pane, window) = NewDrawer(viewModel);
        try
        {
            var upload = viewModel.UploadAsync(new[] { "/local/a.txt" });

            await WaitUntil(() => viewModel.IsConflictVisible, "ask about the conflict");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("a (1).txt", pane.ConflictRenameBoxControl.Text);

            viewModel.ResolveConflict(SftpConflictAction.Overwrite);
            await upload.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task ConflictPanelIsNotAModalDialog()
    {
        using var env = new TestEnvironment();
        var (viewModel, remote, local) = NewViewModel();
        remote.AddFile("/remote/a.txt", "old");
        local.AddFile("/local/a.txt", "new");
        await viewModel.LoadAsync(CancellationToken.None);

        var terminal = new TerminalControl { Process = string.Empty };
        var drawer = new SftpPane { ViewModel = viewModel };
        viewModel.IsExpanded = true;

        // The browser is a panel of the sidebar; the terminal keeps its
        // own column while the question is shown.
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("420,*") };
        grid.Children.Add(drawer);
        Grid.SetColumn(terminal, 1);
        grid.Children.Add(terminal);

        var window = new Window { Content = grid, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var upload = viewModel.UploadAsync(new[] { "/local/a.txt" });

            await WaitUntil(() => viewModel.IsConflictVisible, "ask about the conflict");
            Dispatcher.UIThread.RunJobs();

            // The prompt sits inside the panel, so the terminal stays
            // usable while it is shown.
            Assert.True(drawer.ConflictPanelControl.IsVisible);
            Assert.Contains(
                drawer.ConflictPanelControl,
                drawer.GetVisualDescendants().OfType<Border>());

            Assert.True(terminal.IsVisible);
            Assert.True(terminal.IsEnabled);

            viewModel.ResolveConflict(SftpConflictAction.Overwrite);
            await upload.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
