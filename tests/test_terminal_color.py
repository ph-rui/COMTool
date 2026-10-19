"""Color rendering tests for the terminal frontend."""

import os
import sys

import pytest

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

pytest.importorskip("PyQt5")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from PyQt5.QtWidgets import QApplication  # noqa: E402

from COMTool.plugins.terminal import Terminal_Backend, Terminal_Frontend  # noqa: E402


@pytest.fixture(scope="module")
def app():
    return QApplication.instance() or QApplication([])


def _frontend():
    return Terminal_Frontend(lambda data: None, lambda w, h: None, lambda: True)


def test_pyte_truecolor_hex_is_resolved(app):
    backend = Terminal_Backend()
    backend.feed(b"\x1b[38;2;255;0;0mred\x1b[0m")
    assert backend.screen.buffer[0][0].fg == "ff0000"

    frontend = _frontend()
    assert frontend.get_pen("ff0000").color().name() == "#ff0000"
    assert frontend.get_brush("00ff00").color().name() == "#00ff00"
    assert frontend.get_pen("brightred").color().name() == "#ff6b6b"
    frontend.deleteLater()


def test_default_color_still_uses_theme(app):
    frontend = _frontend()
    assert frontend.get_pen("default").color().name() == "#ffffff"
    assert frontend.get_brush("default").color().name() == "#212121"
    frontend.deleteLater()
