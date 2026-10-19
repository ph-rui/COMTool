
if __name__ == "__main__":
    import sys, os
    path = os.path.join(os.path.abspath(os.path.dirname(__file__)), "..")
    sys.path.insert(0, path)
    sys.path.insert(0, os.path.join(path, ".."))

from PyQt5.QtCore import pyqtSignal,Qt, QRect, QMargins, QObject, pyqtSlot, QTimer
from PyQt5.QtWidgets import (QWidget,QPushButton,QMessageBox,QDesktopWidget,QMainWindow,
                             QVBoxLayout,QHBoxLayout,QGridLayout,QTextEdit,QLabel,QRadioButton,QCheckBox,
                             QLineEdit,QGroupBox,QSplitter,QFileDialog, QScrollArea)
from PyQt5.QtGui import QIcon,QFont,QTextCursor,QPixmap,QColor
try:
    import parameters,helpAbout,autoUpdate
    from Combobox import ComboBox
    import i18n
    from i18n import _
    import version
    import utils
except ImportError:
    from COMTool import parameters,helpAbout,autoUpdate, utils
    from COMTool.Combobox import ComboBox
    from COMTool import i18n
    from COMTool.i18n import _
    from COMTool import version
try:
    from base import COMM, ConnectionStatus
except Exception:
    from .base import COMM, ConnectionStatus
try:
    from port_utils import (
        choose_index,
        collect_ports,
        display_text,
        format_port_details,
        port_keys,
    )
except ImportError:
    from .port_utils import (
        choose_index,
        collect_ports,
        display_text,
        format_port_details,
        port_keys,
    )
import serial, threading, time
import serial.tools.list_ports


PORT_REFRESH_INTERVAL_MS = 500


