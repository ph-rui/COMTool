using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace COMTool.App.Tools;

/// <summary>
/// 纯托管 PNG 解码器（非隔行、8-bit、色彩类型 0/2/4/6）。
/// 用途：实机自检（r72）对已落盘截图逐像素分析。
/// 不在进程内二次创建 RenderTargetBitmap 或读回 GPU 表面：
/// 2026-11-26 两次实机事件日志证实，VM 虚拟 GPU 上对已挂树控件重复
/// Render + CopyPixels 会令渲染线程 AccessViolation（FailFast，栈落在
/// DefaultRenderLoop.TimerTick → StringLogSink.FormatSource）。
/// 输出按 B,G,R,A 字节序排列，与 RenderTargetBitmap 的 BGRA32 一致，
/// 现有逐像素分析代码无需改动。
/// </summary>
internal static class PngPixelDecoder
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static void Decode(byte[] png, out int width, out int height, out byte[] pixels)
    {
        width = 0;
        height = 0;
        pixels = Array.Empty<byte>();

        if (png.Length < Signature.Length)
        {
            throw new InvalidDataException("PNG 长度不足");
        }
        for (var i = 0; i < Signature.Length; i++)
        {
            if (png[i] != Signature[i])
            {
                throw new InvalidDataException("PNG 签名不符");
            }
        }

        var pos = Signature.Length;
        var bitDepth = 0;
        var colorType = 0;
        var idat = new MemoryStream();
        var iend = false;

        while (pos + 8 <= png.Length)
        {
            var len = ReadBE32(png, pos);
            var type = Encoding.ASCII.GetString(png, pos + 4, 4);
            pos += 8;
            if (pos + len + 4 > png.Length)
            {
                throw new InvalidDataException($"PNG 块 {type} 越界");
            }
            if (type == "IHDR")
            {
                if (len != 13)
                {
                    throw new InvalidDataException("IHDR 长度不符");
                }
                width = ReadBE32(png, pos);
                height = ReadBE32(png, pos + 4);
                bitDepth = png[pos + 8];
                colorType = png[pos + 9];
                if (bitDepth != 8 || png[pos + 10] != 0 || png[pos + 11] != 0 || png[pos + 12] != 0)
                {
                    throw new InvalidDataException(
                        $"仅支持 8-bit 非隔行 PNG（bitDepth={bitDepth}）");
                }
                if (colorType is not (0 or 2 or 4 or 6))
                {
                    throw new InvalidDataException($"不支持的色彩类型 {colorType}");
                }
            }
            else if (type == "IDAT")
            {
                idat.Write(png, pos, len);
            }
            else if (type == "IEND")
            {
                iend = true;
            }
            pos += len + 4;
        }
        if (width <= 0 || height <= 0 || !iend)
        {
            throw new InvalidDataException("PNG 缺少 IHDR/IEND 或尺寸非法");
        }

        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            4 => 2,
            _ => 4,
        };
        var rowBytes = width * channels;
        idat.Position = 0;
        using var zr = new ZLibStream(idat, CompressionMode.Decompress);
        var raw = new byte[width * height * channels + height];
        var off = 0;
        while (off < raw.Length)
        {
            var n = zr.Read(raw, off, raw.Length - off);
            if (n <= 0)
            {
                break;
            }
            off += n;
        }
        if (off != raw.Length)
        {
            throw new InvalidDataException($"解码后数据长度不符（{off}/{raw.Length}）");
        }

        pixels = new byte[width * height * 4];
        // PNG 规范 15.6.7.1：参考样本按「像素」定位，不是按「字节」：
        //   a（left）  = 上一像素的同通道重建样本，行首（x < channels）为 0；
        //   b（up）    = 上一行同字节位置的重建样本，首行为 0；
        //   c（up-left）= 上一行的 a（即上一像素同通道、上一行），x < channels 或首行为 0。
        // 2026-11-26 r74 实机 PNG 取证发现旧实现按「前一字节」取 left/up-left，
        // 与 Skia/PIL 编码的规范 PNG 不符（文字边缘像素错乱、行带测量失真），
        // 以 PIL 编码 PNG + 逐像素对照修正。
        var curRow = new byte[rowBytes];
        var prevRow = new byte[rowBytes];
        for (var y = 0; y < height; y++)
        {
            var ftype = raw[y * (rowBytes + 1)];
            if (ftype > 4)
            {
                throw new InvalidDataException($"不支持的滤波器 {ftype}");
            }
            var rowStart = y * (rowBytes + 1) + 1;
            for (var x = 0; x < rowBytes; x++)
            {
                var v = (sbyte)raw[rowStart + x];
                var a = x >= channels ? curRow[x - channels] : (byte)0;        // left（同通道、上一像素）
                var b = prevRow[x];                                            // up（同字节位置、上一行）
                var u = x >= channels ? prevRow[x - channels] : (byte)0;       // up-left（b 的左参考）
                int p;
                switch (ftype)
                {
                    case 0: p = v; break;
                    case 1: p = v + (sbyte)a; break;
                    case 2: p = v + (sbyte)b; break;
                    case 3: p = v + (a + b) / 2; break; // PNG 规范：floor((left+up)/2)，按无符号值计算
                    default: p = v + Paeth(a, b, u); break;
                }
                p &= 0xFF;
                curRow[x] = (byte)p;
                WriteChannel(pixels, y * width * 4 + (x / channels) * 4,
                    colorType, x % channels, p);
            }
            var tmp = curRow; curRow = prevRow; prevRow = tmp;
        }
        if (colorType != 6 && colorType != 4)
        {
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 0xFF;
            }
        }
    }

    private static void WriteChannel(byte[] pixels, int idx, int colorType, int c, int p)
    {
        if (colorType == 6)
        {
            // RGBA → BGRA
            if (c == 0) pixels[idx + 2] = (byte)p;
            else if (c == 1) pixels[idx + 1] = (byte)p;
            else if (c == 2) pixels[idx] = (byte)p;
            else pixels[idx + 3] = (byte)p;
        }
        else if (colorType == 2)
        {
            if (c == 0) pixels[idx + 2] = (byte)p;
            else if (c == 1) pixels[idx + 1] = (byte)p;
            else pixels[idx] = (byte)p;
        }
        else if (colorType == 4)
        {
            if (c == 0)
            {
                pixels[idx] = pixels[idx + 1] = pixels[idx + 2] = (byte)p;
            }
            else
            {
                pixels[idx + 3] = (byte)p;
            }
        }
        else
        {
            pixels[idx] = pixels[idx + 1] = pixels[idx + 2] = (byte)p;
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

    private static int ReadBE32(byte[] data, int offset)
    {
        return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }
}
