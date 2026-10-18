import os
import sys

import pytest

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

pytest.importorskip("PyQt5")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from PyQt5.QtWidgets import QApplication, QFileDialog, QWidget  # noqa: E402

from COMTool.conn.base import ConnectionStatus  # noqa: E402
from COMTool.plugins.terminal import Plugin  # noqa: E402
from COMTool.zmodem.receiver import ReceivedFile  # noqa: E402


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
    instance.sent = []
    instance.send = instance.sent.append
    instance.isConnected = lambda: False
    instance.ctrlConn = lambda *args: None
    instance.connCallbak = lambda *args: None
    instance.hintSignal = SignalStub()
    parent = QWidget()
    instance.statusBar = instance.onWidgetStatusBar(parent)
    instance.onWidgetMain(parent)
    instance.onUiInitDone()
    yield instance
    instance.onDel()
    parent.deleteLater()


def test_transfer_controls_start_hidden(plugin):
    assert plugin.zmodemLabel.isHidden()
    assert plugin.zmodemBar.isHidden()
    assert plugin.zmodemCancel.isHidden()


def test_progress_updates_bar_and_label(plugin):
    plugin._zmodem_on_file_start("file.bin", 1024)
    plugin._zmodem_on_progress("file.bin", 512, 1024)

    assert not plugin.zmodemLabel.isHidden()
    assert plugin.zmodemLabel.text() == "file.bin"
    assert plugin.zmodemBar.maximum() == 1024
    assert plugin.zmodemBar.value() == 512


def test_send_overall_progress_across_files(plugin):
    plugin._zmodem_mode = "send"
    plugin._zmodem_send_count = 2
    plugin._zmodem_send_total = 3000

    plugin._zmodem_on_file_start("a.bin", 1000)
    plugin._zmodem_on_progress("a.bin", 500, 1000)

    assert plugin.zmodemBar.maximum() == 3000
    assert plugin.zmodemBar.value() == 500
    assert "a.bin" in plugin.zmodemLabel.text()
    assert "1/2" in plugin.zmodemLabel.text()
    assert "16%" in plugin.zmodemLabel.text()

    plugin._zmodem_on_file_start("b.bin", 2000)
    assert plugin.zmodemBar.value() == 1000

    plugin._zmodem_on_progress("b.bin", 1000, 2000)
    assert plugin.zmodemBar.value() == 2000
    assert "b.bin" in plugin.zmodemLabel.text()
    assert "2/2" in plugin.zmodemLabel.text()
    assert "66%" in plugin.zmodemLabel.text()


def test_receive_progress_shows_file_count_and_bytes(plugin):
    plugin._zmodem_mode = "receive"

    plugin._zmodem_on_file_start("a.bin", 1000)
    plugin._zmodem_on_progress("a.bin", 500, 1000)

    assert plugin.zmodemBar.maximum() == 1000
    assert plugin.zmodemBar.value() == 500
    assert "[1]" in plugin.zmodemLabel.text()
    assert "500 B" in plugin.zmodemLabel.text()
    assert "50%" in plugin.zmodemLabel.text()

    plugin._zmodem_on_file_start("b.bin", 2000)
    assert plugin.zmodemBar.maximum() == 2000
    assert plugin.zmodemBar.value() == 0
    assert "[2]" in plugin.zmodemLabel.text()
    assert "1000 B" in plugin.zmodemLabel.text()

    plugin._zmodem_on_progress("b.bin", 1000, 2000)
    assert plugin.zmodemBar.value() == 1000
    assert "2.0 KB" in plugin.zmodemLabel.text()


def test_busy_updates_status_message(plugin, app):
    plugin._zmodem_set_busy("Sending...")
    app.processEvents()

    assert "Sending..." in plugin.statusBar.msg.text()


def test_complete_moves_received_file(plugin, tmp_path, monkeypatch):
    temp_dir = tmp_path / "temp"
    temp_dir.mkdir()
    source = temp_dir / "received.bin"
    source.write_bytes(b"payload")
    target = tmp_path / "saved.bin"
    monkeypatch.setattr(
        QFileDialog,
        "getSaveFileName",
        lambda *args, **kwargs: (str(target), ""),
    )
    plugin._zmodem_temp_dir = str(temp_dir)
    plugin.widget.terminal.setTransferActive(True)

    plugin._zmodem_on_complete(
        [ReceivedFile("received.bin", str(source), 7, 7)], b""
    )

    assert target.read_bytes() == b"payload"
    assert not temp_dir.exists()
    assert not plugin.widget.terminal.transferActive
    assert plugin.zmodemLabel.isHidden()


def test_complete_cancel_deletes_temporary_file(plugin, tmp_path, monkeypatch):
    temp_dir = tmp_path / "temp"
    temp_dir.mkdir()
    source = temp_dir / "received.bin"
    source.write_bytes(b"payload")
    monkeypatch.setattr(
        QFileDialog,
        "getSaveFileName",
        lambda *args, **kwargs: ("", ""),
    )
    plugin._zmodem_temp_dir = str(temp_dir)

    plugin._zmodem_on_complete(
        [ReceivedFile("received.bin", str(source), 7, 7)], b""
    )

    assert not source.exists()
    assert not temp_dir.exists()


def test_cancel_pending_send_sends_can(plugin, monkeypatch):
    monkeypatch.setattr(
        QFileDialog,
        "getOpenFileNames",
        lambda *args, **kwargs: ([], ""),
    )
    plugin._zmodem_pending = object()
    plugin._zmodem_pending_buffer = b"buffered"
    plugin.widget.terminal.setTransferActive(True)

    plugin._zmodem_choose_send_files()

    assert plugin._zmodem_pending is None
    assert plugin._zmodem_pending_buffer == b""
    assert any(b"\x18" in data for data in plugin.sent)
    assert not plugin.widget.terminal.transferActive
    assert plugin.zmodemLabel.isHidden()


def test_complete_truncates_huge_trailing(plugin):
    fed = []
    plugin._feed_terminal = fed.append

    plugin._zmodem_on_complete(None, b"x" * 100000)

    assert len(fed[0]) == 4096


def test_error_purges_queued_serial_output(plugin):
    calls = []
    plugin.ctrlConn = lambda key, value: calls.append((key, value))

    plugin._zmodem_on_error("boom")

    assert ("purge", None) in calls


def test_cancel_pending_send_purges_before_can(plugin):
    calls = []
    plugin.ctrlConn = lambda key, value: calls.append((key, value))
    plugin._zmodem_pending = object()

    plugin._zmodem_clear_pending(cancel_remote=True)

    assert calls and calls[0] == ("purge", None)


def test_error_restores_terminal_and_reports(plugin):
    plugin.widget.terminal.setTransferActive(True)
    plugin._zmodem_set_busy("Receiving...")

    plugin._zmodem_on_error("boom")

    assert not plugin.widget.terminal.transferActive
    assert plugin.zmodemLabel.isHidden()
    assert plugin.hintSignal.calls[-1][0] == "error"
    assert "boom" in plugin.hintSignal.calls[-1][2]


def test_disconnect_aborts_pending_transfer(plugin):
    plugin._zmodem_pending = object()
    plugin.widget.terminal.setTransferActive(True)

    plugin.onConnChanged(ConnectionStatus.CLOSED, "closed")

    assert plugin._zmodem_pending is None
    assert not plugin.widget.terminal.transferActive
