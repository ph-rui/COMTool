"""Left-hand list of remembered serial and SSH sessions."""

from PyQt5.QtCore import QSize, Qt, QTimer, pyqtSignal
from PyQt5.QtGui import QColor, QPalette
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
    from .ssh_sessions import SSH_SESSION_PREFIX
except ImportError:
    from i18n import _
    from serial_sessions import normalize_port
    from ssh_sessions import SSH_SESSION_PREFIX


class SessionItemWidget(QWidget):
    """Two-line session row: name plus endpoint/hardware description."""

    def __init__(self, name, detail, connected, icon_name="fa.plug", parent=None):
        super().__init__(parent)
        self.setObjectName("sessionItem")
        self.setAttribute(Qt.WA_TransparentForMouseEvents, True)
        self._icon_name = icon_name
        layout = QHBoxLayout(self)
        layout.setContentsMargins(6, 3, 4, 3)
        layout.setSpacing(6)

        self.iconLabel = QLabel()
        self.iconLabel.setFixedSize(16, 16)
        self.iconLabel.setAttribute(Qt.WA_TransparentForMouseEvents, True)
        layout.addWidget(self.iconLabel, 0, Qt.AlignVCenter)

        text = QVBoxLayout()
        text.setContentsMargins(0, 0, 0, 0)
        text.setSpacing(0)
        self.nameLabel = QLabel(name)
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
            qta.icon(self._icon_name, color=color).pixmap(QSize(14, 14))
        )

    def set_selected(self, selected):
        name_color = self._selected_color if selected else self._name_color
        detail_color = self._selected_color if selected else self._detail_color
        self.nameLabel.setStyleSheet(
            "color: {}; font-weight: 600;".format(name_color)
        )
        if self.detailLabel is not None:
            self.detailLabel.setStyleSheet(
                "color: {}; font-size: 10px;".format(detail_color)
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
        # Indent the bordered list under the header, like the nested
        # MobaXterm session tree. This keeps the header and rows visually
        # distinct without adding another toolbar.
        listRow = QHBoxLayout()
        listRow.setContentsMargins(16, 0, 0, 0)
        listRow.setSpacing(0)
        listRow.addWidget(self.listWidget)
        layout.addLayout(listRow, 1)

        self.messageLabel = QLabel("")
        self.messageLabel.setObjectName("sessionSidebarMessage")
        self.messageLabel.setContentsMargins(16, 0, 0, 0)
        self.messageLabel.setWordWrap(True)
        self.messageLabel.hide()
        layout.addWidget(self.messageLabel)
        self._messageTimer = QTimer(self)
        self._messageTimer.setSingleShot(True)
        self._messageTimer.timeout.connect(self._clearMessage)

    def set_sessions(self, sessions, connected_ports=None, active_port=None,
                     ssh_sessions=None, connected_ssh=None, active_ssh=None):
        connected = {normalize_port(port) for port in (connected_ports or [])}
        active = normalize_port(active_port)
        connected_ssh = set(connected_ssh or [])
        active_ssh = str(active_ssh or "")
        sessions = sessions or []
        ssh_sessions = ssh_sessions or []
        self.listWidget.clear()

        if not sessions and not ssh_sessions:
            item = QListWidgetItem(_("No saved sessions"))
            item.setFlags(Qt.NoItemFlags)
            item.setSizeHint(QSize(0, 32))
            self.listWidget.addItem(item)
            return

        selected = None
        if sessions and ssh_sessions:
            self._add_section_header(_("Serial"))
        for session in sessions:
            port = session["port"]
            name = session.get("name") or port
            if "  " in name:
                head, detail = name.split("  ", 1)
            else:
                head, detail = port, ""
            is_connected = normalize_port(port) in connected
            item = self._add_session_item(
                head,
                detail,
                is_connected,
                port,
                name,
                "fa.plug",
            )
            if normalize_port(port) == active:
                selected = item

        if ssh_sessions:
            self._add_section_header(_("SSH"))
        for session in ssh_sessions:
            head = session.get("alias") or session.get("host") or ""
            detail = "{}@{}:{}".format(
                session.get("user") or "",
                session.get("host") or "",
                session.get("port") or 22,
            )
            data = SSH_SESSION_PREFIX + session.get("key", "")
            tooltip = "{}\n{}".format(head, detail) if head else detail
            item = self._add_session_item(
                head,
                detail,
                session.get("key") in connected_ssh,
                data,
                tooltip,
                "fa.server",
            )
            if session.get("key") == active_ssh:
                selected = item

        if selected is not None:
            self.listWidget.setCurrentItem(selected)
        self._updateSelectionColors()

    def _add_section_header(self, text):
        dark = self.palette().color(QPalette.Window).lightness() < 128
        item = QListWidgetItem("  " + text)
        item.setFlags(Qt.NoItemFlags)
        item.setSizeHint(QSize(0, 26))
        font = item.font()
        font.setBold(True)
        font.setPointSize(max(7, font.pointSize() - 1))
        item.setFont(font)
        item.setForeground(QColor("#9aa7b1" if dark else "#607d8b"))
        self.listWidget.addItem(item)

    def _add_session_item(self, head, detail, connected, data, tooltip, icon_name):
        item = QListWidgetItem()
        item.setData(Qt.UserRole, data)
        item.setToolTip(tooltip)
        item.setSizeHint(QSize(0, 40))
        self.listWidget.addItem(item)
        self.listWidget.setItemWidget(
            item, SessionItemWidget(head, detail, connected, icon_name)
        )
        return item

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
            data = item.data(Qt.UserRole)
            if data is not None and normalize_port(data) == key:
                row = self.listWidget.itemWidget(item)
                if isinstance(row, SessionItemWidget):
                    row.set_connected(connected)
                return

    def set_ssh_status(self, key, connected):
        data = SSH_SESSION_PREFIX + str(key or "")
        for index in range(self.listWidget.count()):
            item = self.listWidget.item(index)
            if item.data(Qt.UserRole) == data:
                row = self.listWidget.itemWidget(item)
                if isinstance(row, SessionItemWidget):
                    row.set_connected(connected)
                return

    def update_active(self, active_port, active_ssh=None):
        active = normalize_port(active_port)
        active_ssh_data = SSH_SESSION_PREFIX + str(active_ssh or "")
        selected = None
        for index in range(self.listWidget.count()):
            item = self.listWidget.item(index)
            data = item.data(Qt.UserRole)
            if data is None:
                continue
            if ((active and normalize_port(data) == active)
                    or (active_ssh and data == active_ssh_data)):
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
        data = item.data(Qt.UserRole)
        if data:
            self.sessionActivated.emit(str(data))

    def _showContextMenu(self, pos):
        item = self.listWidget.itemAt(pos)
        if item is None:
            return
        data = item.data(Qt.UserRole)
        if not data:
            return
        menu = QMenu(self)
        openAction = menu.addAction(_("Open"))
        removeAction = menu.addAction(_("Remove session"))
        action = menu.exec_(self.listWidget.viewport().mapToGlobal(pos))
        if action == openAction:
            self.sessionActivated.emit(str(data))
        elif action == removeAction:
            self.removeRequested.emit(str(data))
