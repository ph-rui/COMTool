"""Offscreen integration test for opening a remembered SSH session."""

import os
import sys

import pytest

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

pytest.importorskip("PyQt5")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from PyQt5.QtWidgets import QApplication  # noqa: E402

from COMTool import main2, parameters  # noqa: E402
from COMTool.widgets import EventFilter  # noqa: E402


@pytest.fixture(scope="module")
def app():
    return QApplication.instance() or QApplication([])


@pytest.fixture
def window(app, monkeypatch):
    monkeypatch.setattr(main2.SerialPortWatcher, "start", lambda self: None)
    monkeypatch.setattr(main2.SerialPortWatcher, "stop", lambda self: None)

    config = parameters.Parameters()
    config["serialSessions"] = []
    config["serialSessionsMigrated"] = True
    config["sshSessions"] = [
        {
            "key": "root@10.0.0.1:22",
            "host": "10.0.0.1",
            "port": 22,
            "user": "root",
            "alias": "board",
            "ssh_key": "",
            "updated": 0,
        }
    ]
    config["sshSessionsMigrated"] = True
    config["sessionSidebar"] = {"visible": True, "width": 220}

    win = main2.MainWindow(app, EventFilter(), config)
    win.saveConfig = lambda: None
    win.resize(900, 560)
    win.show()
    app.processEvents()
    yield win
    for item in win.items:
        if getattr(item, "settingsDialog", None) is not None:
            item.settingsDialog.close()
    win.hide()
    win.deleteLater()
    app.processEvents()


def test_clicking_ssh_session_creates_tab_and_opens_password_settings(window, app):
    window.onSessionActivated("ssh:root@10.0.0.1:22")
    app.processEvents()

    assert len(window.items) == 1
    item = window.items[0]
    assert item.isSshConnection()
    assert item.getDisplayName() == "board"
    assert window.tabWidget.tabText(0) == "board"

    dialog = item.settingsDialog
    assert dialog is not None
    assert dialog.isVisible()
    conn = item.currentConnection()
    assert conn.nameInput.text() == "board"
    assert conn.hostInput.text() == "10.0.0.1"
    assert conn.userInput.text() == "root"

    # Selecting the same remembered session again reuses the tab.
    window.onSessionActivated("ssh:root@10.0.0.1:22")
    app.processEvents()
    assert len(window.items) == 1


def test_cached_password_reconnects_without_settings(window, app, monkeypatch):
    opened = []

    def fake_open(item, password=None):
        opened.append(password)

    monkeypatch.setattr(main2.PluginItem, "openConnection", fake_open)
    window._ssh_password_cache["root@10.0.0.1:22"] = "secret"

    window.onSessionActivated("ssh:root@10.0.0.1:22")
    app.processEvents()

    assert len(window.items) == 1
    item = window.items[0]
    dialog = getattr(item, "settingsDialog", None)
    assert dialog is None or not dialog.isVisible()
    assert opened == ["secret"]


def test_reselecting_current_ssh_session_focuses_terminal(window, app, monkeypatch):
    opened = []

    def fake_open(item, password=None):
        opened.append(password)

    monkeypatch.setattr(main2.PluginItem, "openConnection", fake_open)
    window._ssh_password_cache["root@10.0.0.1:22"] = "secret"

    window.onSessionActivated("ssh:root@10.0.0.1:22")
    app.processEvents()
    item = window.items[0]
    terminal = item.plugin.widget.terminal

    # Clicking the already-current row leaves focus in the sidebar.  The
    # activation path must hand it back to the terminal.
    window.sessionSidebar.listWidget.setFocus()
    assert QApplication.focusWidget() is window.sessionSidebar.listWidget

    window.onSessionActivated("ssh:root@10.0.0.1:22")
    app.processEvents()

    assert opened == ["secret", "secret"]
    assert QApplication.focusWidget() is terminal


def test_saved_encrypted_password_reconnects_after_restart(window, app, monkeypatch):
    opened = []

    def fake_open(item, password=None):
        opened.append(password)

    monkeypatch.setattr(main2.PluginItem, "openConnection", fake_open)
    monkeypatch.setattr(
        main2,
        "unprotect_password",
        lambda token: "secret" if token == "encrypted-token" else "",
    )
    window.sshSessionStore.upsert(
        "10.0.0.1", 22, "root", alias="board",
        passwd_enc="encrypted-token",
    )
    window._ssh_password_cache.clear()

    window.onSessionActivated("ssh:root@10.0.0.1:22")
    app.processEvents()

    assert len(window.items) == 1
    item = window.items[0]
    dialog = getattr(item, "settingsDialog", None)
    assert dialog is None or not dialog.isVisible()
    assert opened == ["secret"]
