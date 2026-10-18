import pytest

from COMTool.zmodem.io import ZmodemAbort, ZmodemIO, ZmodemTimeout
from COMTool.zmodem.reader import ZmodemReader


class ScriptedIO(ZmodemIO):
    def __init__(self, chunks):
        super().__init__(lambda data: None)
        self.chunks = list(chunks)

    def read_some(self, size, timeout=None):
        if not self.chunks:
            raise ZmodemTimeout("no more data")
        return self.chunks.pop(0)


def test_reader_detects_eight_can_cancel():
    io = ZmodemIO(lambda data: None)
    io.feed(b"\x18" * 8)
    reader = ZmodemReader(io, read_timeout=0.1)

    with pytest.raises(ZmodemAbort):
        reader.read_header(timeout=0.1)


def test_reader_detects_cancel_split_across_reads():
    reader = ZmodemReader(
        ScriptedIO([b"\x18" * 4, b"\x18" * 4]), read_timeout=0.1
    )

    with pytest.raises(ZmodemAbort):
        reader.read_header(timeout=0.1)


def test_reader_ignores_single_can_byte():
    io = ZmodemIO(lambda data: None)
    io.feed(b"\x18")
    reader = ZmodemReader(io, read_timeout=0.1)

    with pytest.raises(ZmodemTimeout):
        reader.read_header(timeout=0.1)
