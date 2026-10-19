"""A faster pyte history screen.

``pyte.HistoryScreen`` wraps every screen event through ``__getattribute__``.
That is convenient, but it rebuilds a wrapper on every attribute access and
shows up clearly when large SSH/serial bursts are fed into the terminal.

This class keeps the same history semantics as pyte's ``HistoryScreen`` but
binds the event wrappers once at construction time.  It is adapted from
``pyte.screens.HistoryScreen`` (LGPL-3.0).
"""

import math
from collections import deque

from pyte import modes as mo
from pyte.screens import History, Margins, Screen
from pyte.streams import Stream


class FastHistoryScreen(Screen):
    """A ``pyte.Screen`` with paginated scrollback and low dispatch overhead."""

    _wrapped = set(Stream.events)
    _wrapped.update(["next_page", "prev_page"])

    def __init__(self, columns, lines, history=100, ratio=0.5):
        self.history = History(
            deque(maxlen=history),
            deque(maxlen=history),
            float(ratio),
            history,
            history,
        )
        self._draw_cache = {}
        super().__init__(columns, lines)

        # pyte's HistoryScreen creates these wrappers dynamically from
        # __getattribute__.  Bind them once instead; Stream dispatch then finds
        # a normal instance method and pays no attribute-hook cost.
        for event in self._wrapped:
            handler = getattr(type(self), event).__get__(self, type(self))
            setattr(self, event, self._make_wrapper(event, handler))

    def _make_wrapper(self, event, handler):
        def inner(*args, **kwargs):
            self.before_event(event)
            result = handler(*args, **kwargs)
            self.after_event(event)
            return result

        return inner

    def before_event(self, event):
        """Ensure the screen is at the bottom of the history buffer."""
        if event not in ("prev_page", "next_page"):
            while self.history.position < self.history.size:
                self.next_page()

    def after_event(self, event):
        """Keep lines at the screen width and update cursor visibility."""
        if event in ("prev_page", "next_page"):
            for line in self.buffer.values():
                for x in line:
                    if x > self.columns:
                        line.pop(x)

        self.cursor.hidden = not (
            self.history.position == self.history.size
            and mo.DECTCEM in self.mode
        )

    def draw(self, data):
        """Fast path for printable ASCII output.

        pyte's generic ``draw`` calls ``wcwidth`` and creates a new ``Char``
        namedtuple for every character.  Most terminal traffic is plain ASCII,
        so keep a small per-style cache of character cells and update the
        cursor locally.  Wide and combining characters use pyte's original
        implementation.
        """
        source = data
        data = data.translate(
            self.g1_charset if self.charset else self.g0_charset
        )
        if not data.isascii() or not data.isprintable():
            return super().draw(source)

        columns = self.columns
        cursor = self.cursor
        x = cursor.x
        y = cursor.y
        line = self.buffer[y]
        attrs = cursor.attrs
        decawm = mo.DECAWM in self.mode
        insert_mode = mo.IRM in self.mode
        cache = self._draw_cache.get(attrs)
        if cache is None:
            if len(self._draw_cache) >= 128:
                self._draw_cache.clear()
            cache = {}
            self._draw_cache[attrs] = cache

        for char in data:
            if x == columns:
                if decawm:
                    self.dirty.add(y)
                    self.carriage_return()
                    self.linefeed()
                    x = 0
                    y = self.cursor.y
                    line = self.buffer[y]
                else:
                    x -= 1

            if insert_mode:
                cursor.x = x
                self.insert_characters(1)
                line = self.buffer[y]

            cell = cache.get(char)
            if cell is None:
                cell = attrs._replace(data=char)
                cache[char] = cell
            line[x] = cell
            x += 1

        cursor.x = min(x, columns)
        self.dirty.add(y)

    def _reset_history(self):
        self.history.top.clear()
        self.history.bottom.clear()
        self.history = self.history._replace(position=self.history.size)

    def reset(self):
        super().reset()
        self._reset_history()

    def erase_in_display(self, how=0, *args, **kwargs):
        super().erase_in_display(how, *args, **kwargs)
        if how == 3:
            self._reset_history()

    def index(self):
        top, bottom = self.margins or Margins(0, self.lines - 1)
        if self.cursor.y == bottom:
            self.history.top.append(self.buffer[top])
        super().index()

    def reverse_index(self):
        top, bottom = self.margins or Margins(0, self.lines - 1)
        if self.cursor.y == top:
            self.history.bottom.append(self.buffer[bottom])
        super().reverse_index()

    def prev_page(self):
        """Move the screen page up through the history buffer."""
        if self.history.position > self.lines and self.history.top:
            mid = min(
                len(self.history.top),
                int(math.ceil(self.lines * self.history.ratio)),
            )

            self.history.bottom.extendleft(
                self.buffer[y]
                for y in range(self.lines - 1, self.lines - mid - 1, -1)
            )
            self.history = self.history._replace(
                position=self.history.position - mid
            )

            for y in range(self.lines - 1, mid - 1, -1):
                self.buffer[y] = self.buffer[y - mid]
            for y in range(mid - 1, -1, -1):
                self.buffer[y] = self.history.top.pop()

            self.dirty = set(range(self.lines))

    def next_page(self):
        """Move the screen page down through the history buffer."""
        if self.history.position < self.history.size and self.history.bottom:
            mid = min(
                len(self.history.bottom),
                int(math.ceil(self.lines * self.history.ratio)),
            )

            self.history.top.extend(self.buffer[y] for y in range(mid))
            self.history = self.history._replace(
                position=self.history.position + mid
            )

            for y in range(self.lines - mid):
                self.buffer[y] = self.buffer[y + mid]
            for y in range(self.lines - mid, self.lines):
                self.buffer[y] = self.history.bottom.popleft()

            self.dirty = set(range(self.lines))
