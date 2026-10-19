import sys,os

if sys.version_info < (3, 7):
    print("only support python >= 3.7, but now is {}".format(sys.version_info))
    sys.exit(1)

# Init lanuage first to ensure use function _  works correctly
try:
    import parameters
    import i18n
except ImportError:
    from COMTool import parameters
    from COMTool import i18n

def loadConfig():
    paramObj = parameters.Parameters()
    paramObj.load(parameters.configFilePath)
    return paramObj

log = parameters.log
log.i("loading config from", parameters.configFilePath)
programConfig = loadConfig()
log.i("loading config complete")
i18n.set_locale(programConfig["locale"])

try:
    import helpAbout,autoUpdate
    from Combobox import ComboBox
    from i18n import _
    import version
    import utils_ui
    from conn import ConnectionStatus, conns
    from plugins import builtinPlugins
    from pluginItems import PluginItem
    from widgets import TitleBar, CustomTitleBarWindowMixin, EventFilter, ButtonCombbox, HelpWidget
    from settings_behavior import should_auto_hide_settings
    from serial_sessions import SerialSessionStore, session_name, normalize_port
    from session_sidebar import SessionSidebar
    from session_watcher import SerialPortWatcher
    from connection_settings import ConnectionSettingsDialog
    from ssh_sessions import (
        SSH_SESSION_PREFIX,
        SshSessionStore,
        ssh_display_name,
        ssh_session_detail,
    )
    from secure_store import protect_password, unprotect_password
except ImportError:
    from COMTool import helpAbout,autoUpdate, utils_ui
    from COMTool.Combobox import ComboBox
    from COMTool.i18n import _
    from COMTool import version
    from COMTool.conn import ConnectionStatus, conns
    from COMTool.plugins import builtinPlugins
    from COMTool.pluginItems import PluginItem
    from .widgets import TitleBar, CustomTitleBarWindowMixin, EventFilter, ButtonCombbox, HelpWidget
    from COMTool.settings_behavior import should_auto_hide_settings
    from COMTool.serial_sessions import SerialSessionStore, session_name, normalize_port
    from COMTool.session_sidebar import SessionSidebar
    from COMTool.session_watcher import SerialPortWatcher
    from COMTool.connection_settings import ConnectionSettingsDialog
    from COMTool.ssh_sessions import (
        SSH_SESSION_PREFIX,
        SshSessionStore,
        ssh_display_name,
        ssh_session_detail,
    )
    from COMTool.secure_store import protect_password, unprotect_password

from PyQt5.QtCore import pyqtSignal, Qt, QRect, QMargins, QCoreApplication, QTimer
from PyQt5.QtWidgets import (QApplication, QWidget,QPushButton,QMessageBox,QDesktopWidget,QMainWindow,
                             QVBoxLayout,QHBoxLayout,QGridLayout,QTextEdit,QLabel,QRadioButton,QCheckBox,
                             QLineEdit,QGroupBox,QSplitter,QFileDialog, QScrollArea, QTabWidget, QMenu, QSplashScreen,
                             QAbstractSpinBox,QStackedWidget)
from PyQt5.QtGui import QIcon,QFont,QTextCursor,QPixmap,QColor, QCloseEvent, QCursor, QPainter, QPen
import qtawesome as qta # https://github.com/spyder-ide/qtawesome
import threading
import time
from datetime import datetime
import binascii,re
if sys.platform == "win32":
    import ctypes

g_all_windows = []


class SettingsHoverStrip(QWidget):
    """Thin invisible hover area at the left edge of a panel."""
    hovered = pyqtSignal()

    def __init__(self, parent=None, width=6):
        super().__init__(parent)
        self.setFixedWidth(width)
        self.setCursor(Qt.PointingHandCursor)

    def enterEvent(self, event):
        self.hovered.emit()
        super().enterEvent(event)


class SettingsHandle(QWidget):
    """Small chevron button shown next to the auto-hiding settings panel."""
    clicked = pyqtSignal()
    hovered = pyqtSignal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self._expanded = False
        self.setFixedSize(10, 48)
        self.setCursor(Qt.PointingHandCursor)
        self.setAttribute(Qt.WA_Hover, True)

    def setExpanded(self, expanded):
        if self._expanded != expanded:
            self._expanded = expanded
            self.update()

    def enterEvent(self, event):
        self.update()
        self.hovered.emit()
        super().enterEvent(event)

    def leaveEvent(self, event):
        self.update()
        super().leaveEvent(event)

    def mouseReleaseEvent(self, event):
        if event.button() == Qt.LeftButton and self.rect().contains(event.pos()):
            self.clicked.emit()
        super().mouseReleaseEvent(event)

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing)
        color = QColor("#2196f3")
        if self.underMouse():
            color = color.lighter(115)
        painter.setBrush(color)
        painter.setPen(Qt.NoPen)
        painter.drawRoundedRect(self.rect().adjusted(0, 0, -1, -1), 4, 4)
        pen = QPen(QColor("white"))
        pen.setWidth(2)
        pen.setCapStyle(Qt.RoundCap)
        pen.setJoinStyle(Qt.RoundJoin)
        painter.setPen(pen)
        cx = self.width() // 2
        cy = self.height() // 2
        if self._expanded:
            # Point left: click to collapse.
            painter.drawLine(cx + 2, cy - 6, cx - 2, cy)
            painter.drawLine(cx - 2, cy, cx + 2, cy + 6)
        else:
            # Point right: click to expand.
            painter.drawLine(cx - 2, cy - 6, cx + 2, cy)
            painter.drawLine(cx + 2, cy, cx - 2, cy + 6)


