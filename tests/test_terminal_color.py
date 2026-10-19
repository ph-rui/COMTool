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


def test_plain_output_gets_mobaxterm_style_highlights(app):
    frontend = _frontend()

    ip_colors = frontend._highlight_line("inet 172.16.66.48 netmask 255.255.255.0")
    assert ip_colors[5] == "brightmagenta"
    assert ip_colors[16] == "brightmagenta"

    mac_colors = frontend._highlight_line("ether fe:fc:fe:ed:91:b7")
    assert mac_colors[6] == "brown"

    ipv6_colors = frontend._highlight_line("inet6 fe80::920f:2317:d1e8:9ba9/64")
    assert ipv6_colors[6] == "brightmagenta"

    error_colors = frontend._highlight_line(
        "        RX errors 0 dropped 0 overruns 0 frame 0"
    )
    assert error_colors[8] == "brightred"
    assert error_colors[-1] == "brightred"

    frontend.deleteLater()
