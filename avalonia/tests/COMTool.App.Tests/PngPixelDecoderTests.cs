using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using COMTool.App.Tools;
using Xunit;

namespace COMTool.App.Tests;

/// <summary>
/// r67.12 的实机自检对落盘 PNG 做纯托管解码（COMTool.App.Tools.PngPixelDecoder），
/// 这里验证解码正确性：
/// ① None 滤波器往返（编码-解码逐字节一致）；
/// ② 五种滤波器（None/Sub/Up/Average/Paeth）按 PNG 规范正向编码后能还原。
/// 不依赖 Avalonia 编码器：headless 下其对指针型 WriteableBitmap 输出为空；
/// 实机（Windows）上 r72 自检承担编码器-解码器集成验证。
/// </summary>
public sealed class PngPixelDecoderTests
{
    [Fact]
    public void TERM39_PngDecodeFilter0RoundTrip()
    {
        const int w = 40, h = 30;
        // 期望像素（B,G,R,A 顺序）
        var expect = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                expect[i + 0] = (byte)(x * 6);       // B
                expect[i + 1] = (byte)(y * 8);       // G
                expect[i + 2] = (byte)(255 - x * 5); // R
                expect[i + 3] = (byte)(128 + y);     // A
            }
        }

        var png = EncodePng(expect, w, h, filterPerRow: y => 0);
        PngPixelDecoderTestBridge.Decode(png, out var dw, out var dh, out var px);

        Assert.Equal(w, dw);
        Assert.Equal(h, dh);
        Assert.Equal(expect.Length, px.Length);
        for (var i = 0; i < expect.Length; i++)
        {
            Assert.Equal(expect[i], px[i]);
        }
    }

    [Fact]
    public void TERM40_PngDecodeAllFilterTypes()
    {
        const int w = 7, h = 5;
        var expect = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                expect[i + 0] = (byte)(x * 30);
                expect[i + 1] = (byte)(y * 40);
                expect[i + 2] = (byte)(255 - x * y * 5);
                expect[i + 3] = 200;
            }
        }

        // 每行一个滤波器类型：0..4
        var png = EncodePng(expect, w, h, filterPerRow: y => (byte)(y % 5));
        PngPixelDecoderTestBridge.Decode(png, out var dw, out var dh, out var px);

        Assert.Equal(w, dw);
        Assert.Equal(h, dh);
        Assert.Equal(expect.Length, px.Length);
        for (var i = 0; i < expect.Length; i++)
        {
            Assert.Equal(expect[i], px[i]);
        }
    }

    /// <summary>
    /// 按 PNG 规范正向编码（非隔行、8-bit、RGBA/色型 6、zlib 压缩、CRC32 齐全）。
    /// filterPerRow(y) 指定每行的滤波器（0=None 1=Sub 2=Up 3=Average 4=Paeth）。
    /// </summary>
    private static byte[] EncodePng(byte[] expectBgra, int w, int h,
        Func<int, byte> filterPerRow)
    {
        var rowBytes = w * 4;
        var stored = new byte[h * (rowBytes + 1)];
        var recon = new byte[h * rowBytes];  // 重建值：滤波参考必须用重建样本，不是存储字节
        for (var y = 0; y < h; y++)
        {
            var ftype = filterPerRow(y);
            stored[y * (rowBytes + 1)] = ftype;
            // PNG 规范 15.6.7.1：参考样本按「像素」定位（同通道上一像素，行内像素首字节为 0），
            // 不是按线性前一个字节。2026-11-26 以 Pillow 自编码 PNG 逐像素对照修正
            // （旧实现按前一字节，与 Skia/Pillow 编码的实机 PNG 不符）。
            for (var lin = 0; lin < rowBytes; lin++)
            {
                var x = lin / 4;
                var c = lin % 4;
                // expect 为 B,G,R,A；PNG 通道 c 为 R,G,B,A → 源下标 2,1,0,3
                var src = c == 3 ? 3 : 2 - c;
                var v = expectBgra[(y * w + x) * 4 + src];
                int a = lin >= 4 ? recon[y * rowBytes + lin - 4] : 0;
                int b = y > 0 ? recon[(y - 1) * rowBytes + lin] : 0;
                int u = lin >= 4 && y > 0 ? recon[(y - 1) * rowBytes + lin - 4] : 0;
                int p = ftype switch
                {
                    0 => v,
                    1 => v - a,
                    2 => v - b,
                    3 => v - (a + b) / 2,
                    _ => v - Paeth(a, b, u),
                };
                stored[y * (rowBytes + 1) + 1 + lin] = (byte)(p & 0xFF);
                recon[y * rowBytes + lin] = (byte)v;
            }
        }

        using var zms = new MemoryStream();
        using (var zs = new ZLibStream(zms, CompressionMode.Compress, leaveOpen: true))
        {
            zs.Write(stored, 0, stored.Length);
        }

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WriteChunk(png, "IHDR", new byte[]
        {
            (byte)(w >> 24), (byte)(w >> 16), (byte)(w >> 8), (byte)w,
            (byte)(h >> 24), (byte)(h >> 16), (byte)(h >> 8), (byte)h,
            8, 6, 0, 0, 0,
        });
        WriteChunk(png, "IDAT", zms.ToArray());
        WriteChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        s.Write(new[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length });
        var tb = Encoding.ASCII.GetBytes(type);
        s.Write(tb);
        s.Write(data);
        s.Write(Crc32(tb, data));
    }

    private static byte[] Crc32(params byte[][] parts)
    {
        unchecked
        {
            var table = new uint[256];
            for (var n = 0; n < 256; n++)
            {
                var c = (uint)n;
                for (var k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }
                table[n] = c;
            }
            var crc = 0xFFFFFFFFu;
            foreach (var p in parts)
            {
                foreach (var b in p)
                {
                    crc = table[(byte)(crc ^ b)] ^ (crc >> 8);
                }
            }
            crc ^= 0xFFFFFFFFu;
            return new[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc };
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : (pb <= pc ? b : c);
    }
}

/// <summary>把 internal 解码器暴露给测试程序集（与 InternalsVisibleTo 配合）。</summary>
internal static class PngPixelDecoderTestBridge
{
    public static void Decode(byte[] png, out int width, out int height, out byte[] pixels)
        => COMTool.App.Tools.PngPixelDecoder.Decode(png, out width, out height, out pixels);
}
