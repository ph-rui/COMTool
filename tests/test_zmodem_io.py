import threading
import time

import pytest

from COMTool.zmodem.io import ZmodemAbort, ZmodemIO, ZmodemTimeout


def test_read_exact_across_feeds():
    written = []
    io = ZmodemIO(written.append)
    io.feed(b"abc")
    assert io.read(2, timeout=1) == b"ab"
    io.feed(b"def")
    assert io.read(4, timeout=1) == b"cdef"
    assert written == []


def test_read_timeout():
    io = ZmodemIO(lambda data: None)
    with pytest.raises(ZmodemTimeout):
        io.read(1, timeout=0.02)


def test_cancel_wakes_reader():
    io = ZmodemIO(lambda data: None)
    caught = []

    def reader():
        try:
            io.read(1, timeout=5)
        except Exception as error:  # noqa: BLE001 - test captures exact type
            caught.append(error)

    thread = threading.Thread(target=reader)
    thread.start()
    time.sleep(0.05)
    io.cancel()
    thread.join(1)
    assert not thread.is_alive()
    assert len(caught) == 1
    assert isinstance(caught[0], ZmodemAbort)


def test_write_callback_receives_data():
    written = []
    io = ZmodemIO(written.append)
    io.write(b"hello")
    assert written == [b"hello"]


def test_multithreaded_producer_consumer():
    io = ZmodemIO(lambda data: None)
    chunks = [bytes([i]) * 100 for i in range(10)]

    def producer():
        for chunk in chunks:
            io.feed(chunk)

    thread = threading.Thread(target=producer)
    thread.start()
    received = bytearray()
    while len(received) < 1000:
        received.extend(io.read(min(37, 1000 - len(received)), timeout=1))
    thread.join(1)
    assert bytes(received) == b"".join(chunks)
