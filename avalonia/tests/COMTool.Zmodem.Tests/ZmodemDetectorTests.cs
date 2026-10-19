namespace COMTool.Zmodem.Tests;

public sealed class ZmodemDetectorTests
{
    [Fact]
    public void PlainTextPassesThrough()
    {
        var detector = new ZmodemDetector();
        var result = detector.Feed("hello world"u8);

        Assert.Equal("hello world"u8.ToArray(), result.TerminalData);
        Assert.Null(result.Detection);
    }

    [Fact]
    public void DetectsZRQInitAsReceive()
    {
        var detector = new ZmodemDetector();
        var header = ZmodemCodec.EncodeHexHeader(ZmodemCodec.FrameZRQInit);

        var result = detector.Feed(header);

        Assert.Empty(result.TerminalData);
        Assert.NotNull(result.Detection);
        Assert.Equal("receive", result.Detection!.Direction);
        Assert.Equal(ZmodemCodec.FrameZRQInit, result.Detection.Header.FrameType);
        Assert.Empty(result.Detection.Remainder);
    }

    [Fact]
    public void DetectsZRInitAsSendWithTextAroundHeader()
    {
        var detector = new ZmodemDetector();
        var header = ZmodemCodec.EncodeHexHeader(
            ZmodemCodec.FrameZRInit,
            new byte[] { 0, 0, 0, 0x27 });

        var result = detector.Feed("before"u8.ToArray().Concat(header).Concat("after"u8.ToArray()).ToArray());

        Assert.Equal("before"u8.ToArray(), result.TerminalData);
        Assert.NotNull(result.Detection);
        Assert.Equal("send", result.Detection!.Direction);
        Assert.Equal(new byte[] { 0, 0, 0, 0x27 }, result.Detection.Header.Data);
        Assert.Equal("after"u8.ToArray(), result.Detection.Remainder);
    }

    [Fact]
    public void DetectsHeaderSplitAcrossChunks()
    {
        var detector = new ZmodemDetector();
        var header = ZmodemCodec.EncodeHexHeader(ZmodemCodec.FrameZRQInit);

        var first = detector.Feed("text"u8.ToArray().Concat(header.Take(9)).ToArray());
        Assert.Equal("text"u8.ToArray(), first.TerminalData);
        Assert.Null(first.Detection);

        var second = detector.Feed(header.Skip(9).ToArray());
        Assert.NotNull(second.Detection);
        Assert.Equal("receive", second.Detection!.Direction);
    }

    [Fact]
    public void InvalidCrcFallsBackToTerminal()
    {
        var detector = new ZmodemDetector();
        var header = ZmodemCodec.EncodeHexHeader(ZmodemCodec.FrameZRQInit);
        header[8] = header[8] == (byte)'F' ? (byte)'E' : (byte)'F';

        var result = detector.Feed(header);

        Assert.Null(result.Detection);
        Assert.Equal(header, result.TerminalData);
    }

    [Fact]
    public void IncompletePrefixIsReleasedWhenNextChunkIsInvalid()
    {
        var detector = new ZmodemDetector();
        var first = detector.Feed("hello**\x18"u8);
        Assert.Equal("hello"u8.ToArray(), first.TerminalData);
        Assert.Null(first.Detection);

        var second = detector.Feed("Xnot a header"u8);
        Assert.Null(second.Detection);
        Assert.Equal("**\x18Xnot a header"u8.ToArray(), second.TerminalData);
    }

    [Fact]
    public void MultipleStarsBeforeHeaderPassThrough()
    {
        var detector = new ZmodemDetector();
        var header = ZmodemCodec.EncodeHexHeader(ZmodemCodec.FrameZRQInit);

        var result = detector.Feed("a*b*c"u8.ToArray().Concat(header).ToArray());

        Assert.Equal("a*b*c"u8.ToArray(), result.TerminalData);
        Assert.NotNull(result.Detection);
        Assert.Equal("receive", result.Detection!.Direction);
    }

    [Fact]
    public void BinaryHeaderIsDetected()
    {
        var detector = new ZmodemDetector();
        var header = ZmodemCodec.EncodeBinaryHeader(ZmodemCodec.FrameZRQInit);

        var result = detector.Feed(header);

        Assert.NotNull(result.Detection);
        Assert.Equal("receive", result.Detection!.Direction);
        Assert.Equal(16, result.Detection.Header.CrcLength);
    }

    [Fact]
    public void DetectsRealLrzszSzZRQInit()
    {
        var bytes = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "lsz-zrqinit.bin"));
        var detector = new ZmodemDetector();

        var result = detector.Feed(bytes);

        Assert.Equal("rz\r"u8.ToArray(), result.TerminalData);
        Assert.NotNull(result.Detection);
        Assert.Equal("receive", result.Detection!.Direction);
        Assert.Empty(result.Detection.Remainder);
    }
}
