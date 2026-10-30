namespace COMTool.Connections;

/// <summary>
/// 终端行尾仿真的共享开关状态。两个字段对齐 MobaXterm「实时终端重配置 → 终端」
/// 里的两项（默认均关闭）：
/// <list type="bullet">
///   <item>Implicit CR in every LF —— 收到 \n 时隐式补一个 \r（把裸 LF 当整行换行）。</item>
///   <item>Implicit LF in every CR —— 收到 \r 时隐式补一个 \n（把裸 CR 当整行换行）。</item>
/// </list>
/// 该对象被所有终端的 <see cref="LineEndingStream"/> 共享引用，设置对话框里勾选/取消
/// 会直接改这里，因而对已连接终端立即生效（MobaXterm 的“Live”行为），无需重连。
/// </summary>
public sealed class LineEndingOptions
{
    /// <summary>Implicit CR in every LF：每个 \n 前补 \r。</summary>
    public bool ImplicitCrInLf;

    /// <summary>Implicit LF in every CR：每个 \r 后补 \n（紧跟的 \n 不重复补）。</summary>
    public bool ImplicitLfInCr;
}

/// <summary>
/// 套在终端读流（<c>PtyStreamConnection.ReaderStream</c>）之外的行尾变换层。
/// 只对“显示字节”做变换：包在最外层时，Zmodem 二进制已被内层 <c>ZmodemStream</c>
/// 消费，不会经过这里，故不影响 Zmodem 传文件。
///
/// 变换规则（逐项字节，带 1 字节前瞻以区分裸 \r 与 \r\n 成对）：
/// <list type="bullet">
///   <item>ImplicitLfInCr 开：裸 \r → \r\n；\r\n 保持 \r\n（不产生双空行）。</item>
///   <item>ImplicitCrInLf 开：\n → \r\n（前面已有 \r 时不重复）。</item>
///   <item>两项都关：纯直通（整块转发，零额外开销）。</item>
/// </list>
/// 开关读取的是 <see cref="LineEndingOptions"/> 的实时值，可在运行中切换。
/// </summary>
public sealed class LineEndingStream : Stream
{
    private const int ChunkSize = 4096;

    private readonly Stream _inner;
    private readonly LineEndingOptions _options;

    private readonly byte[] _in = new byte[ChunkSize];
    private byte[] _out = new byte[ChunkSize * 2];
    private int _outPos;
    private int _outLen;

    // 前瞻：见 \r 时先扣住，等下一个字节才能判断它是裸 \r 还是 \r\n 的前半。
    private bool _pendingCr;
    private bool _eof;

    public LineEndingStream(Stream inner, LineEndingOptions options)
    {
        _inner = inner;
        _options = options;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        var crInLf = _options.ImplicitCrInLf;
        var lfInCr = _options.ImplicitLfInCr;

        // 快路径：两项都关且无扣留的 \r，整块直通，避免逐字节开销。
        if (!crInLf && !lfInCr && !_pendingCr)
        {
            if (_eof)
            {
                return 0;
            }

            var n = _inner.Read(buffer, offset, count);
            if (n <= 0)
            {
                _eof = true;
                return 0;
            }

            return n;
        }

        // 慢路径必须保持内层流“有数据就返回”的读语义（返回可用量，可小于 count）：
        // 库的读循环是 Read(buffer, 0, 0x40000)，内层（如 ZmodemStream）等 ≥1 字节
        // 后只返回当前可用字节。若此处坚持填满 count，远端一次输出不足 256KB 时
        // Read 就会卡在内层等待，读循环停摆、已变换文本无法上屏——这是使能
        // 任一开关后终端黑屏的根因。正确语义：一次 Read 最多阻塞内层一次，
        // 一旦有可交付的变换输出立即返回；内层非 EOF 时绝不返回 0。
        while (true)
        {
            // 1) 已有变换输出：直接返回可用量（不碰内层，不阻塞）。
            if (_outPos < _outLen)
            {
                var n = Math.Min(count, _outLen - _outPos);
                Buffer.BlockCopy(_out, _outPos, buffer, offset, n);
                _outPos += n;
                if (_outPos >= _outLen)
                {
                    _outPos = 0;
                    _outLen = 0;
                }
                return n;
            }

            // 2) 输出空了，从内层拉一块原始数据做变换（内层阻塞至 ≥1 字节，或 EOF 返回 0）。
            var read = _eof ? 0 : _inner.Read(_in, 0, _in.Length);
            if (read <= 0)
            {
                _eof = true;
                read = 0;
            }

            if (read > 0)
            {
                TransformChunk(read, crInLf, lfInCr);
                if (_outPos < _outLen)
                {
                    continue; // 转出了数据，回到第 1 步立即交付
                }
                // 本块字节全部被扣作待决 \r（等下一字节才能区分裸 \r / \r\n），
                // 输出仍为空：继续拉下一字节判定；内层没到 EOF，不得返回 0。
                continue;
            }

            // 3) 内层到 EOF：结算扣留的 \r，交付剩余输出，没有剩余才返回 0。
            FlushPending(crInLf, lfInCr);
            if (_outPos >= _outLen)
            {
                return 0;
            }

            var n2 = Math.Min(count, _outLen - _outPos);
            Buffer.BlockCopy(_out, _outPos, buffer, offset, n2);
            _outPos += n2;
            if (_outPos >= _outLen)
            {
                _outPos = 0;
                _outLen = 0;
            }
            return n2;
        }
    }

    // 把 _in[0..read) 的原始字节变换后写入 _out（输出与输入不同数组，无自引用）。
    private void TransformChunk(int read, bool crInLf, bool lfInCr)
    {
        _outPos = 0;
        _outLen = 0;

        for (var i = 0; i < read; i++)
        {
            var b = _in[i];
            if (_pendingCr)
            {
                _pendingCr = false;
                Emit((byte)'\r');
                if (b == (byte)'\n')
                {
                    // \r\n 成对：一个换行，不再补。
                    Emit((byte)'\n');
                    continue;
                }

                if (lfInCr)
                {
                    Emit((byte)'\n'); // 裸 \r → 补 LF
                }

                // 落到下方正常处理当前字节 b。
            }

            if (b == (byte)'\r')
            {
                if (lfInCr)
                {
                    _pendingCr = true; // 扣留，等下一字节
                }
                else
                {
                    Emit((byte)'\r');
                }
            }
            else if (b == (byte)'\n')
            {
                if (crInLf)
                {
                    Emit((byte)'\r');
                }

                Emit((byte)'\n');
            }
            else
            {
                Emit(b);
            }
        }
    }

    private void Emit(byte b)
    {
        if (_outLen >= _out.Length)
        {
            Array.Resize(ref _out, _out.Length * 2);
        }

        _out[_outLen++] = b;
    }

    private void FlushPending(bool crInLf, bool lfInCr)
    {
        if (_pendingCr)
        {
            _pendingCr = false;
            Emit((byte)'\r');
            if (lfInCr)
            {
                Emit((byte)'\n');
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        // 不拥有内层流（由 PtyStreamConnection 负责释放）。
        base.Dispose(disposing);
    }
}
