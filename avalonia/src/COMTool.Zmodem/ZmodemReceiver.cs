namespace COMTool.Zmodem;

public sealed record ZmodemReceivedFile(
    string Name,
    string Path,
    long Size,
    long? ExpectedSize);

/// <summary>
/// ZMODEM receiver ported from the Python implementation. Files are written
/// to a unique temporary path in the configured output directory and are
/// removed if the transfer aborts.
/// </summary>
public sealed class ZmodemReceiver
{
    private readonly ZmodemReader _reader;

    public ZmodemReceiver(
        ZmodemIo io,
        string outputDirectory,
        Action<ZmodemFileMeta, string>? onFileStart = null,
        Action<string, long, long?>? onProgress = null,
        Action<ZmodemFileMeta, string>? onFileComplete = null,
        TimeSpan? headerTimeout = null,
        TimeSpan? dataTimeout = null,
        int maxRetries = 5)
    {
        Io = io;
        OutputDirectory = outputDirectory;
        OnFileStart = onFileStart;
        OnProgress = onProgress;
        OnFileComplete = onFileComplete;
        DataTimeout = dataTimeout ?? TimeSpan.FromSeconds(30);
        MaxRetries = maxRetries;
        _reader = new ZmodemReader(io, DataTimeout);
    }

    public ZmodemIo Io { get; }

    public string OutputDirectory { get; }

    public Action<ZmodemFileMeta, string>? OnFileStart { get; }

    public Action<string, long, long?>? OnProgress { get; }

    public Action<ZmodemFileMeta, string>? OnFileComplete { get; }

    public TimeSpan DataTimeout { get; }

    public int MaxRetries { get; }

    public List<ZmodemReceivedFile> Files { get; } = new();

    public byte[] Trailing { get; private set; } = Array.Empty<byte>();

    public void Run()
    {
        SendZrinit();
        while (true)
        {
            ZHeader header;
            try
            {
                header = _reader.ReadHeader();
            }
            catch (ZmodemTimeoutException)
            {
                throw;
            }
            catch (ZmodemProtocolException)
            {
                SendZrinit();
                continue;
            }

            switch (header.FrameType)
            {
                case ZmodemCodec.FrameZRQInit:
                    SendZrinit();
                    break;
                case ZmodemCodec.FrameZSInit:
                    _reader.ReadSubpacket(header.CrcLength);
                    SendZack(1);
                    break;
                case ZmodemCodec.FrameZFile:
                    if (ReceiveFile(header) == "finish")
                    {
                        SendHeader(ZmodemCodec.FrameZFin);
                        ReadOo();
                        Trailing = _reader.Drain();
                        return;
                    }

                    break;
                case ZmodemCodec.FrameZFin:
                    SendHeader(ZmodemCodec.FrameZFin);
                    ReadOo();
                    Trailing = _reader.Drain();
                    return;
                case ZmodemCodec.FrameZSkip:
                    break;
                case ZmodemCodec.FrameZAbort:
                case ZmodemCodec.FrameZCan:
                    throw new ZmodemAbortException("sender aborted the transfer");
                case ZmodemCodec.FrameZNak:
                    SendZrinit();
                    break;
            }
        }
    }

