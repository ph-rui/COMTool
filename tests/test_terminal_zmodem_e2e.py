import hashlib
import os
import sys
import time

import pytest

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

pytest.importorskip("PyQt5")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from PyQt5.QtWidgets import QApplication, QFileDialog, QWidget  # noqa: E402

from COMTool.plugins.terminal import Plugin  # noqa: E402
from tests.zmodem_peer import LrzszPeer  # noqa: E402


class PluginSink:
    def __init__(self, callback):
        self.callback = callback

    def feed(self, data):
        self.callback(data)


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
def peer():
    instance = LrzszPeer()
    if not instance.available:
        pytest.skip("lrzsz not available; set COMTOOL_LRZSZ_DIR")
    try:
        yield instance
    finally:
        instance.close()


@pytest.fixture
def plugin(app):
    instance = Plugin()
    instance.onInit({})
    instance.send = lambda data: None
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


def _sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(65536), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _wait_until(app, predicate, timeout=20.0):
    deadline = time.time() + timeout
    while time.time() < deadline:
        app.processEvents()
        if predicate():
            return True
        time.sleep(0.01)
    app.processEvents()
    return predicate()


def test_plugin_auto_receives_from_lsz(
    app, plugin, peer, tmp_path, monkeypatch
):
    source = tmp_path / "incoming.bin"
    source.write_bytes(os.urandom(128 * 1024))
    target = tmp_path / "saved.bin"
    monkeypatch.setattr(
        QFileDialog,
        "getSaveFileName",
        lambda *args, **kwargs: (str(target), ""),
    )

    plugin.send = peer._write
    peer.start_sender(
        [str(source)],
        cwd=str(tmp_path),
        io_factory=lambda _write: PluginSink(plugin.onReceived),
    )

    assert _wait_until(app, target.exists, timeout=20), peer.stderr_lines
    assert peer.wait(timeout=20) == 0
    assert _sha256(str(target)) == _sha256(str(source))


def test_plugin_auto_sends_after_lrz_starts(
    app, plugin, peer, tmp_path, monkeypatch
):
    source = tmp_path / "outgoing.bin"
    source.write_bytes(os.urandom(128 * 1024))
    output = tmp_path / "output"
    output.mkdir()
    monkeypatch.setattr(
        QFileDialog,
        "getOpenFileNames",
        lambda *args, **kwargs: ([str(source)], ""),
    )

    plugin.send = peer._write
    peer.start_receiver(
        cwd=str(output),
        io_factory=lambda _write: PluginSink(plugin.onReceived),
    )

    assert _wait_until(
        app, lambda: (output / "outgoing.bin").exists(), timeout=20
    ), peer.stderr_lines
    assert peer.wait(timeout=20) == 0
    assert _sha256(str(output / "outgoing.bin")) == _sha256(str(source))