class MainWindow(CustomTitleBarWindowMixin, QMainWindow):
    hintSignal = pyqtSignal(str, str, str) # type(error, warning, info), title, msg
    # statusBarSignal = pyqtSignal(str, str)
    updateSignal = pyqtSignal(object)
    # countUpdateSignal = pyqtSignal(int, int)
    reloadWindowSignal = pyqtSignal(str, str, object)
    receiveCount = 0
    sendCount = 0
    DataPath = "./"
    app = None
    needRestart = False

    def __init__(self,app, eventFilter, config):
        QMainWindow.__init__(self)
        self.app = app
        self.eventFilter = eventFilter
        self.DataPath = parameters.dataPath
        self.config = config
        log.i("init main window")
        self.initVar()
        self.initWindow()
        self.uiLoadConfigs()
        log.i("init main window complete")
        self.loadPluginsInfoList()
        self.initSerialSessions()
        log.i("load serial sessions complete")
        self.initEvent()
        self.updateSettingsButton()
        self.updateMainStack()

    def initVar(self):
        self.loadPluginStr = _("Load plugin from file")
        self.closeTimerId = None
        self.items = []
        self.pluginClasses = []
        self.helpWindow = None
        # remembered serial and SSH sessions
        self.sessionStore = None
        self.sshSessionStore = None
        self.portWatcher = None
        self.sessionSidebar = None
        self.bodySplitter = None
        self.mainStack = None
        self.emptyWidget = None
        self.emptyPluginsSelector = None
        self.rightContent = None
        self.sessionsButton = None
        self._available_ports = set()
        self._available_ports_known = False
        self._port_descriptions = {}
        self._suppressed_session_ports = set()
        self._suppressed_ssh_session_keys = set()
        # Passwords are deliberately never written to config.json.  Keep the
        # last successful password in memory only so a session that is still
        # open in this run can reconnect from the sidebar without prompting.
        self._ssh_password_cache = {}
        # hover-to-show remembered-session sidebar
        self.sessionHoverStrip = None
        self.sessionHandle = None
        self._session_hover_seen = False
        self._session_shown_at = 0.0
        self._last_session_cursor_x = QCursor.pos().x()
        self._session_hover_timer = QTimer(self)
        self._session_hover_timer.setInterval(250)
        self._session_hover_timer.timeout.connect(self._checkSessionSidebarAutoHide)


    def initSerialSessions(self):
        self.sessionStore = SerialSessionStore(self.config)
        self.sshSessionStore = SshSessionStore(self.config)
        config_dict = getattr(self.config, "config", self.config)
        if not config_dict.get("serialSessionsMigrated", False):
            migrated = self.sessionStore.migrate_items(self.config["items"])
            self.config["serialSessionsMigrated"] = True
            self.sessionStore.save(self.config)
            if migrated:
                log.i("migrated {} serial session(s)".format(migrated))
            self.saveConfig()
        if not config_dict.get("sshSessionsMigrated", False):
            migrated = self.sshSessionStore.migrate_items(self.config["items"])
            self.config["sshSessionsMigrated"] = True
            self.sshSessionStore.save(self.config)
            if migrated:
                log.i("migrated {} SSH session(s)".format(migrated))
            self.saveConfig()
        self.refreshSessionSidebar()
        self.portWatcher = SerialPortWatcher(self)
        self.portWatcher.portsScanned.connect(self.onPortsScanned)
        self.portWatcher.start()

    def loadPluginsInfoList(self):
        for id, pluginClass in builtinPlugins.items(): 
            self.addPluginInfo(pluginClass)
            self.pluginClasses.append(pluginClass)
        rm = []
        for uid, info in self.config["pluginsInfo"]["external"].items():
            pluginClass = self._importPlugin(info["path"], test=True)
            if pluginClass:
                self.addPluginInfo(pluginClass)
                self.pluginClasses.append(pluginClass)
            else:
                rm.append(uid)
        for uid in rm:
            self.config["pluginsInfo"]["external"].pop(uid)
        # find in python packages
        ignore_paths = ["DLLs"]
        for path in sys.path:
            if os.path.isdir(path) and not path in ignore_paths:
                for name in os.listdir(path):
                    if name.lower().startswith("comtool_plugin_") and not name.endswith("dist-info"):
                        log.i(f"find plugin package <{name}>")
                        pluginClass = __import__(name).Plugin
                        print(pluginClass, self.pluginClasses)
                        if not pluginClass in self.pluginClasses:
                            self.addPluginInfo(pluginClass)
                            self.pluginClasses.append(pluginClass)

    def getPluginClassById(self, id):
        '''
            must call after loadPluginsInfoList
        '''
        for pluginClass in self.pluginClasses:
            if id == pluginClass.id:
                return pluginClass
        return None

    def loadPluginItems(self):
        items = self.config["items"]
        if items:
            for item in items:
                log.i("load plugin item", item["name"])
                pluginClass = self.getPluginClassById(item["pluginId"])
                if pluginClass:
                    setCurr = False
                    if self.config["currItem"] == item["name"]:
                        setCurr = True
                    # check language change, update item name to current lanuage
                    old_name_tail = item["name"].split(" ")[-1]
                    try:
                        int(old_name_tail)
                        item["name"] = pluginClass.name + " " + old_name_tail
                    except Exception: # for no number tailed name
                        item["name"] = pluginClass.name
                    self.addItem(pluginClass, nameSaved=item["name"], setCurrent=setCurr, connsConfigs = item["config"]["conns"], pluginConfig=item["config"]["plugin"])
        else:  # load builtin plugins
            for id, pluginClass in builtinPlugins.items(): 
                self.addItem(pluginClass)

    def addItem(self, pluginClass, nameSaved = None, setCurrent = False, connsConfigs=None, pluginConfig=None):
        '''
            @name add saved item, not add new item
        '''
        # set here, not set in arg, cause arg can lead to multi item use one object
        if not connsConfigs:
            connsConfigs = {}
        if not pluginConfig:
            pluginConfig = {}
        if nameSaved:
            name = nameSaved
        else:
            numbers = []
            for item in self.items:
                if item.plugin.id == pluginClass.id:
                    name = item.name.replace(item.plugin.name, "").split(" ")
                    if len(name) > 1:
                        number = int(name[-1])
                        numbers.append(number)
                    else:
                        numbers.append(0)
            if numbers:
                numbers = sorted(numbers)
            if (not numbers) or numbers[0] != 0:
                name = pluginClass.name
            else:
                last = numbers[0]
                number = -1
                for n in numbers[1:]:
                    if n != last + 1:
                        number = last + 1
                        break
                    last = n
                if number < 0:
                    number = numbers[-1] + 1
                name = f'{pluginClass.name} {number}'
        item = PluginItem(name, pluginClass,
                        conns, connsConfigs,
                        self.config, pluginConfig,
                        self.hintSignal, self.reloadWindowSignal,
                        self.onConnChnaged)
        self._wireSshSignals(item)
        item.titleChanged = self.onItemTitleChanged
        self.tabAddItem(item)
        self.items.append(item)
        if setCurrent:
            self.tabWidget.setCurrentWidget(item.widget)
        if not nameSaved:
            config_entry = {
                    "name": name,
                    "pluginId": pluginClass.id,
                    "config": {
                        "conns": connsConfigs,
                        "plugin": pluginConfig
                        }
                }
            self.config["items"].append(config_entry)
            item.configEntry = config_entry
        self.updateMainStack()
        self.refreshSessionSidebar()
        return item

    def tabAddItem(self, item):
        title = item.getDisplayName()
        self.tabWidget.addTab(item.widget, title)
        self.tabWidget.setTabToolTip(
            self.tabWidget.count() - 1,
            self._tabToolTip(item),
        )

    def _tabToolTip(self, item):
        tooltip = item.getDisplayName()
        try:
            detail = item.getDisplayDetail()
        except Exception:
            detail = ""
        if detail:
            tooltip = "{}  ({})".format(tooltip, detail)
        return tooltip + _(", Double click to detach as a window")

    def onItemTitleChanged(self, item):
        title = item.getDisplayName()
        for i in range(self.tabWidget.count()):
            if self.tabWidget.widget(i) == item.widget:
                self.tabWidget.setTabText(i, title)
                self.tabWidget.setTabToolTip(i, self._tabToolTip(item))
                break
        if item.widget.isWindow():
            item.widget.setWindowTitle(title)
        try:
            if item.plugin.id == "terminal" and item.isSshConnection():
                self.recordSshSession(item)
                self.refreshSessionSidebar()
        except Exception as exc:
            log.w("update SSH session title failed: {}".format(exc))

    def onConnChnaged(self, plugin, status:ConnectionStatus, msg):
        for item in self.items:
            if item.plugin == plugin:
                title = item.getDisplayName()
                for i in range(self.tabWidget.count()):
                    if self.tabWidget.widget(i) == item.widget:
                        self.setTabIcon(status, i)
                        self.tabWidget.setTabText(i, title)
                        self.tabWidget.setTabToolTip(i, self._tabToolTip(item))
                        break
                item.widget.setWindowTitle(title + " - {}".format(_("Connected" if status == ConnectionStatus.CONNECTED else _("Connection lose") if status == ConnectionStatus.LOSE else _("Disconnected"))))
                if (status == ConnectionStatus.CONNECTED
                        and getattr(item, "settingsDetached", False)):
                    dialog = getattr(item, "settingsDialog", None)
                    if dialog is not None and dialog.isVisible():
                        dialog.close()
                        self.updateSettingsButton()
                try:
                    if item.plugin.id == "terminal" and item.isSerialConnection():
                        port = item.getSerialPort()
                        if status == ConnectionStatus.CONNECTED:
                            item.serialSessionRecorded = True
                            self._suppressed_session_ports.discard(normalize_port(port))
                            self.recordSerialSession(item, allow_unavailable=True)
                        elif status in (ConnectionStatus.CLOSED, ConnectionStatus.LOSE):
                            self.recordSerialSession(item)
                        self.sessionSidebar.set_port_status(
                            port, status == ConnectionStatus.CONNECTED
                        )
                    elif item.plugin.id == "terminal" and item.isSshConnection():
                        session_key = item.getSshSessionKey()
                        if status == ConnectionStatus.CONNECTED:
                            item.sshSessionRecorded = True
                            self._suppressed_ssh_session_keys.discard(session_key)
                            self._cacheSshPassword(item)
                            self.recordSshSession(item)
                        elif status in (ConnectionStatus.CLOSED, ConnectionStatus.LOSE):
                            self.recordSshSession(item)
                except Exception as exc:
                    log.w("update remembered session from connection status failed: {}".format(exc))
                self.refreshSessionSidebar()
                break

    def setTabIcon(self, status:ConnectionStatus, i:int):
        if status == ConnectionStatus.CONNECTED:
            self.tabWidget.setTabIcon(i, qta.icon("fa.circle", color="#4caf50"))
        elif status == ConnectionStatus.LOSE:
            self.tabWidget.setTabIcon(i, qta.icon("fa.circle", color="orange"))
        else:
            self.tabWidget.setTabIcon(i, QIcon())

    def addPluginInfo(self, pluginClass):
        text = f'{pluginClass.name} - {pluginClass.id}'
        for selector in (self.pluginsSelector, getattr(self, "emptyPluginsSelector", None)):
            if selector is not None:
                selector.insertItem(selector.count() - 1, text)

    def _importPlugin(self, path, test = False):
        if not os.path.exists(path):
            return None
        dir = os.path.dirname(path)
        name = os.path.splitext(os.path.basename(path))[0]
        sys.path.insert(0, dir)
        try:
            print("import")
            pluginClass = __import__(name).Plugin
        except Exception as e:
            import traceback
            msg = traceback.format_exc()
            self.hintSignal.emit("error", _("Error"), '{}: {}'.format(_("Load plugin failed"), msg))
            return None
        if test:
            sys.path.remove(dir)
        return pluginClass

    def loadExternalPlugin(self, path):
        extPlugsInfo = self.config["pluginsInfo"]["external"]
        found = False
        for uid, info in extPlugsInfo.items():
            if info["path"] == path:
                for pluginClass in self.pluginClasses:
                    # same plugin
                    if uid == pluginClass.id:
                        self.addItem(pluginClass, setCurrent = True)
                        found = True
                        break
                if found:
                    return True, ""
        pluginClass = self._importPlugin(path)
        if not pluginClass:
            return False, _("Load plugin fail")
        extPlugsInfo[pluginClass.id] = {
            "path": path
        }
        if not pluginClass in self.pluginClasses:
            self.pluginClasses.append(pluginClass)
            self.addPluginInfo(pluginClass)
        # find old item config for this plugin and recover
        oldFound = False
        for item in self.config["items"]:
            if item["pluginId"] == pluginClass.id:
                self.addItem(pluginClass, nameSaved=item["name"], setCurrent=True, connsConfigs=item["config"]["conns"], pluginConfig=item["config"]["plugin"])
                oldFound = True
                break
        if not oldFound:
            self.addItem(pluginClass, setCurrent=True)
        return True, ""

    def onPluginSelectorChanged(self, idx, selector=None):
        selector = selector or self.pluginsSelector
        text = selector.currentText()
        if text == self.loadPluginStr:
            oldPath = os.getcwd()
            fileName_choose, filetype = QFileDialog.getOpenFileName(self,
                                    _("Select file"),
                                    oldPath,
                                    _("python script (*.py)"))
            if fileName_choose != "":
                ok, msg = self.loadExternalPlugin(fileName_choose)
                if not ok:
                    self.hintSignal.emit("error", _("Error"), f'{_("Load plugin error")}: {msg}')
        else:
            loadID = text.split("-")[-1].strip()
            for pluginClass in self.pluginClasses:
                if loadID == pluginClass.id:
                    item = self.addItem(pluginClass, setCurrent = True)
                    if item is not None and getattr(item, "settingsDetached", False):
                        self.showSettings()
                    break

    def initWindow(self):
        # set skin for utils_ui
        utils_ui.setSkin(self.config["skin"])
        # menu layout
        self.settingsButton = QPushButton()
        self.settingsButton.setCheckable(True)
        self.settingsButton.setFixedSize(27, 27)
        self.settingsButton.setObjectName("connectionSettingsButton")
        utils_ui.setButtonIcon(self.settingsButton, "fa.cog")
        self.settingsButton.setToolTip(_("Settings"))
        self.skinButton = ButtonCombbox(icon=None, btnClass="menuItem", btnId="menuItem2")
        self.languageCombobox = ButtonCombbox(icon=None, btnClass="menuItem", btnId="menuItemLang")
        self.languages = i18n.get_languages()
        for locale in self.languages:
            self.languageCombobox.addItem(self.languages[locale])
        for skin_name in utils_ui.get_skins():
            self.skinButton.addItem(_(skin_name))
        self.aboutButton = QPushButton()
        self.functionalButton = QPushButton()
        self.encodingCombobox = ComboBox()
        self.supportedEncoding = parameters.encodings
        for encoding in self.supportedEncoding:
            self.encodingCombobox.addItem(encoding)
        self.aboutButton.setProperty("class", "menuItem")
        self.functionalButton.setProperty("class", "menuItem")
        self.aboutButton.setObjectName("menuItem3")
        self.functionalButton.setObjectName("menuItem4")
        # plugins slector
        self.pluginsSelector = ButtonCombbox(icon="fa.plus", btnClass="smallBtn2")
        self.pluginsSelector.addItem(self.loadPluginStr)
        self.pluginsSelector.activated.connect(self.onPluginSelectorChanged)
        self.emptyPluginsSelector = ButtonCombbox(icon="fa.plus", btnClass="smallBtn2")
        self.emptyPluginsSelector.addItem(self.loadPluginStr)

        # sessions toggle in the title bar is always visible, even with no tabs
        self.sessionsButton = QPushButton()
        self.sessionsButton.setCheckable(True)
        self.sessionsButton.setChecked(True)
        self.sessionsButton.setFixedHeight(35)
        self.sessionsButton.setMinimumWidth(35)
        self.sessionsButton.setProperty("class", "menuItem")
        utils_ui.setButtonIcon(self.sessionsButton, "fa.list")
        self.sessionsButton.setToolTip(_("Show/hide sessions"))

        # title bar
        title = parameters.appName+" v"+version.__version__
        iconPath = self.DataPath+"/"+parameters.appIcon
        log.i("icon path: " + iconPath)
        self.titleBar = TitleBar(self, icon=iconPath, title=title, brothers=[], widgets=[[self.sessionsButton], []])
        CustomTitleBarWindowMixin.__init__(self, titleBar=self.titleBar, init = True)
        self.setWindowTitle(title)

        # root layout
        self.frameWidget = QWidget()
        self.frameWidget.setMouseTracking(True)
        self.frameWidget.setLayout(self.rootLayout)
        self.setCentralWidget(self.frameWidget)
        # tab widgets
        self.tabWidget = QTabWidget()
        self.tabWidget.setTabsClosable(True)
        # tab left menu
        tabConerWidget = QWidget()
        tabConerLayout = QHBoxLayout()
        tabConerLayout.setSpacing(0)
        tabConerLayout.setContentsMargins(0, 0, 0, 0)
        tabConerWidget.setLayout(tabConerLayout)
        tabConerLayout.addWidget(self.settingsButton)
        # tab right menu
        tabConerWidgetRight = QWidget()
        tabConerLayoutRight = QHBoxLayout()
        tabConerLayoutRight.setSpacing(0)
        tabConerLayoutRight.setContentsMargins(0, 0, 0, 0)
        tabConerWidgetRight.setLayout(tabConerLayoutRight)
        tabConerLayoutRight.addWidget(self.pluginsSelector)
        tabConerLayoutRight.addWidget(self.encodingCombobox)
        tabConerLayoutRight.addWidget(self.functionalButton)
        self.tabWidget.setCornerWidget(tabConerWidget, Qt.TopLeftCorner)
        self.tabWidget.setCornerWidget(tabConerWidgetRight, Qt.TopRightCorner)

        # empty state shown when no plugin tab is open
        self.emptyWidget = QWidget()
        emptyLayout = QVBoxLayout(self.emptyWidget)
        emptyLayout.addStretch(1)
        emptyTitle = QLabel(_("Select a session from the left"))
        emptyTitle.setAlignment(Qt.AlignCenter)
        emptyTitle.setObjectName("emptyStateTitle")
        emptyLayout.addWidget(emptyTitle)
        emptyHint = QLabel(_("or click + to add a plugin"))
        emptyHint.setAlignment(Qt.AlignCenter)
        emptyHint.setObjectName("emptyStateHint")
        emptyLayout.addWidget(emptyHint)
        emptyRow = QHBoxLayout()
        emptyRow.addStretch(1)
        emptyRow.addWidget(self.emptyPluginsSelector)
        emptyRow.addStretch(1)
        emptyLayout.addLayout(emptyRow)
        emptyLayout.addStretch(2)
        self.mainStack = QStackedWidget()
        self.mainStack.addWidget(self.emptyWidget)
        self.mainStack.addWidget(self.tabWidget)

        self.rightContent = QWidget()
        rightLayout = QVBoxLayout(self.rightContent)
        rightLayout.setContentsMargins(16, 0, 0, 0)
        rightLayout.setSpacing(0)
        rightLayout.addWidget(self.mainStack)

        self.sessionSidebar = SessionSidebar()
        self.bodySplitter = QSplitter(Qt.Horizontal)
        self.bodySplitter.setChildrenCollapsible(False)
        self.bodySplitter.setHandleWidth(3)
        self.bodySplitter.addWidget(self.sessionSidebar)
        self.bodySplitter.addWidget(self.rightContent)
        self.bodySplitter.setStretchFactor(0, 0)
        self.bodySplitter.setStretchFactor(1, 1)

        self.contentLayout = QVBoxLayout()
        self.contentWidget.setLayout(self.contentLayout)
        self.contentLayout.setContentsMargins(16, 0, 10, 10)
        self.contentLayout.addWidget(self.bodySplitter)

        # Hover/click affordances for auto showing the remembered-session
        # sidebar. The handle stays visible so it can also be used as a normal
        # manual toggle when the sidebar is expanded.
        self.sessionHoverStrip = SettingsHoverStrip(self.contentWidget, 10)
        self.sessionHoverStrip.hovered.connect(self.onSessionHoverStripEntered)
        self.sessionHoverStrip.hide()
        self.sessionHandle = SettingsHandle(self.contentWidget)
        self.sessionHandle.setToolTip(_("Show/hide sessions"))
        self.sessionHandle.clicked.connect(self.onSessionHandleClicked)
        self.sessionHandle.hovered.connect(self.onSessionHandleHovered)
        self.sessionHandle.hide()

        sidebar_cfg = {}
        try:
            sidebar_cfg = self.config["sessionSidebar"] or {}
        except Exception:
            pass
        sidebar_visible = sidebar_cfg.get("visible", True)
        try:
            sidebar_width = int(sidebar_cfg.get("width", 200))
        except Exception:
            sidebar_width = 200
        sidebar_width = max(160, min(320, sidebar_width))
        self.sessionSidebar.setVisible(sidebar_visible)
        self.sessionsButton.setChecked(sidebar_visible)
        QTimer.singleShot(0, lambda: self.applySessionSidebarLayout(sidebar_visible, sidebar_width))

        if sys.platform == 'darwin':
            self.macOsAddDockMenu()

        self.resize(850, 500)
        self.MoveToCenter()
        self.show()

    def applySessionSidebarLayout(self, visible, width):
        if self.bodySplitter is None:
            return
        self.sessionSidebar.setVisible(bool(visible))
        self.sessionsButton.setChecked(bool(visible))
        total = max(1, self.bodySplitter.width())
        if visible:
            self.bodySplitter.setSizes([max(160, width), max(1, total - max(160, width))])
        else:
            self.bodySplitter.setSizes([0, total])
        self.positionSessionHoverStrip()
        self._updateSessionHoverWidgets(bool(visible))
        QTimer.singleShot(0, self.positionSessionHoverStrip)

    def toggleSessionsSidebar(self):
        visible = not self.sessionSidebar.isVisible()
        cfg = None
        try:
            cfg = self.config["sessionSidebar"]
        except Exception:
            cfg = {}
            self.config["sessionSidebar"] = cfg
        cfg["visible"] = visible
        width = cfg.get("width", 200)
        self.applySessionSidebarLayout(visible, width)

    def onSessionSplitterMoved(self, pos, index):
        if not self.sessionSidebar.isVisible():
            return
        cfg = None
        try:
            cfg = self.config["sessionSidebar"]
        except Exception:
            cfg = {}
            self.config["sessionSidebar"] = cfg
        sizes = self.bodySplitter.sizes()
        if sizes:
            cfg["width"] = max(160, min(320, sizes[0]))

    def add_new_window(self):
        import copy
        mainWindow = MainWindow(self.app, self.eventFilter, copy.deepcopy(self.config))
        self.eventFilter.listenWindow(mainWindow)
        g_all_windows.append(mainWindow)

    def macOsAddDockMenu(self):
        self.dockMenu = QMenu(self)
        self.dockMenu.addAction(_('New Window'),
                                self.add_new_window)
        self.dockMenu.setAsDockMenu()
        self.app.setAttribute(Qt.AA_DontShowIconsInMenus, True)

    def resizeEvent(self, event):
        super().resizeEvent(event)
        self.positionSessionHoverStrip()

    def initEvent(self):
        # menu
        self.settingsButton.clicked.connect(self.toggleSettings)
        self.sessionsButton.clicked.connect(self.toggleSessionsSidebar)
        self.emptyPluginsSelector.activated.connect(
            lambda idx: self.onPluginSelectorChanged(idx, self.emptyPluginsSelector)
        )
        self.languageCombobox.currentIndexChanged.connect(self.onLanguageChanged)
        self.encodingCombobox.currentIndexChanged.connect(lambda: self.bindVar(self.encodingCombobox, self.config, "encoding"))
        self.functionalButton.clicked.connect(self.toggleFunctional)
        self.skinButton.currentIndexChanged.connect(self.skinChange)
        self.aboutButton.clicked.connect(self.showAbout)
        # sessions
        self.sessionSidebar.sessionActivated.connect(self.onSessionActivated)
        self.sessionSidebar.removeRequested.connect(self.onSessionRemoveRequested)
        self.bodySplitter.splitterMoved.connect(self.onSessionSplitterMoved)
        # main
        self.tabWidget.currentChanged.connect(self.onSwitchTab)
        self.tabWidget.tabCloseRequested.connect(self.closeTab)
        self.tabWidget.tabBarDoubleClicked.connect(self.onTabDoubleClicked)
        # others
        self.updateSignal.connect(self.showUpdate)
        self.hintSignal.connect(self.showHint)
        self.reloadWindowSignal.connect(self.onReloadWindow)

    def bindVar(self, uiObj, varObj, varName: str, vtype=None, vErrorMsg="", checkVar=lambda v:v, invert = False):
        objType = type(uiObj)
        if objType == QCheckBox:
            v = uiObj.isChecked()
            if hasattr(varObj, varName):
                varObj.__setattr__(varName, v if not invert else not v)
            else:
                varObj[varName] = v if not invert else not v
            return
        elif objType == QLineEdit:
            v = uiObj.text()
        elif objType == ComboBox:
            if hasattr(varObj, varName):
                varObj.__setattr__(varName, uiObj.currentText())
            else:
                varObj[varName] = uiObj.currentText()
            return
        elif objType == QRadioButton:
            v = uiObj.isChecked()
            if hasattr(varObj, varName):
                varObj.__setattr__(varName, v if not invert else not v)
            else:
                varObj[varName] = v if not invert else not v
            return
        else:
            raise Exception("not support this object")
        if vtype:
            try:
                v = vtype(v)
            except Exception:
                uiObj.setText(str(varObj.__getattribute__(varName)))
                self.hintSignal.emit("error", _("Error"), vErrorMsg)
                return
        try:
            v = checkVar(v)
        except Exception as e:
            self.hintSignal.emit("error", _("Error"), str(e))
            return
        varObj.__setattr__(varName, v)

    def onSwitchTab(self, idx):
        item = self.getCurrentItem()
        if item:
            self.config["currItem"] = item.name
            item.plugin.onActive()
        self.closeOtherSettingsDialogs(item)
        self.updateSettingsButton()
        self.refreshSessionSidebar()
        self.updateMainStack()

    def updateMainStack(self):
        if self.mainStack is None:
            return
        if self.tabWidget.count() > 0:
            self.mainStack.setCurrentWidget(self.tabWidget)
        else:
            self.mainStack.setCurrentWidget(self.emptyWidget)

    def refreshSessionSidebar(self):
        if self.sessionStore is None or self.sessionSidebar is None:
            return
        connected = set()
        connected_ssh = set()
        for item in self.items:
            try:
                if item.plugin.id != "terminal":
                    continue
                if item.isSerialConnection():
                    if item.plugin.getConnStatus() == ConnectionStatus.CONNECTED:
                        connected.add(item.getSerialPort())
                elif item.isSshConnection():
                    if item.plugin.getConnStatus() == ConnectionStatus.CONNECTED:
                        connected_ssh.add(item.getSshSessionKey())
            except Exception:
                pass
        active = None
        active_ssh = None
        item = self.getCurrentItem()
        if item is not None:
            try:
                if item.plugin.id == "terminal" and item.isSerialConnection():
                    active = item.getSerialPort()
                elif item.plugin.id == "terminal" and item.isSshConnection():
                    active_ssh = item.getSshSessionKey()
            except Exception:
                active = None
                active_ssh = None
        ssh_sessions = (
            self.sshSessionStore.sorted_sessions()
            if self.sshSessionStore is not None
            else []
        )
        self.sessionSidebar.set_sessions(
            self.sessionStore.sorted_sessions(),
            connected,
            active,
            ssh_sessions,
            connected_ssh,
            active_ssh,
        )

    def onPortsScanned(self, ports, success):
        if not success:
            return
        self._available_ports = {normalize_port(port.device) for port in ports}
        self._available_ports_known = True
        self._port_descriptions = {
            normalize_port(port.device): port.description for port in ports
        }
        names_changed = self.sessionStore.update_descriptions(ports)
        removed = self.sessionStore.prune([port.device for port in ports])
        if removed:
            self.sessionStore.save(self.config)
            self.saveConfig()
        elif names_changed:
            self.sessionStore.save(self.config)
            self.saveConfig()
        self.refreshSessionSidebar()
        if removed:
            self.sessionSidebar.show_message(
                _("Removed missing serial session(s): ") + ", ".join(removed)
            )

    def _findSerialItem(self, port):
        key = normalize_port(port)
        for item in self.items:
            try:
                if (item.plugin.id == "terminal"
                        and item.isSerialConnection()
                        and normalize_port(item.getSerialPort()) == key):
                    return item
            except Exception:
                pass
        return None

    def _findSshItem(self, key):
        key = str(key or "")
        for item in self.items:
            try:
                if (item.plugin.id == "terminal"
                        and item.isSshConnection()
                        and item.getSshSessionKey() == key):
                    return item
            except Exception:
                pass
        return None

    def _focusTerminal(self, item):
        """Give keyboard focus back to a terminal after a sidebar click.

        Clicking a remembered session leaves focus on the sidebar's list
        widget.  Defer the focus change so it wins over the click handler and
        also works when the tab was already the current tab.
        """
        def focus():
            try:
                if item.widget is None or not item.widget.isVisible():
                    return
                item.plugin.onActive()
            except Exception:
                pass

        QTimer.singleShot(0, focus)

    def _focusItem(self, item):
        if item.widget.parent() is not None and self.tabWidget.indexOf(item.widget) >= 0:
            self.tabWidget.setCurrentWidget(item.widget)
            self.mainStack.setCurrentWidget(self.tabWidget)
        else:
            item.widget.show()
            item.widget.raise_()
            item.widget.activateWindow()
            self.updateMainStack()
        self._focusTerminal(item)

    def _cacheSshPassword(self, item):
        try:
            config = item.getSshConfig()
            password = str(config.get("passwd") or "")
            if password:
                self._ssh_password_cache[item.getSshSessionKey()] = password
        except Exception:
            pass

    def _wireSshSignals(self, item):
        for conn in getattr(item, "conns", []):
            if (getattr(conn, "id", "") == "ssh"
                    and hasattr(conn, "rememberPasswordChanged")):
                conn.rememberPasswordChanged.connect(
                    lambda enabled, i=item, c=conn:
                    self._onSshRememberPasswordChanged(i, c, enabled)
                )

    def _onSshRememberPasswordChanged(self, item, conn, enabled):
        try:
            key = item.getSshSessionKey()
            if not key or self.sshSessionStore is None:
                return
            session = (
                self.sshSessionStore.get(key)
                if self.sshSessionStore is not None
                else None
            )
            if not enabled:
                self._ssh_password_cache.pop(key, None)
                if session and session.get("passwd_enc"):
                    self.sshSessionStore.upsert(
                        session.get("host"),
                        session.get("port"),
                        session.get("user"),
                        alias=session.get("alias") or "",
                        ssh_key=session.get("ssh_key") or "",
                        passwd_enc="",
                    )
                    self.sshSessionStore.save(self.config)
                    self.saveConfig()
                return
            if not conn.isConnected():
                return
            password = str(conn.getConfig().get("passwd") or "")
            token = protect_password(password) if password else ""
            if token:
                self._ssh_password_cache[key] = password
                self.sshSessionStore.upsert(
                    session.get("host") if session else conn.getConfig().get("host"),
                    session.get("port") if session else conn.getConfig().get("port"),
                    session.get("user") if session else conn.getConfig().get("user"),
                    alias=session.get("alias") if session else conn.getConfig().get("name") or "",
                    ssh_key=session.get("ssh_key") if session else conn.getConfig().get("ssh_key") or "",
                    passwd_enc=token,
                )
                self.sshSessionStore.save(self.config)
                self.saveConfig()
        except Exception as exc:
            log.w("update SSH password preference failed: {}".format(exc))

    def _connectSshItem(self, item):
        if item.plugin.isConnected():
            return
        config = item.getSshConfig()
        if str(config.get("ssh_key") or "").strip() or str(config.get("passwd") or "").strip():
            item.openConnection()
            return
        key = item.getSshSessionKey()
        cached_password = self._ssh_password_cache.get(key)
        if cached_password:
            item.openConnection(cached_password)
            return
        session = (
            self.sshSessionStore.get(key)
            if self.sshSessionStore is not None
            else None
        )
        stored_password = unprotect_password(
            (session or {}).get("passwd_enc")
        )
        if stored_password:
            self._ssh_password_cache[key] = stored_password
            item.openConnection(stored_password)
            return
        # No key, cached password, or saved encrypted password.  Open the
        # settings dialog so the user can enter one, then use OPEN.  A
        # successful connection is cached in memory and, when the remember
        # option is enabled, saved encrypted with Windows DPAPI.
        self.showSettings()
        conn = item.conns[item.currConnIdx] if item.currConnIdx < len(item.conns) else None
        if conn is None:
            return

        def focus_password():
            try:
                conn.passwdInput.setFocus()
                conn.passwdInput.selectAll()
            except Exception:
                pass

        QTimer.singleShot(0, focus_password)

    def _activateSshSession(self, key):
        item = self._findSshItem(key)
        if item is not None:
            self._focusItem(item)
            self._connectSshItem(item)
            return
        pluginClass = self.getPluginClassById("terminal")
        session = (
            self.sshSessionStore.get(key)
            if self.sshSessionStore is not None
            else None
        )
        if pluginClass is None or session is None:
            return
        connsConfigs = {
            "currConn": "ssh",
            "ssh": {
                "name": session.get("alias") or "",
                "host": session.get("host") or "",
                "port": session.get("port") or 22,
                "user": session.get("user") or "",
                "ssh_key": session.get("ssh_key") or "",
                "passwd": "",
            },
        }
        item = self.addItem(
            pluginClass,
            setCurrent=True,
            connsConfigs=connsConfigs,
            pluginConfig={},
        )
        self.updateMainStack()
        self._focusTerminal(item)
        self._connectSshItem(item)

    def onSessionActivated(self, session_id):
        session_id = str(session_id or "")
        if session_id.startswith(SSH_SESSION_PREFIX):
            self._activateSshSession(session_id[len(SSH_SESSION_PREFIX):])
            return
        item = self._findSerialItem(session_id)
        if item is not None:
            self._focusItem(item)
            if not item.plugin.isConnected():
                item.openConnection()
            return
        pluginClass = self.getPluginClassById("terminal")
        session = self.sessionStore.get(session_id) if self.sessionStore is not None else None
        if pluginClass is None or session is None:
            return
        connsConfigs = {
            "currConn": "serial",
            "serial": dict(session["serial"]),
        }
        item = self.addItem(
            pluginClass,
            setCurrent=True,
            connsConfigs=connsConfigs,
            pluginConfig={},
        )
        self.updateMainStack()
        item.openConnection()
        self._focusTerminal(item)

    def onSessionRemoveRequested(self, session_id):
        session_id = str(session_id or "")
        if session_id.startswith(SSH_SESSION_PREFIX):
            key = session_id[len(SSH_SESSION_PREFIX):]
            session = (
                self.sshSessionStore.get(key)
                if self.sshSessionStore is not None
                else None
            )
            label = ssh_session_detail(session) if session else key
            if session:
                alias = ssh_display_name(session)
                if alias and alias != session.get("host"):
                    label = "{} ({})".format(alias, label)
            reply = QMessageBox.question(
                self,
                _("Remove session"),
                _("Remove this SSH session from memory?") + "\n" + label,
                QMessageBox.Yes | QMessageBox.No,
                QMessageBox.No,
            )
            if reply != QMessageBox.Yes:
                return
            self._suppressed_ssh_session_keys.add(key)
            self._ssh_password_cache.pop(key, None)
            if self.sshSessionStore is not None:
                self.sshSessionStore.remove(key)
                self.sshSessionStore.save(self.config)
                self.saveConfig()
            self.refreshSessionSidebar()
            return
        reply = QMessageBox.question(
            self,
            _("Remove session"),
            _("Remove this serial session from memory?") + "\n" + session_id,
            QMessageBox.Yes | QMessageBox.No,
            QMessageBox.No,
        )
        if reply != QMessageBox.Yes:
            return
        self._suppressed_session_ports.add(normalize_port(session_id))
        self.sessionStore.remove(session_id)
        self.sessionStore.save(self.config)
        self.saveConfig()
        self.refreshSessionSidebar()

    def recordSerialSession(self, item, allow_unavailable=False):
        try:
            if item.plugin.id != "terminal" or not item.isSerialConnection():
                return
            if not getattr(item, "serialSessionRecorded", False):
                return
            port = item.getSerialPort()
            key = normalize_port(port)
            if not port or key in self._suppressed_session_ports:
                return
            if (not allow_unavailable
                    and self._available_ports_known
                    and key not in self._available_ports):
                return
            description = self._port_descriptions.get(key, "")
            existing = self.sessionStore.get(port)
            name = session_name(port, description) if description else None
            if not name and existing is not None:
                name = existing.get("name")
            self.sessionStore.upsert(port, item.getSerialConfig(), name)
            self.sessionStore.save(self.config)
            self.saveConfig()
        except Exception as exc:
            log.w("record serial session failed: {}".format(exc))

    def recordSshSession(self, item):
        try:
            if item.plugin.id != "terminal" or not item.isSshConnection():
                return
            if not getattr(item, "sshSessionRecorded", False):
                return
            config = item.getSshConfig()
            host = str(config.get("host") or "").strip()
            if not host:
                return
            key = item.getSshSessionKey()
            if key in self._suppressed_ssh_session_keys:
                return
            if self.sshSessionStore is None:
                return
            remember = bool(config.get("remember_password", True))
            password = str(config.get("passwd") or "")
            if not remember:
                passwd_enc = ""
            elif password:
                passwd_enc = protect_password(password) or None
            else:
                passwd_enc = None
            self.sshSessionStore.upsert(
                host,
                config.get("port"),
                config.get("user"),
                alias=config.get("name") or "",
                ssh_key=config.get("ssh_key") or "",
                passwd_enc=passwd_enc,
            )
            self.sshSessionStore.save(self.config)
            self.saveConfig()
        except Exception as exc:
            log.w("record SSH session failed: {}".format(exc))

    def setSessionsSidebarVisible(self, visible):
        cfg = None
        try:
            cfg = self.config["sessionSidebar"]
        except Exception:
            cfg = {}
            self.config["sessionSidebar"] = cfg
        cfg["visible"] = bool(visible)
        width = cfg.get("width", 200)
        self.applySessionSidebarLayout(bool(visible), width)

    def positionSessionHoverStrip(self):
        if (self.sessionHoverStrip is None
                or self.sessionHandle is None
                or self.bodySplitter is None):
            return
        body = self.bodySplitter.geometry()
        if body.height() <= 0:
            return
        strip_width = self.sessionHoverStrip.width()
        strip_x = max(6, body.x() - strip_width)
        self.sessionHoverStrip.setGeometry(
            strip_x, body.y(), strip_width, body.height()
        )
        self.sessionHoverStrip.raise_()
        handle_w = self.sessionHandle.width()
        handle_h = self.sessionHandle.height()
        handle_x = max(6, body.x() - handle_w)
        handle_y = body.y() + max(0, (body.height() - handle_h) // 2)
        self.sessionHandle.setGeometry(handle_x, handle_y, handle_w, handle_h)
        self.sessionHandle.raise_()

    def _updateSessionHoverWidgets(self, visible):
        if self.sessionHoverStrip is None:
            return
        if visible:
            self.sessionHoverStrip.hide()
            self._session_hover_seen = False
            self._last_session_cursor_x = QCursor.pos().x()
            self._session_hover_timer.start()
        else:
            self._session_hover_timer.stop()
            self._session_hover_seen = False
            self.positionSessionHoverStrip()
            self.sessionHoverStrip.show()
            self.sessionHoverStrip.raise_()
        if self.sessionHandle is not None:
            # Keep the manual handle visible in both states, like the
            # settings-panel handle.
            self.sessionHandle.setExpanded(bool(visible))
            self.sessionHandle.show()
            self.sessionHandle.raise_()

    def _sessionSidebarContains(self, cursor):
        """Whether the cursor is inside the sidebar or its left-edge approach."""
        if self.sessionSidebar is None or not self.sessionSidebar.isVisible():
            return False
        if self.sessionSidebar.rect().contains(
                self.sessionSidebar.mapFromGlobal(cursor)):
            return True
        if self.sessionHandle is not None and self.sessionHandle.isVisible():
            if self.sessionHandle.rect().contains(
                    self.sessionHandle.mapFromGlobal(cursor)):
                return True
        # Include the small left margin after the sidebar opens, otherwise a
        # pointer entering through the hover strip has to cross a 10 px gap
        # before the panel counts as visited.
        content_pos = self.contentWidget.mapFromGlobal(cursor)
        body = self.bodySplitter.geometry()
        return (0 <= content_pos.x() <= body.x()
                and body.y() <= content_pos.y() < body.y() + body.height())

    def _showSessionSidebarAuto(self):
        if self.sessionSidebar.isVisible():
            return
        cfg = {}
        try:
            cfg = self.config["sessionSidebar"] or {}
        except Exception:
            pass
        width = cfg.get("width", 200)
        self.applySessionSidebarLayout(True, width)
        self._session_shown_at = time.monotonic()
        self._session_hover_seen = True

    def onSessionHoverStripEntered(self):
        if self.sessionSidebar is None or self.sessionSidebar.isVisible():
            return
        self._showSessionSidebarAuto()

    def onSessionHandleHovered(self):
        if self.sessionSidebar is None or self.sessionSidebar.isVisible():
            return
        self._showSessionSidebarAuto()

    def onSessionHandleClicked(self):
        if self.sessionSidebar is None:
            return
        if self.sessionSidebar.isVisible():
            # Ignore the click immediately following hover-open.
            if time.monotonic() - self._session_shown_at < 0.35:
                return
            self.setSessionsSidebarVisible(False)
        else:
            self.setSessionsSidebarVisible(True)
            self._session_shown_at = time.monotonic()
            self._session_hover_seen = True

    def _checkSessionSidebarAutoHide(self):
        if (self.sessionSidebar is None
                or not self.sessionSidebar.isVisible()):
            self._session_hover_timer.stop()
            self._session_hover_seen = False
            return
        cursor = QCursor.pos()
        dx = cursor.x() - self._last_session_cursor_x
        self._last_session_cursor_x = cursor.x()
        if self._sessionSidebarContains(cursor):
            self._session_hover_seen = True
            return
        # Moving right while still outside the window's left edge is moving
        # towards the panel, not away from it.
        if self.contentWidget.mapFromGlobal(cursor).x() < 0:
            return
        # Keep the panel open while the user is dragging the splitter, a text
        # selection, or a context menu.
        if QApplication.mouseButtons() != Qt.NoButton:
            return
        if should_auto_hide_settings(
                inside=False,
                hover_seen=self._session_hover_seen,
                dx=dx,
                popup_active=QApplication.activePopupWidget() is not None,
                editing=False):
            cfg = {}
            try:
                cfg = self.config["sessionSidebar"] or {}
            except Exception:
                pass
            self.applySessionSidebarLayout(False, cfg.get("width", 200))

    def closeTab(self, idx):
        item = None
        for _item in self.items:
            if _item.widget == self.tabWidget.widget(idx):
                item = _item
                break
        if item is None:
            return
        self.closeSettingsDialog(item, delete=True)
        self.recordSerialSession(item)
        self.tabWidget.removeTab(idx)
        config_entry = getattr(item, "configEntry", None)
        if config_entry is not None:
            for index, entry in enumerate(self.config["items"]):
                if entry is config_entry:
                    self.config["items"].pop(index)
                    break
        else:
            for _item in list(self.config["items"]):
                if _item.get("name") == item.name:
                    self.config["items"].remove(_item)
                    break
        item.onDel()
        self.items.remove(item)
        self.updateSettingsButton()
        self.refreshSessionSidebar()
        self.updateMainStack()

    def onTabDoubleClicked(self, idx):
        widget = self.tabWidget.widget(idx)
        item = None
        for _item in self.items:
            if _item.widget == widget:
                item = _item
                break
        if item is None:
            return
        self.tabWidget.removeTab(idx)
        parent = item.widget.parent()
        item.widget.setWindowFlag(Qt.Window) # this method is not stable in QT, do not use it
        # item.widget.setParent(None)
        item.widget.setWindowTitle(item.getDisplayName())
        item.widget.closeEvent = lambda event: self.onPluginWindowClose(item, parent)
        item.widget.show()
        self.updateMainStack()

    def onPluginWindowClose(self, item, parent):
        self.recoverTab(item, parent)

    def recoverTab(self, item, parent):
        def add(i):
            title = item.getDisplayName()
            self.tabWidget.insertTab(i, item.widget, title)
            self.tabWidget.setTabToolTip(
                i, title + _(", Double click to detach as a window")
            )
            self.tabWidget.setCurrentIndex(i)
            item.widget.setWindowFlag(Qt.Window, False)
            # item.widget.setParent(parent)
            status = item.plugin.getConnStatus()
            self.setTabIcon(status, i)
            self.updateMainStack()
        # prevent close and add this widget to tab
        insertIdx = self.tabWidget.count()
        idx = self.items.index(item)
        for i in range(self.tabWidget.count()):
            widget = self.tabWidget.widget(i)
            idx2 = -1
            for _item in self.items:
                if _item.widget == widget:
                    idx2 = self.items.index(_item)
                    break
            if idx2 > idx:
                insertIdx = i
                break
        add(insertIdx)


    def updateStyle(self, widget):
        self.frameWidget.style().unpolish(widget)
        self.frameWidget.style().polish(widget)
        self.frameWidget.update()

    def onLanguageChanged(self):
        idx = self.languageCombobox.currentIndex()
        locale = list(self.languages.keys())[idx]
        self.config["locale"] = locale
        i18n.set_locale(locale)
        reply = QMessageBox.question(self, _('Restart now?'),
                                     _("language changed to: ") + self.languages[self.config["locale"]] + "\n" + _("Restart software to take effect now?"), QMessageBox.Yes |
                                     QMessageBox.No, QMessageBox.No)
        if reply == QMessageBox.Yes:
            self.needRestart = True
            self.close()

    def onReloadWindow(self, title, msg, callback):
        if not title:
            title = _('Restart now?')
        reply = QMessageBox.question(self, title, msg,
                            QMessageBox.Yes |
                            QMessageBox.No, QMessageBox.No)
        if reply == QMessageBox.Yes:
            callback(True)
            self.needRestart = True
            self.close()
        else:
            callback(False)

    def MoveToCenter(self):
        qr = self.frameGeometry()
        cp = QDesktopWidget().availableGeometry().center()
        qr.moveCenter(cp)
        self.move(qr.topLeft())

    def showHint(self, info_type: str, title: str, msg: str):
        # A settings dialog can be the active tool window. Parent the message
        # to it so connection failures are not hidden behind that window.
        parent = QApplication.activeWindow() or self
        if info_type == "info":
            QMessageBox.information(parent, title, msg)
        elif info_type == "warning":
            QMessageBox.warning(parent, title, msg)
        elif info_type == "error":
            QMessageBox.critical(parent, title, msg)

    def closeEvent(self, event):
        if self.closeTimerId:
            event.accept()
            return
        print("----- close event")
        # reply = QMessageBox.question(self, 'Sure To Quit?',
        #                              "Are you sure to quit?", QMessageBox.Yes |
        #                              QMessageBox.No, QMessageBox.No)
        if 1: # reply == QMessageBox.Yes:
            self.receiveProgressStop = True
            if self.portWatcher is not None:
                self.portWatcher.stop()
            # capture the latest session settings before plugins close
            for item in self.items:
                self.recordSerialSession(item)
                self.recordSshSession(item)
            # inform plugins
            for item in self.items:
                item.onDel()
            self.saveConfig()
            # actual exit after 500ms
            self.closeTimerId = self.startTimer(500)
            self.setWindowTitle(_("Closing ..."))
            self.titleBar.setTitle(_("Closing ..."))
            self.setEnabled(False)
            if self.helpWindow:
                self.helpWindow.close()
            event.ignore()
        else:
            event.ignore()

    def timerEvent(self, e):
        if self.closeTimerId:
            log.i("Close window")
            self.killTimer(self.closeTimerId)
            self.close()

    def saveConfig(self):
        # print("save config:", self.config)
        self.config.save(parameters.configFilePath)
        print("save config compelte")

    def uiLoadConfigs(self):
        # language
        try:
            idx = list(self.languages.keys()).index(self.config["locale"])
        except Exception:
            idx = 0
        self.languageCombobox.setCurrentIndex(idx)
        # skin
        try:
            idx = utils_ui.get_skins().index(self.config["skin"])
        except Exception:
            idx = 0
        self.skinButton.setCurrentIndex(idx)
        # encoding
        self.encodingCombobox.setCurrentIndex(self.supportedEncoding.index(self.config["encoding"]))

    def keyPressEvent(self, event):
        CustomTitleBarWindowMixin.keyPressEvent(self, event)
        item = self.getCurrentItem()
        if item is not None:
            item.onKeyPressEvent(event)

    def keyReleaseEvent(self,event):
        CustomTitleBarWindowMixin.keyReleaseEvent(self, event)
        item = self.getCurrentItem()
        if item is not None:
            item.onKeyReleaseEvent(event)

    def getCurrentItem(self, idx = False):
        widget = self.tabWidget.currentWidget()
        for item in self.items:
            if item.widget == widget:
                if idx:
                    return self.tabWidget.indexOf(widget), item
                return item

    def toggleSettings(self):
        item = self.getCurrentItem()
        if item is None:
            return
        if getattr(item, "settingsDetached", False):
            dialog = getattr(item, "settingsDialog", None)
            if dialog is not None and dialog.isVisible():
                self.hideSettings()
            else:
                self.showSettings()
            return
        widget = item.settingWidget
        if widget.isVisible():
            self.hideSettings()
        else:
            self.showSettings()

    def updateSettingsButton(self):
        if getattr(self, "settingsButton", None) is None:
            return
        item = self.getCurrentItem()
        if item is not None and getattr(item, "settingsDetached", False):
            dialog = getattr(item, "settingsDialog", None)
            visible = bool(dialog is not None and dialog.isVisible())
        else:
            visible = bool(
                item and item.settingWidget and item.settingWidget.isVisible()
            )
        self.settingsButton.setChecked(visible)

    def _settingsDialogTitle(self, item):
        return "{} - {}".format(item.getDisplayName(), _("Connection settings"))

    def closeSettingsDialog(self, item, delete=False):
        dialog = getattr(item, "settingsDialog", None)
        if dialog is None:
            return
        dialog.close()
        if delete:
            item.settingsDialog = None
            dialog.deleteLater()

    def closeOtherSettingsDialogs(self, keep_item):
        for item in self.items:
            if item is keep_item:
                continue
            self.closeSettingsDialog(item)

    def showSettings(self):
        item = self.getCurrentItem()
        if not item or item.settingWidget is None:
            return
        if not getattr(item, "settingsDetached", False):
            item.settingWidget.show()
            self.updateSettingsButton()
            return
        self.closeOtherSettingsDialogs(item)
        dialog = getattr(item, "settingsDialog", None)
        if dialog is None:
            dialog = ConnectionSettingsDialog(
                self, self._settingsDialogTitle(item), item.settingWidget
            )
            item.settingsDialog = dialog
            dialog.finished.connect(
                lambda _result=0, it=item: self.updateSettingsButton()
            )
        dialog.show()
        dialog.raise_()
        dialog.activateWindow()
        self.updateSettingsButton()

    def hideSettings(self):
        item = self.getCurrentItem()
        if not item:
            return
        if getattr(item, "settingsDetached", False):
            dialog = getattr(item, "settingsDialog", None)
            if dialog is not None:
                dialog.close()
            self.updateSettingsButton()
            return
        item.settingWidget.hide()
        self.updateSettingsButton()

    def toggleFunctional(self):
        widget = self.getCurrentItem().functionalWidget
        if widget is None:
            return
        if widget.isVisible():
            self.hideFunctional()
        else:
            self.showFunctional()

    def showFunctional(self):
        widget = self.getCurrentItem().functionalWidget
        if not widget is None:
            widget.show()
        self.functionalButton.setStyleSheet(
            parameters.strStyleShowHideButtonRight.replace("$DataPath",self.DataPath))

    def hideFunctional(self):
        widget = self.getCurrentItem().functionalWidget
        if not widget is None:
            widget.hide()
        self.functionalButton.setStyleSheet(
            parameters.strStyleShowHideButtonLeft.replace("$DataPath", self.DataPath))

    def skinChange(self):
        idx = self.skinButton.currentIndex()
        skin = utils_ui.get_skins()[idx]
        file = open(self.DataPath + '/assets/qss/style-{}.qss'.format(skin), "r", encoding="utf-8")
        self.app.setStyleSheet(file.read().replace("$DataPath", self.DataPath))
        utils_ui.setSkin(skin)
        self.config["skin"] = skin

    def showAbout(self):
        help = helpAbout.HelpInfo()
        pluginsHelp = {
            _("About"): help
        }
        for p in self.pluginClasses:
            if not p.help is None:
                pluginsHelp[p.name] = p.help
        iconPath = self.DataPath+"/"+parameters.appIcon
        self.helpWindow = HelpWidget(pluginsHelp, icon = iconPath)
        self.helpWindow.closed.connect(self.helpWindowClosed)
        self.eventFilter.listenWindow(self.helpWindow)

    def helpWindowClosed(self):
        self.eventFilter.unlistenWindow(self.helpWindow)
        self.helpWindow = None
        # self.helpWindow

    def showUpdate(self, versionInfo):
        versionInt = versionInfo.int()
        if self.config["skipVersion"] and self.config["skipVersion"] >= versionInt:
            return
        msgBox = QMessageBox()
        desc = versionInfo.desc if len(versionInfo.desc) < 300 else versionInfo.desc[:300] + " ... "
        link = '<a href="https://github.com/Neutree/COMTool/releases">github.com/Neutree/COMTool/releases</a>'
        info = '{}<br>{}<br><br>v{}: {}<br><br>{}'.format(_("New versioin detected, please click learn more to download"), link, '{}.{}.{}'.format(versionInfo.major, versionInfo.minor, versionInfo.dev), versionInfo.name, desc)
        learn = msgBox.addButton(_("Learn More"), QMessageBox.YesRole)
        skip = msgBox.addButton(_("Skip this version"), QMessageBox.YesRole)
        nextTime = msgBox.addButton(_("Remind me next time"), QMessageBox.NoRole)
        msgBox.setWindowTitle(_("Need update"))
        msgBox.setText(info)
        result = msgBox.exec_()
        if result == 0:
            auto = autoUpdate.AutoUpdate()
            auto.OpenBrowser()
        elif result == 1:
            self.config["skipVersion"] = versionInt



    def autoUpdateDetect(self):
        auto = autoUpdate.AutoUpdate()
        needUpdate, versionInfo = auto.detectNewVersion()
        if needUpdate:
            self.updateSignal.emit(versionInfo)

def load_fonts(paths):
    from PyQt5 import QtGui
    for path in paths:
        id = QtGui.QFontDatabase.addApplicationFont(path)
        fonts = QtGui.QFontDatabase.applicationFontFamilies(id)
        print("load fonts:", fonts)

class Splash(QSplashScreen):
    '''
        show splash when window is loading
    '''
    def __init__(self, app) -> None:
        super().__init__(QPixmap(os.path.join(parameters.assetsDir, "logo.png")))
        self.app = app
        self.exit = False
        self.show()
        t = threading.Thread(target=self._processEventsProcess)
        t.setDaemon(True)
        t.start()

    def event(self, e):
        if type(e) == QCloseEvent:
            self.exit = True
        return super().event(e)

    def finish(self, w):
        self.exit = True
        return super().finish(w)

    def _processEventsProcess(self):
        while not self.exit:
            self.app.processEvents()
            time.sleep(0.001)


def main():
    '''
        @retval None: need restart
                0: exit ok
                others: exit error
    '''
    ret = 1
    try:
        QCoreApplication.setAttribute(Qt.AA_EnableHighDpiScaling)
        app = QApplication(sys.argv)
        splash = Splash(app)
        eventFilter = EventFilter()
        mainWindow = MainWindow(app, eventFilter, programConfig)
        eventFilter.listenWindow(mainWindow)
        app.installEventFilter(eventFilter)
        g_all_windows.append(mainWindow)
        # path = os.path.join(mainWindow.DataPath, "assets", "fonts", "JosefinSans-Regular.ttf")
        # load_fonts([path])
        log.i("data path:"+mainWindow.DataPath)
        file = open(mainWindow.DataPath+'/assets/qss/style-{}.qss'.format(mainWindow.config["skin"]),"r", encoding="utf-8")
        qss = file.read().replace("$DataPath",mainWindow.DataPath)
        app.setStyleSheet(qss)
        t = threading.Thread(target=mainWindow.autoUpdateDetect)
        t.setDaemon(True)
        t.start()
        splash.finish(mainWindow)
        ret = app.exec_()
        if mainWindow.needRestart:
            ret = None
        else:
            app.removeEventFilter(eventFilter)
            print("-- no need to restart, now exit")
    except Exception as e:
        import traceback
        exc = traceback.format_exc()
        show_error(_("Error"), exc)
    return ret

def show_error(title, msg):
    print("error:", msg)
    app = QApplication(sys.argv)
    window = QMainWindow()
    QMessageBox.information(window, title, msg)



