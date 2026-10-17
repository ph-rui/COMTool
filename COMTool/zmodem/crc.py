"""CRC routines used by ZMODEM.

ZMODEM headers use the XMODEM CRC16 (poly 0x1021, initial value 0) and
transmit it big-endian.  Data subpackets may use the same CRC16 or the
standard IEEE CRC32, which is transmitted little-endian.
"""


def _make_crc16_table():
    table = []
    for value in range(256):
        crc = value << 8
        for _ in range(8):
            if crc & 0x8000:
                crc = ((crc << 1) ^ 0x1021) & 0xFFFF
            else:
                crc = (crc << 1) & 0xFFFF
        table.append(crc)
    return tuple(table)


_CRC16_TABLE = _make_crc16_table()


def _make_crc32_table():
    table = []
    for value in range(256):
        crc = value
        for _ in range(8):
            if crc & 1:
                crc = (crc >> 1) ^ 0xEDB88320
            else:
                crc >>= 1
        table.append(crc & 0xFFFFFFFF)
    return tuple(table)


_CRC32_TABLE = _make_crc32_table()


def crc16(data: bytes) -> int:
    """Return the ZMODEM/XMODEM CRC16 of *data*."""
    crc = 0
    table = _CRC16_TABLE
    for byte in data:
        crc = ((crc << 8) & 0xFFFF) ^ table[((crc >> 8) ^ byte) & 0xFF]
    return crc


def crc32(data: bytes) -> int:
    """Return the standard IEEE CRC32 of *data*."""
    crc = 0xFFFFFFFF
    table = _CRC32_TABLE
    for byte in data:
        crc = (crc >> 8) ^ table[(crc ^ byte) & 0xFF]
    return crc ^ 0xFFFFFFFF


def pack_crc16(value: int) -> bytes:
    if not 0 <= value <= 0xFFFF:
        raise ValueError("CRC16 value out of range")
    return value.to_bytes(2, "big")


def unpack_crc16(data: bytes) -> int:
    if len(data) != 2:
        raise ValueError("CRC16 must be 2 bytes")
    return int.from_bytes(data, "big")


def pack_crc32(value: int) -> bytes:
    if not 0 <= value <= 0xFFFFFFFF:
        raise ValueError("CRC32 value out of range")
    return value.to_bytes(4, "little")


def unpack_crc32(data: bytes) -> int:
    if len(data) != 4:
        raise ValueError("CRC32 must be 4 bytes")
    return int.from_bytes(data, "little")
