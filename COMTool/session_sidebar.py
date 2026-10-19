"""Left-hand list of remembered serial sessions."""

from PyQt5.QtCore import QSize, Qt, QTimer, pyqtSignal
from PyQt5.QtGui import QPalette
from PyQt5.QtWidgets import (
    QHBoxLayout,
    QLabel,
    QListWidget,
    QListWidgetItem,
    QMenu,
    QVBoxLayout,
    QWidget,
)

import qtawesome as qta

try:
    from .i18n import _
    from .serial_sessions import normalize_port
except ImportError:
    from i18n import _
    from serial_sessions import normalize_port


class SessionItemWidget(QWidget):
    """Two-line session row: port name plus hardware description."""

    def __init__(self, port, detail, connected, parent=None):
        super().__init__(parent)
        self.setObjectName("sessionItem")
        self.setAttribute(Qt.WA_TransparentForMouseEvents, True)
        layout = QHBoxLayout(self)
        layout.setContentsMargins(7, 3, 4, 3)
        layout.setSpacing(7)

        self.iconLabel = QLabel()
        self.iconLabel.setFixedSize(18, 18)
        self.iconLabel.setAttribute(Qt.WA_TransparentForMouseEvents, True)
        layout.addWidget(self.iconLabel, 0, Qt.AlignVCenter)

        text = QVBoxLayout()
        text.setContentsMargins(0, 0, 0, 0)
        text.setSpacing(0)
        self.nameLabel = QLabel(port)
        self.nameLabel.setObjectName("sessionName")
        self.nameLabel.setAttribute(Qt.WA_TransparentForMouseEvents, True)
        text.addWidget(self.nameLabel)
        if detail:
            # Keep MobaXterm's "COMxx (hardware description)" convention.
            self.detailLabel = QLabel("({})".format(detail))
            self.detailLabel.setObjectName("sessionDescription")
            self.detailLabel.setAttribute(Qt.WA_TransparentForMouseEvents, True)
            text.addWidget(self.detailLabel)
        else:
            self.detailLabel = None
        layout.addLayout(text, 1)
        dark = self.palette().color(QPalette.Window).lightness() < 128
        self._name_color = "#e6edf3" if dark else "#263238"
        self._detail_color = "#9aa7b1" if dark else "#607d8b"
        self._selected_color = "#0f88eb"
        self.set_selected(False)
        self.set_connected(connected)

    def set_connected(self, connected):
        color = "#43a047" if connected else "#90a4ae"
        self.iconLabel.setPixmap(
            qta.icon("fa.plug", color=color).pixmap(QSize(14, 14))
        )

    def set_selected(self, selected):
        name_color = self._selected_color if selected else self._name_color
        detail_color = self._selected_color if selected else self._detail_color
        self.nameLabel.setStyleSheet(
            "color: {}; font-weight: 600;".format(name_color)
        )
        if self.detailLabel is not None:
            self.detailLabel.setStyleSheet(
                "color: {}; font-size: 11px;".format(detail_color)
            )


