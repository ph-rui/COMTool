using System.IO;
using System.Text;
using COMTool.Connections;
using Xunit;

namespace COMTool.Connections.Tests;

/// <summary>
/// LineEndingStream（MobaXterm「Implicit CR in every LF / Implicit LF in every CR」
/// 的 COMTool 实现）行为验证。
/// </summary>
public sealed class LineEndingStreamTests
{
    /// <summary>按固定块大小分批吐出数据的只读流，用于构造跨块边界。</summary>
    private sealed class ChunkedStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunk;
        private int _pos;

        public ChunkedStream(string data, int chunk)
        {
            _data = Encoding.ASCII.GetBytes(data);
            _chunk = chunk;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position
        {
            get => _pos;
            set => throw new System.NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(_chunk, Math.Min(count, _data.Length - _pos));
            if (n <= 0)
            {
                return 0;
            }

            Buffer.BlockCopy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new System.NotSupportedException();

        public override void SetLength(long value) => throw new System.NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new System.NotSupportedException();
    }

    /// <summary>
    /// 可控供给流：测试方按需 Supply 字节，Read 非阻塞地返回当前可用字节，
    /// 队列耗尽时才返回 0。用于验证同一条流在运行中切换开关（Live）的行为，
    /// 避免 MemoryStream 读干后 _eof 置位、无法二次喂数据的问题。
    /// </summary>
    private sealed class SupplyStream : Stream
    {
        private readonly System.Collections.Generic.Queue<byte> _q = new();

        public void Supply(string s)
        {
            foreach (var b in Encoding.ASCII.GetBytes(s))
            {
                _q.Enqueue(b);
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new System.NotSupportedException();
        public override long Position
        {
            get => throw new System.NotSupportedException();
            set => throw new System.NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(count, _q.Count);
            for (var i = 0; i < n; i++)
            {
                buffer[offset + i] = _q.Dequeue();
            }

            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new System.NotSupportedException();

        public override void SetLength(long value) => throw new System.NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new System.NotSupportedException();
    }

    private static LineEndingOptions Options(bool crInLf, bool lfInCr) =>
        new() { ImplicitCrInLf = crInLf, ImplicitLfInCr = lfInCr };

    /// <summary>读干 stream 并转字符串（小 buffer 强制多轮 Read，适用于一次性数据 + EOF 用例）。</summary>
    private static string ReadAll(LineEndingStream stream)
    {
        using var output = new MemoryStream();
        var buffer = new byte[3];
        int n;
        while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, n);
        }

        return Encoding.ASCII.GetString(output.ToArray());
    }

    private static string Transform(string input, bool crInLf, bool lfInCr, int chunk = 4096) =>
        ReadAll(new LineEndingStream(new ChunkedStream(input, chunk), Options(crInLf, lfInCr)));

    [Fact]
    public void BothOff_PassthroughByteForByte()
    {
        Assert.Equal("a\r\nb", Transform("a\r\nb", false, false));
        Assert.Equal("a\rb", Transform("a\rb", false, false));
        Assert.Equal("a\nb", Transform("a\nb", false, false));
        Assert.Equal("plain", Transform("plain", false, false));
    }

    [Fact]
    public void BothOn_LoneLfBecomesCrLf() =>
        Assert.Equal("a\r\nb", Transform("a\nb", true, true));

    [Fact]
    public void BothOn_CrLfPairPreserved() =>
        Assert.Equal("a\r\nb", Transform("a\r\nb", true, true));

    [Fact]
    public void BothOn_LoneCrBecomesCrLf() =>
        Assert.Equal("a\r\nb", Transform("a\rb", true, true));

    [Fact]
    public void BothOn_LoneLfAtEofBecomesCrLf() =>
        Assert.Equal("a\r\n", Transform("a\n", true, true));

    [Fact]
    public void CrInLfOnly_LoneLfGainsCarriageReturn() =>
        Assert.Equal("a\r\nb", Transform("a\nb", true, false));

    [Fact]
    public void LfInCrOnly_LoneCrGainsLineFeed() =>
        Assert.Equal("a\r\nb", Transform("a\rb", false, true));

    [Fact]
    public void LfInCrOnly_CrlfPairIsNotDoubled() =>
        Assert.Equal("a\r\nb", Transform("a\r\nb", false, true));

    [Fact]
    public void CrInLfOnly_CrlfPairGetsOneExtraHarmlessCr() =>
        // \r 原样保留 + \n 补 \r：终端侧 \r\r\n 与 \r\n 渲染等价（两次回行首再换行）。
        Assert.Equal("a\r\r\nb", Transform("a\r\nb", true, false));

    [Fact]
    public void TrailingLoneCrAtEof_GetsImplicitLf() =>
        Assert.Equal("abc\r\n", Transform("abc\r", false, true));

    [Fact]
    public void TrailingLoneCrAtEof_NoImplicitLfWhenOff() =>
        Assert.Equal("abc\r", Transform("abc\r", false, false));

    [Fact]
    public void ChunkBoundary_CrAndLfSplitAcrossChunksIsStillOnePair()
    {
        // \r 在 chunk 末尾、\n 在下一 chunk 开头：不得误判为裸 \r 而多补 LF。
        Assert.Equal("a\r\nb", Transform("a\r\nb", false, true, chunk: 2));
        // 逐字节最极端分块同样正确。
        Assert.Equal("a\r\nb", Transform("a\r\nb", true, true, chunk: 1));
    }

    [Fact]
    public void OptionsToggleLive_WithoutRebuildingStream()
    {
        var options = Options(false, false);
        using var inner = new SupplyStream();
        var stream = new LineEndingStream(inner, options);
        var buf = new byte[16];

        // 阶段一：双关 → 直通。供给 "a\rb"，精确读 3 字节。
        inner.Supply("a\rb");
        var n1 = stream.Read(buf, 0, 3);
        Assert.Equal(3, n1);
        Assert.Equal("a\rb", Encoding.ASCII.GetString(buf, 0, n1));

        // 阶段二：运行中打开 ImplicitLfInCr，同一条流立即按新开关变换。
        // 供给 "c\rd"（裸 \r），应变成 "c\r\nd"，精确读 4 字节。
        options.ImplicitLfInCr = true;
        inner.Supply("c\rd");
        var n2 = stream.Read(buf, 0, 4);
        Assert.Equal(4, n2);
        Assert.Equal("c\r\nd", Encoding.ASCII.GetString(buf, 0, n2));

        // 阶段三：再关 → 恢复直通。供给 "e\rf"，精确读 3 字节。
        options.ImplicitLfInCr = false;
        inner.Supply("e\rf");
        var n3 = stream.Read(buf, 0, 3);
        Assert.Equal(3, n3);
        Assert.Equal("e\rf", Encoding.ASCII.GetString(buf, 0, n3));
    }
}
