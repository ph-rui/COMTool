import os
import sys
import time

import pytest

# The GUI tests only exercise the widget/model logic; keep them headless.
os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

pytest.importorskip("PyQt5")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from PyQt5.QtCore import Qt  # noqa: E402
from PyQt5.QtWidgets import QApplication  # noqa: E402

from COMTool.conn import conn_serial  # noqa: E402
from COMTool.conn.port_utils import PortInfo  # noqa: E402


@pytest.fixture(scope="module")
def app():
    application = QApplication.instance() or QApplication([])
    yield application


def wait_for_scan(serial_conn, app, timeout=3.0):
    deadline = time.time() + timeout
    while serial_conn._port_scan_in_flight and time.time() < deadline:
        app.processEvents()
        time.sleep(0.01)
    app.processEvents()


def make_serial(app, monkeypatch, ports):
    monkeypatch.setattr(conn_serial, "collect_ports", lambda: list(ports))
    serial_conn = conn_serial.Serial()
    serial_conn.onInit({})
    serial_conn.onWidget()
    serial_conn.onUiInitDone()
    wait_for_scan(serial_conn, app)
    return serial_conn


def test_scan_populates_and_selects_first(app, monkeypatch):
    serial_conn = make_serial(
        app,
        monkeypatch,
        [
            PortInfo(device="COM2", description="USB Serial"),
            PortInfo(device="COM10", description="USB Serial"),
        ],
    )
    try:
        combo = serial_conn.serialPortCombobox
        assert combo.count() == 2
        assert combo.itemText(0) == "COM2  USB Serial"
        assert combo.itemText(1) == "COM10  USB Serial"
        assert combo.itemData(0, Qt.UserRole) == "COM2"
        assert serial_conn.config["port"] == "COM2"
        assert serial_conn.com.port == "COM2"
    finally:
        serial_conn.onDel()


def test_refresh_preserves_selected_device(app, monkeypatch):
    serial_conn = make_serial(
        app,
        monkeypatch,
        [
            PortInfo(device="COM2", description="USB Serial"),
            PortInfo(device="COM10", description="USB Serial"),
        ],
    )
    try:
        combo = serial_conn.serialPortCombobox
        combo.setCurrentIndex(1)  # user selects COM10
        assert serial_conn.config["port"] == "COM10"

        monkeypatch.setattr(
            conn_serial,
            "collect_ports",
            lambda: [
                PortInfo(device="COM5", description="USB Serial"),
                PortInfo(device="COM10", description="USB Serial"),
            ],
        )
        serial_conn.requestPortScan()
        wait_for_scan(serial_conn, app)

        assert combo.currentData(Qt.UserRole) == "COM10"
        assert serial_conn.config["port"] == "COM10"
        assert combo.count() == 2
    finally:
        serial_conn.onDel()


def test_refresh_keeps_config_when_selected_port_disappears(app, monkeypatch):
    serial_conn = make_serial(
        app, monkeypatch, [PortInfo(device="COM2", description="USB Serial")]
    )
    try:
        combo = serial_conn.serialPortCombobox
        assert serial_conn.config["port"] == "COM2"

        monkeypatch.setattr(
            conn_serial,
            "collect_ports",
            lambda: [PortInfo(device="COM5", description="CH344")],
        )
        serial_conn.requestPortScan()
        wait_for_scan(serial_conn, app)

        assert combo.currentIndex() == -1
        assert serial_conn.config["port"] == "COM2"
        assert serial_conn.com.port == "COM2"

        monkeypatch.setattr(
            conn_serial,
            "collect_ports",
            lambda: [
                PortInfo(device="COM5", description="CH344"),
                PortInfo(device="COM2", description="USB Serial"),
            ],
        )
        serial_conn.requestPortScan()
        wait_for_scan(serial_conn, app)

        assert combo.currentData(Qt.UserRole) == "COM2"
    finally:
        serial_conn.onDel()


def test_empty_scan_shows_disabled_placeholder(app, monkeypatch):
    serial_conn = make_serial(app, monkeypatch, [])
    try:
        combo = serial_conn.serialPortCombobox
        assert combo.count() == 1
        assert combo.itemText(0) == "No serial port found"
        assert combo.currentIndex() == -1
        assert not combo.model().item(0).isEnabled()
        assert serial_conn.config["port"] is None
    finally:
        serial_conn.onDel()


def test_popup_signals_start_and_stop_timer(app, monkeypatch):
    serial_conn = make_serial(app, monkeypatch, [])
    try:
        timer = serial_conn._port_refresh_timer
        assert not timer.isActive()

        serial_conn.serialPortCombobox.popupShown.emit()
        assert timer.isActive()

        serial_conn.serialPortCombobox.popupHidden.emit()
        assert not timer.isActive()
    finally:
        serial_conn.onDel()
