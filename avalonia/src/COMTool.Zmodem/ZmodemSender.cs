using System.Text;

namespace COMTool.Zmodem;

/// <summary>
/// ZMODEM sender ported from the Python implementation. It uses the same
/// stop-and-wait 1 KiB windows as lrzsz so USB-serial bridges do not overflow
/// their input queues.
/// </summary>
public sealed class ZmodemSender
{
    private readonly ZmodemReader _reader;

    public ZmodemSender(
        ZmodemIo io,
        IEnumerable<string> files,
        ZHeader? initialZrinit = null,
        Action<string, long>? onFileStart = null,
        Action<string, long, long>? onProgress = null,
        Action<string>? onFileComplete = null,
        TimeSpan? headerTimeout = null,
        TimeSpan? dataTimeout = null,
        int chunkSize = 1024,
        int maxRetries = 5,
        int windowSize = 1024,
        TimeSpan? ackTimeout = null)
    {
        Io = io;
        Files = files.ToList();
        InitialZrinit = initialZrinit;
        OnFileStart = onFileStart;
        OnProgress = onProgress;
        OnFileComplete = onFileComplete;
        HeaderTimeout = headerTimeout ?? TimeSpan.FromSeconds(30);
        DataTimeout = dataTimeout ?? TimeSpan.FromSeconds(30);
        ChunkSize = Math.Max(1, chunkSize);
        MaxRetries = maxRetries;
        WindowSize = Math.Max(ChunkSize, windowSize);
        AckTimeout = ackTimeout ?? TimeSpan.FromSeconds(10);
        _reader = new ZmodemReader(io, HeaderTimeout);
    }

    public ZmodemIo Io { get; }

    public IReadOnlyList<string> Files { get; }

    public ZHeader? InitialZrinit { get; }

    public Action<string, long>? OnFileStart { get; }

    public Action<string, long, long>? OnProgress { get; }

    public Action<string>? OnFileComplete { get; }

    public TimeSpan HeaderTimeout { get; }

    public TimeSpan DataTimeout { get; }

    public int ChunkSize { get; }

    public int MaxRetries { get; }

    public int WindowSize { get; }

    public TimeSpan AckTimeout { get; }

    public byte[] Trailing { get; private set; } = Array.Empty<byte>();

    public void Run()
    {
        var zrinit = WaitForZrinit();
        var crcLength = (zrinit.Data[3] & ZmodemCodec.ZrinitCanFc32) != 0 ? 32 : 16;
        foreach (var path in Files)
        {
            SendFile(path, crcLength);
        }

        Finish();
        Trailing = _reader.Drain();
    }

    private ZHeader WaitForZrinit()
    {
        if (InitialZrinit is not null)
        {
            return InitialZrinit;
        }

        SendHeader(ZmodemCodec.FrameZRQInit);
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            ZHeader header;
            try
            {
                header = _reader.ReadHeader();
            }
            catch (ZmodemTimeoutException)
            {
                SendHeader(ZmodemCodec.FrameZRQInit);
                continue;
            }

            if (header.FrameType == ZmodemCodec.FrameZRInit)
            {
                return header;
            }

            if (header.FrameType is ZmodemCodec.FrameZAbort or ZmodemCodec.FrameZCan)
            {
                throw new ZmodemAbortException("receiver aborted the transfer");
            }
        }

