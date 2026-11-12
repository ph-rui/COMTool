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
    private string _targetName = string.Empty;
    private bool _updating;
    private bool _applying;

    public SftpPermissionsWindow()
    {
        InitializeComponent();

        ApplyPermission(_permission);
    }

    /// <summary>
    /// Raised when the user applies a mode.
    /// r63: 签名改为 <c>Func&lt;SftpPermission, Task&gt;</c>——窗口 await 该任务期间
    /// 标题栏显示「正在修改权限...」并禁用按钮，SFTP 写回完成后自动关窗
    /// （MobaXterm 图2 行为）。
    /// </summary>
    public Func<SftpPermission, Task>? PermissionApplied;

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
    /// r62: the entry this window edits. Sets the MobaXterm-style title:
    /// Permissions for "name".
    /// </summary>
    public string TargetName
    {
        get => _targetName;
        set
        {
            _targetName = value ?? string.Empty;
            var text = string.IsNullOrEmpty(_targetName)
                ? "Permissions"
                : $"Permissions for \"{_targetName}\"";
            Title = text;

            // r63: 窗口体内的居中抬头同步显示（长文件名不再被标题栏截断）
            if (Heading is not null)
            {
                Heading.Text = text;
            }
        }
    }

    /// <summary>
    /// r63: the centered bold heading shown in the dialog body,
    /// Permissions for "name" (MobaXterm 图2).
    /// </summary>
    public string HeadingText => Heading.Text ?? string.Empty;

    /// <summary>
    /// The mode as an rwx string, for example "rw-r--r--".
    /// </summary>
    public string RwxText
    {
        get => RwxBox.Text ?? string.Empty;
        set
        {
            var text = value ?? string.Empty;
            if (string.Equals(RwxBox.Text, text, StringComparison.Ordinal))
            {
                return;
            }

            RwxBox.Text = text;
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

    internal TextBox RwxBoxControl => RwxBox;

    internal TextBox OctalBoxControl => OctalBox;

    internal Button ApplyButtonControl => ApplyButton;

    internal Button CancelButtonControl => CancelButton;

    // r63: 版式元素（抬头/标签/按钮行），供测试与实机自检断言
    internal TextBlock HeadingControl => Heading;

    internal TextBlock RwxLabelControl => RwxLabel;

    internal TextBlock OwnerLabelControl => OwnerLabel;

    internal TextBlock GroupLabelControl => GroupLabel;

    internal TextBlock OthersLabelControl => OthersLabel;

    internal TextBlock OctalLabelControl => OctalLabel;

    internal StackPanel ButtonRowControl => ButtonRow;

    private void OnBoxChanged(object? sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        _permission = ReadBoxes();
        ApplyPermission(_permission);
    }

    private void OnRwxTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updating || !TryReadRwx(RwxBox.Text, out var permission))
        {
            return;
        }

        _permission = permission;
        ApplyPermission(permission);
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

    private async void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (_applying)
        {
            return;
        }

        _applying = true;
        ApplyButton.IsEnabled = false;
        CancelButton.IsEnabled = false;

        // r63: 写回期间标题栏显示「正在修改权限...」（MobaXterm 图2）
        Title = "正在修改权限...";
        try
        {
            if (PermissionApplied is { } apply)
            {
                await apply(_permission);
            }
        }
        catch
        {
            // 写回失败：错误已由 VM 写入 SFTP 状态栏，窗口照常关闭
        }
        finally
        {
            Close();
        }
    }

    /// <summary>
    /// r62: 取消——不写回权限，直接关闭窗口。
    /// </summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
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

            RwxBox.Text = permission.ToRwxString();
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

    /// <summary>
    /// r62: reads a 9-character rwx string like "rw-r--r--". Anything else
    /// is ignored so the boxes keep the last valid mode.
    /// </summary>
    private static bool TryReadRwx(string? text, out SftpPermission permission)
    {
        permission = default;

        var source = text ?? string.Empty;
        if (source.Length != 9)
        {
            return false;
        }

        for (var group = 0; group < 3; group++)
        {
            var i = group * 3;
            if (source[i] is not ('r' or '-'))
            {
                return false;
            }

            if (source[i + 1] is not ('w' or '-'))
            {
                return false;
            }

            if (source[i + 2] is not ('x' or '-'))
            {
                return false;
            }
        }

        permission = SftpPermission.FromRwxString(source);
        return true;
    }
}
