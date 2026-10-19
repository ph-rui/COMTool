"""Small platform helpers for optional secret persistence.

On Windows, passwords are protected with DPAPI and can only be decrypted by
the same Windows user account.  Other platforms intentionally return an empty
token; COMTool will simply ask for the password again there.
"""

import base64
import ctypes
import sys


_CRYPTPROTECT_UI_FORBIDDEN = 0x1


class _DataBlob(ctypes.Structure):
    _fields_ = [
        ("cbData", ctypes.c_uint32),
        ("pbData", ctypes.POINTER(ctypes.c_char)),
    ]


def _protect_windows(value: bytes) -> bytes:
    buffer = ctypes.create_string_buffer(value)
    input_blob = _DataBlob(
        len(value),
        ctypes.cast(buffer, ctypes.POINTER(ctypes.c_char)),
    )
    output_blob = _DataBlob()
    crypt32 = ctypes.windll.crypt32
    if not crypt32.CryptProtectData(
        ctypes.byref(input_blob),
        None,
        None,
        None,
        None,
        _CRYPTPROTECT_UI_FORBIDDEN,
        ctypes.byref(output_blob),
    ):
        return b""
    try:
        return ctypes.string_at(output_blob.pbData, output_blob.cbData)
    finally:
        ctypes.windll.kernel32.LocalFree(output_blob.pbData)


def _unprotect_windows(value: bytes) -> bytes:
    buffer = ctypes.create_string_buffer(value)
    input_blob = _DataBlob(
        len(value),
        ctypes.cast(buffer, ctypes.POINTER(ctypes.c_char)),
    )
    output_blob = _DataBlob()
    crypt32 = ctypes.windll.crypt32
    if not crypt32.CryptUnprotectData(
        ctypes.byref(input_blob),
        None,
        None,
        None,
        None,
        _CRYPTPROTECT_UI_FORBIDDEN,
        ctypes.byref(output_blob),
    ):
        return b""
    try:
        return ctypes.string_at(output_blob.pbData, output_blob.cbData)
    finally:
        ctypes.windll.kernel32.LocalFree(output_blob.pbData)


def protect_password(password):
    """Encrypt a password for storage, returning '' when unsupported."""
    password = str(password or "")
    if not password or sys.platform != "win32":
        return ""
    try:
        encrypted = _protect_windows(password.encode("utf-8"))
    except Exception:
        return ""
    if not encrypted:
        return ""
    return base64.b64encode(encrypted).decode("ascii")


def unprotect_password(token):
    """Decrypt a stored password, returning '' when unavailable/invalid."""
    token = str(token or "")
    if not token or sys.platform != "win32":
        return ""
    try:
        encrypted = base64.b64decode(token.encode("ascii"), validate=True)
        decrypted = _unprotect_windows(encrypted)
    except Exception:
        return ""
    if not decrypted:
        return ""
    try:
        return decrypted.decode("utf-8")
    except UnicodeDecodeError:
        return ""
