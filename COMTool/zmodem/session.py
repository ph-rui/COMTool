"""High-level ZMODEM session running in a worker thread."""

import threading
from typing import Callable, List, Optional, Sequence

from .io import ZmodemAbort, ZmodemIO
from .receiver import ReceivedFile, ZmodemReceiver
from .sender import ZmodemSender


class ZmodemSession:
    def __init__(
        self,
        direction: str,
        io: ZmodemIO,
        files: Optional[Sequence[str]] = None,
        output_dir: Optional[str] = None,
        initial_zrinit=None,
        on_file_start: Optional[Callable] = None,
        on_progress: Optional[Callable] = None,
        on_file_complete: Optional[Callable] = None,
        on_complete: Optional[Callable] = None,
        on_error: Optional[Callable[[BaseException], None]] = None,
        on_cancelled: Optional[Callable[[], None]] = None,
        header_timeout: float = 30.0,
        data_timeout: float = 30.0,
    ):
        if direction not in ("receive", "send"):
            raise ValueError("direction must be receive or send")
        self.direction = direction
        self.io = io
        self.files = list(files or [])
        self.output_dir = output_dir
        self.initial_zrinit = initial_zrinit
        self.on_file_start = on_file_start
        self.on_progress = on_progress
        self.on_file_complete = on_file_complete
        self.on_complete = on_complete
        self.on_error = on_error
        self.on_cancelled = on_cancelled
        self.header_timeout = header_timeout
        self.data_timeout = data_timeout
        self._thread = None
        self._cancelled = False
        self._lock = threading.Lock()
        self.received_files: List[ReceivedFile] = []
        self.trailing = b""

    @property
    def cancelled(self) -> bool:
        with self._lock:
            return self._cancelled

    @property
    def active(self) -> bool:
        return self._thread is not None and self._thread.is_alive()

    def start(self):
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._thread.start()
        return self._thread

    def join(self, timeout: float = None):
        if self._thread is not None:
            self._thread.join(timeout)

    def cancel(self):
        with self._lock:
            if self._cancelled:
                return
            self._cancelled = True

        def _abort():
            try:
                self.io.write(b"\x18" * 8 + b"\x08" * 8)
            except Exception:
                pass
            finally:
                self.io.cancel()

        # Do not block the UI thread while the writer waits for serial I/O.
        threading.Thread(target=_abort, daemon=True).start()

    def _run(self):
        try:
            if self.direction == "receive":
                receiver = ZmodemReceiver(
                    self.io,
                    self.output_dir,
                    on_file_start=self.on_file_start,
                    on_progress=self.on_progress,
                    on_file_complete=self.on_file_complete,
                    header_timeout=self.header_timeout,
                    data_timeout=self.data_timeout,
                )
                self.received_files = receiver.run()
                self.trailing = receiver.reader.drain()
                if self.on_complete is not None:
                    self.on_complete(self.received_files, self.trailing)
            else:
                sender = ZmodemSender(
                    self.io,
                    self.files,
                    initial_zrinit=self.initial_zrinit,
                    on_file_start=self.on_file_start,
                    on_progress=self.on_progress,
                    on_file_complete=self.on_file_complete,
                    header_timeout=self.header_timeout,
                    data_timeout=self.data_timeout,
                    chunk_size=1024,
                )
                self.trailing = sender.run()
                if self.on_complete is not None:
                    self.on_complete(None, self.trailing)
        except ZmodemAbort:
            if self.cancelled:
                if self.on_cancelled is not None:
                    self.on_cancelled()
            elif self.on_error is not None:
                self.on_error(ZmodemAbort("ZMODEM transfer aborted"))
        except BaseException as error:  # noqa: BLE001 - propagated to the UI
            if self.cancelled:
                if self.on_cancelled is not None:
                    self.on_cancelled()
            elif self.on_error is not None:
                self.on_error(error)
