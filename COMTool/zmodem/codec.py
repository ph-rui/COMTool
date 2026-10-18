"""ZMODEM header, ZDLE and data subpacket codec."""

from dataclasses import dataclass
from typing import Optional, Tuple

from .crc import (
    crc16,
    crc32,
    pack_crc16,
    pack_crc32,
    unpack_crc16,
    unpack_crc32,
)

ZPAD = 0x2A
ZDLE = 0x18
ZBIN = 0x41
ZHEX = 0x42
ZBIN32 = 0x43

XON = 0x11
XOFF = 0x13
XON_HIGH = 0x91
XOFF_HIGH = 0x93

# Frame types
FRAME_ZRQINIT = 0
FRAME_ZRINIT = 1
FRAME_ZSINIT = 2
FRAME_ZACK = 3
FRAME_ZFILE = 4
FRAME_ZSKIP = 5
FRAME_ZNAK = 6
FRAME_ZABORT = 7
FRAME_ZFIN = 8
FRAME_ZRPOS = 9
FRAME_ZDATA = 10
FRAME_ZEOF = 11
FRAME_ZFERR = 12
FRAME_ZCRC = 13
FRAME_ZCHALLENGE = 14
FRAME_ZCOMPL = 15
FRAME_ZCAN = 16
FRAME_ZFREECNT = 17
FRAME_ZCOMMAND = 18
FRAME_ZSTDERR = 19

# Data subpacket endings
SUBPACKET_ZCRCE = 0x68
SUBPACKET_ZCRCG = 0x69
SUBPACKET_ZCRCQ = 0x6A
SUBPACKET_ZCRCW = 0x6B

SUBPACKET_ENDS = (
    SUBPACKET_ZCRCE,
    SUBPACKET_ZCRCG,
    SUBPACKET_ZCRCQ,
    SUBPACKET_ZCRCW,
)
SUBPACKET_ACK_ENDS = (SUBPACKET_ZCRCQ, SUBPACKET_ZCRCW)

# ZRINIT flags in ZF3
ZRINIT_CANFDX = 0x01
ZRINIT_CANOVIO = 0x02
ZRINIT_CANBRK = 0x04
ZRINIT_CANCRY = 0x08
ZRINIT_CANLZW = 0x10
ZRINIT_CANFC32 = 0x20
ZRINIT_ESCCTL = 0x40
ZRINIT_ESC8 = 0x80

HEADER_PARSE_OK = "ok"
HEADER_PARSE_NEED_MORE = "need_more"
HEADER_PARSE_INVALID = "invalid"

_HEX_PREFIX = b"**\x18B"
_BIN16_PREFIX = b"*\x18A"
_BIN32_PREFIX = b"*\x18C"


@dataclass
class ZHeader:
    frame_type: int
    data: bytes
    crc_len: int
    raw: bytes


@dataclass
class HeaderParseResult:
    status: str
    header: Optional[ZHeader] = None
    consumed: int = 0


@dataclass
class Subpacket:
    frame_end: int
    payload: bytes
    crc_len: int
    raw_len: int

    @property
    def frame_end_frame(self) -> bool:
        return self.frame_end in (SUBPACKET_ZCRCE, SUBPACKET_ZCRCW)

    @property
    def ack_expected(self) -> bool:
        return self.frame_end in SUBPACKET_ACK_ENDS


def _escape_byte(
    value: int, escape_ctrl: bool, escape_8bit: bool, previous: int
) -> bool:
    # ZDLE, DLE, XON/XOFF and their parity-masked variants are the only
    # bytes the protocol requires escaping.  lrzsz only escapes other
    # control characters when the receiver asks for it (ZRINIT ESCCTL)
    # and only escapes the 8th bit for 7-bit links (ZRINIT ESC8).
    if value in (ZDLE, XON, XOFF, XON_HIGH, XOFF_HIGH, 0x10, 0x90):
        return True
    if escape_ctrl and (value & 0x7F) < 0x20:
        return True
    if escape_8bit and value & 0x80:
        return True
    # Telenet command escape: CR after '@' must be escaped.
    if value in (0x0D, 0x8D) and (previous & 0x7F) == 0x40:
        return True
    return False


def zdle_encode(
    data: bytes, escape_ctrl: bool = False, escape_8bit: bool = False
) -> bytes:
    """ZDLE-encode *data* for transmission.

    Defaults match lrzsz on an 8-bit clean line: only bytes that the
    protocol itself interprets are escaped, which keeps ZMODEM overhead
    around 2% instead of ~25% for random binary data.
    """
    out = bytearray()
    previous = 0
    for value in data:
        if _escape_byte(value, escape_ctrl, escape_8bit, previous):
            out.append(ZDLE)
            out.append(value ^ 0x40)
        else:
            out.append(value)
        previous = value
    return bytes(out)


def _decode_zdle_byte(value: int) -> int:
    if value == 0x6C:  # ZRUB0
        return 0x7F
    if value == 0x6D:  # ZRUB1
        return 0xFF
    return value ^ 0x40


