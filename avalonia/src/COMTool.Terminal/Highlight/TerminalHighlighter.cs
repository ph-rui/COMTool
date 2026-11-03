using System.Text;
using XTerm;
using XTerm.Buffer;
using XTerm.Events;
using XTermTerminal = XTerm.Terminal;

namespace COMTool.Terminal.Highlight;

/// <summary>
/// 终端语法高亮着色器：订阅 XTerm 的 LineFed 事件，对每一行完成（含光标所在活动行）
/// 按方案规则重着色。设计要点（r39 定稿）：
/// - 同线程：LineFed 在 Terminal.Write 解析路径内同步触发，与解析器同线程操作 buffer，无竞态；
/// - 只染默认前景色（fg==256/mode0）的格子，程序自带颜色不被覆盖；
/// - 宽字符（CJK）按 cell 宽度映射，匹配按字符偏移、落色按格子覆盖；
/// - 水标记（watermark）只处理新增行；方案切换调 ApplyToAll 全量重染；
///   缓冲区收缩（Reset/ClearScrollback）后水印归零，新写入的行重新参与着色；
/// - 备用屏（vim/less 等 alt screen）不着色。
/// </summary>
public sealed class TerminalHighlighter : IDisposable
{
    private readonly XTermTerminal _term;
    private int _nextRow;
    private bool _disposed;

    /// <summary>当前方案；置为 Default 后新行不再着色。</summary>
    public HighlightScheme Scheme { get; set; }

    public TerminalHighlighter(XTermTerminal term, HighlightScheme scheme)
    {
        _term = term;
        Scheme = scheme;
        _nextRow = term.Buffer.YBase + term.Buffer.Y; // 从“现在”之后开始着色
        _term.LineFed += OnLineFed;
        _term.Resized += OnResized;
        _term.BufferChanged += OnBufferSwitched;
    }

    private void OnLineFed(object? sender, TerminalEvents.LineFeedEventArgs e)
    {
        if (_disposed || Scheme == HighlightScheme.Default)
        {
            return;
        }
        if (_term.IsAlternateBufferActive)
        {
            return;
        }
        try
        {
            // LF 触发时游标已落到下一行：活动行（可能未写完）= 游标所在行
            ColorizeUpTo(_term.Buffer.YBase + _term.Buffer.Y);
        }
        catch
        {
            // 着色失败绝不影响终端输出路径
        }
    }

    private void OnResized(object? sender, TerminalEvents.ResizeEventArgs e)
    {
        // 重排后旧行的折行可能变化：放弃历史，只染新来的行
        _nextRow = _term.Buffer.YBase + _term.Buffer.Y;
    }

    private void OnBufferSwitched(object? sender, TerminalEvents.BufferChangedEventArgs e)
    {
        // normal<->alt 切换：重置水印，只处理切换后的新内容
        _nextRow = _term.Buffer.YBase + _term.Buffer.Y;
    }

    /// <summary>把 [watermark, lastRow] 全部着色一次（方案切换后全量重染）。</summary>
    public void ApplyToAll()
    {
        if (_disposed)
        {
            return;
        }
        if (Scheme == HighlightScheme.Default)
        {
            _nextRow = _term.Buffer.YBase + _term.Buffer.Y;
            return;
        }
        ColorizeUpTo(_term.Buffer.Length - 1);
    }

    private void ColorizeUpTo(int lastRow)
    {
        var buf = _term.Buffer;
        if (lastRow >= buf.Length)
        {
            lastRow = buf.Length - 1;
        }
        if (lastRow < _nextRow)
        {
            // 缓冲区被 Reset/ClearScrollback 收缩：旧水印失效，从头重染
            // （复位后写入的新行必须能被着色，不能错过）。
            _nextRow = 0;
        }
        for (var row = _nextRow; row <= lastRow; row++)
        {
            ColorizeRow(row);
        }
        _nextRow = lastRow + 1;
    }

    private void ColorizeRow(int absRow)
    {
        var buf = _term.Buffer;
        if (absRow < 0 || absRow >= buf.Length)
        {
            return;
        }
        var line = buf.GetLine(absRow);
        if (line is null || line.Length == 0)
        {
            return;
        }

        // 组装行文本 + 每格字符偏移表（宽字符一格 1 个字符串、Width=2，续格 Width=0 跳过）
        var sb = new StringBuilder(buf.Cols);
        var cellStart = new int[line.Length];
        var cellEnd = new int[line.Length];
        for (var x = 0; x < line.Length; x++)
        {
            cellStart[x] = sb.Length;
            var c = line[x];
            if (c.Width > 0 && !string.IsNullOrEmpty(c.Content))
            {
                sb.Append(c.Content);
            }
            cellEnd[x] = sb.Length;
        }

        var spans = SyntaxHighlightRules.FindSpans(sb.ToString(), Scheme);
        if (spans.Count == 0)
        {
            return;
        }

        foreach (var span in spans)
        {
            for (var x = 0; x < line.Length; x++)
            {
                if (cellStart[x] >= span.End || cellEnd[x] <= span.Start)
                {
                    continue;
                }
                var cell = line[x];
                // 只染默认前景色：程序已显式着色的格子不动
                if (cell.Attributes.GetFgColorMode() == 0 && cell.Attributes.GetFgColor() == 256)
                {
                    cell.Attributes.SetFgColor(span.Color, 0);
                    line.SetCell(x, ref cell);
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _term.LineFed -= OnLineFed;
        _term.Resized -= OnResized;
        _term.BufferChanged -= OnBufferSwitched;
    }
}
