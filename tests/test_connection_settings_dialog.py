"""Offscreen tests for the detached connection-settings dialog."""

import os
import sys

import pytest

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

pytest.importorskip("PyQt5")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from PyQt5.QtWidgets import QApplication, QLabel  # noqa: E402

from COMTool import main2, parameters  # noqa: E402
from COMTool.widgets import EventFilter  # noqa: E402


class FakePlugin:
    """Minimal auto-hide plugin used to exercise the settings host."""

    id = "fake"
    name = "Fake"
    autoHideSettings = True
    displayNameFromConnection = False

    def onInit(self, config):
        self.config = config

    def onIsAddConnWidget(self):
        return False

    def onWidgetMain(self, parent):
        return QLabel("main")

    def onWidgetSettings(self, parent):
        return QLabel("settings")

    def onWidgetStatusBar(self, parent):
        return None

    def onWidgetFunctional(self, parent):
        return None

    def onUiInitDone(self):
        pass

    def onActive(self):
        pass

    def onDel(self):
        pass


@pytest.fixture(scope="module")
def app():
    return QApplication.instance() or QApplication([])


@pytest.fixture
def window(app, monkeypatch):
    monkeypatch.setattr(main2.SerialPortWatcher, "start", lambda self: None)

    config = parameters.Parameters()
    config["serialSessions"] = []
    config["serialSessionsMigrated"] = True
    config["sessionSidebar"] = {"visible": True, "width": 220}

    win = main2.MainWindow(app, EventFilter(), config)
    win.saveConfig = lambda: None
    win.resize(900, 560)
    win.show()
    app.processEvents()
    yield win
    win.portWatcher.stop()
    win.hide()
    win.deleteLater()
    app.processEvents()


def test_auto_hide_settings_are_detached_from_the_tab_splitter(window, app):
    item = window.addItem(FakePlugin, setCurrent=True, pluginConfig={"version": 1})
    app.processEvents()

    assert item.settingWidget is not None
    assert not item.settingWidget.isVisible()
    assert item.settingWidget.parent() is None


def test_settings_button_opens_and_closes_dialog(window, app):
    item = window.addItem(FakePlugin, setCurrent=True, pluginConfig={"version": 1})
    app.processEvents()

    window.showSettings()
    app.processEvents()

    dialog = item.settingsDialog
    assert dialog is not None
    assert dialog.isVisible()
    assert item.settingWidget.isVisible()
    assert item.getDisplayName() in dialog.windowTitle()

    window.hideSettings()
    app.processEvents()
    assert not dialog.isVisible()


def test_switching_tabs_closes_previous_settings_dialog(window, app):
    first = window.addItem(FakePlugin, setCurrent=True, pluginConfig={"version": 1})
    second = window.addItem(FakePlugin, setCurrent=False, pluginConfig={"version": 1})
    app.processEvents()

    window.tabWidget.setCurrentWidget(first.widget)
    window.showSettings()
    app.processEvents()
    first_dialog = first.settingsDialog
    assert first_dialog.isVisible()

    window.tabWidget.setCurrentWidget(second.widget)
    app.processEvents()
    assert not first_dialog.isVisible()

    # The hidden dialog keeps the widget alive for the next visit.
    window.tabWidget.setCurrentWidget(first.widget)
    window.showSettings()
    app.processEvents()
    assert first.settingsDialog is first_dialog
    assert first_dialog.isVisible()
    assert first.settingWidget.isVisible()


def test_closing_tab_closes_its_settings_dialog(window, app):
    item = window.addItem(FakePlugin, setCurrent=True, pluginConfig={"version": 1})
    app.processEvents()

    window.showSettings()
    app.processEvents()
    dialog = item.settingsDialog
    assert dialog.isVisible()

    window.closeTab(window.tabWidget.indexOf(item.widget))
    app.processEvents()
    assert not dialog.isVisible()
