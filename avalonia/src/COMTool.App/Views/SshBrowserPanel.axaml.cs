using Avalonia;
using Avalonia.Controls;
using COMTool.App.ViewModels;

namespace COMTool.App.Views;

/// <summary>
/// Hosts the SFTP panel of the selected SSH session in the sidebar. The
/// control of the panel is rebuilt when the user switches to another SSH
/// tab, so one view is never shared between two tabs.
/// </summary>
public sealed partial class SshBrowserPanel : UserControl
{
    public static readonly StyledProperty<SftpPaneViewModel?> PaneProperty =
        AvaloniaProperty.Register<SshBrowserPanel, SftpPaneViewModel?>(nameof(Pane));

    public SshBrowserPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The state of the SSH browser. It is set from the binding of the
    /// sidebar.
    /// </summary>
    public SftpPaneViewModel? Pane
    {
        get => GetValue(PaneProperty);
        set => SetValue(PaneProperty, value);
    }

    internal ContentControl PaneHostControl => PaneHost;

    internal SftpPane? SftpPaneControl => PaneHost.Content as SftpPane;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PaneProperty)
        {
            ApplyPane();
        }
    }

    private void ApplyPane()
    {
        if (PaneHost.Content is SftpPane old)
        {
            // The panel unsubscribes from its view model here, so the
            // session does not keep a view of a panel that is gone.
            old.ViewModel = null;
        }

        PaneHost.Content = Pane is null
            ? null
            : new SftpPane { ViewModel = Pane };
    }
}
