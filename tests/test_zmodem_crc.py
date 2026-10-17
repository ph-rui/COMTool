import zlib

import pytest

from COMTool.zmodem.crc import (
    crc16,
    crc32,
    pack_crc16,
    pack_crc32,
    unpack_crc16,
    unpack_crc32,
)


def test_crc16_known_vectors():
    assert crc16(b"") == 0
    assert crc16(b"\x00") == 0
    assert crc16(b"123456789") == 0x31C3
    assert crc16(b"A") == 0x58E5


def test_crc32_matches_zlib():
    for data in (b"", b"123456789", b"\x00\xff\x10\x90", bytes(range(256))):
        assert crc32(data) == (zlib.crc32(data) & 0xFFFFFFFF)


def test_crc16_pack_is_big_endian():
    assert pack_crc16(0x31C3) == b"\x31\xc3"
    assert unpack_crc16(b"\x31\xc3") == 0x31C3


def test_crc32_pack_is_little_endian():
    assert pack_crc32(0xCBF43926) == b"\x26\x39\xf4\xcb"
    assert unpack_crc32(b"\x26\x39\xf4\xcb") == 0xCBF43926


def test_unpack_rejects_wrong_lengths():
    with pytest.raises(ValueError):
        unpack_crc16(b"\x00")
    with pytest.raises(ValueError):
        unpack_crc32(b"\x00\x00\x00")