        throw new ZmodemTimeoutException("receiver did not send ZRINIT");
    }

    private void SendFile(string path, int crcLength)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException(path);
        }

        var name = info.Name;
        var mtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
        var mode = ReadMode(path);
        var metadata = Encoding.UTF8.GetBytes(name)
            .Concat(new byte[] { 0 })
            .Concat(Encoding.ASCII.GetBytes($"{info.Length} {mtime} {mode} 0 0 0"))
            .ToArray();

        SendZfile(metadata, crcLength);
        var requestedOffset = WaitForZrpos(metadata, crcLength);
        if (requestedOffset is null)
        {
            return;
        }

        var offset = requestedOffset.Value;
        if (offset < 0 || offset > info.Length)
        {
            throw new ZmodemAbortException("receiver requested an invalid offset");
        }

        OnFileStart?.Invoke(name, info.Length);
        var retries = 0;
        long lastOffset = offset;
        while (true)
        {
            var resume = SendData(path, offset, crcLength);
            if (resume is >= 0 && resume < info.Length)
            {
                if (resume > lastOffset)
                {
                    retries = 0;
                    lastOffset = resume.Value;
                }
                else
                {
                    retries++;
                    if (retries > MaxRetries)
                    {
                        throw new ZmodemAbortException(
                            "too many receiver retransmit requests");
                    }
                }

                offset = resume.Value;
                _reader.ClearBuffer();
                Io.Drain();
                continue;
            }

            string result;
            try
            {
                result = WaitAfterEof(info.Length);
            }
            catch (ZmodemTimeoutException)
            {
                retries++;
                if (retries > MaxRetries)
                {
                    throw;
                }

                continue;
            }

            if (result == "ok")
            {
                break;
            }

            if (result == "skip")
            {
                return;
            }

            if (long.TryParse(result, out var resumeOffset))
            {
                if (resumeOffset >= info.Length)
                {
                    break;
                }

                offset = resumeOffset;
                retries++;
                if (retries > MaxRetries)
                {
                    throw new ZmodemAbortException(
                        "too many receiver retransmit requests");
                }

                continue;
            }

            throw new ZmodemAbortException("unexpected receiver response");
        }

        OnFileComplete?.Invoke(name);
    }

    private long? WaitForZrpos(byte[] metadata, int crcLength)
    {
        var retries = 0;
        while (true)
        {
            ZHeader header;
            try
            {
                header = _reader.ReadHeader();
            }
            catch (ZmodemTimeoutException)
            {
                retries++;
                if (retries > MaxRetries)
                {
                    throw;
                }

                SendZfile(metadata, crcLength);
                continue;
            }

            switch (header.FrameType)
            {
                case ZmodemCodec.FrameZRPos:
                    return ZmodemCodec.UnpackOffset(header.Data);
                case ZmodemCodec.FrameZSkip:
                    return null;
                case ZmodemCodec.FrameZAbort:
                case ZmodemCodec.FrameZCan:
                    throw new ZmodemAbortException("receiver aborted the transfer");
                case ZmodemCodec.FrameZNak:
                    retries++;
                    if (retries > MaxRetries)
                    {
                        throw new ZmodemAbortException("receiver rejected the file header");
                    }

                    SendZfile(metadata, crcLength);
                    break;
            }
        }
    }

    private long? SendData(string path, long offset, int crcLength)
    {
        var size = new FileInfo(path).Length;
        using var handle = File.OpenRead(path);
        handle.Seek(offset, SeekOrigin.Begin);
        var sent = offset;
        if (size == 0)
        {
            SendBinaryHeader(ZmodemCodec.FrameZData, ZmodemCodec.PackOffset(0), crcLength);
            SendSubpacket(Array.Empty<byte>(), ZmodemCodec.SubpacketZcrce, crcLength);
            return null;
        }

        while (sent < size)
        {
            SendBinaryHeader(ZmodemCodec.FrameZData, ZmodemCodec.PackOffset(sent), crcLength);
            var windowEnd = Math.Min(sent + WindowSize, size);
            while (sent < windowEnd)
            {
                var remaining = windowEnd - sent;
                var chunk = new byte[Math.Min(ChunkSize, (int)Math.Min(int.MaxValue, remaining))];
                var read = handle.Read(chunk, 0, chunk.Length);
                if (read <= 0)
                {
                    break;
                }

                if (read != chunk.Length)
                {
                    chunk = chunk[..read];
                }

                sent += read;
                if (sent >= size)
                {
                    SendSubpacket(chunk, ZmodemCodec.SubpacketZcrce, crcLength);
                    ReportProgress(path, sent, size);
                    return null;
                }

                SendSubpacket(
                    chunk,
                    sent >= windowEnd
                        ? ZmodemCodec.SubpacketZcrcw
                        : ZmodemCodec.SubpacketZcrcg,
                    crcLength);
                ReportProgress(path, sent, size);
            }

            var resume = WaitForAck(sent);
            if (resume is not null)
            {
                return resume;
            }
        }

        return null;
    }

    private long? WaitForAck(long sent)
    {
        while (true)
        {
            ZHeader header;
            try
            {
                header = _reader.ReadHeader(AckTimeout);
            }
            catch (ZmodemTimeoutException)
            {
                return sent;
            }

            var frameType = header.FrameType & 0x7F;
            if (frameType == ZmodemCodec.FrameZAck)
            {
                var acked = ZmodemCodec.UnpackOffset(header.Data);
                return acked < sent ? acked : null;
            }

            if (frameType == ZmodemCodec.FrameZRPos)
            {
                return ZmodemCodec.UnpackOffset(header.Data);
            }

            if (frameType == ZmodemCodec.FrameZNak)
            {
                return sent;
            }

            if (frameType is ZmodemCodec.FrameZAbort or ZmodemCodec.FrameZCan)
            {
                throw new ZmodemAbortException("receiver aborted the transfer");
            }
        }
    }

    private string WaitAfterEof(long size)
    {
        SendHeader(ZmodemCodec.FrameZEof, ZmodemCodec.PackOffset(size));
        while (true)
        {
            var header = _reader.ReadHeader();
            switch (header.FrameType)
            {
                case ZmodemCodec.FrameZRInit:
                    return "ok";
                case ZmodemCodec.FrameZSkip:
                    return "skip";
                case ZmodemCodec.FrameZRPos:
                    return ZmodemCodec.UnpackOffset(header.Data).ToString();
                case ZmodemCodec.FrameZAbort:
                case ZmodemCodec.FrameZCan:
                    throw new ZmodemAbortException("receiver aborted the transfer");
                case ZmodemCodec.FrameZNak:
                    return "0";
            }
        }
    }

    private void Finish()
    {
        SendHeader(ZmodemCodec.FrameZFin);
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            ZHeader header;
            try
            {
                header = _reader.ReadHeader();
            }
            catch (ZmodemTimeoutException)
            {
                SendHeader(ZmodemCodec.FrameZFin);
                continue;
            }

            if (header.FrameType == ZmodemCodec.FrameZFin)
            {
                Io.Write(Encoding.ASCII.GetBytes("OO"));
                return;
            }

            if (header.FrameType is ZmodemCodec.FrameZAbort or ZmodemCodec.FrameZCan)
            {
                throw new ZmodemAbortException("receiver aborted the transfer");
            }
        }

        throw new ZmodemTimeoutException("receiver did not acknowledge ZFIN");
    }

    private void SendZfile(byte[] metadata, int crcLength)
    {
        SendBinaryHeader(ZmodemCodec.FrameZFile, new byte[4], crcLength);
        SendSubpacket(metadata, ZmodemCodec.SubpacketZcrcw, crcLength);
    }

    private void SendHeader(int frameType, byte[]? data = null) =>
        Io.Write(ZmodemCodec.EncodeHexHeader(frameType, data));

    private void SendBinaryHeader(int frameType, byte[] data, int crcLength) =>
        Io.Write(ZmodemCodec.EncodeBinaryHeader(frameType, data, crcLength));

    private void SendSubpacket(byte[] payload, byte frameEnd, int crcLength)
    {
        Io.Write(ZmodemCodec.EncodeSubpacket(payload, frameEnd, crcLength));
        if (frameEnd == ZmodemCodec.SubpacketZcrcw)
        {
            Io.Write(new byte[] { ZmodemCodec.Xon });
        }
    }

    private void ReportProgress(string path, long sent, long size) =>
        OnProgress?.Invoke(Path.GetFileName(path), sent, size);

    private static int ReadMode(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return 0x1A4; // 0644
        }

        try
        {
            return (int)File.GetUnixFileMode(path) & 0xFFF;
        }
        catch
        {
            return 0x1A4;
        }
    }
}
