"""Offscreen tests for the remembered-session sidebar auto-hide behavior."""

import os
import sys

import pytest

# The GUI tests only exercise widget logic; keep them headless.
os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

pytest.importorskip("PyQt5")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from PyQt5.QtCore import QPoint  # noqa: E402
from PyQt5.QtWidgets import QApplication  # noqa: E402

from COMTool import main2, parameters  # noqa: E402
from COMTool.widgets import EventFilter  # noqa: E402


@pytest.fixture(scope="module")
def app():
    return QApplication.instance() or QApplication([])


class FakeCursor:
    point = QPoint(0, 0)

    @classmethod
    def pos(cls):
        return cls.point


@pytest.fixture
def window(app, monkeypatch):
    FakeCursor.point = QPoint(0, 0)
    monkeypatch.setattr(main2, "QCursor", FakeCursor)
    monkeypatch.setattr(main2.SerialPortWatcher, "start", lambda self: None)

    config = parameters.Parameters()
    config["serialSessions"] = [
        {
            "port": "COM99",
            "name": "COM99  USB-Enhanced-SERIAL-A CH344",
            "serial": {"port": "COM99", "baudrate": 115200},
            "updated": 0,
        }
    ]
    config["serialSessionsMigrated"] = True
    config["sessionSidebar"] = {"visible": True, "width": 220}

    win = main2.MainWindow(app, EventFilter(), config)
    win.saveConfig = lambda: None
    win.resize(900, 560)
    win.show()
    app.processEvents()
    yield win
    win.portWatcher.stop()
    win.hide()
    win.deleteLater()
    app.processEvents()


def test_startup_shows_sidebar_and_manual_handle(window, app):
    assert window.sessionSidebar.isVisible()
    assert not window.sessionHoverStrip.isVisible()
    assert window.sessionHandle.isVisible()
    assert window.sessionsButton.isChecked()
    app.processEvents()


def test_auto_hide_keeps_saved_preference_and_left_edge_reopens(window, app):
    assert window.sessionSidebar.isVisible()
    assert window.config["sessionSidebar"]["visible"] is True

    # Mark the sidebar as visited, then move rightwards outside it.
    sidebar = window.sessionSidebar
    inside = sidebar.mapToGlobal(QPoint(10, 10))
    FakeCursor.point = inside
    window._session_hover_seen = True
    window._last_session_cursor_x = inside.x() - 5
    window._checkSessionSidebarAutoHide()
    assert sidebar.isVisible()

    outside = sidebar.mapToGlobal(QPoint(sidebar.width() + 80, 10))
    FakeCursor.point = outside
    window._last_session_cursor_x = outside.x() - 10
    window._checkSessionSidebarAutoHide()
    app.processEvents()
    assert not sidebar.isVisible()
    assert window.sessionHoverStrip.isVisible()
    # Auto-hide is transient; it must not change the saved preference.
    assert window.config["sessionSidebar"]["visible"] is True

    window.onSessionHoverStripEntered()
    app.processEvents()
    assert window.sessionSidebar.isVisible()
    assert not window.sessionHoverStrip.isVisible()
    assert window.sessionsButton.isChecked()

    window.setSessionsSidebarVisible(False)
    app.processEvents()
    assert not window.sessionSidebar.isVisible()
    assert window.sessionHoverStrip.isVisible()
    assert not window.sessionsButton.isChecked()
    assert window.config["sessionSidebar"]["visible"] is False


def test_leftward_or_vertical_movement_keeps_sidebar_open(window, app):
    assert window.sessionSidebar.isVisible()
    sidebar = window.sessionSidebar
    window._session_hover_seen = True
    outside = sidebar.mapToGlobal(QPoint(sidebar.width() + 40, 10))
    FakeCursor.point = outside
    window._last_session_cursor_x = outside.x() + 10
    window._checkSessionSidebarAutoHide()
    assert sidebar.isVisible()

    window._last_session_cursor_x = outside.x()
    window._checkSessionSidebarAutoHide()
    assert sidebar.isVisible()

    # Moving right while still outside the window is approaching the panel,
    # not leaving it on the right side.
    left_of_window = sidebar.mapToGlobal(QPoint(-20, 10))
    FakeCursor.point = left_of_window
    window._last_session_cursor_x = left_of_window.x() - 10
    window._checkSessionSidebarAutoHide()
    assert sidebar.isVisible()


def test_handle_toggles_manual_visibility(window, app):
    window.setSessionsSidebarVisible(False)
    app.processEvents()
    window.onSessionHandleClicked()
    app.processEvents()
    assert window.sessionSidebar.isVisible()
    assert window.config["sessionSidebar"]["visible"] is True

    window._session_shown_at = 0
    window.onSessionHandleClicked()
    app.processEvents()
    assert not window.sessionSidebar.isVisible()
    assert window.config["sessionSidebar"]["visible"] is False
