#include "mainwindow.h"

#include "i18n.h"
#include "sessiontab.h"
#include "theme.h"

#include <QAction>
#include <QApplication>
#include <QCloseEvent>
#include <QEvent>
#include <QKeySequence>
#include <QMenu>
#include <QMenuBar>
#include <QMessageBox>
#include <QTabWidget>
#include <QTimer>
#include <QToolButton>

#include <algorithm>

MainWindow::MainWindow(QWidget *parent) : QMainWindow(parent)
{
    config_ = AppConfig::load();
    dark_ = config_.darkTheme;

    setWindowTitle(QStringLiteral("COMTool Native"));
    resize(960, 640);
    if (!config_.geometry.isEmpty()) {
        restoreGeometry(config_.geometry);
    }

    tabs_ = new QTabWidget(this);
    tabs_->setTabsClosable(true);
    tabs_->setMovable(true);
    tabs_->setDocumentMode(true);
    setCentralWidget(tabs_);

    auto *newButton = new QToolButton(tabs_);
    newButton->setText(QStringLiteral("+"));
    newButton->setAutoRaise(true);
    newButton->setToolTip(I18n::tr("New Tab"));
    tabs_->setCornerWidget(newButton, Qt::TopRightCorner);
    connect(newButton, &QToolButton::clicked, this, [this] { newTab(); });

    connect(tabs_, &QTabWidget::currentChanged, this, [this](int) {
        updateWindowTitle();
        if (SessionTab *tab = currentTab()) {
            tab->focusTerminal();
        }
    });
    connect(tabs_, &QTabWidget::tabCloseRequested, this, &MainWindow::closeTab);

    auto *fileMenu = menuBar()->addMenu(I18n::tr("File"));
    auto *newAction = fileMenu->addAction(I18n::tr("New Tab"));
    newAction->setShortcut(QKeySequence::New);
    connect(newAction, &QAction::triggered, this, [this] { newTab(); });
    auto *closeAction = fileMenu->addAction(I18n::tr("Close Tab"));
    closeAction->setShortcut(QKeySequence::Close);
    connect(closeAction, &QAction::triggered, this, [this] {
        if (tabs_->count() > 0) {
            closeTab(tabs_->currentIndex());
        }
    });
    fileMenu->addSeparator();
    auto *quitAction = fileMenu->addAction(I18n::tr("Quit"));
    quitAction->setShortcut(QKeySequence::Quit);
    connect(quitAction, &QAction::triggered, this, &QWidget::close);

    auto *viewMenu = menuBar()->addMenu(I18n::tr("View"));
    auto *darkAction = viewMenu->addAction(I18n::tr("Dark Theme"));
    darkAction->setCheckable(true);
    darkAction->setChecked(dark_);
    connect(darkAction, &QAction::toggled, this, &MainWindow::applyTheme);
    viewMenu->addSeparator();
    auto *increaseAction = viewMenu->addAction(I18n::tr("Increase Font"));
    increaseAction->setShortcut(QKeySequence::ZoomIn);
    connect(increaseAction, &QAction::triggered, this, [this] {
        if (SessionTab *tab = currentTab()) {
            setFontSize(tab->fontPointSize() + 1);
        }
    });
    auto *decreaseAction = viewMenu->addAction(I18n::tr("Decrease Font"));
    decreaseAction->setShortcut(QKeySequence::ZoomOut);
    connect(decreaseAction, &QAction::triggered, this, [this] {
        if (SessionTab *tab = currentTab()) {
            setFontSize(tab->fontPointSize() - 1);
        }
    });

    auto *helpMenu = menuBar()->addMenu(I18n::tr("Help"));
    auto *aboutAction = helpMenu->addAction(I18n::tr("About"));
    connect(aboutAction, &QAction::triggered, this, [this] {
        QMessageBox::about(
            this, I18n::tr("About COMTool Native"),
            QStringLiteral("<b>COMTool Native 0.1.0</b><br><br>"
                           "A native serial terminal with ZMODEM.<br>"
                           "Qt %1 · libvterm 0.3.3")
                .arg(QString::fromLatin1(qVersion())));
    });

    applyTheme(dark_);

    if (config_.tabs.isEmpty()) {
        newTab();
    } else {
        for (const SerialConfig &config : config_.tabs) {
            newTab(config);
        }
        tabs_->setCurrentIndex(0);
    }

    QTimer::singleShot(0, this, [this] {
        if (SessionTab *tab = currentTab()) {
            tab->focusTerminal();
        }
    });
}

