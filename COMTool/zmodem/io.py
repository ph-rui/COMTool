"""Thread-safe byte IO used by the ZMODEM protocol engines."""

import threading
import time
from typing import Callable


class ZmodemError(Exception):
    pass


class ZmodemTimeout(ZmodemError):
    pass


class ZmodemAbort(ZmodemError):
    pass


class ZmodemIO:
    """A blocking byte queue fed by the serial receive thread."""

    def __init__(self, write_callback: Callable[[bytes], None]):
        self._write_callback = write_callback
        self._condition = threading.Condition()
        self._buffer = bytearray()
        self._cancelled = False

    @property
    def cancelled(self) -> bool:
        with self._condition:
            return self._cancelled

    def feed(self, data: bytes) -> None:
        if not data:
            return
        with self._condition:
            if self._cancelled:
                return
            self._buffer.extend(data)
            self._condition.notify_all()

    def read(self, size: int, timeout: float = None) -> bytes:
        if size <= 0:
            return b""
        if timeout is None:
            deadline = None
        else:
            deadline = time.monotonic() + timeout

        with self._condition:
            while len(self._buffer) < size:
                if self._cancelled:
                    raise ZmodemAbort("ZMODEM transfer cancelled")
                if deadline is None:
                    self._condition.wait()
                    continue
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    raise ZmodemTimeout("timed out waiting for ZMODEM data")
                self._condition.wait(remaining)

            data = bytes(self._buffer[:size])
            del self._buffer[:size]
            return data

    def write(self, data: bytes) -> None:
        if not data:
            return
        with self._condition:
            if self._cancelled:
                raise ZmodemAbort("ZMODEM transfer cancelled")
        self._write_callback(data)

    def cancel(self) -> None:
        with self._condition:
            self._cancelled = True
            self._condition.notify_all()
