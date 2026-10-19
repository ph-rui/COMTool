"""Persistence helpers for remembered serial sessions.

This module deliberately does not import PyQt5 so the session store can be
tested without a graphical environment.
"""

import copy
import time

try:
    from .conn.port_utils import PortInfo, display_label, natural_key
except ImportError:
    from conn.port_utils import PortInfo, display_label, natural_key


SESSIONS_KEY = "serialSessions"

SERIAL_CONFIG_KEYS = (
    "port",
    "baudrate",
    "bytesize",
    "parity",
    "stopbits",
    "flowcontrol",
    "rts",
    "dtr",
)


def normalize_port(port):
    """Return the comparison key used to keep one session per serial port."""
    return str(port or "").strip().casefold()


def session_name(port, description=""):
    """Build the label displayed in the session sidebar.

    The description from pyserial often repeats the device name, for example
    ``USB-SERIAL CH340 (COM5)``.  ``display_label`` removes that suffix.
    """
    port = str(port or "").strip()
    if not port:
        return ""
    label = display_label(PortInfo(device=port, description=str(description or "")))
    if label and label.casefold() != port.casefold():
        return "{}  {}".format(port, label)
    return port


def _clean_serial_config(serial_config, port):
    source = serial_config if isinstance(serial_config, dict) else {}
    cleaned = {}
    for key in SERIAL_CONFIG_KEYS:
        if key in source:
            cleaned[key] = copy.deepcopy(source[key])
    cleaned["port"] = port
    return cleaned


class SerialSessionStore:
    """List of remembered serial sessions stored in the app config."""

    def __init__(self, config=None):
        self.sessions = []
        if config is not None:
            self.load(config)

    def load(self, config):
        try:
            raw = config[SESSIONS_KEY]
        except Exception:
            raw = []
        if not isinstance(raw, list):
            raw = []

        loaded = []
        by_port = {}
        for item in raw:
            cleaned = self._clean_item(item)
            if cleaned is None:
                continue
            key = normalize_port(cleaned["port"])
            if key in by_port:
                # Last entry wins, but keep the original position.
                loaded[by_port[key]] = cleaned
            else:
                by_port[key] = len(loaded)
                loaded.append(cleaned)
        self.sessions = loaded

    def save(self, config):
        config[SESSIONS_KEY] = self.to_list()

    def to_list(self):
        return copy.deepcopy(self.sessions)

    def _clean_item(self, item):
        if not isinstance(item, dict):
            return None
        serial = item.get("serial") if isinstance(item.get("serial"), dict) else {}
        port = str(item.get("port") or serial.get("port") or "").strip()
        if not port:
            return None
        name = str(item.get("name") or port)
        return {
            "port": port,
            "name": name,
            "serial": _clean_serial_config(serial, port),
            "updated": item.get("updated", 0),
        }

    def get(self, port):
        key = normalize_port(port)
        for session in self.sessions:
            if normalize_port(session["port"]) == key:
                return copy.deepcopy(session)
        return None

    def upsert(self, port, serial_config, name=None):
        port = str(port or "").strip()
        if not port:
            return None
        key = normalize_port(port)
        session = {
            "port": port,
            "name": str(name or port),
            "serial": _clean_serial_config(serial_config, port),
            "updated": int(time.time()),
        }
        for index, existing in enumerate(self.sessions):
            if normalize_port(existing["port"]) == key:
                self.sessions[index] = session
                return copy.deepcopy(session)
        self.sessions.append(session)
        return copy.deepcopy(session)

    def update_name(self, port, name):
        key = normalize_port(port)
        for session in self.sessions:
            if normalize_port(session["port"]) == key:
                name = str(name or session["port"])
                if session["name"] != name:
                    session["name"] = name
                    session["updated"] = int(time.time())
                    return True
                return False
        return False

    def remove(self, port):
        key = normalize_port(port)
        for index, session in enumerate(self.sessions):
            if normalize_port(session["port"]) == key:
                removed = self.sessions.pop(index)
                return removed["port"]
        return None

    def prune(self, available_ports):
        available = {normalize_port(port) for port in available_ports}
        removed = []
        kept = []
        for session in self.sessions:
            if normalize_port(session["port"]) in available:
                kept.append(session)
            else:
                removed.append(session["port"])
        self.sessions = kept
        return removed

    def update_descriptions(self, ports):
        """Refresh names from a successful scan. Return True when changed."""
        descriptions = {
            normalize_port(port.device): port.description
            for port in ports
            if getattr(port, "device", "")
        }
        changed = False
        for session in self.sessions:
            key = normalize_port(session["port"])
            if key not in descriptions:
                continue
            name = session_name(session["port"], descriptions[key])
            # Do not replace a useful description with the bare port name when
            # a driver reports no description for this scan.
            if name == session["port"]:
                continue
            if name and session["name"] != name:
                session["name"] = name
                session["updated"] = int(time.time())
                changed = True
        return changed

    def sorted_sessions(self):
        return sorted(
            copy.deepcopy(self.sessions),
            key=lambda session: (
                natural_key(session["port"]),
                natural_key(session["name"]),
            ),
        )

    def migrate_items(self, items):
        """Extract serial terminal configs from the old tab layout."""
        count = 0
        for item in items or []:
            if not isinstance(item, dict) or item.get("pluginId") != "terminal":
                continue
            conns = item.get("config", {}).get("conns", {})
            if not isinstance(conns, dict):
                continue
            serial = conns.get("serial")
            if not isinstance(serial, dict):
                continue
            port = str(serial.get("port") or "").strip()
            if not port:
                continue
            self.upsert(port, serial)
            count += 1
        return count
