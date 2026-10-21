using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    public async Task UploadAndDownloadAreDisabledWithoutASelection()
    {
        using var env = new TestEnvironment();
        var (viewModel, remote, local) = NewViewModel();
        remote.AddFile("/remote/a.txt", "a");
        local.AddFile("/local/a.txt", "a");
        await viewModel.LoadAsync(CancellationToken.None);

        var (pane, window) = NewDrawer(viewModel);
        try
        {
            Assert.False(pane.UploadButtonControl.IsEnabled);
            Assert.False(pane.DownloadButtonControl.IsEnabled);

            // A local entry is what an upload needs.
            viewModel.SelectedLocalEntry = viewModel.LocalEntries.Single(entry => entry.Name == "a.txt");
            Dispatcher.UIThread.RunJobs();

            Assert.True(pane.UploadButtonControl.IsEnabled);
            Assert.False(pane.DownloadButtonControl.IsEnabled);

            // A remote entry is what a download needs.
            viewModel.SelectedRemoteEntry = viewModel.RemoteEntries.Single(entry => entry.Name == "a.txt");
            Dispatcher.UIThread.RunJobs();

            Assert.True(pane.DownloadButtonControl.IsEnabled);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
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
            viewModel.SelectedLocalEntry = viewModel.LocalEntries.Single(entry => entry.Name == "a.txt");
            var upload = viewModel.UploadAsync();

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
        var view = new SshSessionView { Terminal = terminal, SftpPane = viewModel };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            viewModel.SelectedLocalEntry = viewModel.LocalEntries.Single(entry => entry.Name == "a.txt");
            var upload = viewModel.UploadAsync();

            await WaitUntil(() => viewModel.IsConflictVisible, "ask about the conflict");
            Dispatcher.UIThread.RunJobs();

            // The prompt sits inside the drawer, so the terminal stays
            // usable while it is shown.
            var drawer = view.SftpHostControl.Content as SftpPane;
            Assert.NotNull(drawer);
            Assert.True(drawer!.ConflictPanelControl.IsVisible);
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
