using System.Collections.Generic;
using System.Text;
using Avalonia.Media;
using Iciclecreek.Avalonia.Terminal;
// 注意：COMTool.Terminal 命名空间（同属 COMTool.*）会把简单名 "Terminal" 解析成
// 命名空间（CS0118），故用别名显式指向 XTerm 引擎类型。
using XTermTerminal = XTerm.Terminal;

namespace COMTool.App.Services;

/// <summary>
/// 把终端选区渲染成 RTF（保留逐格前景/背景色），供「复制带格式文本（RTF）」
/// 粘贴进 Word/WordPad 等富文本应用时保持原色（对齐 MobaXterm 的 Copy RTF）。
/// 输出为纯 ASCII 的 RTF 文档：非 ASCII 字符用 \uNNNN 转义，颜色表按出现顺序去重，
/// 因此任何平台编码下都可安全写入剪贴板。
/// </summary>
public static class TerminalRtf
{
    /// <summary>按当前选区构建 RTF 文档；无选区或选区内无可显示内容时返回 null。</summary>
    public static string? BuildRtf(XTermTerminal term, double fontPixelSize, string fontFamily)
    {
        if (term.Selection is not { HasSelection: true })
        {
            return null;
        }
        if (!term.Selection.TryGetSelection(out var range))
        {
            return null;
        }

        var snapshot = term.Colors.Take();
        var defaultFg = snapshot.Foreground; // 0xRRGGBB
        var defaultBg = snapshot.Background; // 0xRRGGBB
        var buffer = term.Buffer;

        // ── 第一遍：逐行扫描选区，把相邻同色格合并成 run ──
        // SelectionRange 的 Y 是绝对 buffer 行（XTerm 源码注释坐实，跨滚动回看有效）；
        // 每行列跨度用 TryGetRowSpan 取（自动处理换行折行），与 GetSelectionText 同规则。
        var rows = new List<List<(int Fg, int Bg, string Text)>>();
        for (var y = range.StartY; y <= range.EndY; y++)
        {
            if (y < 0 || y >= buffer.Lines.Length)
            {
                continue;
            }
            var line = buffer.Lines[y];
            if (line is null)
            {
                continue;
            }
            if (!range.TryGetRowSpan(y, term.Cols, out var startX, out var endX))
            {
                continue;
            }

            var runs = new List<(int Fg, int Bg, string Text)>();
            var runFg = defaultFg;
            var runBg = defaultBg;
            var runText = new StringBuilder();
            void Flush()
            {
                if (runText.Length > 0)
                {
                    runs.Add((runFg, runBg, runText.ToString()));
                    runText.Clear();
                }
            }

            for (var x = startX; x <= endX; x++)
            {
                var cell = line[x];
                if (cell.Width <= 0)
                {
                    continue; // 宽字符（CJK）第二半格，内容已由前一格计入
                }
                var content = cell.Content;
                if (string.IsNullOrEmpty(content))
                {
                    continue; // 空格占位（CodePoint==0）
                }
                // null = 默认色，回退到终端当前默认前景/背景——与渲染器
                // （BufferCellExtensions）同一条解析路径，含 bold→bright 规则。
                var fg = (int)((cell.GetForegroundColor(snapshot, boldIsBright: true)
                    ?? FromRgb24(defaultFg)).ToUInt32() & 0x00FFFFFF);
                var bg = (int)((cell.GetBackgroundColor(snapshot)
                    ?? FromRgb24(defaultBg)).ToUInt32() & 0x00FFFFFF);
                if (runText.Length > 0 && (fg != runFg || bg != runBg))
                {
                    Flush();
                }
                runFg = fg;
                runBg = bg;
                runText.Append(content);
            }
            Flush();
            rows.Add(runs);
        }

        if (rows.Count == 0)
        {
            return null;
        }

        // ── 第二遍：颜色表（默认前景/背景占前两位，其余按出现顺序去重）──
        var orderedColors = new List<int> { defaultFg, defaultBg };
        foreach (var runs in rows)
        {
            foreach (var (fg, bg, _) in runs)
            {
                if (!orderedColors.Contains(fg))
                {
                    orderedColors.Add(fg);
                }
                if (!orderedColors.Contains(bg))
                {
                    orderedColors.Add(bg);
                }
            }
        }
        var colorIndex = new Dictionary<int, int>();
        for (var i = 0; i < orderedColors.Count; i++)
        {
            colorIndex[orderedColors[i]] = i + 1; // colortbl 第 0 位是占位，颜色从 1 起
        }

        var sb = new StringBuilder();
        sb.Append("{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fmodern ");
        AppendEscaped(sb, fontFamily);
        sb.Append(";}}{\\colortbl ;");
        foreach (var rgb in orderedColors)
        {
            sb.Append("\\red").Append((rgb >> 16) & 0xFF)
              .Append("\\green").Append((rgb >> 8) & 0xFF)
              .Append("\\blue").Append(rgb & 0xFF).Append(';');
        }
        // 字号：px → pt(×72/96) → 半点(×2)，即 px×1.5；13.5px → \fs20
        sb.Append("}\\f0\\fs").Append(Math.Max(2, (int)Math.Round(fontPixelSize * 1.5))).Append(' ');

        foreach (var runs in rows)
        {
            foreach (var (fg, bg, text) in runs)
            {
                sb.Append("\\cf").Append(colorIndex[fg])
                  .Append("\\cb").Append(colorIndex[bg]);
                AppendEscaped(sb, text);
            }
            sb.Append("\\par");
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>0xRRGGBB → Color（默认色回退用）。</summary>
    private static Color FromRgb24(int rgb) =>
        Color.FromRgb((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    /// <summary>RTF 转义：\ { } 必须转义；非 ASCII 用 \uNNNN（有符号）+ 一个回退空格。</summary>
    private static void AppendEscaped(StringBuilder sb, string text)
    {
        for (var i = 0; i < text.Length;)
        {
            int cp;
            if (char.IsHighSurrogate(text[i])
                && i + 1 < text.Length
                && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i += 2;
            }
            else
            {
                cp = text[i];
                i += 1;
            }

            switch (cp)
            {
                case 0x5C:
                    sb.Append("\\\\");
                    break;
                case 0x7B:
                    sb.Append("\\{");
                    break;
                case 0x7D:
                    sb.Append("\\}");
                    break;
                default:
                    if (cp >= 0x20 && cp <= 0x7E)
                    {
                        sb.Append((char)cp);
                    }
                    else if (cp != 0)
                    {
                        sb.Append("\\u").Append(cp > 0x7FFF ? cp - 0x10000 : cp).Append(' ');
                    }
                    break;
            }
        }
    }
}
