using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace COMTool.App.Views;

/// <summary>
/// r60: 内建远程图像查看器（对齐 MobaXterm 双击图片打开的行为）。
/// 图像以等比缩放适配窗口；解码失败时显示错误提示。
/// </summary>
public sealed partial class SftpImageViewerWindow : Window
{
    public SftpImageViewerWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 填充标题、图像与状态栏。
    /// </summary>
    internal void SetImage(string fileName, Bitmap bitmap, long sizeBytes)
    {
        Title = $"{fileName} — 图像查看";
        ImageHost.Source = bitmap;
        StatusText.Text = $"{fileName}    {sizeBytes} B";
    }

    /// <summary>
    /// 图像解码失败时的错误提示。
    /// </summary>
    internal void SetError(string fileName, string message)
    {
        Title = $"{fileName} — 图像查看";
        ImageHost.Source = null;
        StatusText.Text = message;
    }

    internal Image ImageHostControl => ImageHost;

    internal TextBlock StatusTextControl => StatusText;
}
