using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using COMTool.App.ViewModels;
using Iciclecreek.Terminal;

namespace COMTool.App.Views;

/// <summary>
/// The view of an SSH session: the terminal above the SFTP drawer. The
/// drawer can be folded away and its height can be dragged, and both
/// live in the view model of the drawer.
/// </summary>
public partial class SshSessionView : UserControl
{
    private const int DrawerRow = 2;

    private SftpPaneViewModel? _sftpPane;
    private TerminalControl? _terminal;

    public SshSessionView()
    {
        InitializeComponent();

        SftpSplitter.DragCompleted += OnSftpSplitterDragCompleted;
    }

    /// <summary>
    /// The terminal of the session. It is put into the host of the view.
    /// </summary>
    public TerminalControl? Terminal
    {
        get => _terminal;
        set
        {
            _terminal = value;
            TerminalHost.Content = value;
        }
    }

    /// <summary>
    /// The drawer of the session. Its state and its height decide how
    /// much room the drawer takes.
    /// </summary>
    public SftpPaneViewModel? SftpPane
    {
        get => _sftpPane;
        set
        {
            if (ReferenceEquals(_sftpPane, value))
            {
                return;
            }

            if (_sftpPane is not null)
            {
                _sftpPane.PropertyChanged -= OnSftpPanePropertyChanged;
            }

            _sftpPane = value;

            if (value is not null)
            {
                value.PropertyChanged += OnSftpPanePropertyChanged;
            }

            SftpHost.Content = value is null
                ? null
                : new COMTool.App.Views.SftpPane { ViewModel = value };
            ApplyPaneState();
        }
    }

    internal ContentControl TerminalHostControl => TerminalHost;

    internal ContentControl SftpHostControl => SftpHost;

    internal GridSplitter SftpSplitterControl => SftpSplitter;

    /// <summary>
    /// The height the drawer takes, in pixels.
    /// </summary>
    internal double DrawerHeight => SessionGrid.RowDefinitions[DrawerRow].Height.Value;

    private void OnSftpPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SftpPaneViewModel.IsExpanded)
            or nameof(SftpPaneViewModel.PaneHeight))
        {
            ApplyPaneState();
        }
    }

    /// <summary>
    /// The drawer and its handle are shown only when the drawer is open,
    /// and the row of the drawer takes the height that the drawer asks
    /// for.
    /// </summary>
    private void ApplyPaneState()
    {
        var pane = _sftpPane;
        var expanded = pane?.IsExpanded ?? false;

        SftpHost.IsVisible = expanded;
        SftpSplitter.IsVisible = expanded;

        SessionGrid.RowDefinitions[DrawerRow].Height = new GridLength(
            expanded ? pane?.PaneHeight ?? 0 : 0);
    }

    /// <summary>
    /// The height that the user dragged the handle to becomes the height
    /// of the drawer.
    /// </summary>
    private void OnSftpSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        var pane = _sftpPane;
        if (pane is null)
        {
            return;
        }

        var height = DrawerHeight;
        if (height > 0)
        {
            pane.PaneHeight = height;
        }
    }
}