class SessionSidebar(QWidget):
    sessionActivated = pyqtSignal(str)
    removeRequested = pyqtSignal(str)

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("sessionSidebar")
        self.setMinimumWidth(160)
        self.setMaximumWidth(320)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 5, 0, 0)
        layout.setSpacing(5)

        header = QHBoxLayout()
        header.setContentsMargins(10, 1, 8, 1)
        header.setSpacing(6)
        self.headerIcon = QLabel()
        self.headerIcon.setObjectName("sessionSidebarIcon")
        self.headerIcon.setPixmap(
            qta.icon("fa.star", color="#f2b01e").pixmap(QSize(15, 15))
        )
        self.headerIcon.setAttribute(Qt.WA_TransparentForMouseEvents, True)
        self.headerUserIcon = QLabel()
        self.headerUserIcon.setObjectName("sessionSidebarUserIcon")
        self.headerUserIcon.setPixmap(
            qta.icon("fa.user", color="#5b8fd6").pixmap(QSize(15, 15))
        )
        self.headerUserIcon.setAttribute(Qt.WA_TransparentForMouseEvents, True)
        title = QLabel(_("User sessions"))
        title.setObjectName("sessionSidebarTitle")
        header.addWidget(self.headerIcon, 0, Qt.AlignVCenter)
        header.addWidget(self.headerUserIcon, 0, Qt.AlignVCenter)
        header.addWidget(title)
        header.addStretch(1)
        layout.addLayout(header)

        self.listWidget = QListWidget()
        self.listWidget.setObjectName("sessionList")
        self.listWidget.setContextMenuPolicy(Qt.CustomContextMenu)
        self.listWidget.setSelectionMode(QListWidget.SingleSelection)
        self.listWidget.setUniformItemSizes(False)
        self.listWidget.setWordWrap(True)
        self.listWidget.setTextElideMode(Qt.ElideNone)
        self.listWidget.itemClicked.connect(self._onItemClicked)
        self.listWidget.itemSelectionChanged.connect(self._updateSelectionColors)
        self.listWidget.customContextMenuRequested.connect(self._showContextMenu)
        layout.addWidget(self.listWidget, 1)

        self.messageLabel = QLabel("")
        self.messageLabel.setObjectName("sessionSidebarMessage")
        self.messageLabel.setWordWrap(True)
        self.messageLabel.hide()
        layout.addWidget(self.messageLabel)
        self._messageTimer = QTimer(self)
        self._messageTimer.setSingleShot(True)
        self._messageTimer.timeout.connect(self._clearMessage)

    def set_sessions(self, sessions, connected_ports=None, active_port=None):
        connected = {normalize_port(port) for port in (connected_ports or [])}
        active = normalize_port(active_port)
        self.listWidget.clear()

        if not sessions:
            item = QListWidgetItem(_("No saved serial sessions"))
            item.setFlags(Qt.NoItemFlags)
            item.setSizeHint(QSize(0, 32))
            self.listWidget.addItem(item)
            return

        selected = None
        for session in sessions:
            port = session["port"]
            name = session.get("name") or port
            if "  " in name:
                head, detail = name.split("  ", 1)
            else:
                head, detail = port, ""
            is_connected = normalize_port(port) in connected
            item = QListWidgetItem()
            item.setData(Qt.UserRole, port)
            item.setToolTip(name)
            item.setSizeHint(QSize(0, 40))
            self.listWidget.addItem(item)
            self.listWidget.setItemWidget(
                item, SessionItemWidget(head, detail, is_connected)
            )
            if normalize_port(port) == active:
                selected = item
        if selected is not None:
            self.listWidget.setCurrentItem(selected)
        self._updateSelectionColors()

    def _updateSelectionColors(self):
        for index in range(self.listWidget.count()):
            item = self.listWidget.item(index)
            row = self.listWidget.itemWidget(item)
            if isinstance(row, SessionItemWidget):
                row.set_selected(item.isSelected())

    def set_port_status(self, port, connected):
        key = normalize_port(port)
        for index in range(self.listWidget.count()):
            item = self.listWidget.item(index)
            if normalize_port(item.data(Qt.UserRole)) == key:
                row = self.listWidget.itemWidget(item)
                if isinstance(row, SessionItemWidget):
                    row.set_connected(connected)
                return

    def update_active(self, active_port):
        active = normalize_port(active_port)
        selected = None
        for index in range(self.listWidget.count()):
            item = self.listWidget.item(index)
            if normalize_port(item.data(Qt.UserRole)) == active:
                selected = item
                break
        self.listWidget.setCurrentItem(selected)

    def show_message(self, text, timeout_ms=4000):
        if not text:
            return
        self.messageLabel.setText(text)
        self.messageLabel.show()
        self._messageTimer.start(timeout_ms)

    def _clearMessage(self):
        self.messageLabel.clear()
        self.messageLabel.hide()

    def _onItemClicked(self, item):
        port = item.data(Qt.UserRole)
        if port:
            self.sessionActivated.emit(str(port))

    def _showContextMenu(self, pos):
        item = self.listWidget.itemAt(pos)
        if item is None:
            return
        port = item.data(Qt.UserRole)
        if not port:
            return
        menu = QMenu(self)
        openAction = menu.addAction(_("Open"))
        removeAction = menu.addAction(_("Remove session"))
        action = menu.exec_(self.listWidget.viewport().mapToGlobal(pos))
        if action == openAction:
            self.sessionActivated.emit(str(port))
        elif action == removeAction:
            self.removeRequested.emit(str(port))
