using Avalonia.Controls;
using Avalonia.Interactivity;
using COMTool.Sftp;

namespace COMTool.App.Views;

/// <summary>
/// The window that changes the permissions of a remote entry. The nine
/// boxes and the octal field always show the same mode: a box that is
/// ticked changes the field, and a mode that is typed changes the
/// boxes.
/// </summary>
public sealed partial class SftpPermissionsWindow : Window
{
    private SftpPermission _permission = SftpPermission.FromOctalText("644");
    private bool _updating;

    public SftpPermissionsWindow()
    {
        InitializeComponent();

        ApplyPermission(_permission);
    }

    /// <summary>
    /// Raised when the user applies a mode.
    /// </summary>
    public event Action<SftpPermission>? PermissionApplied;

    /// <summary>
    /// The mode that the window shows.
    /// </summary>
    public SftpPermission Permission
    {
        get => _permission;
        set
        {
            if (_permission == value)
            {
                return;
            }

            _permission = value;
            ApplyPermission(value);
        }
    }

    /// <summary>
    /// The mode as three octal digits, for example "640".
    /// </summary>
    public string OctalText
    {
        get => OctalBox.Text ?? string.Empty;
        set
        {
            var text = value ?? string.Empty;
            if (string.Equals(OctalBox.Text, text, StringComparison.Ordinal))
            {
                return;
            }

            OctalBox.Text = text;
        }
    }

    internal CheckBox OwnerReadBoxControl => OwnerReadBox;

    internal CheckBox OwnerWriteBoxControl => OwnerWriteBox;

    internal CheckBox OwnerExecuteBoxControl => OwnerExecuteBox;

    internal CheckBox GroupReadBoxControl => GroupReadBox;

    internal CheckBox GroupWriteBoxControl => GroupWriteBox;

    internal CheckBox GroupExecuteBoxControl => GroupExecuteBox;

    internal CheckBox OthersReadBoxControl => OthersReadBox;

    internal CheckBox OthersWriteBoxControl => OthersWriteBox;

    internal CheckBox OthersExecuteBoxControl => OthersExecuteBox;

    internal TextBox OctalBoxControl => OctalBox;

    internal Button ApplyButtonControl => ApplyButton;

    private void OnBoxChanged(object? sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var permission = ReadBoxes();
        _permission = permission;

        _updating = true;
        try
        {
            OctalBox.Text = permission.ToOctalText();
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnOctalTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updating || !TryReadOctal(OctalBox.Text, out var permission))
        {
            return;
        }

        _permission = permission;
        ApplyPermission(permission);
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        PermissionApplied?.Invoke(_permission);
        Close();
    }

    /// <summary>
    /// Puts the mode into the boxes and into the field.
    /// </summary>
    private void ApplyPermission(SftpPermission permission)
    {
        _updating = true;
        try
        {
            OwnerReadBox.IsChecked = permission.OwnerRead;
            OwnerWriteBox.IsChecked = permission.OwnerWrite;
            OwnerExecuteBox.IsChecked = permission.OwnerExecute;
            GroupReadBox.IsChecked = permission.GroupRead;
            GroupWriteBox.IsChecked = permission.GroupWrite;
            GroupExecuteBox.IsChecked = permission.GroupExecute;
            OthersReadBox.IsChecked = permission.OthersRead;
            OthersWriteBox.IsChecked = permission.OthersWrite;
            OthersExecuteBox.IsChecked = permission.OthersExecute;

            OctalBox.Text = permission.ToOctalText();
        }
        finally
        {
            _updating = false;
        }
    }

    private SftpPermission ReadBoxes()
    {
        return new SftpPermission(
            OwnerReadBox.IsChecked == true,
            OwnerWriteBox.IsChecked == true,
            OwnerExecuteBox.IsChecked == true,
            GroupReadBox.IsChecked == true,
            GroupWriteBox.IsChecked == true,
            GroupExecuteBox.IsChecked == true,
            OthersReadBox.IsChecked == true,
            OthersWriteBox.IsChecked == true,
            OthersExecuteBox.IsChecked == true);
    }

    /// <summary>
    /// Reads a mode that the user typed. A mode that is not written in
    /// octal is ignored, so that the boxes keep the mode they had.
    /// </summary>
    private static bool TryReadOctal(string? text, out SftpPermission permission)
    {
        permission = default;

        var digits = (text ?? string.Empty).Trim();
        if (digits.Length is 0 or > 4)
        {
            return false;
        }

        foreach (var digit in digits)
        {
            if (digit is < '0' or > '7')
            {
                return false;
            }
        }

        permission = SftpPermission.FromOctalText(digits);
        return true;
    }
}
