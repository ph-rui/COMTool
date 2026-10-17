import threading

import pytest

from COMTool.zmodem.codec import (
    FRAME_ZCAN,
    FRAME_ZDATA,
    FRAME_ZEOF,
    FRAME_ZFILE,
    FRAME_ZFIN,
    FRAME_ZNAK,
    FRAME_ZRINIT,
    FRAME_ZRPOS,
    FRAME_ZSKIP,
    SUBPACKET_ZCRCE,
    SUBPACKET_ZCRCG,
    ZHeader,
    encode_hex_header,
    pack_offset,
    unpack_offset,
)
from COMTool.zmodem.io import ZmodemAbort, ZmodemIO
from COMTool.zmodem.reader import ZmodemReader
from COMTool.zmodem.sender import ZmodemSender


class Duplex:
    def __init__(self):
        self.sender_io = None
        self.peer_io = None
        self.sender_io = ZmodemIO(lambda data: self.peer_io.feed(data))
        self.peer_io = ZmodemIO(lambda data: self.sender_io.feed(data))


def zrinit(flags=0x23):
    return ZHeader(FRAME_ZRINIT, bytes([0, 0, 0, flags]), 16, b"")


def run_sender(link, files, flags=0x23, **kwargs):
    sender = ZmodemSender(
        link.sender_io,
        files,
        initial_zrinit=zrinit(flags),
        header_timeout=1,
        data_timeout=1,
        **kwargs
    )
    result = {}
    errors = []

    def target():
        try:
            result["trailing"] = sender.run()
        except Exception as error:  # noqa: BLE001 - test reports the error
            errors.append(error)

    thread = threading.Thread(target=target, daemon=True)
    thread.start()
    return thread, result, errors


def read_file_data(peer, crc_len):
    header = peer.read_header()
    assert header.frame_type == FRAME_ZDATA
    payload = bytearray()
    while True:
        packet = peer.read_subpacket(crc_len)
        payload.extend(packet.payload)
        if packet.frame_end == SUBPACKET_ZCRCE:
            return unpack_offset(header.data), bytes(payload)


def finish_session(peer, link):
    header = peer.read_header()
    assert header.frame_type == FRAME_ZFIN
    link.peer_io.write(encode_hex_header(FRAME_ZFIN))
    assert peer.read_raw(2) == b"OO"


def test_sender_uses_16_bit_crc_when_receiver_does_not_offer_32(tmp_path):
    source = tmp_path / "small.bin"
    source.write_bytes(b"abcdef")
    link = Duplex()
    peer = ZmodemReader(link.peer_io, read_timeout=1)
    thread, result, errors = run_sender(link, [str(source)], flags=0x03)

    file_header = peer.read_header()
    assert file_header.frame_type == FRAME_ZFILE
    assert file_header.crc_len == 16
    metadata = peer.read_subpacket(16)
    assert metadata.payload.startswith(b"small.bin\0")

    link.peer_io.write(encode_hex_header(FRAME_ZRPOS, pack_offset(0)))
    offset, payload = read_file_data(peer, 16)
    assert offset == 0
    assert payload == b"abcdef"

    eof = peer.read_header()
    assert eof.frame_type == FRAME_ZEOF
    assert unpack_offset(eof.data) == 6
    link.peer_io.write(encode_hex_header(FRAME_ZRINIT))
    finish_session(peer, link)

    thread.join(3)
    assert not thread.is_alive()
    assert errors == []
    assert "trailing" in result


def test_sender_resumes_from_zrpos_offset(tmp_path):
    source = tmp_path / "resume.bin"
    source.write_bytes(b"abcdef")
    link = Duplex()
    peer = ZmodemReader(link.peer_io, read_timeout=1)
    progress = []
    thread, result, errors = run_sender(
        link, [str(source)], on_progress=lambda n, s, t: progress.append((s, t))
    )

    assert peer.read_header().frame_type == FRAME_ZFILE
    peer.read_subpacket(32)
    link.peer_io.write(encode_hex_header(FRAME_ZRPOS, pack_offset(3)))

    offset, payload = read_file_data(peer, 32)
    assert offset == 3
    assert payload == b"def"
    assert peer.read_header().frame_type == FRAME_ZEOF
    link.peer_io.write(encode_hex_header(FRAME_ZRINIT))
    finish_session(peer, link)

    thread.join(3)
    assert errors == []
    assert progress == [(6, 6)]


def test_sender_resends_zfile_after_znak(tmp_path):
    source = tmp_path / "retry.bin"
    source.write_bytes(b"data")
    link = Duplex()
    peer = ZmodemReader(link.peer_io, read_timeout=1)
    thread, result, errors = run_sender(link, [str(source)])

    assert peer.read_header().frame_type == FRAME_ZFILE
    peer.read_subpacket(32)
    link.peer_io.write(encode_hex_header(FRAME_ZNAK))

    assert peer.read_header().frame_type == FRAME_ZFILE
    peer.read_subpacket(32)
    link.peer_io.write(encode_hex_header(FRAME_ZRPOS, pack_offset(0)))

    offset, payload = read_file_data(peer, 32)
    assert (offset, payload) == (0, b"data")
    assert peer.read_header().frame_type == FRAME_ZEOF
    link.peer_io.write(encode_hex_header(FRAME_ZRINIT))
    finish_session(peer, link)

    thread.join(3)
    assert errors == []


def test_sender_skips_file_after_zskip(tmp_path):
    source = tmp_path / "skip.bin"
    source.write_bytes(b"data")
    link = Duplex()
    peer = ZmodemReader(link.peer_io, read_timeout=1)
    thread, result, errors = run_sender(link, [str(source)])

    assert peer.read_header().frame_type == FRAME_ZFILE
    peer.read_subpacket(32)
    link.peer_io.write(encode_hex_header(FRAME_ZSKIP))
    finish_session(peer, link)

    thread.join(3)
    assert errors == []


def test_sender_aborts_on_zcan(tmp_path):
    source = tmp_path / "abort.bin"
    source.write_bytes(b"data")
    link = Duplex()
    peer = ZmodemReader(link.peer_io, read_timeout=1)
    thread, result, errors = run_sender(link, [str(source)])

    assert peer.read_header().frame_type == FRAME_ZFILE
    peer.read_subpacket(32)
    link.peer_io.write(encode_hex_header(FRAME_ZCAN))

    thread.join(3)
    assert len(errors) == 1
    assert isinstance(errors[0], ZmodemAbort)


def test_sender_missing_file_fails_without_hanging(tmp_path):
    link = Duplex()
    thread, result, errors = run_sender(
        link, [str(tmp_path / "missing.bin")]
    )
    thread.join(3)
    assert len(errors) == 1
    assert isinstance(errors[0], FileNotFoundError)
