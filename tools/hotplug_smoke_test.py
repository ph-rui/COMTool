"""Interactive serial hot-plug smoke test.

Run this on the machine that has the USB-serial adapter:

    python tools/hotplug_smoke_test.py

The script opens COMTool's real serial settings panel and automatically opens
the port dropdown. It then asks you to plug in a USB-serial device and checks
that the device appears in the already-open list within a few seconds. Finally
it asks you to unplug the device and checks that it disappears.

Requires PyQt5 and pyserial (the normal COMTool runtime dependencies).
Exit code: 0 = pass, 1 = assertion failure, 2 = timeout.
"""

import os
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from PyQt5.QtCore import Qt, QTimer
from PyQt5.QtWidgets import QApplication, QLabel, QVBoxLayout, QWidget

from COMTool.conn.conn_serial import Serial
from COMTool.conn.port_utils import collect_ports


PHASE_TIMEOUT_S = 40
POLL_INTERVAL_MS = 200


def devices():
    return {port.device for port in collect_ports()}


def combo_devices(serial_conn):
    combo = serial_conn.serialPortCombobox
    values = set()
    for index in range(combo.count()):
        device = combo.itemData(index, Qt.UserRole)
        if device:
            values.add(device)
    return values


def main():
    app = QApplication(sys.argv)

    serial_conn = Serial()
    serial_conn.onInit({})
    panel = serial_conn.onWidget()
    serial_conn.onUiInitDone()

    window = QWidget()
    window.setWindowTitle("COMTool hot-plug smoke test")
    layout = QVBoxLayout(window)
    hint = QLabel()
    hint.setWordWrap(True)
    layout.addWidget(hint)
    layout.addWidget(panel)
    window.resize(720, 260)
    window.show()

    initial = devices()
    hint.setText(
        "步骤 1/2：保持串口下拉框展开，然后插入 USB 串口设备。\n"
        "当前已有设备：{}".format(", ".join(sorted(initial)) or "无")
    )
    print("== initial devices: {}".format(", ".join(sorted(initial)) or "(none)"))
    print("== keep the port dropdown open and plug in a USB-serial device...")

    state = {
        "phase": "insert",
        "inserted": set(),
        "phase_started": time.time(),
        "result": None,
    }

    def fail(message):
        state["result"] = ("FAIL", message)
        print("FAIL:", message)
        app.quit()

    def pass_test(message):
        state["result"] = ("PASS", message)
        print("PASS:", message)
        app.quit()

    def tick():
        if state["result"] is not None:
            return
        if time.time() - state["phase_started"] > PHASE_TIMEOUT_S:
            fail("phase '{}' timed out after {}s".format(
                state["phase"], PHASE_TIMEOUT_S))
            return

        current = devices()
        shown = combo_devices(serial_conn)

        if state["phase"] == "insert":
            new_devices = current - initial
            if not new_devices:
                return
            state["inserted"] = new_devices
            popup_visible = serial_conn.serialPortCombobox._popup_visible
            print("== detected new device(s): {}".format(
                ", ".join(sorted(new_devices))))
            print("== dropdown items: {}".format(
                ", ".join(sorted(shown)) or "(none)"))
            print("== dropdown open while detected: {}".format(popup_visible))
            if not new_devices.issubset(shown):
                fail("new device(s) not present in the dropdown: {}".format(
                    ", ".join(sorted(new_devices - shown))))
                return
            if not popup_visible:
                print("WARN: dropdown was closed; reopen it to test the "
                      "live-refresh path specifically")
            state["phase"] = "remove"
            state["phase_started"] = time.time()
            hint.setText(
                "步骤 2/2：请拔掉刚插入的串口设备。\n"
                "等待从列表中消失：{}".format(", ".join(sorted(new_devices)))
            )
            print("== now unplug {}...".format(", ".join(sorted(new_devices))))
            return

        if state["phase"] == "remove":
            remaining = state["inserted"] & current
            if remaining:
                return
            if state["inserted"] & shown:
                fail("unplugged device still present in dropdown: {}".format(
                    ", ".join(sorted(state["inserted"] & shown))))
                return
            pass_test(
                "insert and remove detected; list is refreshed "
                "(final devices: {})".format(
                    ", ".join(sorted(current)) or "(none)")
            )

    timer = QTimer()
    timer.setInterval(POLL_INTERVAL_MS)
    timer.timeout.connect(tick)
    timer.start()

    # Open the dropdown after the window is mapped, then keep it open.
    QTimer.singleShot(1500, serial_conn.serialPortCombobox.showItems)

    app.exec_()
    timer.stop()
    serial_conn.onDel()

    if state["result"] is None:
        print("FAIL: test ended without a result")
        return 1
    return 0 if state["result"][0] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