def zdle_decode(data: bytes, ignore_xonoff: bool = False) -> bytes:
    """Decode a ZDLE-encoded byte string."""
    out = bytearray()
    index = 0
    while index < len(data):
        value = data[index]
        index += 1
        if value == ZDLE:
            if index >= len(data):
                raise ValueError("trailing ZDLE")
            out.append(_decode_zdle_byte(data[index]))
            index += 1
        elif ignore_xonoff and value in (XON, XOFF, XON_HIGH, XOFF_HIGH):
            continue
        else:
            out.append(value)
    return bytes(out)


def _zdle_decode_n(
    data: bytes, start: int, count: int, skip_xonoff: bool = True
) -> Optional[Tuple[bytes, int]]:
    """Decode exactly *count* bytes from *data* starting at *start*.

    Returns (decoded, encoded_length) or None if more bytes are needed.
    """
    out = bytearray()
    index = start
    while len(out) < count:
        if index >= len(data):
            return None
        value = data[index]
        index += 1
        if value == ZDLE:
            if index >= len(data):
                return None
            out.append(_decode_zdle_byte(data[index]))
            index += 1
        elif skip_xonoff and value in (XON, XOFF, XON_HIGH, XOFF_HIGH):
            continue
        else:
            out.append(value)
    return bytes(out), index - start


def _is_prefix_of(data: bytes, offset: int, prefix: bytes) -> bool:
    available = data[offset : offset + len(prefix)]
    return prefix.startswith(available)


def _parse_hex_header(data: bytes, offset: int) -> HeaderParseResult:
    start = offset + len(_HEX_PREFIX)
    hex_end = start + 14
    if len(data) < hex_end:
        return HeaderParseResult(HEADER_PARSE_NEED_MORE)

    digits = data[start:hex_end]
    try:
        raw_body = bytes.fromhex(digits.decode("ascii"))
    except (ValueError, UnicodeDecodeError):
        return HeaderParseResult(HEADER_PARSE_INVALID)
    if len(raw_body) != 7:
        return HeaderParseResult(HEADER_PARSE_INVALID)

    # Accept CRLF and CRLF-XON.  A bare LF is accepted for robustness.
    index = hex_end
    if index >= len(data):
        return HeaderParseResult(HEADER_PARSE_NEED_MORE)
    if data[index] == 0x0D:
        if index + 1 >= len(data):
            return HeaderParseResult(HEADER_PARSE_NEED_MORE)
        # lrzsz sends CR, 0x8a instead of CR, LF.
        if data[index + 1] not in (0x0A, 0x8A):
            return HeaderParseResult(HEADER_PARSE_INVALID)
        index += 2
    elif data[index] == 0x0A:
        index += 1
    else:
        return HeaderParseResult(HEADER_PARSE_INVALID)

    if index < len(data) and data[index] == XON:
        index += 1

    body = raw_body[:5]
    got_crc = raw_body[5:7]
    if crc16(body) != unpack_crc16(got_crc):
        return HeaderParseResult(HEADER_PARSE_INVALID)
    return HeaderParseResult(
        HEADER_PARSE_OK,
        ZHeader(
            frame_type=body[0],
            data=body[1:5],
            crc_len=16,
            raw=data[offset:index],
        ),
        index - offset,
    )


