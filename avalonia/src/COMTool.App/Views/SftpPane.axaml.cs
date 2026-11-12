using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using COMTool.App.Services;
using COMTool.App.ViewModels;
using COMTool.Sftp;

namespace COMTool.App.Views;

/// <summary>
/// r60: 打开远程文件的方式。Auto 为「打开」/双击：图片与文本用内建查看器，
/// 其余类型回退系统默认程序；TextEditor 用系统默认文本编辑器；WithProgram
/// 由用户选择程序；SystemDefault 一律用系统默认程序。
/// </summary>
internal enum SftpOpenMode
{
    Auto,
    TextEditor,
    WithProgram,
    SystemDefault,
}

/// <summary>
/// The panel of an SSH session: the entries of the remote directory, the
/// icon toolbar above them and the state of the transfer below. The panel
/// has one list, like the side panel of MobaXterm; the local side of an
/// upload or a download comes from a system dialog.
/// </summary>
public sealed partial class SftpPane : UserControl
{
    private enum PromptMode
    {
        None,
        NewFolder,
        NewFile,
        Rename,
    }

    private SftpPaneViewModel? _viewModel;
    private SftpPermissionsWindow? _permissionsWindow;
    private SftpPropertiesWindow? _propertiesWindow;
    private SftpTextViewerWindow? _textViewerWindow;
    private SftpImageViewerWindow? _imageViewerWindow;
    private ILocalPickers? _pickers;
    private ILocalFileLauncher? _fileLauncher;
    private PromptMode _promptMode;
    private bool _deleteArmed;

