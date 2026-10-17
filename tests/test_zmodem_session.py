import threading
import time

import pytest

from COMTool.zmodem.io import ZmodemIO
from COMTool.zmodem.session import ZmodemSession


def test_cancel_sends_can_and_calls_cancelled(tmp_path):
    writes = []
    io = ZmodemIO(writes.append)
    cancelled = threading.Event()
    errors = []

    session = ZmodemSession(
        "receive",
        io,
        output_dir=str(tmp_path),
        on_cancelled=cancelled.set,
        on_error=errors.append,
        header_timeout=5,
    )
    session.start()
    time.sleep(0.05)
    session.cancel()
    assert cancelled.wait(2)
    session.join(2)
    assert errors == []
    assert any(b"\x18\x18" in data for data in writes)


def test_timeout_reports_error(tmp_path):
    io = ZmodemIO(lambda data: None)
    errors = []
    session = ZmodemSession(
        "receive",
        io,
        output_dir=str(tmp_path),
        on_error=errors.append,
        header_timeout=0.05,
        data_timeout=0.05,
    )
    session.start()
    session.join(2)
    assert len(errors) == 1
    assert "timed out" in str(errors[0]).lower()


def test_invalid_direction_rejected(tmp_path):
    with pytest.raises(ValueError):
        ZmodemSession("bogus", ZmodemIO(lambda data: None), output_dir=str(tmp_path))
