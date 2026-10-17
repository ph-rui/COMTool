"""Pure-Python ZMODEM support used by the terminal plugin."""

from .crc import crc16, crc32

__all__ = ["crc16", "crc32"]