    public SftpPane()
    {
        InitializeComponent();

        UpdateCommandState();

        // 远程监控开关必须在第一次物理按下时就生效（用户 2026-10-31：刚连上 SSH 后
        // 按钮绿色，点第一下没反应、第二下才变灰）。按钮自身的点击依赖 hover/
        // pointer-over 状态，而首按下该状态可能尚未建立，ClickMode=Press 仍会吞掉
        // 第一击。因此在隧道阶段（早于按钮自身逻辑）直接翻转视图模型，并把这次按下
        // 标记为已处理，顺带抑制按钮内部点击，保证状态恰好翻转一次。
        MonitorToggleButton.AddHandler(
            InputElement.PointerPressedEvent,
            OnMonitorTogglePressed,
            RoutingStrategies.Tunnel);

        // r60: 隧道阶段处理列表指针按下——右键先选中被点行（保证右键菜单作用于
        // 被点的那一行，Avalonia 的 ListBox 右键不自动改选中）；中键把选中路径
        // 送入本会话终端（MobaXterm 中键粘贴语义）。
        RemoteList.AddHandler(
            InputElement.PointerPressedEvent,
            OnRemoteListPointerPressed,
            RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// 隧道阶段处理远程监控按钮的左键按下：直接翻转视图模型并标记已处理，
    /// 使按钮自身的点击（含 hover 守卫）不再触发，首击不丢、也不双切换。
    /// 非左键不处理，交回按钮正常逻辑。
    /// </summary>
    private void OnMonitorTogglePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        viewModel.ToggleMonitorFromPress();
        e.Handled = true;
    }

    /// <summary>
    /// The state of the panel. It becomes the context of the view, so that
    /// every binding inside the panel finds it.
    /// </summary>
    public SftpPaneViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value))
            {
                return;
            }

            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _viewModel = value;
            DataContext = value;

            if (value is not null)
            {
                value.PropertyChanged += OnViewModelPropertyChanged;
            }

            HidePrompt();
            DisarmDelete();
            UpdateCommandState();
        }
    }

    /// <summary>
    /// The dialogs of the upload and of the download. A test puts its own
    /// answer here; when nothing is put, the system dialogs are asked.
    /// </summary>
    public ILocalPickers? Pickers
    {
        get => _pickers;
        set => _pickers = value;
    }

    /// <summary>
    /// r60: 本地文件启动器。测试注入记录器；缺省首次使用时创建系统实现。
    /// </summary>
    public ILocalFileLauncher? FileLauncher
    {
        get => _fileLauncher;
        set => _fileLauncher = value;
    }

    /// <summary>
    /// r60: 最近一次「复制文件路径」复制到的路径（剪贴板写入的镜像，供断言）。
    /// </summary>
    internal string? LastCopiedPath { get; private set; }

    internal ListBox RemoteListControl => RemoteList;

    internal TextBox RemotePathBoxControl => RemotePathBox;

    internal Button ParentRowButtonControl => ParentRowButton;

    internal Button PathMenuButtonControl => PathMenuButton;

    internal Button RefreshButtonControl => RefreshButton;

    internal Button NewFolderButtonControl => NewFolderButton;

    internal Button RenameButtonControl => RenameButton;

    internal Button DeleteButtonControl => DeleteButton;

    internal Button ChmodButtonControl => ChmodButton;

    internal Button UploadButtonControl => UploadButton;

    internal Button DownloadButtonControl => DownloadButton;

    internal ToggleButton FollowLinksButtonControl => FollowLinksButton;

    internal ToggleButton ShowHiddenButtonControl => ShowHiddenButton;

    internal Border PromptRowControl => PromptRow;

    internal TextBlock PromptLabelControl => PromptLabel;

    internal TextBox PromptBoxControl => PromptBox;

    internal Button PromptOkButtonControl => PromptOkButton;

    internal Button PromptCancelButtonControl => PromptCancelButton;

    internal Button NameHeaderButtonControl => NameHeaderButton;

    internal Button SizeHeaderButtonControl => SizeHeaderButton;

    internal Button ModifiedHeaderButtonControl => ModifiedHeaderButton;

    internal Button PermissionsHeaderButtonControl => PermissionsHeaderButton;

    internal TextBlock OwnerHeaderControl => OwnerHeader;

    internal TextBlock NameSortGlyphControl => NameSortGlyph;

    internal TextBlock SizeSortGlyphControl => SizeSortGlyph;

    internal TextBlock ModifiedSortGlyphControl => ModifiedSortGlyph;

    internal TextBlock PermissionsSortGlyphControl => PermissionsSortGlyph;

    internal Button CancelButtonControl => CancelButton;

    internal Button RetryButtonControl => RetryButton;

    internal ProgressBar TransferProgressBarControl => TransferProgressBar;

    internal Border ConflictPanelControl => ConflictPanel;

    internal TextBox ConflictRenameBoxControl => ConflictRenameBox;

    internal CheckBox ApplyToAllBoxControl => ApplyToAllBox;

    internal CheckBox NeverAskAgainBoxControl => NeverAskAgainBox;

    /// <summary>
    /// True after the first click on the delete button: the next click
    /// removes the marked entry.
    /// </summary>
    internal bool IsDeleteArmed => _deleteArmed;

    /// <summary>
    /// The window that changes the permissions, while it is open.
    /// </summary>
    internal SftpPermissionsWindow? PermissionsWindow => _permissionsWindow;

    /// <summary>
    /// r60: currently open viewer windows (null when closed).
    /// </summary>
    internal SftpTextViewerWindow? TextViewerWindowControl => _textViewerWindow;

    internal SftpImageViewerWindow? ImageViewerWindowControl => _imageViewerWindow;

    /// <summary>
    /// r60: 右键菜单及其条目（供 headless 测试与实机自检）。
    /// </summary>
    internal ContextMenu RemoteContextMenuControl => RemoteContextMenu;

    internal MenuItem MenuOpenControl => MenuOpen;

    internal MenuItem MenuOpenTextEditorControl => MenuOpenTextEditor;

    internal MenuItem MenuOpenWithControl => MenuOpenWith;

    internal MenuItem MenuOpenSystemControl => MenuOpenSystem;

    internal MenuItem MenuDownloadControl => MenuDownload;

    internal MenuItem MenuDeleteControl => MenuDelete;

    internal MenuItem MenuRenameControl => MenuRename;

    internal MenuItem MenuCopyPathControl => MenuCopyPath;

    internal MenuItem MenuSendPathControl => MenuSendPath;

    internal MenuItem MenuPropertiesControl => MenuProperties;

    internal MenuItem MenuPermissionsControl => MenuPermissions;

    /// <summary>
    /// r62: 空白处（目录级）右键菜单项。
    /// </summary>
    internal MenuItem MenuBlankUpControl => MenuBlankUp;

    internal MenuItem MenuBlankNewFolderControl => MenuBlankNewFolder;

    internal MenuItem MenuBlankNewFileControl => MenuBlankNewFile;

    internal MenuItem MenuBlankRefreshControl => MenuBlankRefresh;

    internal MenuItem MenuBlankUploadControl => MenuBlankUpload;

    internal SftpPropertiesWindow? PropertiesWindow => _propertiesWindow;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SftpPaneViewModel.SelectedRemoteEntry))
        {
            DisarmDelete();
            UpdateCommandState();
        }
    }

    /// <summary>
    /// The commands that work on an entry need a marked entry.
    /// </summary>
    private void UpdateCommandState()
    {
        var entry = _viewModel?.SelectedRemoteEntry;

        DeleteButton.IsEnabled = entry is not null;
        RenameButton.IsEnabled = entry is not null;
        ChmodButton.IsEnabled = entry is not null;

        // r60: 右键菜单项随选中条目启用/禁用
        MenuOpen.IsEnabled = entry is not null;
        MenuDownload.IsEnabled = entry is not null;
        MenuDelete.IsEnabled = entry is not null;
        MenuRename.IsEnabled = entry is not null;
        MenuCopyPath.IsEnabled = entry is not null;
        MenuSendPath.IsEnabled = entry is not null && _viewModel?.PathToTerminal is not null;
        MenuProperties.IsEnabled = entry is not null;
        MenuPermissions.IsEnabled = entry is not null;

        // r62: 空白处（目录级）菜单项的可用性
        var pathOk = _viewModel is { RemotePath.Length: > 0 };
        var idle = pathOk && _viewModel!.IsTransferring == false;
        MenuBlankUp.IsEnabled = pathOk && _viewModel!.RemotePath != SftpPath.Root;
        MenuBlankNewFolder.IsEnabled = idle;
        MenuBlankNewFile.IsEnabled = idle;
        MenuBlankRefresh.IsEnabled = pathOk;
        MenuBlankUpload.IsEnabled = idle;

        ApplyMenuVariant();
    }

    /// <summary>
    /// r62: 切换右键菜单变体（对齐 MobaXterm）。无选中条目时（空白处右键）
    /// 显示目录级 5 项菜单；有选中条目时显示 11 项条目菜单，目录隐藏
    /// 三个「打开*」文件项。
    /// </summary>
    private void ApplyMenuVariant()
    {
        var entry = _viewModel?.SelectedRemoteEntry;
        var showEntryMenu = entry is not null;
        var isFile = entry is not null && !entry.IsDirectory;

        MenuBlankUp.IsVisible = !showEntryMenu;
        MenuBlankNewFolder.IsVisible = !showEntryMenu;
        MenuBlankNewFile.IsVisible = !showEntryMenu;
        MenuBlankRefresh.IsVisible = !showEntryMenu;
        MenuBlankUpload.IsVisible = !showEntryMenu;

        MenuOpen.IsVisible = showEntryMenu;
        MenuOpenTextEditor.IsVisible = isFile;
        MenuOpenWith.IsVisible = isFile;
        MenuOpenSystem.IsVisible = isFile;
        MenuDownload.IsVisible = showEntryMenu;
        MenuSep1.IsVisible = showEntryMenu;
        MenuDelete.IsVisible = showEntryMenu;
        MenuRename.IsVisible = showEntryMenu;
        MenuSep2.IsVisible = showEntryMenu;
        MenuCopyPath.IsVisible = showEntryMenu;
        MenuSendPath.IsVisible = showEntryMenu;
        MenuSep3.IsVisible = showEntryMenu;
        MenuProperties.IsVisible = showEntryMenu;
        MenuPermissions.IsVisible = showEntryMenu;
    }

    /// <summary>
    /// r62: 菜单打开时重申一次变体（选中变化时已设置，此处兜底）。
    /// </summary>
    private void OnRemoteContextMenuOpened(object? sender, RoutedEventArgs e)
        => ApplyMenuVariant();

    private void OnUpClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            _ = viewModel.GoUpRemoteAsync();
        }
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            _ = viewModel.RefreshRemoteAsync();
        }
    }

    private void OnPathHomeClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            _ = viewModel.GoHomeRemoteAsync();
        }
    }

    /// <summary>
    /// r60: 双击目录/符号链接进入（原有行为）；双击文件 = 「打开」
    /// （图片/文本用内建查看器，其余走系统默认程序，对齐 MobaXterm）。
    /// </summary>
    private async void OnRemoteListDoubleTapped(object? sender, TappedEventArgs e)
    {
        var viewModel = ViewModel;
        var entry = viewModel?.SelectedRemoteEntry;
        if (viewModel is null || entry is null)
        {
            return;
        }

        if (entry.IsDirectory || entry.IsSymbolicLink)
        {
            await viewModel.EnterRemoteAsync(entry);
            return;
        }

        await OpenEntryAsync(entry, SftpOpenMode.Auto);
    }

    /// <summary>
    /// r60: 隧道阶段处理列表指针按下。右键先选中被点行，使上下文菜单作用于
    /// 被点的那一行（Avalonia ListBox 右键不自动改选中）；中键把选中条目的
    /// 远程路径送入本会话终端（MobaXterm 中键粘贴语义），并标记已处理。
    /// </summary>
    private void OnRemoteListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(RemoteList);
        if (point.Properties.IsMiddleButtonPressed)
        {
            var viewModel = ViewModel;
            if (viewModel?.SelectedRemoteEntry is not null
                && RemoteList.Bounds.Contains(point.Position))
            {
                viewModel.SendSelectedPathToTerminal();
                e.Handled = true;
            }
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>();
            if (item is not null)
            {
                var index = RemoteList.IndexFromContainer(item);
                if (index >= 0 && index != RemoteList.SelectedIndex)
                {
                    RemoteList.SelectedIndex = index;
                }
            }
        }
    }

    // ───────────────── r60：右键菜单「打开」系列 ─────────────────

    private const int MaxTextViewerBytes = 1024 * 1024;
    private const int MaxImageViewerBytes = 10 * 1024 * 1024;

    /// <summary>
    /// r60: 打开指定条目。目录/符号链接进入；文件按 mode 决定打开方式。
    /// </summary>
    internal async Task OpenEntryAsync(SftpEntry entry, SftpOpenMode mode)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        if (entry.IsDirectory || entry.IsSymbolicLink)
        {
            await viewModel.EnterRemoteAsync(entry);
            return;
        }

        var launcher = FileLauncher ??= new SystemLocalFileLauncher();
        var kind = RemoteFileClassifier.Classify(entry.Name);

        switch (mode)
        {
            case SftpOpenMode.TextEditor:
            {
                var local = await viewModel.DownloadToCacheAsync(entry);
                if (local is null)
                {
                    return;
                }

                if (!launcher.OpenWithProgram(SystemLocalFileLauncher.TextEditorProgram, local))
                {
                    viewModel.ReportOpenFailed(local);
                }
                break;
            }

            case SftpOpenMode.WithProgram:
            {
                var pickers = Pickers ?? LocalPickers.Create(this);
                if (pickers is null)
                {
                    return;
                }

                var programs = await pickers.PickFilesAsync("选择用于打开文件的程序");
                if (programs.Count == 0)
                {
                    return;
                }

                var local = await viewModel.DownloadToCacheAsync(entry);
                if (local is null)
                {
                    return;
                }

                if (!launcher.OpenWithProgram(programs[0], local))
                {
                    viewModel.ReportOpenFailed(local);
                }
                break;
            }

            case SftpOpenMode.SystemDefault:
            {
                await OpenWithSystemDefaultAsync(viewModel, entry, launcher);
                break;
            }

            default:
            {
                // Auto：MobaXterm 式内建查看器，图片/文本优先
                if (kind == RemoteFileKind.Image)
                {
                    var bytes = await viewModel.ReadEntryBytesAsync(entry, MaxImageViewerBytes);
                    if (bytes is null)
                    {
                        await OpenWithSystemDefaultAsync(viewModel, entry, launcher);
                        break;
                    }

                    var window = new SftpImageViewerWindow();
                    try
                    {
                        using var stream = new MemoryStream(bytes);
                        window.SetImage(entry.Name, new Bitmap(stream), bytes.Length);
                    }
                    catch
                    {
                        window.SetError(entry.Name, "无法解码图像，文件可能不是有效图像");
                    }

                    TrackImageViewer(window);
                    window.Show(TopLevel.GetTopLevel(this) as Window);
                    break;
                }

                if (kind == RemoteFileKind.Text)
                {
                    var bytes = await viewModel.ReadEntryBytesAsync(entry, MaxTextViewerBytes);
                    if (bytes is null)
                    {
                        await OpenWithSystemDefaultAsync(viewModel, entry, launcher);
                        break;
                    }

                    var window = new SftpTextViewerWindow();
                    window.SetContent(entry.Name, bytes);
                    TrackTextViewer(window);
                    window.Show(TopLevel.GetTopLevel(this) as Window);
                    break;
                }

                await OpenWithSystemDefaultAsync(viewModel, entry, launcher);
                break;
            }
        }
    }

    private static async Task OpenWithSystemDefaultAsync(
        SftpPaneViewModel viewModel, SftpEntry entry, ILocalFileLauncher launcher)
    {
        var local = await viewModel.DownloadToCacheAsync(entry);
        if (local is null)
        {
            return;
        }

        if (!launcher.OpenWithDefaultProgram(local))
        {
            viewModel.ReportOpenFailed(local);
        }
    }

    /// <summary>
    /// r60: 记住当前打开的文本查看器窗口；窗口关闭时清空引用。
    /// </summary>
    private void TrackTextViewer(SftpTextViewerWindow window)
    {
        _textViewerWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_textViewerWindow, window))
            {
                _textViewerWindow = null;
            }
        };
    }

    /// <summary>
    /// r60: 记住当前打开的图像查看器窗口；窗口关闭时清空引用。
    /// </summary>
    private void TrackImageViewer(SftpImageViewerWindow window)
    {
        _imageViewerWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_imageViewerWindow, window))
            {
                _imageViewerWindow = null;
            }
        };
    }

    private Task OpenSelectedEntryAsync(SftpOpenMode mode)
    {
        var entry = ViewModel?.SelectedRemoteEntry;
        if (entry is null)
        {
            return Task.CompletedTask;
        }

        return OpenEntryAsync(entry, mode);
    }

    private async void OnMenuOpenClick(object? sender, RoutedEventArgs e)
        => await OpenSelectedEntryAsync(SftpOpenMode.Auto);

    private async void OnMenuOpenTextEditorClick(object? sender, RoutedEventArgs e)
        => await OpenSelectedEntryAsync(SftpOpenMode.TextEditor);

    private async void OnMenuOpenWithClick(object? sender, RoutedEventArgs e)
        => await OpenSelectedEntryAsync(SftpOpenMode.WithProgram);

    private async void OnMenuOpenSystemClick(object? sender, RoutedEventArgs e)
        => await OpenSelectedEntryAsync(SftpOpenMode.SystemDefault);

    private void OnMenuDownloadClick(object? sender, RoutedEventArgs e)
        => OnDownloadClick(sender, e);

    private async void OnMenuDeleteClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel?.SelectedRemoteEntry is null)
        {
            return;
        }

        // 右键菜单是明确动作，无需工具栏的两次确认
        DisarmDelete();
        await viewModel.DeleteRemoteAsync();
    }

    // ───────────────── r62：空白处右键菜单（目录级操作，对齐 MobaXterm 图1） ─────────────────

    private void OnMenuBlankUpClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            _ = viewModel.GoUpRemoteAsync();
        }
    }

    private void OnMenuBlankNewFolderClick(object? sender, RoutedEventArgs e)
        => ShowPrompt(PromptMode.NewFolder, "新建文件夹", string.Empty);

    private void OnMenuBlankNewFileClick(object? sender, RoutedEventArgs e)
        => ShowPrompt(PromptMode.NewFile, "新建文件", string.Empty);

    private void OnMenuBlankRefreshClick(object? sender, RoutedEventArgs e)
        => OnRefreshClick(sender, e);

    private void OnMenuBlankUploadClick(object? sender, RoutedEventArgs e)
        => OnUploadClick(sender, e);

    private void OnMenuRenameClick(object? sender, RoutedEventArgs e)
        => OnRenameClick(sender, e);

    private void OnMenuCopyPathClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        var entry = viewModel?.SelectedRemoteEntry;
        if (viewModel is null || entry is null)
        {
            return;
        }

        LastCopiedPath = entry.Path;
        if (TopLevel.GetTopLevel(this) is { } topLevel)
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(entry.Path));
            _ = topLevel.Clipboard.SetDataAsync(data);
        }

        viewModel.ReportPathCopied(entry.Path);
    }

    private void OnMenuSendPathClick(object? sender, RoutedEventArgs e)
        => ViewModel?.SendSelectedPathToTerminal();

    private void OnMenuPropertiesClick(object? sender, RoutedEventArgs e)
    {
        var entry = ViewModel?.SelectedRemoteEntry;
        if (entry is null)
        {
            return;
        }

        var window = new SftpPropertiesWindow();
        window.SetEntry(entry);
        _propertiesWindow = window;
        window.Closed += (_, _) => _propertiesWindow = null;
        window.Show(TopLevel.GetTopLevel(this) as Window);
    }

    private void OnMenuPermissionsClick(object? sender, RoutedEventArgs e)
        => OnChmodClick(sender, e);

    private async void OnRemotePathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        viewModel.RemotePath = RemotePathBox.Text ?? string.Empty;
        await viewModel.RefreshRemoteAsync();
    }

    private void OnSortNameClick(object? sender, RoutedEventArgs e)
        => ViewModel?.SortBy(SftpSortColumn.Name);

    private void OnSortSizeClick(object? sender, RoutedEventArgs e)
        => ViewModel?.SortBy(SftpSortColumn.Size);

    private void OnSortModifiedClick(object? sender, RoutedEventArgs e)
        => ViewModel?.SortBy(SftpSortColumn.Modified);

    private void OnSortPermissionsClick(object? sender, RoutedEventArgs e)
        => ViewModel?.SortBy(SftpSortColumn.Permissions);

    /// <summary>
    /// Puts the name of a new folder in front of the user.
    /// </summary>
    private void OnNewFolderClick(object? sender, RoutedEventArgs e)
    {
        ShowPrompt(PromptMode.NewFolder, "新建文件夹", string.Empty);
    }

    /// <summary>
    /// Puts the name of the marked entry in front of the user, so that it
    /// can be changed.
    /// </summary>
    private void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        var entry = ViewModel?.SelectedRemoteEntry;
        if (entry is null)
        {
            return;
        }

        ShowPrompt(PromptMode.Rename, "重命名", entry.Name);
    }

    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = ApplyPromptAsync();
        }
        else if (e.Key == Key.Escape)
        {
            HidePrompt();
        }
    }

    private void OnPromptOkClick(object? sender, RoutedEventArgs e)
    {
        _ = ApplyPromptAsync();
    }

    private void OnPromptCancelClick(object? sender, RoutedEventArgs e)
    {
        HidePrompt();
    }

    private async Task ApplyPromptAsync()
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        var name = (PromptBox.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            PromptBox.Focus();
            return;
        }

        if (_promptMode == PromptMode.NewFolder)
        {
            HidePrompt();
            await viewModel.NewRemoteFolderAsync(name);
            return;
        }

        // r62: 新建文件（空白处右键菜单）
        if (_promptMode == PromptMode.NewFile)
        {
            HidePrompt();
            await viewModel.NewRemoteFileAsync(name);
            return;
        }

        if (_promptMode == PromptMode.Rename)
        {
            var entry = viewModel.SelectedRemoteEntry;
            if (entry is null)
            {
                HidePrompt();
                return;
            }

            if (string.Equals(name, entry.Name, StringComparison.Ordinal))
            {
                // Nothing to rename to yet: keep the field in front.
                PromptBox.Focus();
                PromptBox.SelectAll();
                return;
            }

            HidePrompt();
            await viewModel.RenameRemoteAsync(name);
        }
    }

    private void ShowPrompt(PromptMode mode, string label, string text)
    {
        _promptMode = mode;
        PromptLabel.Text = label;
        PromptBox.Text = text;
        PromptRow.IsVisible = true;
        PromptBox.Focus();
        PromptBox.SelectAll();
    }

    private void HidePrompt()
    {
        _promptMode = PromptMode.None;
        PromptRow.IsVisible = false;
        PromptBox.Text = string.Empty;
    }

    /// <summary>
    /// The first click only asks, the second one deletes.
    /// </summary>
    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null || viewModel.SelectedRemoteEntry is null)
        {
            return;
        }

        if (!_deleteArmed)
        {
            ArmDelete();
            return;
        }

        DisarmDelete();
        await viewModel.DeleteRemoteAsync();
    }

    private void ArmDelete()
    {
        _deleteArmed = true;
        ToolTip.SetTip(DeleteButton, "再次点击确认删除");
        DeleteButton.Classes.Set("Armed", true);
    }

    private void DisarmDelete()
    {
        _deleteArmed = false;
        ToolTip.SetTip(DeleteButton, "删除");
        DeleteButton.Classes.Set("Armed", false);
    }

    private void OnChmodClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        var entry = viewModel?.SelectedRemoteEntry;
        if (viewModel is null || entry is null)
        {
            return;
        }

        // r62: 标题带条目名（MobaXterm: Permissions for "name"）
        var window = new SftpPermissionsWindow
        {
            Permission = entry.Permission,
            TargetName = entry.Name,
        };

        // r63: 事件为 Func<SftpPermission, Task>——窗口 await 期间标题栏显示
        // 「正在修改权限...」，SFTP 写回完成后由窗口自动关闭（MobaXterm 图2）
        window.PermissionApplied = permission =>
            viewModel.SetRemotePermissionsAsync(permission.ToOctal());

        _permissionsWindow = window;
        window.Closed += (_, _) => _permissionsWindow = null;
        window.Show();
    }

    /// <summary>
    /// Asks the system dialog for local files and sends them into the
    /// directory that the panel shows.
    /// </summary>
    private async void OnUploadClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        var pickers = Pickers ?? LocalPickers.Create(this);
        if (viewModel is null || pickers is null)
        {
            return;
        }

        var files = await pickers.PickFilesAsync("上传到当前文件夹");
        if (files.Count == 0)
        {
            return;
        }

        await viewModel.UploadAsync(files);
    }

    /// <summary>
    /// Asks the system dialog for a local directory and takes the marked
    /// entry into it.
    /// </summary>
    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        var pickers = Pickers ?? LocalPickers.Create(this);
        var entry = viewModel?.SelectedRemoteEntry;
        if (viewModel is null || pickers is null || entry is null)
        {
            return;
        }

        var folder = await pickers.PickFolderAsync("下载到本地");
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        await viewModel.DownloadAsync(folder);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CancelTransfer();
    }

    private async void OnRetryClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        await viewModel.EnsureLoadedAsync(CancellationToken.None);
    }

    private void OnConflictOverwriteClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ResolveConflict(SftpConflictAction.Overwrite);
    }

    private void OnConflictSkipClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ResolveConflict(SftpConflictAction.Skip);
    }

    private void OnConflictRenameClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ResolveConflict(SftpConflictAction.Rename);
    }

    private void OnConflictCancelClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ResolveConflict(SftpConflictAction.Cancel);
    }
}
