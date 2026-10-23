using Avalonia.Controls;
using Iciclecreek.Terminal;

namespace COMTool.App.Views;

/// <summary>
/// The view of an SSH session. The remote files live in the SSH browser
/// of the sidebar, so this view is the terminal alone.
/// </summary>
public partial class SshSessionView : UserControl
{
    private TerminalControl? _terminal;

    public SshSessionView()
    {
        InitializeComponent();
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

    internal ContentControl TerminalHostControl => TerminalHost;
}
