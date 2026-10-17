"""Test helper for running lrzsz as the remote ZMODEM peer."""

import os
import shutil
import subprocess
import threading
from typing import List, Optional

from COMTool.zmodem.io import ZmodemIO


def _find_tool(directory: Optional[str], names: List[str]) -> Optional[str]:
    candidates = []
    if directory:
        candidates.extend(os.path.join(directory, name) for name in names)
    candidates.extend(shutil.which(name) or "" for name in names)
    for candidate in candidates:
        if candidate and os.path.isfile(candidate) and os.access(candidate, os.X_OK):
            return candidate
    return None


class LrzszPeer:
    def __init__(self):
        directory = os.environ.get("COMTOOL_LRZSZ_DIR")
        self.send_tool = _find_tool(directory, ["sz", "sz.exe", "lsz", "lsz.exe"])
        self.receive_tool = _find_tool(directory, ["rz", "rz.exe", "lrz", "lrz.exe"])
        self.process = None
        self.io = None
        self._stdout_thread = None
        self._stderr_thread = None
        self.stderr_lines = []

    @property
    def available(self) -> bool:
        return self.send_tool is not None and self.receive_tool is not None

    def _start(self, command, cwd, io_factory=None):
        self.process = subprocess.Popen(
            command,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            cwd=cwd,
            bufsize=0,
        )
        self.io = (
            io_factory(self._write)
            if io_factory is not None
            else ZmodemIO(self._write)
        )

        self._stdout_thread = threading.Thread(target=self._read_stdout, daemon=True)
        self._stdout_thread.start()
        self._stderr_thread = threading.Thread(target=self._read_stderr, daemon=True)
        self._stderr_thread.start()

    def start_sender(
        self,
        files: List[str],
        cwd: Optional[str] = None,
        io_factory=None,
        extra_args: Optional[List[str]] = None,
    ):
        assert self.send_tool is not None
        command = (
            [self.send_tool, "-b", "-q"]
            + list(extra_args or [])
            + list(files)
        )
        self._start(command, cwd, io_factory=io_factory)

    def start_receiver(
        self, cwd: Optional[str] = None, io_factory=None, extra_args=None
    ):
        assert self.receive_tool is not None
        command = (
            [self.receive_tool, "-y", "-b", "-q"] + list(extra_args or [])
        )
        self._start(command, cwd, io_factory=io_factory)

    def _write(self, data: bytes):
        assert self.process is not None and self.process.stdin is not None
        self.process.stdin.write(data)
        self.process.stdin.flush()

    def _read_stdout(self):
        assert self.process is not None and self.process.stdout is not None
        while True:
            data = os.read(self.process.stdout.fileno(), 4096)
            if not data:
                return
            if self.io is not None:
                self.io.feed(data)

    def _read_stderr(self):
        assert self.process is not None and self.process.stderr is not None
        while True:
            line = self.process.stderr.readline()
            if not line:
                return
            self.stderr_lines.append(line.decode("utf-8", "replace"))

    def wait(self, timeout: float = 20.0):
        assert self.process is not None
        try:
            return self.process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait(timeout=5)
            raise

    def close(self):
        if self.process is not None and self.process.poll() is None:
            self.process.kill()
            self.process.wait(timeout=5)
        for stream in (
            getattr(self.process, "stdin", None),
            getattr(self.process, "stdout", None),
            getattr(self.process, "stderr", None),
        ):
            if stream is not None:
                try:
                    stream.close()
                except OSError:
                    pass
