import importlib.util
import os
import sys
import types

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODULE_PATH = os.path.join(ROOT, "COMTool", "conn", "port_utils.py")

# Load the module directly so the test does not import COMTool.conn.__init__,
# which pulls in PyQt5. Keep the port-list logic testable without a GUI.
_spec = importlib.util.spec_from_file_location("comtool_port_utils", MODULE_PATH)
port_utils = importlib.util.module_from_spec(_spec)
sys.modules[_spec.name] = port_utils
_spec.loader.exec_module(port_utils)

PortInfo = port_utils.PortInfo
choose_index = port_utils.choose_index
collect_ports = port_utils.collect_ports
display_text = port_utils.display_text
format_port_details = port_utils.format_port_details
from_pyserial = port_utils.from_pyserial
normalize_ports = port_utils.normalize_ports
port_keys = port_utils.port_keys


def fake_port(**kwargs):
    values = {
        "device": "COM1",
        "description": "",
        "name": "",
        "manufacturer": "",
        "vid": None,
        "pid": None,
        "serial_number": None,
        "hwid": "",
    }
    values.update(kwargs)
    return types.SimpleNamespace(**values)


def test_from_pyserial_handles_missing_fields():
    port = from_pyserial(types.SimpleNamespace(device="COM3"))

    assert port.device == "COM3"
    assert port.description == ""
    assert port.name == ""
    assert port.vid is None
    assert port.pid is None
    assert port.hwid == ""


def test_display_text_with_vid_pid():
    port = PortInfo(
        device="COM5",
        description="USB-SERIAL CH340 (COM5)",
        vid=0x1A86,
        pid=0x55D4,
    )

    assert display_text(port) == "COM5  USB-SERIAL CH340  [1A86:55D4]"


def test_display_text_without_vid_pid_has_no_brackets():
    port = PortInfo(device="/dev/ttyUSB0", description="CP2102 USB to UART")

    assert display_text(port) == "/dev/ttyUSB0  CP2102 USB to UART"
    assert "[" not in display_text(port)


def test_display_prefers_description_over_manufacturer():
    port = PortInfo(
        device="COM5",
        description="USB-SERIAL CH340",
        manufacturer="wch.cn",
    )

    assert "USB-SERIAL CH340" in display_text(port)
    assert "wch.cn" not in display_text(port)


def test_display_falls_back_to_name_then_manufacturer():
    named = PortInfo(device="COM7", name="USB UART")
    manufacturer_only = PortInfo(device="COM8", manufacturer="Silicon Labs")

    assert display_text(named) == "COM7  USB UART"
    assert display_text(manufacturer_only) == "COM8  Silicon Labs"


def test_display_ignores_na_description():
    port = PortInfo(device="COM3", description="n/a", name="USB UART")

    assert display_text(port) == "COM3  USB UART"


def test_display_ignores_na_description_and_falls_back_to_manufacturer():
    port = PortInfo(device="COM3", description="N/A", manufacturer="Silicon Labs")

    assert display_text(port) == "COM3  Silicon Labs"


def test_display_strips_windows_com_suffix_case_insensitively():
    port = PortInfo(device="COM12", description="USB-SERIAL CH340 (com12)")

    assert display_text(port) == "COM12  USB-SERIAL CH340"


def test_display_does_not_duplicate_device_as_label():
    port = PortInfo(device="COM4", description="COM4")

    assert display_text(port) == "COM4"


def test_display_collapses_whitespace():
    port = PortInfo(device="COM4", description="  USB   Serial   Bridge  ")

    assert display_text(port) == "COM4  USB Serial Bridge"


def test_natural_sort_orders_com2_before_com10():
    ports = normalize_ports(
        [
            PortInfo(device="COM10"),
            PortInfo(device="COM2"),
            PortInfo(device="COM1"),
        ]
    )

    assert [p.device for p in ports] == ["COM1", "COM2", "COM10"]


def test_macos_dedupes_tty_when_cu_exists():
    ports = normalize_ports(
        [
            PortInfo(device="/dev/tty.usbserial-1", description="USB Serial"),
            PortInfo(device="/dev/cu.usbserial-1", description="USB Serial"),
        ]
    )

    assert [p.device for p in ports] == ["/dev/cu.usbserial-1"]


def test_macos_keeps_tty_when_no_cu_counterpart():
    ports = normalize_ports([PortInfo(device="/dev/tty.usbserial-2")])

    assert [p.device for p in ports] == ["/dev/tty.usbserial-2"]


def test_bluetooth_pseudo_ports_are_filtered():
    ports = normalize_ports(
        [
            PortInfo(device="/dev/cu.Bluetooth-Incoming-Port"),
            PortInfo(device="/dev/tty.Bluetooth-Incoming-Port"),
            PortInfo(device="/dev/cu.usbserial-9"),
        ]
    )

    assert [p.device for p in ports] == ["/dev/cu.usbserial-9"]


def test_duplicate_devices_are_removed():
    ports = normalize_ports(
        [
            PortInfo(device="COM3", description="first"),
            PortInfo(device="COM3", description="second"),
        ]
    )

    assert len(ports) == 1
    assert ports[0].description == "first"


def test_choose_index_matches_device_exactly():
    ports = [PortInfo(device="COM3"), PortInfo(device="COM10")]

    assert choose_index(ports, "COM10") == 1
    assert choose_index(ports, "COM1") == -1
    assert choose_index(ports, None) == -1
    assert choose_index([], "COM3") == -1


def test_port_keys_detects_description_change():
    before = port_keys([PortInfo(device="COM3", description="old")])
    after = port_keys([PortInfo(device="COM3", description="new")])

    assert before != after
    assert port_keys([]) == ()


def test_collect_ports_accepts_callable_and_sorts():
    ports = collect_ports(
        lambda: [
            fake_port(device="COM10", description="USB Serial"),
            fake_port(device="COM2", description="USB Serial"),
        ]
    )

    assert [p.device for p in ports] == ["COM2", "COM10"]


def test_normalize_ports_is_stable_for_same_input():
    source = [PortInfo(device="COM4"), PortInfo(device="COM2")]

    assert normalize_ports(source) == normalize_ports(list(reversed(source)))


def test_format_port_details_contains_hwid_and_serial():
    port = PortInfo(
        device="COM5",
        description="USB-SERIAL CH340",
        manufacturer="wch.cn",
        vid=0x1A86,
        pid=0x7523,
        serial_number="ABC123",
        hwid="USB VID:PID=1A86:7523",
    )

    details = format_port_details(port)

    assert "COM5" in details
    assert "wch.cn" in details
    assert "ABC123" in details
    assert "USB VID:PID=1A86:7523" in details


def test_display_text_handles_empty_label():
    assert display_text(PortInfo(device="/dev/ttyS0")) == "/dev/ttyS0"
