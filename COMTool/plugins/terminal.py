'''
    @author neucrack
    @license LGPL-3.0
    @update 2022-03-15 create
'''
from ast import arg
import re
from PyQt5.QtCore import QObject, Qt, QRect, pyqtSignal
from PyQt5.QtWidgets import (QApplication, QWidget,QPushButton,QMessageBox,QDesktopWidget,QMainWindow,
                             QVBoxLayout,QHBoxLayout,QGridLayout,QTextEdit,QLabel,QRadioButton,QCheckBox,
                             QLineEdit,QGroupBox,QSplitter,QFileDialog, QScrollArea, QScrollBar,
                             QProgressBar)
from PyQt5.QtGui import (QColor, QPixmap, QFontMetrics, QFont, QBrush, QPen, QPainter,
                        QKeyEvent, QWheelEvent)

try:
    from .base import Plugin_Base
    from Combobox import ComboBox
    from i18n import _
    import utils, parameters
    from conn.base import ConnectionStatus
    from ..zmodem.detector import ZmodemDetector
    from ..zmodem.io import ZmodemIO
    from ..zmodem.session import ZmodemSession
except ImportError:
    from COMTool.plugins.base import Plugin_Base
    from COMTool import utils, parameters
    from COMTool.i18n import _
    from COMTool.Combobox import ComboBox
    from COMTool.conn.base import  ConnectionStatus
    from COMTool.zmodem.detector import ZmodemDetector
    from COMTool.zmodem.io import ZmodemIO
    from COMTool.zmodem.session import ZmodemSession

import os
import shutil
import tempfile
import pyte
from wcwidth import wcswidth
import threading
import time
import pyperclip


def _format_size(size) -> str:
    try:
        size = int(size)
    except (TypeError, ValueError):
        return "0 B"
    if size < 1024:
        return "%d B" % size
    if size < 1024 * 1024:
        return "%.1f KB" % (size / 1024.0)
    if size < 1024 * 1024 * 1024:
        return "%.1f MB" % (size / 1024.0 / 1024.0)
    return "%.1f GB" % (size / 1024.0 / 1024.0 / 1024.0)


class Terminal_Backend:
    def __init__(self) -> None:
        self.screen = pyte.HistoryScreen(60, 20, history=9999, ratio = 0.05)
        self.screen.set_mode(pyte.modes.LNM)
        self.stream = pyte.ByteStream(self.screen)

    def feed(self, data:bytes):
        self.stream.feed(data)

    def resize(self, width, height):
        self.screen.resize(columns=width, lines=height)
        self.screen.history = self.screen.history._replace(ratio = 1.0/height)


# MobaXterm-style highlighting for plain program output.  This is applied only
# to cells that have no ANSI color of their own, so `ls --color` etc. keep
# their original colors.
_HIGHLIGHT_IPV4 = re.compile(r"(?<![\w.])(?:\d{1,3}\.){3}\d{1,3}(?![\w.])")
_HIGHLIGHT_MAC = re.compile(
    r"(?i)(?<![0-9a-f:])(?:[0-9a-f]{2}:){5}[0-9a-f]{2}(?![0-9a-f:])"
)
_HIGHLIGHT_IPV6 = re.compile(
    r"(?i)(?<![\w:])(?=[0-9a-f:]*:[0-9a-f:]*:)[0-9a-f:]{2,}(?![\w:])"
)
_HIGHLIGHT_ERROR_LINE = re.compile(r"\b(?:RX|TX)\s+errors?\b", re.IGNORECASE)


