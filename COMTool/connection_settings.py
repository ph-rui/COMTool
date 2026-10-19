"""Detached dialog used for serial/connection settings."""

from PyQt5.QtCore import Qt
from PyQt5.QtWidgets import QDialog, QDialogButtonBox, QVBoxLayout


class ConnectionSettingsDialog(QDialog):
    """Host a plugin's existing settings widget in a normal tool window.

    The widget keeps owning its controls and signals; this dialog only provides
    a separate surface so it does not share space with the session sidebar.
    """

    def __init__(self, parent, title, settings_widget):
        super().__init__(parent)
        self.setModal(False)
        self.setWindowTitle(title)
        self.setWindowFlag(Qt.Tool, True)
        self.setWindowFlag(Qt.WindowContextHelpButtonHint, False)
        self.setSizeGripEnabled(True)
        self._settings_widget = settings_widget

        layout = QVBoxLayout(self)
        layout.setContentsMargins(12, 12, 12, 12)
        layout.setSpacing(8)
        layout.addWidget(settings_widget)

        buttons = QDialogButtonBox(QDialogButtonBox.Close)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

        self.resize(380, 460)

    def showEvent(self, event):
        # The settings widget was hidden while it lived outside the layout.
        self._settings_widget.show()
        super().showEvent(event)
