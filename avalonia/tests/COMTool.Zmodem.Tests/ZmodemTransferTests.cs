using COMTool.Zmodem;

namespace COMTool.Zmodem.Tests;

public sealed class ZmodemTransferTests
{
    [Fact]
    public void SubpacketRoundTrip()
    {
        var payload = Enumerable.Range(0, 4096)
            .Select(value => (byte)(value * 37))
            .ToArray();
        var encoded = ZmodemCodec.EncodeSubpacket(
            payload,
            ZmodemCodec.SubpacketZcrcw,
            crcLength: 32);

        var parsed = ZmodemCodec.ParseSubpacket(encoded, crcLength: 32);

        Assert.NotNull(parsed);
        Assert.Equal(encoded.Length, parsed.Value.Consumed);
        Assert.Equal(ZmodemCodec.SubpacketZcrcw, parsed.Value.Packet.FrameEnd);
        Assert.Equal(payload, parsed.Value.Packet.Payload);
    }

    [Fact]
    public void OffsetRoundTrip()
    {
        var bytes = ZmodemCodec.PackOffset(0x12345678);
        Assert.Equal(new byte[] { 0x78, 0x56, 0x34, 0x12 }, bytes);
        Assert.Equal(0x12345678, ZmodemCodec.UnpackOffset(bytes));
    }

