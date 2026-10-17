"""ZMODEM file name decoding, sanitizing and metadata parsing."""

import os
import unicodedata
from dataclasses import dataclass
from typing import Optional

_WINDOWS_RESERVED = {
    "CON",
    "PRN",
    "AUX",
    "NUL",
    *("COM%d" % i for i in range(1, 10)),
    *("LPT%d" % i for i in range(1, 10)),
}
_INVALID_CHARS = set('<>:"|?*')


@dataclass
class FileMeta:
    name: str
    size: Optional[int] = None
    mtime: Optional[int] = None
    mode: Optional[int] = None
    serial: Optional[int] = None


def decode_zmodem_name(raw: bytes) -> str:
    return raw.decode("utf-8", "surrogateescape")


def _is_surrogate(char: str) -> bool:
    code = ord(char)
    return 0xD800 <= code <= 0xDFFF


def sanitize_basename(name: str) -> str:
    name = unicodedata.normalize("NFC", name)
    name = name.replace("\\", "/").split("/")[-1]

    cleaned = []
    for char in name:
        code = ord(char)
        if _is_surrogate(char):
            cleaned.append("_")
        elif code < 0x20 or code == 0x7F:
            continue
        elif char in _INVALID_CHARS or char in "/\\":
            cleaned.append("_")
        else:
            cleaned.append(char)
    name = "".join(cleaned).strip(" .")

    if not name or name in (".", ".."):
        return "download"

    root, extension = os.path.splitext(name)
    if root.upper() in _WINDOWS_RESERVED:
        name += "_"

    if len(name) > 200:
        root, extension = os.path.splitext(name)
        keep = max(1, 200 - len(extension))
        name = root[:keep] + extension

    return name or "download"


def unique_temp_path(directory: str, name: str) -> str:
    base = sanitize_basename(name)
    candidate = os.path.join(directory, base)
    if not os.path.exists(candidate):
        return candidate

    root, extension = os.path.splitext(base)
    index = 1
    while True:
        candidate = os.path.join(directory, "%s (%d)%s" % (root, index, extension))
        if not os.path.exists(candidate):
            return candidate
        index += 1


def _parse_int(value: str, base: int = 10) -> Optional[int]:
    try:
        parsed = int(value, base)
    except (TypeError, ValueError):
        return None
    return parsed if parsed >= 0 else None


def parse_zfile_metadata(payload: bytes) -> FileMeta:
    if b"\0" in payload:
        name_raw, rest_raw = payload.split(b"\0", 1)
    else:
        name_raw, rest_raw = payload, b""

    name = sanitize_basename(decode_zmodem_name(name_raw))
    parts = rest_raw.decode("ascii", "replace").split()
    size = _parse_int(parts[0], 10) if parts else None
    mtime = _parse_int(parts[1], 8) if len(parts) > 1 else None
    mode = _parse_int(parts[2], 8) if len(parts) > 2 else None
    serial = _parse_int(parts[3], 10) if len(parts) > 3 else None
    return FileMeta(name=name, size=size, mtime=mtime, mode=mode, serial=serial)
