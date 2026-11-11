using System.Text;
using Avalonia.Controls;

namespace COMTool.App.Views;

/// <summary>
/// r60: 只读的内建远程文本查看器（对齐 MobaXterm「打开」文本文件的行为）。
/// 文件内容先经 SFTP 读到内存再展示，只读，不落缓存目录。
/// </summary>
public sealed partial class SftpTextViewerWindow : Window
{
    public SftpTextViewerWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 填充标题、内容与状态栏。
    /// </summary>
    internal void SetContent(string fileName, byte[] data)
    {
        Title = $"{fileName} — 文本查看";
        TextHost.Text = Encoding.UTF8.GetString(data);
        StatusText.Text = $"{fileName}    {data.Length} B";
    }

    internal TextBox TextHostControl => TextHost;

    internal TextBlock StatusTextControl => StatusText;
}
