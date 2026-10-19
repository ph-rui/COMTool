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
    assert ip_colors[5:17] == ["brightmagenta"] * 12
    # MobaXterm's default profile deliberately excludes 255.x.x.x addresses.
    assert all(color is None for color in ip_colors[26:39])

    # The default MobaXterm profile only highlights IPv4, not MAC or IPv6.
    assert all(color is None for color in frontend._highlight_line(
        "ether fe:fc:fe:ed:91:b7"
    ))
    assert all(color is None for color in frontend._highlight_line(
        "inet6 fe80::920f:2317:d1e8:9ba9/64"
    ))

    error_colors = frontend._highlight_line(
        "        RX errors 0 dropped 0 overruns 0 frame 0"
    )
    assert error_colors[11:17] == ["brightred"] * 6
    assert all(
        color is None for index, color in enumerate(error_colors)
        if not 11 <= index < 17
    )

    warning_colors = frontend._highlight_line(
        "Warning: Locale not supported by X"
    )
    assert warning_colors[0:7] == ["brightyellow"] * 7
    assert warning_colors[16:29] == ["brightred"] * 13

    status_colors = frontend._highlight_line(
        "(II) AIGLX: enabled GLX_EXT_texture_from_pixmap"
    )
    assert status_colors[0:4] == ["brightcyan"] * 4
    assert status_colors[12:19] == ["brightgreen"] * 7

    assert frontend._highlight_line("last login: ok")[0:11] == ["brightcyan"] * 11
    assert frontend._highlight_line(
        "connect http://example.com/path?q=1 now"
    )[8:35] == ["underline"] * 27
    assert frontend._highlight_line('Variant = "none"')[11:15] == ["brightmagenta"] * 4

    assert frontend._highlight_line("status=false")[7:12] == ["brightred"] * 5
    assert frontend._highlight_line("status=true")[7:11] == ["brightgreen"] * 4

    frontend.deleteLater()


def test_received_data_paints_on_the_gui_thread(app):
    frontend = _frontend()
    frontend.onReceived(b"hello\r\nworld\r\n")

    assert frontend._repaint_pending is True
    app.processEvents()
    assert frontend._repaint_pending is False
    assert frontend.backend.screen.dirty == set()
    frontend.deleteLater()