MainWindow::~MainWindow() = default;

void MainWindow::newTab(const SerialConfig &config)
{
    auto *tab = new SessionTab(config, dark_, config_.fontPointSize, config_.scrollbackLines,
                               config_.panelVisible, config_.lastDir, config_.zmodemLastDir,
                               tabs_);
    const QString name = config.portName.isEmpty()
                             ? QStringLiteral("%1 %2").arg(I18n::tr("Session")).arg(tabs_->count() + 1)
                             : config.portName;
    const int index = tabs_->addTab(tab, name);
    tabs_->setCurrentIndex(index);
    tab->focusTerminal();
    updateWindowTitle();
}

void MainWindow::closeTab(int index)
{
    auto *tab = qobject_cast<SessionTab *>(tabs_->widget(index));
    if (tab == nullptr) {
        return;
    }
    if (tab->isBusy()) {
        const auto answer = QMessageBox::question(
            this, I18n::tr("Close Tab"),
            I18n::tr("Close this session while a file transfer is active?"));
        if (answer != QMessageBox::Yes) {
            return;
        }
    }
    tab->closePort();
    tabs_->removeTab(index);
    tab->deleteLater();
    if (tabs_->count() == 0) {
        newTab();
    }
}

void MainWindow::changeEvent(QEvent *event)
{
    QMainWindow::changeEvent(event);
    if (event->type() == QEvent::WindowActivate && !initialFocusDone_) {
        initialFocusDone_ = true;
        QTimer::singleShot(0, this, [this] {
            if (SessionTab *tab = currentTab()) {
                tab->focusTerminal();
            }
        });
    }
}

void MainWindow::closeEvent(QCloseEvent *event)
{
    bool busy = false;
    for (int index = 0; index < tabs_->count(); ++index) {
        auto *tab = qobject_cast<SessionTab *>(tabs_->widget(index));
        if (tab != nullptr && tab->isBusy()) {
            busy = true;
            break;
        }
    }
    if (busy) {
        const auto answer = QMessageBox::question(
            this, I18n::tr("Quit"),
            I18n::tr("Close COMTool Native while a file transfer is active?"));
        if (answer != QMessageBox::Yes) {
            event->ignore();
            return;
        }
    }

    saveConfig();
    for (int index = 0; index < tabs_->count(); ++index) {
        if (auto *tab = qobject_cast<SessionTab *>(tabs_->widget(index))) {
            tab->closePort();
        }
    }
    event->accept();
}

void MainWindow::saveConfig()
{
    config_.geometry = saveGeometry();
    config_.windowState = saveState();
    config_.darkTheme = dark_;
    if (SessionTab *tab = currentTab()) {
        config_.fontPointSize = tab->fontPointSize();
        config_.scrollbackLines = tab->scrollbackLinesForConfig();
        config_.panelVisible = tab->panelVisible();
        config_.lastDir = tab->lastDir();
        config_.zmodemLastDir = tab->zmodemLastDir();
    }
    config_.tabs.clear();
    for (int index = 0; index < tabs_->count(); ++index) {
        if (auto *tab = qobject_cast<SessionTab *>(tabs_->widget(index))) {
            config_.tabs.append(tab->config());
        }
    }
    AppConfig::save(config_);
}

void MainWindow::applyTheme(bool dark)
{
    dark_ = dark;
    Theme::apply(*qApp, dark_);
    for (int index = 0; index < tabs_->count(); ++index) {
        if (auto *tab = qobject_cast<SessionTab *>(tabs_->widget(index))) {
            tab->setDarkTheme(dark_);
        }
    }
    updateWindowTitle();
}

void MainWindow::updateWindowTitle()
{
    QString title = QStringLiteral("COMTool Native");
    if (SessionTab *tab = currentTab()) {
        const QString port = tab->config().portName;
        if (!port.isEmpty()) {
            title += QStringLiteral(" — ") + port;
        }
    }
    setWindowTitle(title);
}

void MainWindow::setFontSize(int pointSize)
{
    pointSize = std::clamp(pointSize, 6, 40);
    for (int index = 0; index < tabs_->count(); ++index) {
        if (auto *tab = qobject_cast<SessionTab *>(tabs_->widget(index))) {
            tab->setFontPointSize(pointSize);
        }
    }
    config_.fontPointSize = pointSize;
}

SessionTab *MainWindow::currentTab() const
{
    return qobject_cast<SessionTab *>(tabs_->currentWidget());
}
