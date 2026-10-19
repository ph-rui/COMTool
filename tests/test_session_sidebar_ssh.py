"""Offscreen tests for the combined serial/SSH session sidebar."""

import os
import sys

import pytest

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

pytest.importorskip("PyQt5")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from PyQt5.QtCore import Qt  # noqa: E402
from PyQt5.QtWidgets import QApplication  # noqa: E402

from COMTool.session_sidebar import SessionSidebar, SessionItemWidget  # noqa: E402


@pytest.fixture(scope="module")
def app():
    return QApplication.instance() or QApplication([])


def _serial():
    return {
        "port": "COM7",
        "name": "COM7  USB Serial",
        "serial": {"port": "COM7"},
    }


def _ssh():
    return {
        "key": "root@10.0.0.1:22",
        "host": "10.0.0.1",
        "port": 22,
        "user": "root",
        "alias": "board",
        "ssh_key": "",
    }


def test_mixed_sessions_show_sections_and_ssh_prefix(app):
    sidebar = SessionSidebar()
    activated = []
    sidebar.sessionActivated.connect(activated.append)

    sidebar.set_sessions(
        [_serial()],
        {"COM7"},
        "COM7",
        [_ssh()],
        set(),
        "root@10.0.0.1:22",
    )

    assert sidebar.listWidget.count() == 4
    assert sidebar.listWidget.item(0).data(Qt.UserRole) is None
    assert sidebar.listWidget.item(0).text().strip() == "Serial"
    assert sidebar.listWidget.item(1).data(Qt.UserRole) == "COM7"
    assert sidebar.listWidget.item(2).data(Qt.UserRole) is None
    assert sidebar.listWidget.item(2).text().strip() == "SSH"
    assert sidebar.listWidget.item(3).data(Qt.UserRole) == "ssh:root@10.0.0.1:22"

    serial_row = sidebar.listWidget.itemWidget(sidebar.listWidget.item(1))
    ssh_row = sidebar.listWidget.itemWidget(sidebar.listWidget.item(3))
    assert isinstance(serial_row, SessionItemWidget)
    assert isinstance(ssh_row, SessionItemWidget)
    assert serial_row.nameLabel.text() == "COM7"
    assert serial_row.detailLabel.text() == "(USB Serial)"
    assert ssh_row.nameLabel.text() == "board"
    assert ssh_row.detailLabel.text() == "(root@10.0.0.1:22)"

    sidebar._onItemClicked(sidebar.listWidget.item(3))
    assert activated == ["ssh:root@10.0.0.1:22"]
    sidebar.deleteLater()


def test_only_serial_keeps_current_single_list_layout(app):
    sidebar = SessionSidebar()
    sidebar.set_sessions([_serial()], {"COM7"}, "COM7")
    assert sidebar.listWidget.count() == 1
    assert sidebar.listWidget.item(0).data(Qt.UserRole) == "COM7"
    sidebar.deleteLater()


def test_only_ssh_uses_ssh_section(app):
    sidebar = SessionSidebar()
    sidebar.set_sessions([], [], None, [_ssh()], {"root@10.0.0.1:22"}, None)
    assert sidebar.listWidget.count() == 2
    assert sidebar.listWidget.item(0).text().strip() == "SSH"
    assert sidebar.listWidget.item(1).data(Qt.UserRole) == "ssh:root@10.0.0.1:22"
    sidebar.deleteLater()


def test_empty_state(app):
    sidebar = SessionSidebar()
    sidebar.set_sessions([], [], None, [], set(), None)
    assert sidebar.listWidget.count() == 1
    assert "No saved sessions" in sidebar.listWidget.item(0).text()
    sidebar.deleteLater()


def test_unchanged_sessions_do_not_rebuild_rows(app):
    sidebar = SessionSidebar()
    args = ([_serial()], {"COM7"}, "COM7", [_ssh()], set(), None)
    sidebar.set_sessions(*args)
    first = sidebar.listWidget.itemWidget(sidebar.listWidget.item(1))

    sidebar.set_sessions(*args)
    second = sidebar.listWidget.itemWidget(sidebar.listWidget.item(1))

    assert second is first
    sidebar.deleteLater()