    [Fact]
    public async Task SenderAndReceiverTransferFile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "comtool-zmodem-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.bin");
            var content = new byte[32 * 1024 + 17];
            new Random(1234).NextBytes(content);
            await File.WriteAllBytesAsync(source, content);

            ZmodemIo? senderIo = null;
            ZmodemIo? receiverIo = null;
            senderIo = new ZmodemIo(data => receiverIo!.Feed(data));
            receiverIo = new ZmodemIo(data => senderIo!.Feed(data));

            var output = Path.Combine(directory, "output");
            Directory.CreateDirectory(output);
            var receiver = new ZmodemReceiver(
                receiverIo,
                output,
                dataTimeout: TimeSpan.FromSeconds(10));
            var sender = new ZmodemSender(
                senderIo,
                new[] { source },
                headerTimeout: TimeSpan.FromSeconds(10),
                dataTimeout: TimeSpan.FromSeconds(10),
                ackTimeout: TimeSpan.FromSeconds(10));

            var senderTask = Task.Run(sender.Run);
            var receiverTask = Task.Run(receiver.Run);
            var all = Task.WhenAll(senderTask, receiverTask);
            var completed = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.Same(all, completed);
            await all;

            var received = Assert.Single(receiver.Files);
            Assert.Equal("source.bin", received.Name);
            Assert.Equal(content, await File.ReadAllBytesAsync(received.Path));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task StreamDetectsAndReceivesZmodemSession()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "comtool-zmodem-stream-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "stream.bin");
            var content = new byte[12 * 1024 + 3];
            new Random(4321).NextBytes(content);
            await File.WriteAllBytesAsync(source, content);

            using var input = new QueueReadStream();
            ZmodemIo? senderIo = null;
            var receiverToSender = new ZmodemIo(data => senderIo!.Feed(data));
            senderIo = new ZmodemIo(input.Feed);
            var sender = new ZmodemSender(
                senderIo,
                new[] { source },
                headerTimeout: TimeSpan.FromSeconds(10),
                dataTimeout: TimeSpan.FromSeconds(10),
                ackTimeout: TimeSpan.FromSeconds(10));
            using var stream = new ZmodemStream(input, data => receiverToSender.Write(data));
            var completion = new TaskCompletionSource<ZmodemTransferResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            stream.TransferCompleted += result => completion.TrySetResult(result);

            var senderTask = Task.Run(sender.Run);
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await senderTask.WaitAsync(TimeSpan.FromSeconds(20));

            Assert.False(result.IsSend);
            var received = Assert.Single(result.Files);
            Assert.Equal("stream.bin", received.Name);
            Assert.Equal(content, await File.ReadAllBytesAsync(received.Path));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task CancelPendingSendRaisesCancelled()
    {
        using var input = new QueueReadStream();
        using var stream = new ZmodemStream(input, _ => { });
        var requested = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        stream.SendRequested += _ => requested.TrySetResult();
        stream.TransferCancelled += () => cancelled.TrySetResult();
        stream.TransferFailed += message => failed.TrySetResult(message);

        input.Feed(ZmodemCodec.EncodeHexHeader(ZmodemCodec.FrameZRInit, new byte[4]));
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stream.CancelSend();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stream.IsTransferActive);
        Assert.False(failed.Task.IsCompleted);
    }

    [Fact]
    public async Task CancelActiveReceiveRaisesCancelled()
    {
        using var input = new QueueReadStream();
        using var stream = new ZmodemStream(input, _ => { });
        var cancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        stream.TransferCancelled += () => cancelled.TrySetResult();

        input.Feed(ZmodemCodec.EncodeHexHeader(ZmodemCodec.FrameZRQInit, new byte[4]));
        var active = false;
        for (var attempt = 0; attempt < 50 && !active; attempt++)
        {
            await Task.Delay(20);
            active = stream.IsTransferActive;
        }

        Assert.True(active);
        stream.CancelTransfer();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stream.IsTransferActive);
    }

    [Fact]
    public async Task TransportEofCompletesInputStream()
    {
        using var input = new QueueReadStream();
        using var stream = new ZmodemStream(input, _ => { });
        input.Complete();

        for (var attempt = 0; attempt < 100 && !stream.IsInputCompleted; attempt++)
        {
            await Task.Delay(20);
        }

        Assert.True(stream.IsInputCompleted);
    }

    [Fact]
    public async Task StreamDetectsAndSendsZmodemSession()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "comtool-zmodem-send-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "send.bin");
            var content = new byte[9 * 1024 + 11];
            new Random(9876).NextBytes(content);
            await File.WriteAllBytesAsync(source, content);

            using var input = new QueueReadStream();
            var output = Path.Combine(directory, "received");
            Directory.CreateDirectory(output);
            var receiverIo = new ZmodemIo(data => input.Feed(data));
            var receiver = new ZmodemReceiver(
                receiverIo,
                output,
                dataTimeout: TimeSpan.FromSeconds(10));
            using var stream = new ZmodemStream(input, data => receiverIo.Feed(data));
            var completion = new TaskCompletionSource<ZmodemTransferResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            stream.TransferCompleted += result => completion.TrySetResult(result);
            stream.SendRequested += _ => stream.StartSend(new[] { source });

            var receiverTask = Task.Run(receiver.Run);
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await receiverTask.WaitAsync(TimeSpan.FromSeconds(20));

            Assert.True(result.IsSend);
            var received = Assert.Single(receiver.Files);
            Assert.Equal("send.bin", received.Name);
            Assert.Equal(content, await File.ReadAllBytesAsync(received.Path));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class QueueReadStream : Stream
    {
        private readonly System.Collections.Concurrent.BlockingCollection<byte[]> _chunks = new();
        private byte[] _current = Array.Empty<byte>();
        private int _offset;

        public void Feed(byte[] data) => _chunks.Add(data);

        public void Complete() => _chunks.CompleteAdding();

        public override int Read(byte[] buffer, int offset, int count)
        {
            while (_offset >= _current.Length)
            {
                if (_chunks.IsCompleted)
                {
                    return 0;
                }

                if (!_chunks.TryTake(out var next, TimeSpan.FromSeconds(20)))
                {
                    return 0;
                }

                _current = next;
                _offset = 0;
            }

            var taken = Math.Min(count, _current.Length - _offset);
            Array.Copy(_current, _offset, buffer, offset, taken);
            _offset += taken;
            return taken;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _chunks.CompleteAdding();
            }

            base.Dispose(disposing);
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

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