class Terminal_Frontend(QWidget):
    def __init__(self, send_func, resizeConnFunc, isConnectedFunc) -> None:
        super().__init__()
        self.send = send_func
        self.resizeConn = resizeConnFunc
        self.isConnected = isConnectedFunc
        self.theme = {
            "bg": QColor("#212121"),
            "color": QColor(255, 255, 255),
            "cursor": QColor(76, 175, 80),
            "colors": {
                'black': QColor("#212121"),
                'red': QColor("#f44336"),
                'green': QColor("#4caf50"),
                'blue': QColor("#2196f3"),
                'cyan': QColor("#26c6da"),
                'brown': QColor("#a1887f"),
                'yellow': QColor("#ffeb3b"),
                'magenta': QColor("#e85aad"),
                'white': QColor("#cacaca"),
                'brightblack': QColor("#666666"),
                'brightred': QColor("#ff6b6b"),
                'brightgreen': QColor("#7ee787"),
                'brightyellow': QColor("#ffff66"),
                'brightblue': QColor("#6cb6ff"),
                'brightmagenta': QColor("#ff7cd6"),
                'brightcyan': QColor("#66e0ff"),
                'brightwhite': QColor("#ffffff"),
                "selected": QColor(255, 255, 255, 80),
            }
        }
        self.keymap = {
            # for term type string equal to linux
            Qt.Key_Backspace: b'\x7F', # use DEL, not 0x08(^H, Backspace)
            Qt.Key_Escape:    b'\x1B',
            Qt.Key_AsciiTilde: b'\x7E',
            Qt.Key_ScrollLock: b'\x1A',
            Qt.Key_Up:       b'\x1b[A',
            Qt.Key_Down:     b'\x1b[B',
            Qt.Key_Left:     b'\x1b[D',
            Qt.Key_Right:    b'\x1b[C',
            Qt.Key_PageUp:   b"\x1b[5~",
            Qt.Key_PageDown: b"\x1b[6~",
            Qt.Key_Home:     b'\x1b[1~',
            Qt.Key_End:      b'\x1b[4~',
            Qt.Key_Insert:   b"\x1b[2~",
            Qt.Key_Delete:   b"\x1b[3~",
            Qt.Key_F1:       b"\x1b[11~",
            Qt.Key_F2:       b"\x1b[12~",
            Qt.Key_F3:       b"\x1b[13~",
            Qt.Key_F4:       b"\x1b[14~",
            Qt.Key_F5:       b"\x1b[15~",
            Qt.Key_F6:       b"\x1b[17~",
            Qt.Key_F7:       b"\x1b[18~",
            Qt.Key_F8:       b"\x1b[19~",
            Qt.Key_F9:       b"\x1b[20~",
            Qt.Key_F10:      b"\x1b[21~",
            Qt.Key_F11:      b"\x1b[23~",
            Qt.Key_F12:      b"\x1b[24~",
            Qt.Key_F13:      b"\x1b[25~",
        }
        self.setAttribute(Qt.WA_InputMethodEnabled)
        self.resize(580, 380)
        self.setMinimumWidth(40)
        self.setMinimumHeight(40)
        self.backend = Terminal_Backend()
        self.setCursor(Qt.IBeamCursor)
        self.setFocusPolicy(Qt.StrongFocus)
        self.font_name = 'Consolas,Bitstream Vera Sans Mono,Courier New,monospace, Microsoft Yahei Mono, Microsoft YaHei'
        self.font_p_size = 15
        self.font = self.new_font()
        self.fm = QFontMetrics(self.font)
        self._char_height = self.fm.height()
        self._char_width = self.fm.width("W")
        self._columns, self._rows = self._pixel2pos(self.width(), self.height())
        self.cursor_x = 0
        self.cursor_y = 0
        self.transferActive = False
        self._selection = None
        # cache
        self.pens = {}
        self.brushes = {}
        self.default_brush = QBrush(self.theme["bg"])
        self.default_pen = QPen(self.theme["color"])
        # pixmap, all operation on this pixmap in receive thread,
        #         then invoke UI thread to paint this pixmap on main widget
        self.pixmap = QPixmap(self.width(), self.height())
        self.pixmap.fill(self.theme["bg"])
        # scroll
        self.scrollBar = None
        self.scrollEventSet = False
        self.scrollStatus = (0, 0)
        # timer for check change
        self.startTimer(100)
        self.updatePixmapLock = threading.Lock()
        # selection
        self.selectionStart = None
        self.lastSelection = None
        self.selectScroll = {     # for speed up scroll when select
            "speed": 0.1,
            "start_speed": 0.1,     # 0.5s
            "max_speed": 0.01,
            "ts": 0
        }

    def new_font(self):
        font = QFont()
        font.setFamily(self.font_name)
        font.setPixelSize(self.font_p_size)
        return font

    def _theme_color(self, color_name):
        """Resolve named colors and pyte's 256/truecolor hex values."""
        if not color_name:
            return None
        color = self.theme["colors"].get(color_name)
        if color:
            return color
        text = str(color_name)
        if len(text) == 6 and all(ch in "0123456789abcdefABCDEF" for ch in text):
            return QColor("#" + text)
        return None

    def get_pen(self, color_name="white", keyword = None):
        pen = self.pens.get(color_name)
        if not pen:
            if keyword:
                color = self.theme.get(keyword)
            else:
                color = self._theme_color(color_name)
            if not color:
                pen = self.default_pen
            else:
                pen = QPen(color)
            self.pens[color_name] = pen
        return pen

    def get_brush(self, color_name):
        brush = self.brushes.get(color_name)
        if not brush:
            color = self._theme_color(color_name)
            if not color:
                brush = self.default_brush
            else:
                brush = QBrush(color)
            self.brushes[color_name] = brush
        return brush

    def _pixel2pos(self, x, y):
        col = int(x / self._char_width)
        row = int(y / self._char_height)
        return col, row

    def _pos2pixel(self, col, row):
        x = col * self._char_width
        y = row * self._char_height
        return x, y

    def paint_full_pixmap(self):
        painter = QPainter(self.pixmap)
        screen = self.backend.screen
        self.paint_full_text(painter, screen)
        self.paint_cursor(painter, screen)

    def paint_part_pixmap(self):
        painter = QPainter(self.pixmap)
        screen = self.backend.screen
        self.paint_dirty_text(painter, screen)
        self.paint_cursor(painter, screen)

    def paint_full_text(self, painter, screen):
        painter.setFont(self.font)
        for line_num in range(self._rows):
            self.paint_line_text(painter, screen, line_num, clear=True)

    def paint_dirty_text(self, painter, screen):
        painter.setFont(self.font)
        # 重绘旧光标所在行
        screen.dirty.add(self.cursor_y)
        for line_num in screen.dirty:
            self.paint_line_text(painter, screen, line_num, clear=True)
        # clear paint dirty info mannually
        screen.dirty.clear()

    def _highlight_line(self, text):
        """Return per-character highlight color names for plain output."""
        colors = [None] * len(text)

        def fill(match, color, only_empty=True, to_end=False):
            end = len(text.rstrip()) if to_end else match.end()
            for idx in range(match.start(), max(match.start(), end)):
                if not only_empty or colors[idx] is None:
                    colors[idx] = color

        for match in _HIGHLIGHT_IPV4.finditer(text):
            fill(match, "brightmagenta")
        for match in _HIGHLIGHT_MAC.finditer(text):
            fill(match, "brown")
        for match in _HIGHLIGHT_IPV6.finditer(text):
            fill(match, "brightmagenta")
        for match in _HIGHLIGHT_ERROR_LINE.finditer(text):
            fill(match, "brightred", only_empty=False, to_end=True)
        return colors

    def paint_line_text(self, painter, screen, line_num, clear=False):
        start_x = 0
        start_y = line_num * self._char_height
        if clear:
            clear_rect = QRect(start_x, start_y, self.width(), self._char_height)
            painter.fillRect(clear_rect, self.default_brush)

        line = screen.buffer[line_num]
        line_text = "".join(line[col].data or "" for col in range(screen.columns))
        highlights = self._highlight_line(line_text)

        same_text = ""
        text_width = 0
        pre_char = None
        pre_fg = None
        align = Qt.AlignTop | Qt.AlignLeft

        for col in range(screen.columns):
            char = line[col]
            fg = char.fg
            if (fg == "default" and char.bg == "default"
                    and col < len(highlights) and highlights[col]):
                fg = highlights[col]
            if pre_char and fg == pre_fg and char.bg == pre_char.bg:
                same_text += char.data
                continue
            else:
                if same_text:
                    text_width = self.fm.width(same_text)
                    self.draw_text(same_text, start_x, start_y, text_width, pre_fg, pre_char.bg, painter, align)

                pre_char = char
                pre_fg = fg
                same_text = char.data
                start_x = start_x + text_width

        if same_text:
            text_width = self.fm.width(same_text)
            self.draw_text(same_text, start_x, start_y, text_width, pre_fg, pre_char.bg, painter, align)

    def draw_text(self, text, start_x, start_y, text_width, fg, bg, painter, align):
        rect = QRect(start_x, start_y, text_width, self._char_height)

        if bg and bg != 'default':
            painter.fillRect(rect, self.get_brush(bg))

        painter.setPen(self.get_pen(fg))
        painter.drawText(rect, align, text)

    def paint_cursor(self, painter, screen):
        align = Qt.AlignTop | Qt.AlignLeft
        cursor = screen.cursor
        self.cursor_x = cursor.x
        self.cursor_y = cursor.y
        char = ""
        if self.cursor_y >= len(screen.display):
            cursor_x_pixel_width = 0
        else:
            line = screen.display[self.cursor_y]
            # to get actual text before cursor.x
            x = self.cursor_x
            while 1:
                w = wcswidth(line[:x])
                if w > self.cursor_x:
                    x -= 1
                elif w < self.cursor_x:
                    x += 1
                else:
                    break
            text = line[:x]
            if x < len(line):
                char = line[x]
            cursor_x_pixel_width = self.fm.width(text)
        bcol = self.theme["cursor"]
        brush = QBrush(bcol)

        rect = QRect(cursor_x_pixel_width, self.cursor_y * self._char_height,
                    self._char_width, self._char_height)
        painter.setPen(Qt.NoPen)
        painter.setBrush(brush)
        painter.drawRect(rect)
        if char:
            painter.setPen(self.get_pen(keyword="bg"))
            painter.drawText(rect, align, char)

    def wheelEvent(self, event: QWheelEvent):
        try:
            y = event.angleDelta().y()
            if y > 0:
                self.backend.screen.prev_page()
            else:
                self.backend.screen.next_page()
            self.update()
        except:
            import traceback
            traceback.print_exc()

    def onScrolled(self, curr):
        self.updatePixmapLock.acquire()
        top = len(self.backend.screen.history.top)
        bottom = len(self.backend.screen.history.bottom)
        maxlen = self.backend.screen.history.size
        if top + bottom + self._rows > maxlen:
            newBottom = maxlen - self._rows - curr
        else:
            newBottom = top + bottom - curr
        lastBottom = -1
        while 1:
            if newBottom > bottom:
                self.backend.screen.prev_page()
            elif newBottom < bottom:
                self.backend.screen.next_page()
            else:
                break
            # move up 1 line(ratio must set to move on line every prev_page)
            bottom = len(self.backend.screen.history.bottom)
            if bottom == lastBottom:
                break
            lastBottom = bottom
        self.updatePixmapLock.release()


    def paintEvent(self, event):
        '''
            paint pixmap to on widget
        '''
        painter = QPainter(self)
        painter.drawPixmap(0, 0, self.pixmap)
        # set scrollbar
        top = len(self.backend.screen.history.top)
        bottom = len(self.backend.screen.history.bottom)
        maxlen = self.backend.screen.history.size
        if top + bottom + self._rows > maxlen:
            barCount = maxlen - self._rows
            curr = maxlen - bottom - self._rows
        else:
            barCount = top + bottom
            curr = top
        # prevent scrollbar event
        if self.scrollStatus != (curr, barCount):
            if self.scrollEventSet:
                self.scrollBar.valueChanged.disconnect()
            self.scrollBar.setMaximum(barCount)
            self.scrollBar.setMinimum(0)
            self.scrollBar.setSliderPosition(curr)
            self.scrollBar.valueChanged.connect(self.onScrolled)
            self.scrollEventSet = True
            self.scrollStatus = (curr, barCount)

    def timerEvent(self, event):
        '''
            scheduled check change
        '''
        self.updatePixmapLock.acquire()
        self.updatePixmap()
        self.updatePixmapLock.release()

    def onReceived(self, data:bytes):
        '''
            in receive thread, not UI thread
            lock to make sure receive thread not change screen when check changes here
        '''
        self.updatePixmapLock.acquire()
        self.backend.feed(data)
        self.updatePixmap()
        self.updatePixmapLock.release()

    def updatePixmap(self):
        cursor = self.backend.screen.cursor
        # display changed or cursor changed
        if self.backend.screen.dirty or (self.cursor_x != cursor.x or self.cursor_y != cursor.y):
            self.paint_part_pixmap()
            # trigger paintEvent
            self.update()

    def event(self, e):
        if type(e) == QKeyEvent and e.key() == Qt.Key_Tab:
            if e.type() == QKeyEvent.KeyPress:
                self.keyPressEvent(e)
            return True
        return super().event(e)

    def setTransferActive(self, active: bool):
        self.transferActive = active

    def inputMethodEvent(self, e):
        if self.transferActive:
            return
        text = e.commitString()
        if text:
            self.send(text.encode("utf-8"))

    def keyPressEvent(self, event):
        if self.transferActive:
            event.accept()
            return
        text = str(event.text())
        key = event.key()
        modifiers = event.modifiers()
        ctrl = (modifiers == Qt.ControlModifier | Qt.ShiftModifier) or (modifiers == Qt.ControlModifier)
        if ctrl and key == Qt.Key_V:
            content = pyperclip.paste()
            self.send(content.encode("utf-8"))
        elif text and not key in self.keymap:
            self.send(text.encode("utf-8"))
        else:
            s = self.keymap.get(key)
            if s:
                self.send(s)
        event.accept()

    def resizeEvent(self, event):
        try:
            self.updatePixmapLock.acquire()
            self._columns, self._rows = self._pixel2pos(self.width(), self.height())
            self.backend.resize(self._columns, self._rows)
            self.resizeConn(self._columns, self._rows)
            self.pixmap = QPixmap(self.width(), self.height())
            self.pixmap.fill(self.theme["bg"])
            self.paint_full_pixmap()
            self.updatePixmapLock.release()
        except:
            import traceback
            traceback.print_exc()

    def setScrollBar(self, scrollBar):
        self.scrollBar = scrollBar
        self.scrollBar.setMinimum(0)
        height = len(self.backend.screen.history.top) + len(self.backend.screen.history.bottom) - self._rows
        self.scrollBar.setMaximum(height if height > 0 else 0)
        self.scrollBar.setMinimum(0)
        if not self.scrollEventSet:
            self.scrollBar.valueChanged.connect(self.onScrolled)
            self.scrollEventSet = True

    def _pos_pix2colrow(self, x, y):
        '''
            @return x, y, scroll(-1: up, 0:not scroll, 1: down)
        '''
        scroll = 0
        row = y // self._char_height
        if row >= self._rows:
            row = self._rows - 1
            scroll = 1
        if row < 0:
            row = 0
            col = 0
            scroll = -1
        else:
            col = round(x / self._char_width)
            if col > self._columns:
                col = self._columns
            elif col < 0:
                col = 0
        return col, row, scroll

    def _pos_to_absolute(self, col, row):
        row = len(self.backend.screen.history.top) + row
        return col, row

    def _copy2clipboard(self, start, end):
        content = self._get_selected_data(start, end)
        pyperclip.copy(content)

    def _get_selected_data(self, start, end):
        pixLen = 0
        content = ""
        if start[3] == end[3]: # same row
            line = self._get_line(start[3])
            for i in line:
                pixLen += self.fm.width(line[i].data)
                if pixLen > start[2] * self._char_width:
                    content += line[i].data
                if pixLen >= end[0]:
                    break
        else:
            # select last line to end
            line = self._get_line(start[3])
            draw = False
            for i in line:
                if not draw:
                    pixLen += self.fm.width(line[i].data)
                    if pixLen > start[2] * self._char_width:
                        draw = True
                    else:
                        continue
                if draw:
                    content += line[i].data
            content += "\n"
            # select middle lines(if have)
            for r in range(start[3] + 1, end[3]):
                line = self._get_line(r)
                for i in line:
                    content += line[i].data
                content += "\n"
            # select from the start of current line to the pos of pointer
            line = self._get_line(end[3])
            pixLen = 0
            for i in line:
                pixLen += self.fm.width(line[i].data)
                if pixLen <= end[2] * self._char_width:
                    content += line[i].data
                if pixLen >= end[0]:
                    break
        return content

    def mousePressEvent(self, e):
        self.setFocus()
        self.mouseButton = e.button()
        x = e.x(); y = e.y()
        col, row, scroll = self._pos_pix2colrow(x, y)
        col, row = self._pos_to_absolute(col, row)
        self.selectionStart = (x, y, col, row)
        if self.lastSelection:
            if self.lastSelection[0] == self.lastSelection[1] and e.button() == Qt.RightButton: # no selection
                content = pyperclip.paste()
                self.send(content.encode("utf-8"))
            else:
                # mouse right button copy content to clipboard
                if e.button() == Qt.RightButton:
                    self._copy2clipboard(self.lastSelection[0], self.lastSelection[1])
                # unselect
                self.select(self.lastSelection[0], self.lastSelection[1], unselect=True)
        self.lastSelection = [self.selectionStart, self.selectionStart]
        self.selectScroll["speed"] =self.selectScroll["start_speed"]

    def toggleSelect(self, item):
        if item.bg == "selected":
            item = item._replace(bg="default")
        else:
            item = item._replace(bg="selected")
        return item

    def mouseMoveEvent(self, e):
        if self.mouseButton != Qt.LeftButton: # e.button() not work here, so use variable
            return
        self.updatePixmapLock.acquire()
        x = e.x(); y = e.y()
        col, row, scroll = self._pos_pix2colrow(x, y)
        col, row = self._pos_to_absolute(col, row)
        cursorPos = (x, y, col, row)
        # scroll
        if scroll != 0 and time.time() - self.selectScroll["ts"] > self.selectScroll["speed"]:
            if scroll < 0:
                self.backend.screen.prev_page()
            else:
                self.backend.screen.next_page()
            self.selectScroll["ts"] = time.time()
            if self.selectScroll["speed"] > self.selectScroll["max_speed"]:
                self.selectScroll["speed"] -= 0.005

        # get selection
        if col + row * self._columns > self.selectionStart[2] + self.selectionStart[3] * self._columns:
            front2rear = True
        else:
            front2rear = False
        if front2rear:
            newSelection = [self.selectionStart, cursorPos]
        else:
            newSelection = [cursorPos, self.selectionStart]
        # selection not change
        if newSelection[0][2:] == self.lastSelection[0][2:] and newSelection[1][2:] == self.lastSelection[1][2:]:
            self.updatePixmapLock.release()
            if scroll != 0:
                self.update()
            return
        # find need select range
        # find need erase election range
        needSlect = []; needErase = []
        newIdx = newSelection[0][2] + newSelection[0][3] * self._columns
        oldIdx = self.lastSelection[0][2] + self.lastSelection[0][3] * self._columns
        if newIdx < oldIdx:
            needSlect.append((newSelection[0], self.lastSelection[0]))
        elif newIdx > oldIdx:
            needErase.append((self.lastSelection[0], newSelection[0]))
        newIdx = newSelection[1][2] + newSelection[1][3] * self._columns
        oldIdx = self.lastSelection[1][2] + self.lastSelection[1][3] * self._columns
        if newIdx < oldIdx:
            needErase.append((newSelection[1], self.lastSelection[1]))
        elif newIdx > oldIdx:
            needSlect.append((self.lastSelection[1], newSelection[1]))
        # select
        for start, end in needSlect:
            self.select(start, end)
        for start, end in needErase:
            self.select(start, end, unselect = True)
        self.updatePixmapLock.release()
        # unselect
        # update last selection
        self.lastSelection = newSelection
        self.update()

    def mouseReleaseEvent(self, e):
        self.mouseButton = None

    def _get_line(self, row):
        if row < len(self.backend.screen.history.top):
            line = self.backend.screen.history.top[row]
        elif row < len(self.backend.screen.history.top) + self._rows:
            line = self.backend.screen.buffer[row - len(self.backend.screen.history.top)]
            self.backend.screen.dirty.add(row - len(self.backend.screen.history.top))
        else:
            line = self.backend.screen.history.bottom[row - len(self.backend.screen.history.top) - self._rows]
        return line

    def select(self, start, end, unselect = False):
        bg = "default" if unselect else "selected"
        pixLen = 0
        if start[3] == end[3]: # same row
            line = self._get_line(start[3])
            for i in line:
                pixLen += self.fm.width(line[i].data)
                if pixLen > start[2] * self._char_width:
                    line[i] = line[i]._replace(bg= bg)
                if pixLen >= end[0]:
                    break
        else:
            # select last line to end
            line = self._get_line(start[3])
            draw = False
            for i in line:
                if not draw:
                    pixLen += self.fm.width(line[i].data)
                    if pixLen > start[2] * self._char_width:
                        draw = True
                    else:
                        continue
                if draw:
                    line[i] = line[i]._replace(bg=bg)
            # select middle lines(if have)
            for r in range(start[3] + 1, end[3]):
                line = self._get_line(r)
                for i in line:
                    line[i] = line[i]._replace(bg=bg)
            # select from the start of current line to the pos of pointer
            line = self._get_line(end[3])
            pixLen = 0
            for i in line:
                pixLen += self.fm.width(line[i].data)
                if pixLen <= end[2] * self._char_width:
                    line[i] = line[i]._replace(bg=bg)
                if pixLen >= end[0]:
                    break


