
from PyQt5.QtCore import pyqtSignal
from PyQt5.QtCore import QObject
from enum import Enum


class ConnectionStatus(Enum):
    CLOSED = 0
    CONNECTED = 1
    LOSE = 2 
    CONNECTING = 3


class COMM(QObject):
    '''
        call sequence:
            onInit
            onWidget
            onUiInitDone
                isConnected or getConnStatus
                send
            getConfig
            onDel
    '''
    onReceived     = lambda self, data:None   # data: bytes
    onConnectionStatus = pyqtSignal(ConnectionStatus, str)    # connected, msg
    displayNameChanged = pyqtSignal()                         # active port/endpoint changed
    hintSignal = pyqtSignal(str, str, str)                    # hintSignal.emit(type(error, warning, info), title, msg)
    configGlobal = {}
    id = ""
    name = ""

    def __init__(self) -> None:
        super().__init__()
        if (not self.id) or not self.name:
            raise ValueError(f"var id of {self} should be set")

    def onInit(self, config):
        '''
            init params, DO NOT take too long time in this func
        '''
        pass

    def onWidget(self):
        '''
            this method runs in UI thread, do not block too long
        '''
        raise NotImplementedError()

    def onUiInitDone(self):
        '''
            UI init done, you can update your widget here
            this method runs in UI thread, do not block too long
        '''

    def send(self, data : bytes):
        raise NotImplementedError()

    def isConnected(self):
        raise NotImplementedError()

    def getConnStatus(self):
        raise NotImplementedError()

    def disconnect(self):
        raise NotImplementedError()

    def getConfig(self):
        '''
            get config, dict type
            this method runs in UI thread, do not block too long
        '''
        return {}

    def getDisplayName(self):
        '''Short name used for the tab title, e.g. COM99 or /dev/ttyUSB0.'''
        return ""

    def ctrl(self, k, v):
        pass

    def onDel(self):
        '''
            del all things and wait all thread to exit
        '''
        pass