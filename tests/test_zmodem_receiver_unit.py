import threading

from COMTool.zmodem.codec import (  # noqa: F401
    FRAME_ZCAN,
    FRAME_ZDATA,
    FRAME_ZEOF,
    FRAME_ZFILE,
    FRAME_ZFIN,
    FRAME_ZRINIT,
    FRAME_ZRPOS,
    SUBPACKET_ZCRCW,
    SUBPACKET_ZCRCE,
    SUBPACKET_ZCRCG,
    encode_binary_header,
    encode_hex_header,
    encode_subpacket,
    pack_offset,
    unpack_offset,
)
from COMTool.zmodem.io import ZmodemAbort, ZmodemIO
from COMTool.zmodem.reader import ZmodemReader
from COMTool.zmodem.receiver import ZmodemReceiver


class Duplex:
    def __init__(self):
        self.receiver_io = None
        self.peer_io = None
        self.receiver_io = ZmodemIO(lambda data: self.peer_io.feed(data))
        self.peer_io = ZmodemIO(lambda data: self.receiver_io.feed(data))


def test_receiver_retries_after_bad_subpacket_crc(tmp_path):
    link = Duplex()
    peer = ZmodemReader(link.peer_io, read_timeout=1)
    results = []
    errors = []

    def run_receiver():
        try:
            receiver = ZmodemReceiver(
                link.receiver_io,
                str(tmp_path),
                header_timeout=1,
                data_timeout=1,
            )
            results.append(receiver.run())
        except Exception as error:  # noqa: BLE001 - test reports the error
            errors.append(error)

    thread = threading.Thread(target=run_receiver, daemon=True)
    thread.start()

    initial = peer.read_header()
    assert initial.frame_type == FRAME_ZRINIT

    link.peer_io.write(
        encode_binary_header(FRAME_ZFILE, b"\0\0\0\0", crc_len=32)
    )
    link.peer_io.write(
        encode_subpacket(
            b"retry.bin\x005 0 644 0 0 0",
            SUBPACKET_ZCRCW,
            crc_len=32,
        )
    )
    response = peer.read_header()
    assert response.frame_type == FRAME_ZRPOS
    assert unpack_offset(response.data) == 0

    link.peer_io.write(
        encode_binary_header(FRAME_ZDATA, pack_offset(0), crc_len=32)
    )
    bad = bytearray(encode_subpacket(b"bad", SUBPACKET_ZCRCG, crc_len=32))
    bad[-1] ^= 0xFF
    link.peer_io.write(bytes(bad))

    response = peer.read_header()
    assert response.frame_type == FRAME_ZRPOS
    assert unpack_offset(response.data) == 0

    link.peer_io.write(
        encode_binary_header(FRAME_ZDATA, pack_offset(0), crc_len=32)
    )
    link.peer_io.write(
        encode_subpacket(b"hello", SUBPACKET_ZCRCE, crc_len=32)
    )
    link.peer_io.write(encode_hex_header(FRAME_ZEOF, pack_offset(5)))

    response = peer.read_header()
    assert response.frame_type == FRAME_ZRINIT

    link.peer_io.write(encode_hex_header(FRAME_ZFIN))
    response = peer.read_header()
    assert response.frame_type == FRAME_ZFIN
    link.peer_io.write(b"OO")

    thread.join(3)
    assert not thread.is_alive()
    assert errors == []
    assert len(results) == 1
    received = results[0]
    assert len(received) == 1
    with open(received[0].path, "rb") as handle:
        assert handle.read() == b"hello"


def test_receiver_aborts_and_cleans_up_after_zcan(tmp_path):
    link = Duplex()
    peer = ZmodemReader(link.peer_io, read_timeout=1)
    errors = []

    def run_receiver():
        try:
            receiver = ZmodemReceiver(
                link.receiver_io,
                str(tmp_path),
                header_timeout=1,
                data_timeout=1,
            )
            receiver.run()
        except Exception as error:  # noqa: BLE001 - test reports the error
            errors.append(error)

    thread = threading.Thread(target=run_receiver, daemon=True)
    thread.start()

    assert peer.read_header().frame_type == FRAME_ZRINIT
    link.peer_io.write(
        encode_binary_header(FRAME_ZFILE, b"\0\0\0\0", crc_len=32)
    )
    link.peer_io.write(
        encode_subpacket(
            b"cancel.bin\x005 0 644 0 0 0",
            SUBPACKET_ZCRCW,
            crc_len=32,
        )
    )
    assert peer.read_header().frame_type == FRAME_ZRPOS
    link.peer_io.write(encode_hex_header(FRAME_ZCAN))

    thread.join(3)
    assert not thread.is_alive()
    assert len(errors) == 1
    assert isinstance(errors[0], ZmodemAbort)
    assert list(tmp_path.iterdir()) == []
