"""Background serial-port polling for the session sidebar."""

import threading

from PyQt5.QtCore import QObject, QTimer, pyqtSignal

try:
    from .conn.port_utils import collect_ports
    from .parameters import log
except ImportError:
    from conn.port_utils import collect_ports
    from parameters import log


class SerialPortWatcher(QObject):
    """Poll serial ports without blocking the UI thread.

    ``portsScanned`` carries ``(ports, success)``.  Callers must only prune
    remembered sessions when ``success`` is true; an enumeration failure must
    not look like "all devices disappeared".
    """

    portsScanned = pyqtSignal(list, bool)

    def __init__(self, parent=None, interval_ms=2000, collect_func=None):
        super().__init__(parent)
        self._collect = collect_func or collect_ports
        self._busy = False
        self._stopped = False
        self._timer = QTimer(self)
        self._timer.setInterval(interval_ms)
        self._timer.timeout.connect(self.refresh)

    def start(self):
        self._stopped = False
        self.refresh()
        self._timer.start()

    def stop(self):
        self._stopped = True
        self._timer.stop()

    def refresh(self):
        if self._busy or self._stopped:
            return
        self._busy = True
        thread = threading.Thread(target=self._scan_process)
        thread.daemon = True
        thread.start()

    def _scan_process(self):
        try:
            ports = self._collect()
        except Exception as exc:
            log.w("scan serial ports for session sidebar failed: {}".format(exc))
            ports = []
            success = False
        else:
            success = True
        self._busy = False
        if not self._stopped:
            self.portsScanned.emit(ports, success)
