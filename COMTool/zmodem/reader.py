"""Buffered header and subpacket reader for ZMODEM engines."""

from typing import Optional, Tuple

from .codec import (
    HEADER_PARSE_INVALID,
    HEADER_PARSE_NEED_MORE,
    HEADER_PARSE_OK,
    Subpacket,
    ZHeader,
    parse_header_at,
    parse_subpacket,
)
from .io import ZmodemIO


class ZmodemReader:
    def __init__(self, io: ZmodemIO, read_timeout: float = 30.0):
        self.io = io
        self.read_timeout = read_timeout
        self.buffer = bytearray()

    def _read_more(self, timeout: Optional[float] = None) -> None:
        if timeout is None:
            timeout = self.read_timeout
        data = self.io.read_some(8192, timeout=timeout)
        if not data:
            raise EOFError("ZMODEM stream closed")
        self.buffer.extend(data)

    def read_header(self, timeout: Optional[float] = None) -> ZHeader:
        while True:
            if not self.buffer:
                self._read_more(timeout)
                continue

            star = self.buffer.find(b"*")
            if star < 0:
                self.buffer.clear()
                self._read_more(timeout)
                continue
            if star > 0:
                del self.buffer[:star]

            result = parse_header_at(self.buffer, 0)
            if result.status == HEADER_PARSE_OK:
                del self.buffer[: result.consumed]
                return result.header
            if result.status == HEADER_PARSE_NEED_MORE:
                self._read_more(timeout)
                continue
            if result.status == HEADER_PARSE_INVALID:
                del self.buffer[:1]
                continue

            raise ValueError("unexpected header parser state")

    def read_subpacket(self, crc_len: int) -> Subpacket:
        while True:
            if not self.buffer:
                self._read_more()
                continue
            parsed = parse_subpacket(bytes(self.buffer), crc_len=crc_len)
            if parsed is not None:
                subpacket, consumed = parsed
                del self.buffer[:consumed]
                return subpacket
            self._read_more()

    def read_raw(self, count: int) -> bytes:
        while len(self.buffer) < count:
            self._read_more()
        data = bytes(self.buffer[:count])
        del self.buffer[:count]
        return data

    def clear_buffer(self) -> None:
        self.buffer.clear()

    def drain(self) -> bytes:
        data = bytes(self.buffer)
        self.buffer.clear()
        return data + self.io.drain()
