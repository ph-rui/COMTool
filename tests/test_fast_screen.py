"""Correctness tests for the optimized pyte history screen."""

import os
import sys

import pytest

pytest.importorskip("pyte")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

import pyte  # noqa: E402

from COMTool.fast_screen import FastHistoryScreen  # noqa: E402


def _screens():
    history = pyte.HistoryScreen(10, 4, history=100, ratio=0.5)
    fast = FastHistoryScreen(10, 4, history=100, ratio=0.5)
    history.set_mode(pyte.modes.LNM)
    fast.set_mode(pyte.modes.LNM)
    return history, fast


def _assert_same(history, fast):
    for row in range(history.lines):
        for column in range(history.columns):
            assert history.buffer[row][column] == fast.buffer[row][column]
    assert (
        history.cursor.x,
        history.cursor.y,
        history.cursor.hidden,
    ) == (
        fast.cursor.x,
        fast.cursor.y,
        fast.cursor.hidden,
    )
    assert list(history.history.top) == list(fast.history.top)
    assert list(history.history.bottom) == list(fast.history.bottom)
    assert history.history.position == fast.history.position
    assert history.history.size == fast.history.size


def test_fast_screen_matches_pyte_for_plain_and_ansi_output():
    data = (
        b"hello world\r\n"
        b"\x1b[31mred\x1b[0m normal\r\n"
        b"wide \xe4\xb8\xad\xe6\x96\x87 text\r\n"
        b"comb e\xcc\x81 test\r\n"
        b"\x1b[4hinsert\x1b[4l"
    )
    history, fast = _screens()
    pyte.ByteStream(history).feed(data)
    pyte.ByteStream(fast).feed(data)
    _assert_same(history, fast)


def test_fast_screen_history_paging_matches_pyte():
    data = b"".join(b"line%03d\n" % index for index in range(30))
    history, fast = _screens()
    pyte.ByteStream(history).feed(data)
    pyte.ByteStream(fast).feed(data)

    for _ in range(4):
        _assert_same(history, fast)
        history.prev_page()
        fast.prev_page()

    for _ in range(2):
        history.next_page()
        fast.next_page()
        _assert_same(history, fast)
