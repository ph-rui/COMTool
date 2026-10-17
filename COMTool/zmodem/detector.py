"""Detect the start of a ZMODEM session in a terminal byte stream."""

from dataclasses import dataclass
from typing import Optional

from .codec import (
    FRAME_ZRQINIT,
    FRAME_ZRINIT,
    HEADER_PARSE_INVALID,
    HEADER_PARSE_NEED_MORE,
    HEADER_PARSE_OK,
    ZHeader,
    parse_header_at,
)


@dataclass
class Detection:
    direction: str
    header: ZHeader
    remainder: bytes


@dataclass
class DetectionResult:
    terminal_data: bytes
    detection: Optional[Detection] = None


class ZmodemDetector:
    """Scan incoming terminal bytes for ZRQINIT/ZRINIT headers.

    Bytes that are definitely not part of a header are returned immediately.
    Only a potential header at the end of the input is held until the next
    chunk arrives.
    """

    def __init__(self):
        self._pending = bytearray()

    def reset(self):
        self._pending.clear()

    def feed(self, data: bytes) -> DetectionResult:
        if not data:
            return DetectionResult(b"")
        buffer = bytes(self._pending) + data
        self._pending.clear()

        output = bytearray()
        index = 0
        while index < len(buffer):
            star = buffer.find(b"*", index)
            if star < 0:
                output.extend(buffer[index:])
                break
            output.extend(buffer[index:star])

            result = parse_header_at(buffer, star)
            if result.status == HEADER_PARSE_OK:
                if result.header.frame_type in (FRAME_ZRQINIT, FRAME_ZRINIT):
                    direction = (
                        "receive"
                        if result.header.frame_type == FRAME_ZRQINIT
                        else "send"
                    )
                    remainder = buffer[star + result.consumed :]
                    return DetectionResult(
                        bytes(output),
                        Detection(direction, result.header, remainder),
                    )
                # A valid non-session header while idle is not a trigger; let it
                # through rather than silently dropping data.
                output.extend(buffer[star : star + result.consumed])
                index = star + result.consumed
                continue
            if result.status == HEADER_PARSE_NEED_MORE:
                self._pending.extend(buffer[star:])
                return DetectionResult(bytes(output))
            if result.status == HEADER_PARSE_INVALID:
                output.append(buffer[star])
                index = star + 1
                continue

        return DetectionResult(bytes(output))