    private string ReceiveFile(ZHeader header)
    {
        ZSubpacket metadataPacket;
        try
        {
            metadataPacket = _reader.ReadSubpacket(header.CrcLength);
        }
        catch (ZmodemProtocolException)
        {
            SendHeader(ZmodemCodec.FrameZNak);
            _reader.ClearBuffer();
            return "error";
        }

        var meta = ZmodemFileName.ParseMetadata(metadataPacket.Payload);
        var path = ZmodemFileName.UniquePath(OutputDirectory, meta.Name);
        using var handle = File.Create(path);
        OnFileStart?.Invoke(meta, path);

        long offset = 0;
        var retries = 0;
        var finishAfterFile = false;
        SendZrpos(0);
        try
        {
            while (true)
            {
                ZHeader next;
                try
                {
                    next = _reader.ReadHeader();
                }
                catch (ZmodemProtocolException)
                {
                    retries++;
                    if (retries > MaxRetries)
                    {
                        throw;
                    }

                    _reader.ClearBuffer();
                    SendZrpos(offset);
                    continue;
                }

                switch (next.FrameType)
                {
                    case ZmodemCodec.FrameZData:
                    {
                        var dataOffset = ZmodemCodec.UnpackOffset(next.Data);
                        if (dataOffset != offset)
                        {
                            retries++;
                            if (retries > MaxRetries)
                            {
                                throw new ZmodemAbortException(
                                    "sender keeps sending a bad offset");
                            }

                            SendZrpos(offset);
                            continue;
                        }

                        var finished = ReceiveDataFrames(
                            handle,
                            header.CrcLength,
                            offset,
                            meta);
                        if (finished is null)
                        {
                            retries++;
                            if (retries > MaxRetries)
                            {
                                throw new ZmodemAbortException(
                                    "too many data checksum failures");
                            }

                            continue;
                        }

                        offset = finished.Value;
                        retries = 0;
                        continue;
                    }
                    case ZmodemCodec.FrameZEof:
                    {
                        var eofOffset = ZmodemCodec.UnpackOffset(next.Data);
                        if (eofOffset != offset)
                        {
                            retries++;
                            if (retries > MaxRetries)
                            {
                                throw new ZmodemAbortException(
                                    "ZEOF offset does not match");
                            }

                            SendZrpos(offset);
                            continue;
                        }

                        finishAfterFile = false;
                        goto done;
                    }
                    case ZmodemCodec.FrameZFin:
                        finishAfterFile = true;
                        goto done;
                    case ZmodemCodec.FrameZSkip:
                        handle.Close();
                        TryDelete(path);
                        return "skip";
                    case ZmodemCodec.FrameZAbort:
                    case ZmodemCodec.FrameZCan:
                        throw new ZmodemAbortException("sender aborted the transfer");
                    case ZmodemCodec.FrameZNak:
                        retries++;
                        if (retries > MaxRetries)
                        {
                            throw new ZmodemAbortException("too many ZNAK frames");
                        }

                        SendZrpos(offset);
                        continue;
                }
            }

        done:
            handle.Close();
        }
        catch
        {
            handle.Close();
            TryDelete(path);
            throw;
        }

        Files.Add(new ZmodemReceivedFile(meta.Name, path, offset, meta.Size));
        OnFileComplete?.Invoke(meta, path);
        if (finishAfterFile)
        {
            return "finish";
        }

        SendZrinit();
        return "ok";
    }

    private long? ReceiveDataFrames(
        FileStream handle,
        int crcLength,
        long offset,
        ZmodemFileMeta meta)
    {
        while (true)
        {
            ZSubpacket subpacket;
            try
            {
                subpacket = _reader.ReadSubpacket(crcLength);
            }
            catch (ZmodemProtocolException)
            {
                _reader.ClearBuffer();
                SendZrpos(offset);
                return null;
            }

            handle.Write(subpacket.Payload);
            offset += subpacket.Payload.Length;
            OnProgress?.Invoke(meta.Name, offset, meta.Size);

            if (subpacket.AckExpected && !subpacket.FrameEndFrame)
            {
                SendZack(offset);
            }

            if (subpacket.FrameEndFrame)
            {
                if (subpacket.FrameEnd == ZmodemCodec.SubpacketZcrcw)
                {
                    SendZack(offset);
                }

                return offset;
            }
        }
    }

    private void SendZrinit()
    {
        var flags = (byte)(
            ZmodemCodec.ZrinitCanFdx
            | ZmodemCodec.ZrinitCanOvIo
            | ZmodemCodec.ZrinitCanFc32);
        SendHeader(ZmodemCodec.FrameZRInit, new byte[] { 0, 0, 0, flags });
    }

    private void SendZrpos(long offset) =>
        SendHeader(ZmodemCodec.FrameZRPos, ZmodemCodec.PackOffset(offset));

    private void SendZack(long offset) =>
        SendHeader(ZmodemCodec.FrameZAck, ZmodemCodec.PackOffset(offset));

    private void SendHeader(int frameType, byte[]? data = null) =>
        Io.Write(ZmodemCodec.EncodeHexHeader(frameType, data));

    private void ReadOo()
    {
        try
        {
            var data = _reader.ReadRaw(2);
            if (data.Length == 2 && data[0] == 'O' && data[1] == 'O')
            {
                return;
            }

            _reader.Prepend(data);
        }
        catch (ZmodemTimeoutException)
        {
        }
        catch (EndOfStreamException)
        {
        }
    }

    private bool IsAbort() =>
        Io.IsCancelled;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A partial file is best-effort cleanup only.
        }
    }
}
