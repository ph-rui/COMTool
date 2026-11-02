using System.Diagnostics;
using System.Text;

namespace COMTool.App.Services;

/// <summary>
/// 打印辅助（仅 Windows）：Avalonia 12 没有打印 API，走系统打印对话框——
/// 文本先落临时文件（避免命令行长度限制），再起 PowerShell + WinForms
/// （PrintDialog + PrintDocument）打印，与 MobaXterm 的系统对话框行为一致。
/// </summary>
public static class TerminalPrint
{
    private const string PathToken = "__COMTOOL_PRINT_FILE__";

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>生成 PowerShell 打印脚本（纯函数，任意环境可测）。
    /// 非插值原始字符串：PS 里的花括号原样保留，路径经 Replace 注入。</summary>
    public static string BuildScript(string textFilePath)
    {
        var safe = textFilePath.Replace("'", "''");
        const string template = """
            $ErrorActionPreference = 'Stop'
            Add-Type -AssemblyName System.Windows.Forms
            Add-Type -AssemblyName System.Drawing
            $lines = [System.IO.File]::ReadAllLines('__COMTOOL_PRINT_FILE__', [System.Text.Encoding]::UTF8)
            $doc = New-Object System.Windows.Forms.PrintDocument
            $doc.DocumentName = 'COMTool terminal output'
            $font = New-Object System.Drawing.Font('Consolas', 10)
            $idx = 0
            $handler = [System.EventHandler[System.Windows.Forms.PrintPageEventArgs]]{
                param($s, $e)
                $g = $e.Graphics
                $y = $e.MarginBounds.Top
                $more = $false
                while ($idx -lt $lines.Count) {
                    if ($y -gt $e.MarginBounds.Bottom) { $more = $true; break }
                    $g.DrawString($lines[$idx], $font, [System.Drawing.Brushes]::Black, $e.MarginBounds.Left, $y)
                    $idx++
                    $y += 16
                }
                $e.HasMorePages = $more
            }
            $doc.PrintPage += $handler
            $dlg = New-Object System.Windows.Forms.PrintDialog
            $dlg.Document = $doc
            $dlg.UsePageDialog = $false
            if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
                $doc.PrinterSettings = $dlg.PrinterSettings
                [void]$doc.Print()
            }
            exit 0
            """;
        return template.Replace(PathToken, safe);
    }

    /// <summary>起 PowerShell 执行脚本（base64 EncodedCommand 防引号问题）。
    /// 返回 (退出码, stdout, stderr)。</summary>
    public static async Task<(int Code, string Out, string Err)> RunPowershellAsync(string script)
    {
        var b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + b64,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 powershell.exe");
        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (proc.ExitCode, await outTask, await errTask);
    }

    /// <summary>打印文本；非 Windows 直接返回失败说明，不抛异常。</summary>
    public static async Task<(bool Ok, string Message)> PrintTextAsync(string text)
    {
        if (!IsSupported)
        {
            return (false, "打印仅支持 Windows 系统");
        }
        string? tmp = null;
        try
        {
            tmp = Path.Combine(Path.GetTempPath(), "comtool-print-" + Guid.NewGuid().ToString("N") + ".txt");
            await File.WriteAllTextAsync(tmp, text);
            var (code, outp, err) = await RunPowershellAsync(BuildScript(tmp));
            if (code == 0)
            {
                return (true, "已发送打印任务");
            }
            return (false, "打印失败：" + FirstNonEmptyLine(err, outp));
        }
        catch (Exception ex)
        {
            return (false, "打印失败：" + ex.Message);
        }
        finally
        {
            if (tmp is not null)
            {
                try
                {
                    File.Delete(tmp);
                }
                catch
                {
                    // best-effort 清理
                }
            }
        }
    }

    private static string FirstNonEmptyLine(string first, string second)
    {
        foreach (var source in new[] { first, second })
        {
            foreach (var line in (source ?? "").Split('\n'))
            {
                if (line.Trim().Length > 0)
                {
                    return line.Trim();
                }
            }
        }
        return "未知错误";
    }
}
