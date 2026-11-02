using Avalonia.Controls;
using Avalonia.Input;

namespace COMTool.App.Services;

/// <summary>
/// 「复制带格式文本（RTF）」的剪贴板写入分发：
/// Windows 走 Win32RtfClipboard 直写（CF_RTF 规范要求 ANSI，Avalonia Win32 剪贴板
/// 按 UTF-16 写会被标准读取方截断，实机测量 RTF-LEN=1）；其他平台走 Avalonia
/// DataTransfer（X11 字符串为 UTF-8，"text/rtf" platform 格式即正确 MIME atom）。
/// </summary>
public static class TerminalRtfClipboard
{
    /// <summary>写入成功返回 true（Windows 直写 / 其他平台 DataTransfer 提交）。</summary>
    public static async Task<bool> SetAsync(TopLevel? topLevel, string rtf, string plain)
    {
        if (OperatingSystem.IsWindows())
        {
            return Win32RtfClipboard.SetRtfAndText(rtf, plain);
        }
        if (topLevel?.Clipboard is not { } clip)
        {
            return false;
        }
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(
            DataFormat.CreateStringPlatformFormat("Rich Text Format"), rtf));
        transfer.Add(DataTransferItem.Create(
            DataFormat.CreateStringPlatformFormat("text/rtf"), rtf));
        transfer.Add(DataTransferItem.CreateText(plain));
        await clip.SetDataAsync(transfer);
        return true;
    }
}
