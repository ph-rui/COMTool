import pytest

from COMTool.zmodem.codec import (
    FRAME_ZFIN,
    FRAME_ZRQINIT,
    FRAME_ZRINIT,
    HEADER_PARSE_INVALID,
    HEADER_PARSE_NEED_MORE,
    HEADER_PARSE_OK,
    SUBPACKET_ZCRCE,
    SUBPACKET_ZCRCG,
    SUBPACKET_ZCRCQ,
    SUBPACKET_ZCRCW,
    ZHeader,
    encode_binary_header,
    encode_hex_header,
    encode_subpacket,
    parse_header,
    parse_header_at,
    parse_subpacket,
    zdle_decode,
    zdle_encode,
)
from COMTool.zmodem.crc import crc16


def test_frame_constants():
    assert FRAME_ZRQINIT == 0
    assert FRAME_ZRINIT == 1
    assert FRAME_ZFIN == 8
    assert SUBPACKET_ZCRCE == 0x68
    assert SUBPACKET_ZCRCG == 0x69
    assert SUBPACKET_ZCRCQ == 0x6A
    assert SUBPACKET_ZCRCW == 0x6B


def test_encode_hex_header_exact_bytes():
    assert encode_hex_header(FRAME_ZRQINIT, b"\0\0\0\0") == (
        b"**\x18B00000000000000\r\n\x11"
    )


def test_encode_hex_header_zrinit():
    assert encode_hex_header(FRAME_ZRINIT, b"\0\0\0\x27") == (
        b"**\x18B01000000" + b"27fed4\r\n\x11"
    )


def test_parse_hex_header_with_leading_garbage():
    raw = b"hello" + encode_hex_header(FRAME_ZRQINIT, b"\0\0\0\0")
    header, consumed = parse_header(raw)
    assert isinstance(header, ZHeader)
    assert header.frame_type == FRAME_ZRQINIT
    assert header.data == b"\0\0\0\0"
    assert header.crc_len == 16
    assert consumed == len(raw)


def test_parse_lrzsz_hex_header_ending():
    raw = encode_hex_header(FRAME_ZFIN, b"\0\0\0\0").replace(
        b"\r\n\x11", b"\r\x8a"
    )
    result = parse_header_at(raw)
    assert result.status == HEADER_PARSE_OK
    assert result.header.frame_type == FRAME_ZFIN


def test_parse_header_at_need_more_and_invalid():
    raw = encode_hex_header(FRAME_ZRINIT, b"\0\0\0\x27")
    result = parse_header_at(raw[:10])
    assert result.status == HEADER_PARSE_NEED_MORE
    assert result.header is None

    bad = bytearray(raw)
    bad[8] = ord("F") if bad[8] != ord("F") else ord("E")
    result = parse_header_at(bytes(bad))
    assert result.status == HEADER_PARSE_INVALID

    result = parse_header_at(raw)
    assert result.status == HEADER_PARSE_OK
    assert result.header.frame_type == FRAME_ZRINIT
    assert result.consumed == len(raw)


@pytest.mark.parametrize("crc_len", [16, 32])
def test_binary_header_round_trip(crc_len):
    raw = encode_binary_header(FRAME_ZRINIT, b"\0\0\0\x27", crc_len=crc_len)
    result = parse_header_at(raw)
    assert result.status == HEADER_PARSE_OK
    assert result.header.frame_type == FRAME_ZRINIT
    assert result.header.data == b"\0\0\0\x27"
    assert result.header.crc_len == crc_len
    assert result.consumed == len(raw)


def test_binary_header_rejects_bad_crc():
    raw = bytearray(encode_binary_header(FRAME_ZFIN, b"\0\0\0\0", crc_len=16))
    raw[-1] ^= 0x01
    result = parse_header_at(bytes(raw))
    assert result.status == HEADER_PARSE_INVALID


def test_zdle_encode_minimal_escapes_and_decode():
    raw = bytes([0x00, 0x01, 0x0D, 0x0A, 0x10, 0x11, 0x13, 0x18, 0x20, 0x7F, 0x8D, 0x90, 0x91, 0x93, 0xFF])
    encoded = zdle_encode(raw)
    assert encoded == bytes(
        [
            0x00,
            0x01,
            0x0D,
            0x0A,
            0x18, 0x50,
            0x18, 0x51,
            0x18, 0x53,
            0x18, 0x58,
            0x20,
            0x7F,
            0x8D,
            0x18, 0xD0,
            0x18, 0xD1,
            0x18, 0xD3,
            0xFF,
        ]
    )
    assert zdle_decode(encoded) == raw


def test_zdle_encode_escapes_all_controls_when_requested():
    raw = bytes([0x00, 0x01, 0x0D, 0x0A, 0x10, 0x11, 0x13, 0x18, 0x20, 0x7F, 0x8D, 0x90, 0x91, 0x93, 0xFF])
    encoded = zdle_encode(raw, escape_ctrl=True)
    assert encoded == bytes(
        [
            0x18, 0x40,
            0x18, 0x41,
            0x18, 0x4D,
            0x18, 0x4A,
            0x18, 0x50,
            0x18, 0x51,
            0x18, 0x53,
            0x18, 0x58,
            0x20,
            0x7F,
            0x18, 0xCD,
            0x18, 0xD0,
            0x18, 0xD1,
            0x18, 0xD3,
            0xFF,
        ]
    )
    assert zdle_decode(encoded) == raw


def test_zdle_decode_rubouts():
    assert zdle_decode(b"\x18\x6c") == b"\x7f"
    assert zdle_decode(b"\x18\x6d") == b"\xff"


@pytest.mark.parametrize("frame_end", [SUBPACKET_ZCRCE, SUBPACKET_ZCRCG, SUBPACKET_ZCRCQ, SUBPACKET_ZCRCW])
@pytest.mark.parametrize("crc_len", [16, 32])
def test_subpacket_round_trip(frame_end, crc_len):
    payload = bytes(range(0, 64)) + b"hello\x00\xff"
    raw = encode_subpacket(payload, frame_end, crc_len=crc_len)
    parsed, consumed = parse_subpacket(raw, crc_len=crc_len)
    assert parsed.payload == payload
    assert parsed.frame_end == frame_end
    assert parsed.ack_expected == (frame_end in (SUBPACKET_ZCRCQ, SUBPACKET_ZCRCW))
    assert consumed == len(raw)


def test_subpacket_exact_crcw():
    raw = encode_subpacket(b"abc", SUBPACKET_ZCRCW, crc_len=16)
    assert raw == b"abc\x18k\x59\xd9"


def test_subpacket_incomplete_and_bad_crc():
    raw = encode_subpacket(b"abc", SUBPACKET_ZCRCW, crc_len=16)
    assert parse_subpacket(raw[:4], crc_len=16) is None

    bad = bytearray(raw)
    bad[-1] ^= 0x01
    with pytest.raises(ValueError):
        parse_subpacket(bytes(bad), crc_len=16)
