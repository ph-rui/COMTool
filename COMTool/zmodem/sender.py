"""ZMODEM sender."""

import os
from typing import Callable, List, Optional, Sequence

from .codec import (
    FRAME_ZABORT,
    FRAME_ZCAN,
    FRAME_ZDATA,
    FRAME_ZEOF,
    FRAME_ZFILE,
    FRAME_ZFIN,
    FRAME_ZNAK,
    FRAME_ZRQINIT,
    FRAME_ZRINIT,
    FRAME_ZRPOS,
    FRAME_ZSKIP,
    SUBPACKET_ZCRCE,
    SUBPACKET_ZCRCG,
    SUBPACKET_ZCRCW,
    ZRINIT_CANFC32,
    ZHeader,
    encode_binary_header,
    encode_hex_header,
    encode_subpacket,
    pack_offset,
    unpack_offset,
)
from .io import ZmodemAbort, ZmodemIO, ZmodemTimeout
from .reader import ZmodemReader


class ZmodemSender:
    def __init__(
        self,
        io: ZmodemIO,
        files: Sequence[str],
        initial_zrinit: Optional[ZHeader] = None,
        on_file_start: Optional[Callable[[str, int], None]] = None,
        on_progress: Optional[Callable[[str, int, int], None]] = None,
        on_file_complete: Optional[Callable[[str], None]] = None,
        header_timeout: float = 30.0,
        data_timeout: float = 30.0,
        chunk_size: int = 8192,
        max_retries: int = 5,
    ):
        self.io = io
        self.files = list(files)
        self.initial_zrinit = initial_zrinit
        self.on_file_start = on_file_start
        self.on_progress = on_progress
        self.on_file_complete = on_file_complete
        self.header_timeout = header_timeout
        self.data_timeout = data_timeout
        self.chunk_size = max(1, chunk_size)
        self.max_retries = max_retries
        self.reader = ZmodemReader(io, read_timeout=header_timeout)
        self.trailing = b""

    def _send_header(self, frame_type: int, data: bytes = b"\0\0\0\0") -> None:
        self.io.write(encode_hex_header(frame_type, data))

    def _send_binary_header(
        self, frame_type: int, data: bytes, crc_len: int
    ) -> None:
        self.io.write(encode_binary_header(frame_type, data, crc_len=crc_len))

    def _send_subpacket(self, payload: bytes, frame_end: int, crc_len: int) -> None:
        self.io.write(encode_subpacket(payload, frame_end, crc_len=crc_len))
        if frame_end == SUBPACKET_ZCRCW:
            # lrzsz uses XON to release a peer that may have sent XOFF.
            self.io.write(b"\x11")

    def _wait_for_zrinit(self) -> ZHeader:
        if self.initial_zrinit is not None:
            return self.initial_zrinit
        self._send_header(FRAME_ZRQINIT)
        for _ in range(self.max_retries):
            try:
                header = self.reader.read_header()
            except ZmodemTimeout:
                self._send_header(FRAME_ZRQINIT)
                continue
            if header.frame_type == FRAME_ZRINIT:
                return header
            if header.frame_type in (FRAME_ZABORT, FRAME_ZCAN):
                raise ZmodemAbort("receiver aborted the transfer")
        raise ZmodemTimeout("receiver did not send ZRINIT")

    def run(self):
        zrinit = self._wait_for_zrinit()
        crc_len = 32 if (zrinit.data[3] & ZRINIT_CANFC32) else 16
        for path in self.files:
            self._send_file(path, crc_len)
        self._finish()
        self.trailing = self.reader.drain()
        return self.trailing

    def _send_file(self, path: str, crc_len: int) -> None:
        if not os.path.isfile(path):
            raise FileNotFoundError(path)
        stat = os.stat(path)
        name = os.path.basename(path)
        metadata = (
            name.encode("utf-8", "surrogateescape")
            + b"\0"
            + (
                "%d %o %o 0 0 0"
                % (stat.st_size, int(stat.st_mtime), stat.st_mode & 0o7777)
            ).encode("ascii")
        )

        self._send_binary_header(FRAME_ZFILE, b"\0\0\0\0", crc_len)
        self._send_subpacket(metadata, SUBPACKET_ZCRCW, crc_len)

        offset = self._wait_for_zrpos()
        if offset is None:
            return
        if offset < 0 or offset > stat.st_size:
            raise ZmodemAbort("receiver requested an invalid offset")

        if self.on_file_start is not None:
            self.on_file_start(name, stat.st_size)

        retries = 0
        while True:
            try:
                self._send_data(path, offset, crc_len)
                result = self._wait_after_eof(stat.st_size)
            except ZmodemTimeout:
                retries += 1
                if retries > self.max_retries:
                    raise
                continue

            if result == "ok":
                break
            if result == "skip":
                return
            if isinstance(result, int):
                if result >= stat.st_size:
                    break
                offset = result
                retries += 1
                if retries > self.max_retries:
                    raise ZmodemAbort("too many receiver retransmit requests")
                continue
            raise ZmodemAbort("unexpected receiver response")

        if self.on_file_complete is not None:
            self.on_file_complete(name)

    def _wait_for_zrpos(self) -> Optional[int]:
        while True:
            header = self.reader.read_header()
            if header.frame_type == FRAME_ZRPOS:
                return unpack_offset(header.data)
            if header.frame_type == FRAME_ZSKIP:
                return None
            if header.frame_type in (FRAME_ZABORT, FRAME_ZCAN):
                raise ZmodemAbort("receiver aborted the transfer")
            if header.frame_type == FRAME_ZNAK:
                # The receiver did not like the file header; resend nothing
                # here and let the caller's retry logic handle it.
                continue
            # Ignore ZRINIT/ZACK etc. while waiting for ZRPOS.

    def _send_data(self, path: str, offset: int, crc_len: int) -> None:
        with open(path, "rb") as handle:
            handle.seek(offset)
            self._send_binary_header(FRAME_ZDATA, pack_offset(offset), crc_len)
            sent = offset
            chunk = handle.read(self.chunk_size)
            if not chunk:
                self._send_subpacket(b"", SUBPACKET_ZCRCE, crc_len)
                return
            while True:
                next_chunk = handle.read(self.chunk_size)
                if not next_chunk:
                    self._send_subpacket(chunk, SUBPACKET_ZCRCE, crc_len)
                    sent += len(chunk)
                    if self.on_progress is not None:
                        self.on_progress(os.path.basename(path), sent, os.path.getsize(path))
                    return
                self._send_subpacket(chunk, SUBPACKET_ZCRCG, crc_len)
                sent += len(chunk)
                if self.on_progress is not None:
                    self.on_progress(os.path.basename(path), sent, os.path.getsize(path))
                chunk = next_chunk

    def _wait_after_eof(self, size: int):
        self._send_header(FRAME_ZEOF, pack_offset(size))
        while True:
            header = self.reader.read_header()
            if header.frame_type == FRAME_ZRINIT:
                return "ok"
            if header.frame_type == FRAME_ZSKIP:
                return "skip"
            if header.frame_type == FRAME_ZRPOS:
                return unpack_offset(header.data)
            if header.frame_type in (FRAME_ZABORT, FRAME_ZCAN):
                raise ZmodemAbort("receiver aborted the transfer")
            if header.frame_type == FRAME_ZNAK:
                return size

    def _finish(self) -> None:
        self._send_header(FRAME_ZFIN)
        for _ in range(self.max_retries):
            try:
                header = self.reader.read_header()
            except ZmodemTimeout:
                self._send_header(FRAME_ZFIN)
                continue
            if header.frame_type == FRAME_ZFIN:
                self.io.write(b"OO")
                return
            if header.frame_type in (FRAME_ZABORT, FRAME_ZCAN):
                raise ZmodemAbort("receiver aborted the transfer")
        raise ZmodemTimeout("receiver did not acknowledge ZFIN")
