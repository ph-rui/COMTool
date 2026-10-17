from COMTool.zmodem.codec import (
    FRAME_ZRQINIT,
    FRAME_ZRINIT,
    encode_hex_header,
)
from COMTool.zmodem.detector import ZmodemDetector


def test_plain_text_passes_through():
    detector = ZmodemDetector()
    result = detector.feed(b"hello world")
    assert result.terminal_data == b"hello world"
    assert result.detection is None


def test_detects_zrqinit_as_receive():
    detector = ZmodemDetector()
    header = encode_hex_header(FRAME_ZRQINIT, b"\0\0\0\0")
    result = detector.feed(header)
    assert result.terminal_data == b""
    assert result.detection is not None
    assert result.detection.direction == "receive"
    assert result.detection.header.frame_type == FRAME_ZRQINIT
    assert result.detection.remainder == b""


def test_detects_zrinit_as_send_with_leading_and_trailing_text():
    detector = ZmodemDetector()
    header = encode_hex_header(FRAME_ZRINIT, b"\0\0\0\x27")
    result = detector.feed(b"before" + header + b"after")
    assert result.terminal_data == b"before"
    assert result.detection is not None
    assert result.detection.direction == "send"
    assert result.detection.header.data == b"\0\0\0\x27"
    assert result.detection.remainder == b"after"


def test_detects_header_split_across_chunks():
    detector = ZmodemDetector()
    header = encode_hex_header(FRAME_ZRQINIT, b"\0\0\0\0")
    first = detector.feed(b"text" + header[:9])
    assert first.terminal_data == b"text"
    assert first.detection is None
    second = detector.feed(header[9:])
    assert second.detection is not None
    assert second.detection.direction == "receive"


def test_invalid_crc_falls_back_to_terminal():
    detector = ZmodemDetector()
    header = bytearray(encode_hex_header(FRAME_ZRQINIT, b"\0\0\0\0"))
    header[8] = ord("F") if header[8] != ord("F") else ord("E")
    result = detector.feed(bytes(header))
    assert result.detection is None
    assert result.terminal_data == bytes(header)


def test_incomplete_prefix_is_released_after_next_chunk_is_invalid():
    detector = ZmodemDetector()
    first = detector.feed(b"hello**\x18")
    assert first.terminal_data == b"hello"
    assert first.detection is None
    second = detector.feed(b"Xnot a header")
    assert second.detection is None
    assert second.terminal_data == b"**\x18Xnot a header"


def test_multiple_stars_before_header():
    detector = ZmodemDetector()
    header = encode_hex_header(FRAME_ZRQINIT, b"\0\0\0\0")
    result = detector.feed(b"a*b*c" + header)
    assert result.terminal_data == b"a*b*c"
    assert result.detection is not None
    assert result.detection.direction == "receive"