def _parse_binary_header(data: bytes, offset: int, crc_len: int) -> HeaderParseResult:
    prefix_len = len(_BIN16_PREFIX)
    start = offset + prefix_len
    decoded = _zdle_decode_n(data, start, 5 + (crc_len // 8), skip_xonoff=True)
    if decoded is None:
        return HeaderParseResult(HEADER_PARSE_NEED_MORE)
    body_and_crc, encoded_len = decoded
    body = body_and_crc[:5]
    got_crc = body_and_crc[5:]
    expected = crc16(body) if crc_len == 16 else crc32(body)
    got = unpack_crc16(got_crc) if crc_len == 16 else unpack_crc32(got_crc)
    if expected != got:
        return HeaderParseResult(HEADER_PARSE_INVALID)
    consumed = prefix_len + encoded_len
    return HeaderParseResult(
        HEADER_PARSE_OK,
        ZHeader(
            frame_type=body[0],
            data=body[1:5],
            crc_len=crc_len,
            raw=data[offset : offset + consumed],
        ),
        consumed,
    )


def parse_header_at(data: bytes, offset: int = 0) -> HeaderParseResult:
    """Parse a header beginning exactly at *offset*.

    Leading garbage is not skipped; use :func:`parse_header` for that.
    """
    remaining = len(data) - offset
    if remaining <= 0:
        return HeaderParseResult(HEADER_PARSE_NEED_MORE)

    if data.startswith(_HEX_PREFIX, offset):
        return _parse_hex_header(data, offset)
    if data.startswith(_BIN32_PREFIX, offset):
        return _parse_binary_header(data, offset, 32)
    if data.startswith(_BIN16_PREFIX, offset):
        return _parse_binary_header(data, offset, 16)

    # Distinguish an incomplete prefix from definitively invalid data.
    for prefix in (_HEX_PREFIX, _BIN16_PREFIX, _BIN32_PREFIX):
        if _is_prefix_of(data, offset, prefix):
            return HeaderParseResult(HEADER_PARSE_NEED_MORE)
    return HeaderParseResult(HEADER_PARSE_INVALID)


def parse_header(data: bytes) -> Optional[Tuple[ZHeader, int]]:
    """Scan *data* for the first valid header.

    Returns (header, total_consumed_bytes_including_garbage), or None if no
    complete valid header is available yet.
    """
    index = 0
    while index < len(data):
        star = data.find(b"*", index)
        if star < 0:
            return None
        result = parse_header_at(data, star)
        if result.status == HEADER_PARSE_OK:
            return result.header, star + result.consumed
        if result.status == HEADER_PARSE_NEED_MORE:
            return None
        index = star + 1
    return None


def encode_hex_header(frame_type: int, data: bytes = b"\0\0\0\0") -> bytes:
    if len(data) != 4:
        raise ValueError("header data must be 4 bytes")
    body = bytes([frame_type]) + data
    crc_bytes = pack_crc16(crc16(body))
    # lrzsz's hex decoder only accepts lower-case digits.
    digits = (body + crc_bytes).hex().encode("ascii")
    return _HEX_PREFIX + digits + b"\r\n\x11"


def encode_binary_header(
    frame_type: int, data: bytes = b"\0\0\0\0", crc_len: int = 16
) -> bytes:
    if len(data) != 4:
        raise ValueError("header data must be 4 bytes")
    if crc_len not in (16, 32):
        raise ValueError("crc_len must be 16 or 32")
    body = bytes([frame_type]) + data
    if crc_len == 16:
        prefix = _BIN16_PREFIX
        crc_bytes = pack_crc16(crc16(body))
    else:
        prefix = _BIN32_PREFIX
        crc_bytes = pack_crc32(crc32(body))
    return prefix + zdle_encode(body + crc_bytes)


def encode_subpacket(payload: bytes, frame_end: int, crc_len: int = 16) -> bytes:
    if frame_end not in SUBPACKET_ENDS:
        raise ValueError("invalid subpacket frame end")
    if crc_len not in (16, 32):
        raise ValueError("crc_len must be 16 or 32")
    body = payload + bytes([frame_end])
    if crc_len == 16:
        crc_bytes = pack_crc16(crc16(body))
    else:
        crc_bytes = pack_crc32(crc32(body))
    return (
        zdle_encode(payload)
        + bytes([ZDLE, frame_end])
        + zdle_encode(crc_bytes)
    )


def parse_subpacket(data: bytes, crc_len: int = 16) -> Optional[Tuple[Subpacket, int]]:
    """Parse one data subpacket from the start of *data*.

    Returns (subpacket, consumed) or None if more bytes are needed.  Raises
    ValueError when a complete subpacket has an invalid CRC.
    """
    if crc_len not in (16, 32):
        raise ValueError("crc_len must be 16 or 32")

    delimiter = -1
    index = 0
    while index < len(data):
        value = data[index]
        if value == ZDLE:
            if index + 1 >= len(data):
                return None
            following = data[index + 1]
            if following in SUBPACKET_ENDS:
                delimiter = index
                break
            index += 2
        else:
            index += 1

    if delimiter < 0:
        return None

    frame_end = data[delimiter + 1]
    payload_encoded = data[:delimiter]
    try:
        payload = zdle_decode(payload_encoded, ignore_xonoff=True)
    except ValueError:
        return None

    decoded_crc = _zdle_decode_n(
        data, delimiter + 2, crc_len // 8, skip_xonoff=True
    )
    if decoded_crc is None:
        return None
    crc_bytes, encoded_crc_len = decoded_crc
    body = payload + bytes([frame_end])
    expected = crc16(body) if crc_len == 16 else crc32(body)
    got = unpack_crc16(crc_bytes) if crc_len == 16 else unpack_crc32(crc_bytes)
    if expected != got:
        raise ValueError("bad subpacket CRC")

    consumed = delimiter + 2 + encoded_crc_len
    return Subpacket(frame_end, payload, crc_len, consumed), consumed


def pack_offset(offset: int) -> bytes:
    if not 0 <= offset <= 0xFFFFFFFF:
        raise ValueError("offset out of range")
    return offset.to_bytes(4, "little")


def unpack_offset(data: bytes) -> int:
    if len(data) != 4:
        raise ValueError("offset must be 4 bytes")
    return int.from_bytes(data, "little")


def strip_ignored_bytes(data: bytes) -> bytes:
    """Remove unescaped XON/XOFF bytes from a ZMODEM input stream."""
    return bytes(
        value
        for value in data
        if value not in (XON, XOFF, XON_HIGH, XOFF_HIGH)
    )