class Scroll_Terminal(QWidget):
    def __init__(self, sendFunc, resizeConnFunc, isConnectFunc) -> None:
        super().__init__()
        self.terminal = Terminal_Frontend(sendFunc, resizeConnFunc, isConnectFunc)
        self.scrollBar = QScrollBar(Qt.Vertical, self.terminal)
        self.terminal.setScrollBar(self.scrollBar)
        self.scrollBar.setProperty("class", "scrollbar2")
        layout = QHBoxLayout()
        self.setLayout(layout)
        layout.addWidget(self.terminal)
        layout.addWidget(self.scrollBar)
        self.resize(self.terminal.size())

    def onReceived(self, data):
        self.terminal.onReceived(data)

class Plugin(Plugin_Base):
    '''
        call sequence:
            set vars like hintSignal, hintSignal
            onInit
            onWidget
            onUiInitDone
                send
                onReceived
            onDel
    '''
    # vars set by caller
    isConnected = lambda : False
    send = lambda x,y:None          # send(data_bytes=None, file_path=None, callback=lambda ok,msg:None)
    hintSignal = None               # hintSignal.emit(type(error, warning, info), title, msg)
    reloadWindowSignal = None       # reloadWindowSignal.emit(title, msg, callback(close or not)), reload window to load new configs
    configGlobal = {}
    # other vars
    connParent = "main"      # parent id
    connChilds = []          # children ids
    id = "terminal"
    name = _("terminal")

    enabled = False          # user enabled this plugin
    active  = False          # using this plugin
    autoHideSettings = True  # hover left edge to open; rightward move closes
    displayNameFromConnection = True  # tab title shows COM99 / /dev/ttyUSB0

    zmodemSendRequestSignal = pyqtSignal()
    zmodemBusySignal = pyqtSignal(str)
    zmodemActiveSignal = pyqtSignal(bool)
    zmodemProgressSignal = pyqtSignal(str, int, object)
    zmodemFileStartSignal = pyqtSignal(str, object)
    zmodemCompleteSignal = pyqtSignal(object, object)
    zmodemErrorSignal = pyqtSignal(str)
    zmodemCancelledSignal = pyqtSignal()

    def __init__(self):
        super().__init__()
        if not self.id:
            raise ValueError(f"var id of Plugin {self} should be set")
        self._zmodem_lock = threading.RLock()
        self._zmodem_session = None
        self._zmodem_detector = ZmodemDetector()
        self._zmodem_pending = None
        self._zmodem_pending_buffer = b""
        self._zmodem_temp_dir = None
        self._zmodem_mode = None
        self._zmodem_send_count = 0
        self._zmodem_send_total = 0
        self._zmodem_send_index = 0
        self._zmodem_send_done = 0
        self._zmodem_send_current = 0
        self._zmodem_recv_count = 0
        self._zmodem_recv_bytes = 0
        self._zmodem_recv_current = 0
        self.zmodemLabel = None
        self.zmodemBar = None
        self.zmodemCancel = None

    def onInit(self, config):
        '''
            init params, DO NOT take too long time in this func
            @config dict type, just change this var's content,
                               when program exit, this config will be auto save to config file
        '''
        self.config = config
        default = {
            "version": 1,
            "zmodem_last_dir": "",
        }
        for k in default:
            if not k in self.config:
                self.config[k] = default[k]

    def onActive(self):
        try:
            self.widget.terminal.setFocus()
        except Exception:
            pass

    def onDel(self):
        self._zmodem_abort_from_ui()

    def onWidgetMain(self, parent):
        self.widget = Scroll_Terminal(self.send, self.resizeConnOutput, self.isConnected)
        return self.widget

    def resizeConnOutput(self, w, h):
        if self.isConnected:
            self.ctrlConn("resize", (w, h))

    def _feed_terminal(self, data: bytes):
        self.widget.onReceived(data)
        for plugin in self.connChilds:
            plugin.onReceived(data)

    def onReceived(self, data : bytes):
        with self._zmodem_lock:
            session = self._zmodem_session
            pending = self._zmodem_pending
        if session is not None and session.active:
            session.io.feed(data)
            return
        if session is not None:
            with self._zmodem_lock:
                if self._zmodem_session is session:
                    self._zmodem_session = None
        if pending is not None:
            # While the file picker is open the remote only repeats ZRINIT.
            # The first header is enough to start the sender.
            return

        result = self._zmodem_detector.feed(data)
        if result.terminal_data:
            self._feed_terminal(result.terminal_data)
        if result.detection is not None:
            self._handle_zmodem_detection(result.detection)

    def _handle_zmodem_detection(self, detection):
        if detection.direction == "receive":
            self._zmodem_start_receive(detection)
        else:
            self._zmodem_start_pending_send(detection)

    def _zmodem_write(self, data: bytes):
        """Write ZMODEM bytes and wait until the serial layer took them.

        The normal plugin ``send`` is asynchronous (it queues to the serial
        writer thread).  ZMODEM needs backpressure: without it the whole file
        is queued at once, progress jumps to 100%, and a cancelled transfer
        cannot stop in time.  The queue callback fires after the connection
        actually wrote the bytes.
        """
        done = threading.Event()
        result = []

        def callback(ok, msg, length, path):
            result.append((ok, msg))
            done.set()

        try:
            self.send(data, callback=callback)
        except TypeError:
            # Test doubles / custom send implementations with one argument.
            self.send(data)
            callback(True, "", len(data), "")
        if not done.wait(15.0):
            raise OSError(_("Serial write timed out"))
        ok, msg = result[0]
        if not ok:
            raise OSError(msg or _("Serial write failed"))

    def _zmodem_callbacks(self):
        return {
            "on_progress": lambda name, sent, total: self.zmodemProgressSignal.emit(
                name, sent, total
            ),
            "on_complete": lambda files, trailing: self.zmodemCompleteSignal.emit(
                files, trailing
            ),
            "on_error": lambda error: self.zmodemErrorSignal.emit(str(error)),
            "on_cancelled": lambda: self.zmodemCancelledSignal.emit(),
        }

    def _zmodem_start_receive(self, detection):
        self._zmodem_reset_progress()
        self._zmodem_mode = "receive"
        temp_dir = tempfile.mkdtemp(prefix="comtool-zmodem-")
        io = ZmodemIO(self._zmodem_write)
        callbacks = self._zmodem_callbacks()
        session = ZmodemSession(
            "receive",
            io,
            output_dir=temp_dir,
            on_file_start=lambda meta, path: self.zmodemFileStartSignal.emit(
                meta.name, meta.size
            ),
            on_progress=callbacks["on_progress"],
            on_complete=callbacks["on_complete"],
            on_error=callbacks["on_error"],
            on_cancelled=callbacks["on_cancelled"],
        )
        with self._zmodem_lock:
            self._zmodem_session = session
            self._zmodem_temp_dir = temp_dir
            self._zmodem_detector.reset()
        self.zmodemActiveSignal.emit(True)
        self.zmodemBusySignal.emit(_("Receiving..."))
        session.start()
        if detection.remainder:
            io.feed(detection.remainder)

    def _zmodem_start_pending_send(self, detection):
        self._zmodem_reset_progress()
        with self._zmodem_lock:
            self._zmodem_pending = detection.header
            self._zmodem_pending_buffer = detection.remainder
            self._zmodem_detector.reset()
        self.zmodemActiveSignal.emit(True)
        self.zmodemBusySignal.emit(_("Select files to send..."))
        self.zmodemSendRequestSignal.emit()

    def _zmodem_choose_send_files(self):
        with self._zmodem_lock:
            header = self._zmodem_pending
            buffered = self._zmodem_pending_buffer
        if header is None:
            return

        start_dir = self.config.get("zmodem_last_dir", "")
        files, _selected = QFileDialog.getOpenFileNames(
            self.widget, _("Select files to send"), start_dir
        )
        if not files:
            self._zmodem_clear_pending(cancel_remote=True)
            return

        self.config["zmodem_last_dir"] = os.path.dirname(files[0])
        self._zmodem_reset_progress()
        self._zmodem_mode = "send"
        self._zmodem_send_count = len(files)
        for path in files:
            try:
                self._zmodem_send_total += os.path.getsize(path)
            except OSError:
                pass
        io = ZmodemIO(self._zmodem_write)
        callbacks = self._zmodem_callbacks()
        session = ZmodemSession(
            "send",
            io,
            files=files,
            initial_zrinit=header,
            on_file_start=lambda name, size: self.zmodemFileStartSignal.emit(
                name, size
            ),
            on_progress=callbacks["on_progress"],
            on_complete=callbacks["on_complete"],
            on_error=callbacks["on_error"],
            on_cancelled=callbacks["on_cancelled"],
        )
        with self._zmodem_lock:
            self._zmodem_pending = None
            self._zmodem_pending_buffer = b""
            self._zmodem_session = session
        self._zmodem_set_busy(_("Sending..."))
        session.start()
        if buffered:
            io.feed(buffered)

    def _zmodem_clear_pending(self, cancel_remote: bool):
        with self._zmodem_lock:
            self._zmodem_pending = None
            self._zmodem_pending_buffer = b""
        if cancel_remote:
            # Drop queued payload before writing the CAN sequence so the
            # remote sees the abort instead of another kilobyte of data.
            self.ctrlConn("purge", None)
            self.send(b"\x18" * 8 + b"\x08" * 8)
        self._zmodem_restore_terminal()
        status_bar = getattr(self, "statusBar", None)
        if status_bar is not None:
            status_bar.setMsg("info", _("File transfer cancelled"))

    def _zmodem_cancel(self):
        with self._zmodem_lock:
            session = self._zmodem_session
            pending = self._zmodem_pending
        if session is not None and session.active:
            # Discard queued data first; the ZCAN written below then goes out
            # immediately instead of sitting behind a kilobyte of payload.
            self.ctrlConn("purge", None)
            session.cancel()
            if self.zmodemLabel is not None:
                self.zmodemLabel.setText(_("Cancelling..."))
            if self.zmodemCancel is not None:
                self.zmodemCancel.setEnabled(False)
            return
        if pending is not None:
            self._zmodem_clear_pending(cancel_remote=True)

    def _zmodem_abort_from_ui(self):
        with self._zmodem_lock:
            session = self._zmodem_session
            pending = self._zmodem_pending
            temp_dir = self._zmodem_temp_dir
            self._zmodem_session = None
            self._zmodem_pending = None
            self._zmodem_pending_buffer = b""
            self._zmodem_temp_dir = None
        self.ctrlConn("purge", None)
        if session is not None:
            session.cancel()
        if pending is not None:
            self.send(b"\x18" * 8 + b"\x08" * 8)
        self._zmodem_cleanup_temp(temp_dir)
        self._zmodem_restore_terminal()

    def _zmodem_set_transfer_active(self, active: bool):
        widget = getattr(self, "widget", None)
        if widget is not None:
            widget.terminal.setTransferActive(active)

    def _zmodem_set_controls_visible(self, visible: bool):
        for widget in (self.zmodemLabel, self.zmodemBar, self.zmodemCancel):
            if widget is not None:
                widget.setVisible(visible)

    def _zmodem_reset_progress(self):
        self._zmodem_mode = None
        self._zmodem_send_count = 0
        self._zmodem_send_total = 0
        self._zmodem_send_index = 0
        self._zmodem_send_done = 0
        self._zmodem_send_current = 0
        self._zmodem_recv_count = 0
        self._zmodem_recv_bytes = 0
        self._zmodem_recv_current = 0

    def _zmodem_set_busy(self, text: str):
        if self.zmodemLabel is None:
            return
        self.zmodemLabel.setText(text)
        self.zmodemBar.setRange(0, 0)
        self.zmodemCancel.setEnabled(True)
        self._zmodem_set_controls_visible(True)
        status_bar = getattr(self, "statusBar", None)
        if status_bar is not None:
            status_bar.setMsg("info", text)

    def _zmodem_restore_terminal(self):
        self._zmodem_set_transfer_active(False)
        self._zmodem_set_controls_visible(False)
        self._zmodem_reset_progress()

    def _zmodem_cleanup_temp(self, temp_dir):
        if temp_dir:
            shutil.rmtree(temp_dir, ignore_errors=True)
        with self._zmodem_lock:
            if self._zmodem_temp_dir == temp_dir:
                self._zmodem_temp_dir = None

    def _zmodem_on_file_start(self, name: str, size):
        if self.zmodemLabel is None:
            return
        if self._zmodem_mode == "send":
            if self._zmodem_send_index > 0:
                self._zmodem_send_done += self._zmodem_send_current
            self._zmodem_send_index += 1
            self._zmodem_send_current = int(size or 0)
            total = self._zmodem_send_total
            self.zmodemLabel.setText(
                "%s  [%d/%d]  %d%%  %s/%s"
                % (
                    name,
                    self._zmodem_send_index,
                    max(self._zmodem_send_count, self._zmodem_send_index),
                    int(self._zmodem_send_done * 100 / total) if total else 0,
                    _format_size(self._zmodem_send_done),
                    _format_size(total),
                )
            )
            if total > 0:
                self.zmodemBar.setRange(0, total)
                self.zmodemBar.setValue(self._zmodem_send_done)
            else:
                self.zmodemBar.setRange(0, 0)
        elif self._zmodem_mode == "receive":
            if self._zmodem_recv_count > 0:
                self._zmodem_recv_bytes += self._zmodem_recv_current
            self._zmodem_recv_current = int(size or 0)
            self._zmodem_recv_count += 1
            self.zmodemLabel.setText(
                "%s  [%d]  %s"
                % (
                    name,
                    self._zmodem_recv_count,
                    _format_size(self._zmodem_recv_bytes),
                )
            )
            if size:
                self.zmodemBar.setRange(0, int(size))
                self.zmodemBar.setValue(0)
            else:
                self.zmodemBar.setRange(0, 0)
        else:
            self.zmodemLabel.setText(name)
            if size:
                self.zmodemBar.setRange(0, int(size))
                self.zmodemBar.setValue(0)
            else:
                self.zmodemBar.setRange(0, 0)
        self._zmodem_set_controls_visible(True)

    def _zmodem_on_progress(self, name: str, sent: int, total):
        if self.zmodemLabel is None:
            return
        if self._zmodem_mode == "send" and self._zmodem_send_total > 0:
            overall = min(
                self._zmodem_send_done + int(sent), self._zmodem_send_total
            )
            self.zmodemBar.setRange(0, self._zmodem_send_total)
            self.zmodemBar.setValue(overall)
            self.zmodemLabel.setText(
                "%s  [%d/%d]  %d%%  %s/%s"
                % (
                    name,
                    max(1, self._zmodem_send_index),
                    max(self._zmodem_send_count, self._zmodem_send_index, 1),
                    int(overall * 100 / self._zmodem_send_total),
                    _format_size(overall),
                    _format_size(self._zmodem_send_total),
                )
            )
        elif self._zmodem_mode == "receive" and total:
            self.zmodemBar.setRange(0, int(total))
            self.zmodemBar.setValue(min(int(sent), int(total)))
            self.zmodemLabel.setText(
                "%s  [%d]  %d%%  %s"
                % (
                    name,
                    max(1, self._zmodem_recv_count),
                    int(int(sent) * 100 / int(total)),
                    _format_size(self._zmodem_recv_bytes + int(sent)),
                )
            )
        else:
            self.zmodemLabel.setText(name)
            if total:
                self.zmodemBar.setRange(0, int(total))
                self.zmodemBar.setValue(min(int(sent), int(total)))
            else:
                self.zmodemBar.setRange(0, 0)

    def _zmodem_on_complete(self, files, trailing: bytes):
        with self._zmodem_lock:
            self._zmodem_session = None
            temp_dir = self._zmodem_temp_dir
            self._zmodem_temp_dir = None
        self._zmodem_restore_terminal()
        if trailing:
            # Never hand a multi-megabyte binary tail to the terminal
            # renderer: it can block the UI for minutes.
            self._feed_terminal(trailing[-4096:])

        if files is None:
            status_bar = getattr(self, "statusBar", None)
            if status_bar is not None:
                status_bar.setMsg("info", _("Files sent"))
            self._zmodem_cleanup_temp(temp_dir)
            return

        saved = 0
        for received in files:
            start_dir = self.config.get("zmodem_last_dir", "")
            initial = os.path.join(start_dir, received.name) if start_dir else received.name
            path, _selected = QFileDialog.getSaveFileName(
                self.widget, _("Save received file"), initial
            )
            if path:
                try:
                    shutil.move(received.path, path)
                    saved += 1
                    self.config["zmodem_last_dir"] = os.path.dirname(path)
                except Exception as error:
                    if self.hintSignal is not None:
                        self.hintSignal.emit(
                            "error", _("Error"), "%s\n%s" % (path, str(error))
                        )
            else:
                try:
                    os.remove(received.path)
                except OSError:
                    pass

        self._zmodem_cleanup_temp(temp_dir)
        status_bar = getattr(self, "statusBar", None)
        if status_bar is not None:
            status_bar.setMsg(
                "info", _("Received {} file(s)").format(saved)
            )

    def _zmodem_on_error(self, message: str):
        with self._zmodem_lock:
            self._zmodem_session = None
            temp_dir = self._zmodem_temp_dir
            self._zmodem_temp_dir = None
        self._zmodem_cleanup_temp(temp_dir)
        self._zmodem_restore_terminal()
        self.ctrlConn("purge", None)
        status_bar = getattr(self, "statusBar", None)
        if status_bar is not None:
            status_bar.setMsg("error", message)
        if self.hintSignal is not None:
            self.hintSignal.emit("error", _("ZMODEM error"), message)

    def _zmodem_on_cancelled(self):
        with self._zmodem_lock:
            self._zmodem_session = None
            temp_dir = self._zmodem_temp_dir
            self._zmodem_temp_dir = None
        self._zmodem_cleanup_temp(temp_dir)
        self._zmodem_restore_terminal()
        status_bar = getattr(self, "statusBar", None)
        if status_bar is not None:
            status_bar.setMsg("info", _("File transfer cancelled"))

    def onKeyPressEvent(self, event):
        pass

    def onKeyReleaseEvent(self, event):
        pass

    def onUiInitDone(self):
        '''
            UI init done, you can update your widget here
            this method runs in UI thread, do not block too long
        '''
        self.zmodemLabel = QLabel("")
        self.zmodemBar = QProgressBar()
        self.zmodemBar.setFixedWidth(150)
        self.zmodemBar.setTextVisible(True)
        self.zmodemBar.setFormat("%p%")
        self.zmodemBar.setRange(0, 0)
        self.zmodemCancel = QPushButton(_("Cancel"))
        self.zmodemCancel.setFixedWidth(90)
        layout = self.statusBar.layout()
        layout.addWidget(self.zmodemLabel)
        layout.addWidget(self.zmodemBar)
        layout.addWidget(self.zmodemCancel)
        self._zmodem_set_controls_visible(False)
        self.zmodemCancel.clicked.connect(self._zmodem_cancel)
        self.zmodemSendRequestSignal.connect(self._zmodem_choose_send_files)
        self.zmodemBusySignal.connect(self._zmodem_set_busy)
        self.zmodemActiveSignal.connect(self._zmodem_set_transfer_active)
        self.zmodemProgressSignal.connect(self._zmodem_on_progress)
        self.zmodemFileStartSignal.connect(self._zmodem_on_file_start)
        self.zmodemCompleteSignal.connect(self._zmodem_on_complete)
        self.zmodemErrorSignal.connect(self._zmodem_on_error)
        self.zmodemCancelledSignal.connect(self._zmodem_on_cancelled)

    def onConnChanged(self, status:ConnectionStatus, msg:str):
        '''
            call in UI thread, be carefully!!
        '''
        if status != ConnectionStatus.CONNECTED:
            self._zmodem_abort_from_ui()
        super().onConnChanged(status, msg)
        if status == ConnectionStatus.CONNECTED:
            # set pty size when first connect
            self.widget.terminal.resizeEvent("")
            # auto focus terminal
            self.widget.terminal.setFocus()

