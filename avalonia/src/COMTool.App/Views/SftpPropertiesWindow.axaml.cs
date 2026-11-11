using Avalonia.Controls;
using COMTool.Sftp;

namespace COMTool.App.Views;

/// <summary>
/// r60: 只读的远程条目属性窗口（对齐 MobaXterm 右键「属性」）。
/// </summary>
public sealed partial class SftpPropertiesWindow : Window
{
    public SftpPropertiesWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 用条目信息填充各字段。
    /// </summary>
    internal void SetEntry(SftpEntry entry)
    {
        Title = $"文件属性: {entry.Name}";
        NameText.Text = entry.Name;
        PathText.Text = entry.Path;
        TypeText.Text = entry.IsDirectory
            ? "目录"
            : (entry.IsSymbolicLink ? "符号链接" : "文件");
        SizeText.Text = entry.IsDirectory ? "-" : FormatSize(entry.Size);
        ModifiedText.Text = entry.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        PermissionText.Text = $"{entry.Permission.ToRwxString()} ({entry.Permission.ToOctalText()})";
        OwnerText.Text = string.IsNullOrEmpty(entry.Owner) ? "-" : entry.Owner;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:0.##} KB";
        }

        return $"{bytes / 1024.0 / 1024.0:0.##} MB";
    }

    private void OnOkClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    internal TextBlock NameTextControl => NameText;

    internal TextBlock PathTextControl => PathText;

    internal TextBlock TypeTextControl => TypeText;

    internal TextBlock SizeTextControl => SizeText;

    internal TextBlock ModifiedTextControl => ModifiedText;

    internal TextBlock PermissionTextControl => PermissionText;

    internal TextBlock OwnerTextControl => OwnerText;
}
