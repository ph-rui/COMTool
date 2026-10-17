from PyQt5.QtWidgets import QComboBox,QListView,QApplication
from PyQt5.QtCore import pyqtSignal


class ComboBox(QComboBox):
    clicked = pyqtSignal()
    popupShown = pyqtSignal()
    popupHidden = pyqtSignal()

    def __init__(self):
        QComboBox.__init__(self)
        self._popup_visible = False
        listView = QListView()
        listView.executeDelayedItemsLayout()
        self.setView(listView)

    def mouseReleaseEvent(self, QMouseEvent):
        self.showItems()

    def showPopup(self):
        # self.popupAboutToBeShown.emit()
        # prevent show popup, manually call it in mouse release event
        pass

    def _showPopup(self):
        self.updatePopupWidth()
        super(ComboBox, self).showPopup()
        if not self._popup_visible:
            self._popup_visible = True
            self.popupShown.emit()

    def updatePopupWidth(self):
        self.view().executeDelayedItemsLayout()
        max_w = self.view().sizeHintForColumn(0)
        screen_width = QApplication.desktop().availableGeometry().width()
        self.view().setMinimumWidth(min(max_w + 50, screen_width))

    def hidePopup(self):
        was_visible = self._popup_visible
        super(ComboBox, self).hidePopup()
        if was_visible:
            self._popup_visible = False
            self.popupHidden.emit()

    def showItems(self):
        self._showPopup()

    def mousePressEvent(self, QMouseEvent):
        self.clicked.emit()
