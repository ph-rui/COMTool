using System.Runtime.InteropServices;
using System.Text;

namespace COMTool.App.Services;

/// <summary>
/// Win32 剪贴板直写（CF_RTF + CF_UNICODETEXT），必须在 UI 线程调用。
/// 为什么不走 Avalonia 剪贴板：CF_RTF 规范要求 ANSI(CP_A) 编码，而 Avalonia
/// Win32 剪贴板把所有字符串格式一律按 UTF-16 写入 HGlobal（OleDataObjectHelper.
/// WriteStringToHGlobal 源码坐实）——标准读取方（Word/WordPad/WinForms）按 ANSI
/// 解码会在第一个 NUL 字节处截断（实机 --selftest-r38 测量：RTF-LEN=1 RTF-HEAD={）。
/// 本类产出的 RTF 为纯 ASCII（见 TerminalRtf），ANSI 字节即原字符。
/// </summary>
internal static class Win32RtfClipboard
{
    private const uint Ghnd = 0x0002; // GMEM_MOVEABLE（剪贴板内存要求）
    private const uint CfUnicodeText = 13;
    private const uint CfRtf = 0x0305; // 预注册 "Rich Text Format"

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    /// <summary>RTF（ANSI）+ 纯文本（UTF-16）写入系统剪贴板。
    /// 失败时不改动原有剪贴板内容（内存先备齐再 EmptyClipboard）。</summary>
    public static bool SetRtfAndText(string rtf, string plain)
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }
        IntPtr hRtf = IntPtr.Zero;
        IntPtr hText = IntPtr.Zero;
        try
        {
            hRtf = AllocAnsi(rtf);
            hText = AllocUtf16(plain);
            if (hRtf == IntPtr.Zero || hText == IntPtr.Zero)
            {
                return false;
            }
            if (!EmptyClipboard())
            {
                return false;
            }
            if (SetClipboardData(CfRtf, hRtf) == IntPtr.Zero)
            {
                return false;
            }
            hRtf = IntPtr.Zero; // 所有权已移交系统
            if (SetClipboardData(CfUnicodeText, hText) == IntPtr.Zero)
            {
                return false;
            }
            hText = IntPtr.Zero;
            return true;
        }
        finally
        {
            if (hRtf != IntPtr.Zero)
            {
                GlobalFree(hRtf);
            }
            if (hText != IntPtr.Zero)
            {
                GlobalFree(hText);
            }
            CloseClipboard();
        }
    }

    /// <summary>ANSI(CP_A) 编码 + NUL 结尾；非 ASCII 替换为 '?'（本应用的 RTF 恒为纯 ASCII）。</summary>
    private static IntPtr AllocAnsi(string text)
    {
        var bytes = new byte[text.Length + 1];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            bytes[i] = c <= 0x7F ? (byte)c : (byte)'?';
        }
        var h = GlobalAlloc(Ghnd, new UIntPtr((uint)bytes.Length));
        if (h == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }
        var p = GlobalLock(h);
        if (p == IntPtr.Zero)
        {
            GlobalFree(h);
            return IntPtr.Zero;
        }
        Marshal.Copy(bytes, 0, p, bytes.Length);
        GlobalUnlock(h);
        return h;
    }

    /// <summary>UTF-16LE 编码 + NUL 结尾（CF_UNICODETEXT 规范）。</summary>
    private static IntPtr AllocUtf16(string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text);
        var h = GlobalAlloc(Ghnd, new UIntPtr((uint)bytes.Length + 2));
        if (h == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }
        var p = GlobalLock(h);
        if (p == IntPtr.Zero)
        {
            GlobalFree(h);
            return IntPtr.Zero;
        }
        Marshal.Copy(bytes, 0, p, bytes.Length);
        Marshal.WriteInt16(p + bytes.Length, 0);
        GlobalUnlock(h);
        return h;
    }
}
