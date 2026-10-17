"""Pure helpers for serial port enumeration and display.

This module intentionally does not import PyQt5 so the port-list logic can be
unit tested without a graphical environment.
"""

import re
from dataclasses import dataclass
from typing import Callable, List, Optional, Tuple

_IGNORED_DEVICES = (
    "/dev/cu.Bluetooth-Incoming-Port",
    "/dev/tty.Bluetooth-Incoming-Port",
)

_MAC_CU_PREFIX = "/dev/cu."
_MAC_TTY_PREFIX = "/dev/tty."


@dataclass(frozen=True)
class PortInfo:
    device: str
    description: str = ""
    name: str = ""
    manufacturer: str = ""
    vid: Optional[int] = None
    pid: Optional[int] = None
    serial_number: Optional[str] = None
    hwid: str = ""


def from_pyserial(port) -> PortInfo:
    """Convert a pyserial ListPortInfo-like object to PortInfo.

    Missing attributes are tolerated so tests and alternate backends can pass
    simple objects.
    """
    return PortInfo(
        device=str(getattr(port, "device", "") or ""),
        description=str(getattr(port, "description", "") or ""),
        name=str(getattr(port, "name", "") or ""),
        manufacturer=str(getattr(port, "manufacturer", "") or ""),
        vid=getattr(port, "vid", None),
        pid=getattr(port, "pid", None),
        serial_number=getattr(port, "serial_number", None),
        hwid=str(getattr(port, "hwid", "") or ""),
    )


def natural_key(text: str) -> Tuple:
    """Sort key that orders COM2 before COM10."""
    parts = re.split(r"(\d+)", text or "")
    return tuple(int(part) if part.isdigit() else part.casefold() for part in parts)


def _clean_label(value: str) -> str:
    value = re.sub(r"\s+", " ", value or "").strip()
    if value.casefold() in ("n/a", "none", "unknown"):
        return ""
    return value


def display_label(port: PortInfo) -> str:
    label = (
        _clean_label(port.description)
        or _clean_label(port.name)
        or _clean_label(port.manufacturer)
    )
    if port.device:
        # Windows descriptions often repeat the device, e.g.
        # "USB-SERIAL CH340 (COM5)".
        label = re.sub(
            r"\s*\(" + re.escape(port.device) + r"\)\s*$",
            "",
            label,
            flags=re.IGNORECASE,
        ).strip()
    if label.casefold() == port.device.casefold():
        return ""
    return label


def display_text(port: PortInfo) -> str:
    text = port.device
    label = display_label(port)
    if label:
        text += "  " + label
    if port.vid is not None and port.pid is not None:
        text += "  [{:04X}:{:04X}]".format(port.vid, port.pid)
    return text


def format_port_details(port: PortInfo) -> str:
    lines = ["Device: {}".format(port.device)]
    if port.description:
        lines.append("Description: {}".format(port.description))
    if port.name and port.name != port.description:
        lines.append("Name: {}".format(port.name))
    if port.manufacturer:
        lines.append("Manufacturer: {}".format(port.manufacturer))
    if port.vid is not None and port.pid is not None:
        lines.append("VID:PID: {:04X}:{:04X}".format(port.vid, port.pid))
    if port.serial_number:
        lines.append("Serial: {}".format(port.serial_number))
    if port.hwid:
        lines.append("HWID: {}".format(port.hwid))
    return "\n".join(lines)


def normalize_ports(ports: List[PortInfo]) -> List[PortInfo]:
    """Filter pseudo ports, deduplicate, and sort ports for display."""
    filtered = [
        port
        for port in ports
        if port.device and port.device not in _IGNORED_DEVICES
    ]

    # On macOS pyserial exposes both /dev/cu.X and /dev/tty.X for the same
    # device. Prefer the callout (/dev/cu.*) entry.
    cu_suffixes = {
        port.device[len(_MAC_CU_PREFIX):]
        for port in filtered
        if port.device.startswith(_MAC_CU_PREFIX)
    }
    if cu_suffixes:
        filtered = [
            port
            for port in filtered
            if not (
                port.device.startswith(_MAC_TTY_PREFIX)
                and port.device[len(_MAC_TTY_PREFIX):] in cu_suffixes
            )
        ]

    seen = set()
    unique = []
    for port in filtered:
        if port.device in seen:
            continue
        seen.add(port.device)
        unique.append(port)

    return sorted(
        unique,
        key=lambda port: (
            natural_key(port.device),
            natural_key(display_label(port)),
        ),
    )


def collect_ports(comports: Optional[Callable] = None) -> List[PortInfo]:
    """Enumerate and normalize serial ports.

    ``comports`` can be injected for tests; by default pyserial is used.
    """
    if comports is None:
        import serial.tools.list_ports

        comports = serial.tools.list_ports.comports
    return normalize_ports([from_pyserial(port) for port in comports()])


def port_keys(ports: List[PortInfo]) -> Tuple:
    """Return a tuple that changes whenever the visible port set changes."""
    return tuple(
        (
            port.device,
            port.vid,
            port.pid,
            port.serial_number,
            port.description,
        )
        for port in ports
    )


def choose_index(ports: List[PortInfo], preferred_device: Optional[str]) -> int:
    """Return the index of preferred_device, or -1 when it is absent."""
    if not preferred_device:
        return -1
    for index, port in enumerate(ports):
        if port.device == preferred_device:
            return index
    return -1
