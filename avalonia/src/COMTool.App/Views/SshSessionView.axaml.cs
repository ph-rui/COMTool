using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using COMTool.App.ViewModels;
using Iciclecreek.Terminal;

namespace COMTool.App.Views;

/// <summary>
/// The view of an SSH session: the SFTP panel on the left of the
/// terminal. The panel can be folded away and its width can be dragged,
/// and both live in the view model of the panel.
/// </summary>
public partial class SshSessionView : UserControl
{
    private const int PanelColumn = 0;
    private const int SplitterColumn = 1;

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
    /// The panel of the session. Its state and its width decide how much
    /// room the panel takes.
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
    /// The width the panel takes, in pixels. It is zero while the panel is
    /// folded away.
    /// </summary>
    internal double PanelWidth => SessionGrid.ColumnDefinitions[PanelColumn].Width.Value;

    private void OnSftpPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SftpPaneViewModel.IsExpanded)
            or nameof(SftpPaneViewModel.PaneWidth))
        {
            ApplyPaneState();
        }
    }

    /// <summary>
    /// The panel and its handle are shown only when the panel is open, and
    /// the column of the panel takes the width that the panel asks for.
    /// </summary>
    private void ApplyPaneState()
    {
        var pane = _sftpPane;
        var expanded = pane?.IsExpanded ?? false;

        SftpHost.IsVisible = expanded;
        SftpSplitter.IsVisible = expanded;

        SessionGrid.ColumnDefinitions[PanelColumn].Width = new GridLength(
            expanded ? pane?.PaneWidth ?? 0 : 0);
        SessionGrid.ColumnDefinitions[SplitterColumn].Width = new GridLength(expanded ? 4 : 0);
    }

    /// <summary>
    /// The width that the user dragged the handle to becomes the width of
    /// the panel.
    /// </summary>
    private void OnSftpSplitterDragCompleted(object? sender, VectorEventArgs e)
    {
        var pane = _sftpPane;
        if (pane is null)
        {
            return;
        }

        var width = PanelWidth;
        if (width > 0)
        {
            pane.PaneWidth = width;
        }
    }
}
