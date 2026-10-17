"""ZMODEM receiver."""

import os
from dataclasses import dataclass
from typing import Callable, List, Optional

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
    FRAME_ZSINIT,
    FRAME_ZSKIP,
    SUBPACKET_ZCRCW,
    ZRINIT_CANFC32,
    ZRINIT_CANFDX,
    ZRINIT_CANOVIO,
    ZHeader,
    encode_hex_header,
    pack_offset,
    unpack_offset,
)
from .filename import FileMeta, parse_zfile_metadata, unique_temp_path
from .io import ZmodemAbort, ZmodemIO, ZmodemTimeout
from .reader import ZmodemReader


@dataclass
class ReceivedFile:
    name: str
    path: str
    size: int
    expected_size: Optional[int]


class ZmodemReceiver:
    def __init__(
        self,
        io: ZmodemIO,
        output_dir: str,
        on_file_start: Optional[Callable[[FileMeta, str], None]] = None,
        on_progress: Optional[Callable[[str, int, Optional[int]], None]] = None,
        on_file_complete: Optional[Callable[[FileMeta, str], None]] = None,
        header_timeout: float = 30.0,
        data_timeout: float = 30.0,
        max_retries: int = 5,
    ):
        self.io = io
        self.output_dir = output_dir
        self.on_file_start = on_file_start
        self.on_progress = on_progress
        self.on_file_complete = on_file_complete
        self.header_timeout = header_timeout
        self.data_timeout = data_timeout
        self.max_retries = max_retries
        self.reader = ZmodemReader(io, read_timeout=data_timeout)
        self.files: List[ReceivedFile] = []

    def _send_header(self, frame_type: int, data: bytes = b"\0\0\0\0") -> None:
        self.io.write(encode_hex_header(frame_type, data))

    def _send_zrinit(self) -> None:
        flags = ZRINIT_CANFDX | ZRINIT_CANOVIO | ZRINIT_CANFC32
        self._send_header(FRAME_ZRINIT, bytes([0, 0, 0, flags]))

    def _send_zrpos(self, offset: int) -> None:
        self._send_header(FRAME_ZRPOS, pack_offset(offset))

    def _send_zack(self, offset: int) -> None:
        self._send_header(FRAME_ZACK, pack_offset(offset))

    def run(self):
        self._send_zrinit()
        while True:
            try:
                header = self.reader.read_header()
            except ZmodemTimeout:
                raise
            except ValueError:
                self._send_zrinit()
                continue

            if header.frame_type == FRAME_ZRQINIT:
                self._send_zrinit()
                continue
            if header.frame_type == FRAME_ZSINIT:
                self.reader.read_subpacket(header.crc_len)
                self._send_zack(1)
                continue
            if header.frame_type == FRAME_ZFILE:
                if self._receive_file(header) == "finish":
                    self._send_header(FRAME_ZFIN)
                    self._read_oo()
                    return self.files
                continue
            if header.frame_type == FRAME_ZFIN:
                self._send_header(FRAME_ZFIN)
                self._read_oo()
                return self.files
            if header.frame_type == FRAME_ZSKIP:
                continue
            if header.frame_type in (FRAME_ZABORT, FRAME_ZCAN):
                raise ZmodemAbort("sender aborted the transfer")
            if header.frame_type == FRAME_ZNAK:
                self._send_zrinit()
                continue
            # ZACK/ZRPOS/ZRINIT or unknown frames are harmless here.
            continue

    def _read_oo(self) -> None:
        try:
            data = self.reader.read_raw(2)
        except (ZmodemTimeout, EOFError):
            return
        if data != b"OO":
            # Put unexpected bytes back for the terminal.  The common case is
            # an immediate shell prompt after a transfer.
            self.reader.buffer[:0] = data

    def _receive_file(self, header: ZHeader) -> None:
        try:
            metadata_packet = self.reader.read_subpacket(header.crc_len)
        except ValueError:
            self._send_header(FRAME_ZNAK)
            self.reader.clear_buffer()
            return

        meta = parse_zfile_metadata(metadata_packet.payload)
        path = unique_temp_path(self.output_dir, meta.name)
        handle = open(path, "wb")
        if self.on_file_start is not None:
            self.on_file_start(meta, path)

        offset = 0
        retries = 0
        finish_after_file = False
        self._send_zrpos(0)
        try:
            while True:
                try:
                    next_header = self.reader.read_header()
                except ValueError:
                    retries += 1
                    if retries > self.max_retries:
                        raise
                    self.reader.clear_buffer()
                    self._send_zrpos(offset)
                    continue

                if next_header.frame_type == FRAME_ZDATA:
                    data_offset = unpack_offset(next_header.data)
                    if data_offset != offset:
                        retries += 1
                        if retries > self.max_retries:
                            raise ZmodemAbort("sender keeps sending a bad offset")
                        self._send_zrpos(offset)
                        continue

                    frame_finished = self._receive_data_frames(
                        handle, header.crc_len, offset, meta
                    )
                    if frame_finished is None:
                        retries += 1
                        if retries > self.max_retries:
                            raise ZmodemAbort(
                                "too many data checksum failures"
                            )
                        continue
                    offset = frame_finished
                    retries = 0
                    continue

                if next_header.frame_type == FRAME_ZEOF:
                    eof_offset = unpack_offset(next_header.data)
                    if eof_offset != offset:
                        retries += 1
                        if retries > self.max_retries:
                            raise ZmodemAbort("ZEOF offset does not match")
                        self._send_zrpos(offset)
                        continue
                    break

                if next_header.frame_type == FRAME_ZFIN:
                    finish_after_file = True
                    break
                if next_header.frame_type == FRAME_ZSKIP:
                    handle.close()
                    try:
                        os.remove(path)
                    except OSError:
                        pass
                    return "skip"
                if next_header.frame_type in (FRAME_ZABORT, FRAME_ZCAN):
                    raise ZmodemAbort("sender aborted the transfer")
                if next_header.frame_type == FRAME_ZNAK:
                    retries += 1
                    if retries > self.max_retries:
                        raise ZmodemAbort("too many ZNAK frames")
                    self._send_zrpos(offset)
                    continue
                # Ignore other headers while waiting for file data.
        except Exception:
            handle.close()
            try:
                os.remove(path)
            except OSError:
                pass
            raise
        else:
            handle.close()

        self.files.append(
            ReceivedFile(meta.name, path, offset, meta.size)
        )
        if self.on_file_complete is not None:
            self.on_file_complete(meta, path)
        if finish_after_file:
            return "finish"
        self._send_zrinit()
        return "ok"

    def _receive_data_frames(
        self, handle, crc_len: int, offset: int, meta: FileMeta
    ):
        while True:
            try:
                subpacket = self.reader.read_subpacket(crc_len)
            except ValueError:
                self.reader.clear_buffer()
                self._send_zrpos(offset)
                return None

            handle.write(subpacket.payload)
            offset += len(subpacket.payload)
            if self.on_progress is not None:
                self.on_progress(meta.name, offset, meta.size)

            if subpacket.ack_expected and not subpacket.frame_end_frame:
                self._send_zack(offset)
            if subpacket.frame_end_frame:
                if subpacket.frame_end == SUBPACKET_ZCRCW:
                    self._send_zack(offset)
                return offset
