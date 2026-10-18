"""ZMODEM sender."""

import os
from typing import Callable, List, Optional, Sequence

from .codec import (
    FRAME_ZABORT,
    FRAME_ZACK,
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
        chunk_size: int = 1024,
        max_retries: int = 5,
        window_size: int = 1024,
        ack_timeout: float = 10.0,
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
        # Stop-and-wait every window_size bytes with a ZCRCW frame.  The
        # receiver's tty input queue is 4 KiB.  A 2 KiB window technically
        # fits, but lrzsz can ACK a window before its tty has fully drained,
        # so the next header and payload land while a little of the previous
        # one is still queued.  With real USB-serial adapters and a busy
        # board that occasionally loses bytes at the UART, two overlapping
        # 2 KiB windows push the queue over the high-water mark and trigger
        # a retransmission storm (measured: 1.52x wire bytes on a 4 MiB
        # transfer).  1 KiB windows leave 4x headroom: the same 4 MiB
        # transfer measured 1.046x wire bytes with one retry.
        self.window_size = max(self.chunk_size, window_size)
        self.ack_timeout = ack_timeout
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

        self._send_zfile(metadata, crc_len)

        offset = self._wait_for_zrpos(metadata, crc_len)
        if offset is None:
            return
        if offset < 0 or offset > stat.st_size:
            raise ZmodemAbort("receiver requested an invalid offset")

        if self.on_file_start is not None:
            self.on_file_start(name, stat.st_size)

        retries = 0
        last_offset = offset
        while True:
            # _send_data returns a byte offset when the receiver asked us to
            # reposition (ZRPOS/ZNAK/ACK timeout); None means all bytes plus
            # the final ZCRCE subpacket are on the wire.
            resume = self._send_data(path, offset, crc_len)
            if resume is not None and 0 <= resume < stat.st_size:
                if resume > last_offset:
                    # Moving forward means the previous retransmit was
                    # accepted; stale ZRPOS frames must not exhaust the
                    # retry budget for the rest of the file.
                    retries = 0
                    last_offset = resume
                else:
                    retries += 1
                    if retries > self.max_retries:
                        raise ZmodemAbort(
                            "too many receiver retransmit requests"
                        )
                offset = resume
                # Drop duplicate/stale ZRPOS frames from the failed window
                # before retransmitting; otherwise the sender keeps chasing
                # a position the receiver already moved past.
                self.reader.clear_buffer()
                self.io.drain()
                continue

            try:
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

    def _send_zfile(self, metadata: bytes, crc_len: int) -> None:
        self._send_binary_header(FRAME_ZFILE, b"\0\0\0\0", crc_len)
        self._send_subpacket(metadata, SUBPACKET_ZCRCW, crc_len)

    def _wait_for_zrpos(
        self, metadata: bytes, crc_len: int
    ) -> Optional[int]:
        retries = 0
        while True:
            try:
                header = self.reader.read_header()
            except ZmodemTimeout:
                retries += 1
                if retries > self.max_retries:
                    raise
                self._send_zfile(metadata, crc_len)
                continue
            if header.frame_type == FRAME_ZRPOS:
                return unpack_offset(header.data)
            if header.frame_type == FRAME_ZSKIP:
                return None
            if header.frame_type in (FRAME_ZABORT, FRAME_ZCAN):
                raise ZmodemAbort("receiver aborted the transfer")
            if header.frame_type == FRAME_ZNAK:
                retries += 1
                if retries > self.max_retries:
                    raise ZmodemAbort("receiver rejected the file header")
                self._send_zfile(metadata, crc_len)
                continue
            # Ignore repeated ZRINIT/ZACK etc. while waiting for ZRPOS.

    def _report_progress(self, path: str, sent: int, size: int) -> None:
        if self.on_progress is not None:
            self.on_progress(os.path.basename(path), sent, size)

    def _wait_for_ack(self, sent: int) -> Optional[int]:
        """Wait until the receiver acknowledges the current window.

        ``None`` means the peer accepted the window; an int is the offset we
        should resume from.  Timeouts and ZNAK resume at *sent*: if the peer
        already has those bytes it accepts our next window, and if it is
        behind it replies with ZRPOS (or an attention sequence) so the next
        ACK wait corrects the position.
        """
        while True:
            try:
                header = self.reader.read_header(timeout=self.ack_timeout)
            except ZmodemTimeout:
                return sent
            # lrzsz acknowledges ZCRCW with ZACK|0x80.
            frame_type = header.frame_type & 0x7F
            if frame_type == FRAME_ZACK:
                acked = unpack_offset(header.data)
                if acked < sent:
                    return acked
                return None
            if frame_type == FRAME_ZRPOS:
                return unpack_offset(header.data)
            if frame_type == FRAME_ZNAK:
                return sent
            if frame_type in (FRAME_ZABORT, FRAME_ZCAN):
                raise ZmodemAbort("receiver aborted the transfer")
            # Ignore stray headers (e.g. ZRINIT) and keep waiting.

    def _send_data(self, path: str, offset: int, crc_len: int) -> Optional[int]:
        size = os.path.getsize(path)
        with open(path, "rb") as handle:
            handle.seek(offset)
            sent = offset
            if size == 0:
                self._send_binary_header(FRAME_ZDATA, pack_offset(0), crc_len)
                self._send_subpacket(b"", SUBPACKET_ZCRCE, crc_len)
                return None
            while sent < size:
                # Each window starts with a fresh ZDATA header and ends with
                # ZCRCW.  lrzsz returns to header-search mode after ZCRCW, so
                # if a block was corrupted the next header (or the ZRPOS it
                # sends on the CRC error) puts both sides back in sync without
                # restarting the whole file.
                self._send_binary_header(FRAME_ZDATA, pack_offset(sent), crc_len)
                window_end = min(sent + self.window_size, size)
                while sent < window_end:
                    remaining = window_end - sent
                    chunk = handle.read(min(self.chunk_size, remaining))
                    if not chunk:
                        break
                    sent += len(chunk)
                    if sent >= size:
                        self._send_subpacket(chunk, SUBPACKET_ZCRCE, crc_len)
                        self._report_progress(path, sent, size)
                        return None
                    if sent >= window_end:
                        self._send_subpacket(chunk, SUBPACKET_ZCRCW, crc_len)
                    else:
                        self._send_subpacket(chunk, SUBPACKET_ZCRCG, crc_len)
                    self._report_progress(path, sent, size)
                resume = self._wait_for_ack(sent)
                if resume is not None:
                    return resume
            return None

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
                return 0

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