class Serial(COMM):
    '''
        call sequence:
            onInit
            onWidget
            onUiInitDone
                isConnected or getConnStatus
                send
            getConfig
    '''
    id = "serial"
    name = _("Serial")
    showSerialComboboxSignal = pyqtSignal(list)
    showSwitchSignal = pyqtSignal(ConnectionStatus)
    def onInit(self, config):
        self.com = serial.Serial()
        self.config = config
        default = {
            "port" : None,
            "baudrate" : 115200,
            "bytesize" : 8,
            "parity" : "None",
            "stopbits" : "1",
            "flowcontrol" : "None",
            "rts" : False,
            "dtr" : False,
        }
        for k in default:
            if not k in self.config:
                self.config[k] = default[k]
        self.widgetConfMap = {
            "port" : None,
            "baudrate" : None,
            "bytesize" : None,
            "parity" : None,
            "stopbits" : None,
            "flowcontrol" : None,
            "rts" : None,
            "dtr" : None,
        }
        self.isOpened = False
        self.busy = False
        self.status = ConnectionStatus.CLOSED
        self.widget = None
        self.baudrateCustomStr = _("Custom, input baudrate")
        # Port enumeration state. Each Serial instance (tab) owns its own copy.
        self._port_scan_in_flight = False
        self._last_port_keys = None
        self._popup_visible = False
        self._initial_scan_done = False
        self._applying_ports = False
        self._port_refresh_timer = None

    def getDisplayName(self):
        return str(self.config.get("port") or "")

    def disconnect(self):
        if self.isConnected():
            self.openCloseSerial()

    def onDel(self):
        self._stopPortRefresh()
        if self.isConnected():
            self.openCloseSerial()

    def __del__(self):
        try:
            self.com.close()
            self.status = ConnectionStatus.CLOSED
            time.sleep(0.05)  # wait for child threads, not wait also ok, cause child threads are daemon
        except Exception:
            pass

    def getConfig(self):
        '''
            get config, dict type
        '''
        return self.config

    def ctrl(self, k, v):
        if k == "purge":
            # Drop bytes that are still queued in the driver after a failed or
            # cancelled transfer.  Without this a 115200 port keeps sending
            # (and the remote shell keeps echoing) for a long time.
            try:
                if self.com.is_open:
                    self.com.reset_output_buffer()
            except Exception:
                pass
    def onUiInitDone(self):
        for key in self.config:
            self.setSerialConfig(key, self.widgetConfMap[key], self.config[key])
        self.requestPortScan()

    def onWidget(self):
        self.widget = QWidget()
        serialSettingsLayout = QGridLayout()
        serialPortLabek = QLabel(_("Port"))
        serailBaudrateLabel = QLabel(_("Baudrate"))
        serailBytesLabel = QLabel(_("DataBytes"))
        serailParityLabel = QLabel(_("Parity"))
        serailStopbitsLabel = QLabel(_("Stopbits"))
        serialFlowControlLabel = QLabel(_("Flow control"))
        self.serialPortCombobox = ComboBox()
        self._port_refresh_timer = QTimer(self.widget)
        self._port_refresh_timer.setInterval(PORT_REFRESH_INTERVAL_MS)
        self._port_refresh_timer.timeout.connect(self.requestPortScan)
        self.serailBaudrateCombobox = ComboBox()
        for baud in parameters.defaultBaudrates:
            self.serailBaudrateCombobox.addItem(str(baud))
        self.serailBaudrateCombobox.addItem(self.baudrateCustomStr)
        self.serailBaudrateCombobox.setCurrentIndex(5)
        self.serailBaudrateCombobox.setEditable(True)
        self.serailBytesCombobox = ComboBox()
        self.serailBytesCombobox.addItem("5")
        self.serailBytesCombobox.addItem("6")
        self.serailBytesCombobox.addItem("7")
        self.serailBytesCombobox.addItem("8")
        self.serailBytesCombobox.setCurrentIndex(3)
        self.serailParityCombobox = ComboBox()
        self.serailParityCombobox.addItem("None")
        self.serailParityCombobox.addItem("Odd")
        self.serailParityCombobox.addItem("Even")
        self.serailParityCombobox.addItem("Mark")
        self.serailParityCombobox.addItem("Space")
        self.serailParityCombobox.setCurrentIndex(0)
        self.serailStopbitsCombobox = ComboBox()
        self.serailStopbitsCombobox.addItem("1")
        self.serailStopbitsCombobox.addItem("1.5")
        self.serailStopbitsCombobox.addItem("2")
        self.serailStopbitsCombobox.setCurrentIndex(0)
        self.serialFlowControlCombobox = ComboBox()
        self.serialFlowControlCombobox.addItem("None")
        self.serialFlowControlCombobox.addItem("XON/XOFF")
        self.serialFlowControlCombobox.addItem("RTS/CTS")
        self.serialFlowControlCombobox.addItem("DSR/DTR")
        self.serialFlowControlCombobox.setCurrentIndex(0)
        self.checkBoxRTS = QCheckBox("rts")
        self.checkBoxDTR = QCheckBox("dtr")
        self.checkBoxRTS.setToolTip(_("Check to enable(usually output low level)"))
        self.checkBoxDTR.setToolTip(_("Check to enable(usually output low level)"))
        self.serialOpenCloseButton = QPushButton(_("OPEN"))
        serialSettingsLayout.addWidget(serialPortLabek,0,0)
        serialSettingsLayout.addWidget(serailBaudrateLabel, 1, 0)
        serialSettingsLayout.addWidget(serailBytesLabel, 2, 0)
        serialSettingsLayout.addWidget(serailParityLabel, 3, 0)
        serialSettingsLayout.addWidget(serailStopbitsLabel, 4, 0)
        serialSettingsLayout.addWidget(serialFlowControlLabel, 5, 0)
        serialSettingsLayout.addWidget(self.serialPortCombobox, 0, 1)
        serialSettingsLayout.addWidget(self.serailBaudrateCombobox, 1, 1)
        serialSettingsLayout.addWidget(self.serailBytesCombobox, 2, 1)
        serialSettingsLayout.addWidget(self.serailParityCombobox, 3, 1)
        serialSettingsLayout.addWidget(self.serailStopbitsCombobox, 4, 1)
        serialSettingsLayout.addWidget(self.serialFlowControlCombobox, 5, 1)
        serialSettingsLayout.addWidget(self.checkBoxRTS, 6, 0,1,1)
        serialSettingsLayout.addWidget(self.checkBoxDTR, 6, 1,1,1)
        serialSettingsLayout.addWidget(self.serialOpenCloseButton, 7, 0,1,2)
        self.widget.setLayout(serialSettingsLayout)
        self.widgetConfMap["port"]       = self.serialPortCombobox
        self.widgetConfMap["baudrate"]    = self.serailBaudrateCombobox
        self.widgetConfMap["bytesize"]    = self.serailBytesCombobox
        self.widgetConfMap["parity"]      = self.serailParityCombobox
        self.widgetConfMap["stopbits"]    = self.serailStopbitsCombobox
        self.widgetConfMap["flowcontrol"] = self.serialFlowControlCombobox
        self.widgetConfMap["rts"]         = self.checkBoxRTS
        self.widgetConfMap["dtr"]         = self.checkBoxDTR
        self.initEvet()
        return self.widget

    def initEvet(self):
        self.serialPortCombobox.clicked.connect(self.requestPortScan)
        self.serialPortCombobox.popupShown.connect(self._onPopupShown)
        self.serialPortCombobox.popupHidden.connect(self._onPopupHidden)
        self.showSerialComboboxSignal.connect(self._onPortsScanned)
        self.serialPortCombobox.currentIndexChanged.connect(lambda: self.onSerialConfigChanged("port", self.serialPortCombobox, str))
        self.serailBaudrateCombobox.currentIndexChanged.connect(lambda: self.onSerialConfigChanged("baudrate", self.serailBaudrateCombobox, int, caller="index change"))
        self.serailBaudrateCombobox.editTextChanged.connect(lambda: self.onSerialConfigChanged("baudrate", self.serailBaudrateCombobox, int, caller="text change"))
        self.serailBytesCombobox.currentIndexChanged.connect(lambda: self.onSerialConfigChanged("bytesize", self.serailBytesCombobox, int))
        self.serailParityCombobox.currentIndexChanged.connect(lambda: self.onSerialConfigChanged("parity", self.serailParityCombobox, str))
        self.serailStopbitsCombobox.currentIndexChanged.connect(lambda: self.onSerialConfigChanged("stopbits", self.serailStopbitsCombobox, str))
        self.serialFlowControlCombobox.currentIndexChanged.connect(lambda: self.onSerialConfigChanged("flowcontrol", self.serialFlowControlCombobox, str))
        self.checkBoxRTS.clicked.connect(lambda: self.onSerialConfigChanged("rts", self.checkBoxRTS, bool))
        self.checkBoxDTR.clicked.connect(lambda: self.onSerialConfigChanged("dtr", self.checkBoxDTR, bool))
        self.serialOpenCloseButton.clicked.connect(self.openCloseSerial)
        self.showSwitchSignal.connect(self.showSwitch)

    def onSerialConfigChanged(self, conf_type, obj, value_type, caller=""):
        if conf_type == "port":
            details = obj.itemData(obj.currentIndex(), Qt.ToolTipRole)
            obj.setToolTip(details or obj.currentText())
            newPort = obj.currentData(Qt.UserRole) or ""
            if newPort and not self._applying_ports and (newPort != self.config["port"] or  not self.com.port):
                self.config["port"] = newPort
                print("-- set to new port:", self.config["port"])
                self.displayNameChanged.emit()
                try:
                    self.com.port = self.config["port"]
                except Exception as e:
                    msg = _("Open Failed") +"\n"+ str(e)
                    self.status = ConnectionStatus.CLOSED
                    self.onConnectionStatus.emit(self.status, msg)
                    self.showSwitchSignal.emit(self.status)
        elif conf_type in ["baudrate", "bytesize", "parity", "stopbits"]:
            # custom baudrate input
            text = obj.currentText()
            if conf_type == "baudrate" and ((not text) or text == self.baudrateCustomStr):
                self.serailBaudrateCombobox.clearEditText()
                return
            self.config[conf_type] = value_type(text.split(" ")[0])
            print("-- set serial {} to {}".format(conf_type, self.config[conf_type]))
            if conf_type == "parity":
                self.com.__setattr__(conf_type, self.config[conf_type][0])
            elif conf_type == "stopbits":
                self.com.__setattr__(conf_type, float(self.config[conf_type]))
            else:
                self.com.__setattr__(conf_type, self.config[conf_type])
        elif conf_type == "flowcontrol":
            self.config[conf_type] = value_type(obj.currentText().split(" ")[0])
            if self.config[conf_type] == "XON/XOFF":
                self.com.xonxoff = True
            else:
                self.com.xonxoff = False
            if self.config[conf_type] == "RTS/CTS":
                self.com.rtscts = True
            else:
                self.com.rtscts = False
            if self.config[conf_type] == "DSR/DTR":
                self.com.dsrdtr = True
            else:
                self.com.dsrdtr = False
        elif conf_type in ["rts", "dtr"]:
            self.config[conf_type] = obj.isChecked()
            self.com.__setattr__(conf_type, self.config[conf_type])

    def setSerialConfig(self, conf_type, obj, value):
        def getCommboboxItems(obj):
            values = []
            for i in range(len(obj)):
                values.append(obj.itemText(i))
            return values
        if conf_type == "port":
            idx = -1
            for i in range(obj.count()):
                if obj.itemData(i, Qt.UserRole) == str(value):
                    idx = i
                    break
            obj.setCurrentIndex(idx)
            if value:
                self.com.port = str(value)
        elif conf_type in ["baudrate", "bytesize", "parity", "stopbits"]:
            values = getCommboboxItems(obj)
            idx = 0
            try:
                idx = values.index(str(value))
            except Exception:
                print(f"-- set {obj} index {idx} error, value {value}, items {values}")
            obj.setCurrentIndex(idx)
            if conf_type == "parity":
                value = value[0]
            elif conf_type == "stopbits":
                value = float(value)
            self.com.__setattr__(conf_type, value)
            if conf_type == "baudrate":
                self.oneByteTime = 1 / (self.com.baudrate / (self.com.bytesize + 2 + self.com.stopbits)) # 1 byte use time
        elif conf_type == "flowcontrol":
            values = getCommboboxItems(obj)
            idx = 0
            try:
                idx = values.index(str(value))
            except Exception:
                print(f"-- set {obj} index {idx} error, value {value}, items {values}")
            obj.setCurrentIndex(idx)
            if value == "XON/XOFF":
                self.com.xonxoff = True
            else:
                self.com.xonxoff = False
            if value == "RTS/CTS":
                self.com.rtscts = True
            else:
                self.com.rtscts = False
            if value == "DSR/DTR":
                self.com.dsrdtr = True
            else:
                self.com.dsrdtr = False
        elif conf_type in ["rts", "dtr"]:
            obj.setChecked(value)
            self.com.__setattr__(conf_type, value)

    def openCloseSerial(self):
        if self.busy:
            return
        self.busy = True
        if self.serialOpenCloseButton.text() == _("OPEN"):
            self.isOpened = False
        else:
            self.isOpened = True
        t = threading.Thread(target=self.openCloseSerialProcess)
        t.setDaemon(True)
        t.start()

    def open(self):
        """Open the configured port explicitly (used by session restore)."""
        if self.busy or self.isConnected() or self.status == ConnectionStatus.CONNECTING:
            return
        if not self.config.get("port"):
            return
        # The port combobox may still be empty while the first scan is in
        # flight, so do not rely on setSerialConfig having touched com.port.
        self.com.port = self.config["port"]
        self.isOpened = False
        self.busy = True
        t = threading.Thread(target=self.openCloseSerialProcess)
        t.setDaemon(True)
        t.start()

    def openCloseSerialProcess(self):
        if self.isOpened:
            print("-- close serial")
            try:
                # set status first to prevent auto reconnect
                self.status = ConnectionStatus.CLOSED
                self.com.close()
            except Exception:
                pass
            self.onConnectionStatus.emit(self.status, "")
            self.showSwitchSignal.emit(self.status)
        else:
            try:
                print("-- open serial")
                self.onConnectionStatus.emit(ConnectionStatus.CONNECTING, "")
                self.com.open()
                # Drop bytes a previous session (or the driver) left queued;
                # otherwise the remote shell receives stale ZMODEM payload as
                # if the user had typed it.
                self.com.reset_input_buffer()
                self.com.reset_output_buffer()
                self.status = ConnectionStatus.CONNECTED
                self.onConnectionStatus.emit(self.status, "")
                self.showSwitchSignal.emit(self.status)
                self.receiveProcess = threading.Thread(target=self.receiveDataProcess)
                self.receiveProcess.setDaemon(True)
                self.receiveProcess.start()
            except Exception as e:
                try:
                    self.com.close()
                except Exception:
                    pass
                msg = _("Open Failed") +"\n"+ str(e)
                self.hintSignal.emit("error", _("Error"), msg)
                self.status = ConnectionStatus.CLOSED
                self.onConnectionStatus.emit(self.status, msg)
                self.showSwitchSignal.emit(self.status)
        self.busy = False

    def requestPortScan(self):
        """Start one background scan, coalescing concurrent requests."""
        if self._port_scan_in_flight or self.widget is None:
            return
        self._port_scan_in_flight = True
        t = threading.Thread(target=self._scanPortsProcess)
        t.setDaemon(True)
        t.start()

    def _scanPortsProcess(self):
        try:
            ports = collect_ports()
        except Exception as e:
            print("-- scan serial ports failed:", e)
            ports = []
        # Always emit so the UI thread clears the in-flight flag.
        self.showSerialComboboxSignal.emit(ports)

    def _onPortsScanned(self, ports):
        self._port_scan_in_flight = False
        keys = port_keys(ports)
        if self._last_port_keys == keys:
            return
        self._applyPorts(ports)
        self._last_port_keys = keys

    def _applyPorts(self, ports):
        combo = self.serialPortCombobox
        was_popup_visible = self._popup_visible
        preferred = combo.currentData(Qt.UserRole) or self.config.get("port")
        # Only pick the first port automatically for a brand-new terminal
        # whose config has no remembered device. A saved session with a
        # missing COM port must stay pointed at that port so auto-connect
        # fails cleanly instead of silently opening a different device.
        startup_select_first = (
            not self._initial_scan_done
            and bool(ports)
            and not preferred
        )
        selected = -1
        self._applying_ports = True
        combo.blockSignals(True)
        try:
            combo.clear()
            if not ports:
                combo.addItem(_("No serial port found"))
                item = combo.model().item(combo.count() - 1)
                if item is not None:
                    item.setEnabled(False)
                combo.setCurrentIndex(-1)
                combo.setToolTip("")
            else:
                for port in ports:
                    combo.addItem(display_text(port))
                    index = combo.count() - 1
                    combo.setItemData(index, port.device, Qt.UserRole)
                    combo.setItemData(index, format_port_details(port), Qt.ToolTipRole)
                selected = choose_index(ports, preferred)
                if selected < 0 and startup_select_first:
                    selected = 0
                combo.setCurrentIndex(selected)
                if selected >= 0:
                    combo.setToolTip(display_text(ports[selected]))
                else:
                    combo.setToolTip("")
        finally:
            combo.blockSignals(False)
            self._applying_ports = False
        self._initial_scan_done = True
        if selected >= 0:
            # Keep Serial.com in sync with the selected item. Signals were
            # blocked during the rebuild, so do it explicitly.
            self.onSerialConfigChanged("port", combo, str)
        if was_popup_visible:
            combo.updatePopupWidth()
            if not combo.view().isVisible():
                # Some platforms close the popup when its model is cleared.
                combo.showItems()

    def _onPopupShown(self):
        self._popup_visible = True
        if not self._applying_ports:
            self.requestPortScan()
        if self._port_refresh_timer is not None:
            self._port_refresh_timer.start()

    def _onPopupHidden(self):
        self._stopPortRefresh()

    def _stopPortRefresh(self):
        self._popup_visible = False
        if self._port_refresh_timer is not None:
            self._port_refresh_timer.stop()

    # @pyqtSlot(ConnectionStatus)
    def showSwitch(self, status):
        if status == ConnectionStatus.CLOSED:
            self.serialOpenCloseButton.setText(_("OPEN"))
            self.serialOpenCloseButton.setProperty("class", "")
        elif status == ConnectionStatus.CONNECTED:
            self.serialOpenCloseButton.setText(_("CLOSE"))
            self.serialOpenCloseButton.setProperty("class", "")
        else:
            self.serialOpenCloseButton.setText(_("CLOSE"))
            self.serialOpenCloseButton.setProperty("class", "warning")
        self.updateStyle(self.serialOpenCloseButton)

    def updateStyle(self, widget):
        self.widget.style().unpolish(widget)
        self.widget.style().polish(widget)
        self.widget.update()

    def findSerialPort(self):
        self.port_list = list(serial.tools.list_ports.comports())
        return self.port_list

    def portExits(self, port):
        ports = self.findSerialPort()
        devices = []
        for p in ports:
            devices.append(p.device)
        if port in devices:
            return True
        return False

    def receiveDataProcess(self):
        waitingReconnect = False
        # Non-blocking reads: some USB-serial drivers ignore the read timeout
        # and block until the requested byte count arrives.  Polling avoids
        # that and lets us flush the coalescing buffer when the line goes idle
        # (a ZMODEM ack header is often the last thing the peer sends).
        self.com.timeout = 0
        buffer = b''
        t = 0
        while self.status != ConnectionStatus.CLOSED:
            if waitingReconnect:
                if self.portExits(self.com.port):
                    try:
                        self.onConnectionStatus.emit(ConnectionStatus.CONNECTING, "")
                        self.com.open()
                        self.com.reset_input_buffer()
                        self.com.reset_output_buffer()
                        print("-- reopen serial")
                        waitingReconnect = False
                        self.onConnectionStatus.emit(ConnectionStatus.CONNECTED, _("Reconnected"))
                        self.showSwitchSignal.emit(ConnectionStatus.CONNECTED)
                        continue
                    except Exception as e:
                        pass
                time.sleep(0.01)
                continue
            try:
                length = self.com.in_waiting
                if length:
                    data = self.com.read(length)
                    if data:
                        t = time.time()
                        buffer += data
                if buffer and (
                    len(buffer) >= 4096
                    or (time.time() - t > self.oneByteTime * 2)
                ): # no new data in next frame
                    try:
                        self.onReceived(buffer)
                    except Exception as e:
                        print("-- error in onReceived callback:", e)
                    buffer = b''
                if not length:
                    time.sleep(0.001)
            except Exception as e:
                if (self.status != ConnectionStatus.CLOSED):
                    # close as fast as we can to release port
                    try:
                        self.com.close()
                    except Exception:
                        pass
                    waitingReconnect = True
                    self.status = ConnectionStatus.LOSE
                    self.onConnectionStatus.emit(ConnectionStatus.LOSE, _("Connection lose!"))
                    self.showSwitchSignal.emit(ConnectionStatus.LOSE)


    def send(self, data : bytes):
        self.com.write(data)

    def isConnected(self):
        return (self.status == ConnectionStatus.CONNECTED) or (self.status == ConnectionStatus.LOSE)

    def getConnStatus(self):
        return self.status

if __name__ == "__main__":
    from PyQt5.QtWidgets import QApplication
    app = QApplication(sys.argv)

    conn = Serial()
    conn.onInit({})

    def onReceived(data):
        print(data)
        conn.send(data)

    conn.onReceived = onReceived
    window = conn.onWidget()
    window.show()
    ret = app.exec_()
