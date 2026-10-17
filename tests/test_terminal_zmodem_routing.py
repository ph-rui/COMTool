import os
import sys
import threading

import pytest

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

pytest.importorskip("PyQt5")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from PyQt5.QtCore import Qt  # noqa: E402
from PyQt5.QtWidgets import QApplication, QWidget  # noqa: E402

from COMTool.plugins import terminal as terminal_module  # noqa: E402
from COMTool.plugins.terminal import Plugin  # noqa: E402
from COMTool.zmodem.codec import (  # noqa: E402
    FRAME_ZRQINIT,
    FRAME_ZRINIT,
    encode_hex_header,
)


class SignalStub:
    def __init__(self):
        self.calls = []

    def emit(self, *args):
        self.calls.append(args)


@pytest.fixture(scope="module")
def app():
    application = QApplication.instance() or QApplication([])
    yield application


@pytest.fixture
def plugin(app):
    instance = Plugin()
    instance.onInit({})
    instance.send = lambda data: None
    instance.isConnected = lambda: False
    instance.ctrlConn = lambda *args: None
    instance.hintSignal = SignalStub()
    parent = QWidget()
    instance.statusBar = instance.onWidgetStatusBar(parent)
    instance.onWidgetMain(parent)
    instance.onUiInitDone()
    yield instance
    instance.onDel()
    parent.deleteLater()


def test_plain_data_goes_to_terminal_and_children(plugin, monkeypatch):
    terminal_data = []
    child_data = []
    monkeypatch.setattr(plugin.widget, "onReceived", terminal_data.append)

    class Child:
        def onReceived(self, data):
            child_data.append(data)

    plugin.connChilds = [Child()]
    plugin.onReceived(b"hello")

    assert terminal_data == [b"hello"]
    assert child_data == [b"hello"]


def test_zrqinit_starts_receive_without_leaking_binary(plugin, monkeypatch):
    started = []
    terminal_data = []
    monkeypatch.setattr(plugin.widget, "onReceived", terminal_data.append)
    monkeypatch.setattr(
        plugin, "_zmodem_start_receive", lambda detection: started.append(detection)
    )

    plugin.onReceived(b"boot\r" + encode_hex_header(FRAME_ZRQINIT))
    assert terminal_data == [b"boot\r"]
    assert len(started) == 1
    assert started[0].direction == "receive"


def test_zrinit_starts_pending_send(plugin, monkeypatch):
    started = []
    terminal_data = []
    monkeypatch.setattr(plugin.widget, "onReceived", terminal_data.append)
    monkeypatch.setattr(
        plugin, "_zmodem_start_pending_send", lambda detection: started.append(detection)
    )

    header = encode_hex_header(FRAME_ZRINIT, b"\0\0\0\x23")
    plugin.onReceived(b"rz\r" + header + b"after")
    assert terminal_data == [b"rz\r"]
    assert len(started) == 1
    assert started[0].direction == "send"
    assert started[0].remainder == b"after"


def test_invalid_header_is_terminal_data(plugin, monkeypatch):
    terminal_data = []
    started = []
    monkeypatch.setattr(plugin.widget, "onReceived", terminal_data.append)
    monkeypatch.setattr(
        plugin, "_zmodem_start_receive", lambda detection: started.append(detection)
    )
    header = bytearray(encode_hex_header(FRAME_ZRQINIT))
    header[8] = ord("f") if header[8] != ord("f") else ord("e")

    plugin.onReceived(bytes(header))
    assert started == []
    assert terminal_data == [bytes(header)]


def test_active_session_consumes_data(plugin):
    class FakeIO:
        def __init__(self):
            self.received = []

        def feed(self, data):
            self.received.append(data)

    class FakeSession:
        active = True

        def __init__(self):
            self.io = FakeIO()

        def cancel(self):
            pass

    session = FakeSession()
    with plugin._zmodem_lock:
        plugin._zmodem_session = session
    plugin.onReceived(b"\x00\x01binary")
    assert session.io.received == [b"\x00\x01binary"]


def test_inactive_session_falls_back_to_terminal(plugin, monkeypatch):
    terminal_data = []
    monkeypatch.setattr(plugin.widget, "onReceived", terminal_data.append)

    class FakeSession:
        active = False
        io = None

    with plugin._zmodem_lock:
        plugin._zmodem_session = FakeSession()
    plugin.onReceived(b"prompt$ ")
    assert terminal_data == [b"prompt$ "]
    with plugin._zmodem_lock:
        assert plugin._zmodem_session is None


def test_pending_send_ignores_data_until_files_chosen(plugin, monkeypatch):
    terminal_data = []
    monkeypatch.setattr(plugin.widget, "onReceived", terminal_data.append)
    with plugin._zmodem_lock:
        plugin._zmodem_pending = object()
    plugin.onReceived(b"duplicate ZRINIT")
    assert terminal_data == []


def test_keypress_is_ignored_during_transfer(plugin):
    terminal = plugin.widget.terminal
    sent = []
    terminal.send = sent.append
    terminal.setTransferActive(True)

    class Event:
        def __init__(self):
            self.accepted = False

        def text(self):
            return "a"

        def key(self):
            return Qt.Key_A

        def modifiers(self):
            return Qt.NoModifier

        def accept(self):
            self.accepted = True

    event = Event()
    terminal.keyPressEvent(event)
    assert sent == []
    assert event.accepted
