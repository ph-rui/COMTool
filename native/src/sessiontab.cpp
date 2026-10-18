#include "sessiontab.h"

#include "i18n.h"
#include "serialworker.h"
#include "terminalview.h"

#include <QComboBox>
#include <QDir>
#include <QFile>
#include <QFileDialog>
#include <QFileInfo>
#include <QFormLayout>
#include <QHBoxLayout>
#include <QLabel>
#include <QMessageBox>
#include <QPalette>
#include <QProgressBar>
#include <QPushButton>
#include <QSerialPortInfo>
#include <QSignalBlocker>
#include <QStandardPaths>
#include <QTimer>
#include <QToolButton>
#include <QVBoxLayout>
#include <QPointer>

#include <algorithm>

namespace {

QString homeDirectory()
{
    const QString documents = QStandardPaths::writableLocation(QStandardPaths::DocumentsLocation);
    if (!documents.isEmpty() && QDir(documents).exists()) {
        return documents;
    }
    return QDir::homePath();
}

QComboBox *makeCombo(QWidget *parent)
{
    auto *combo = new QComboBox(parent);
    combo->setSizeAdjustPolicy(QComboBox::AdjustToMinimumContentsLengthWithIcon);
    combo->setMinimumContentsLength(8);
    return combo;
}

} // namespace

SessionTab::SessionTab(const SerialConfig &config, bool darkTheme, int fontPointSize,
                       int scrollbackLines, bool panelVisible, const QString &lastDir,
                       const QString &zmodemLastDir, QWidget *parent)
    : QWidget(parent),
      config_(config),
      dark_(darkTheme),
      fontPointSize_(fontPointSize),
      scrollbackLines_(scrollbackLines),
      panelVisible_(panelVisible),
      lastDir_(lastDir.isEmpty() ? homeDirectory() : lastDir),
      zmodemLastDir_(zmodemLastDir.isEmpty() ? lastDir_ : zmodemLastDir)
{
    buildUi();
    applyConfigToUi();
    refreshPorts();
    setPanelVisible(panelVisible_);

    portTimer_ = new QTimer(this);
    portTimer_->setInterval(1200);
    connect(portTimer_, &QTimer::timeout, this, &SessionTab::refreshPorts);
    portTimer_->start();
}

SessionTab::~SessionTab()
{
    closePort();
}

void SessionTab::buildUi()
{
    auto *mainLayout = new QHBoxLayout(this);
    mainLayout->setContentsMargins(0, 0, 0, 0);
    mainLayout->setSpacing(0);

    panel_ = new QWidget(this);
    panel_->setMinimumWidth(210);
    panel_->setMaximumWidth(270);
    auto *panelLayout = new QVBoxLayout(panel_);
    panelLayout->setContentsMargins(10, 10, 10, 10);
    panelLayout->setSpacing(8);

    auto *title = new QLabel(I18n::tr("Serial Port"), panel_);
    QFont titleFont = title->font();
    titleFont.setBold(true);
    title->setFont(titleFont);
    panelLayout->addWidget(title);

    auto *form = new QFormLayout;
    form->setFieldGrowthPolicy(QFormLayout::AllNonFixedFieldsGrow);
    form->setLabelAlignment(Qt::AlignLeft | Qt::AlignVCenter);
    form->setHorizontalSpacing(8);
    form->setVerticalSpacing(6);

    portCombo_ = makeCombo(panel_);
    portCombo_->setEditable(true);
    refreshButton_ = new QPushButton(QStringLiteral("⟳"), panel_);
    refreshButton_->setFixedWidth(28);
    refreshButton_->setToolTip(I18n::tr("Refresh"));
    auto *portRow = new QWidget(panel_);
    auto *portLayout = new QHBoxLayout(portRow);
    portLayout->setContentsMargins(0, 0, 0, 0);
    portLayout->setSpacing(4);
    portLayout->addWidget(portCombo_, 1);
    portLayout->addWidget(refreshButton_);
    form->addRow(I18n::tr("Port"), portRow);
    connect(refreshButton_, &QPushButton::clicked, this, &SessionTab::refreshPorts);

    baudCombo_ = makeCombo(panel_);
    baudCombo_->setEditable(true);
    for (int baud : {9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600, 1500000,
                     2000000, 3000000}) {
        baudCombo_->addItem(QString::number(baud), baud);
    }
    form->addRow(I18n::tr("Baud rate"), baudCombo_);

    dataBitsCombo_ = makeCombo(panel_);
    for (int bits : {5, 6, 7, 8}) {
        dataBitsCombo_->addItem(QString::number(bits), bits);
    }
    form->addRow(I18n::tr("Data bits"), dataBitsCombo_);

    parityCombo_ = makeCombo(panel_);
    parityCombo_->addItem(I18n::tr("None"), static_cast<int>(QSerialPort::NoParity));
    parityCombo_->addItem(QStringLiteral("Even"), static_cast<int>(QSerialPort::EvenParity));
    parityCombo_->addItem(QStringLiteral("Odd"), static_cast<int>(QSerialPort::OddParity));
    parityCombo_->addItem(QStringLiteral("Mark"), static_cast<int>(QSerialPort::MarkParity));
    parityCombo_->addItem(QStringLiteral("Space"), static_cast<int>(QSerialPort::SpaceParity));
    form->addRow(I18n::tr("Parity"), parityCombo_);

    stopBitsCombo_ = makeCombo(panel_);
    stopBitsCombo_->addItem(QStringLiteral("1"), 1);
    stopBitsCombo_->addItem(QStringLiteral("1.5"), 15);
    stopBitsCombo_->addItem(QStringLiteral("2"), 2);
    form->addRow(I18n::tr("Stop bits"), stopBitsCombo_);

    flowCombo_ = makeCombo(panel_);
    flowCombo_->addItem(I18n::tr("None"), static_cast<int>(QSerialPort::NoFlowControl));
    flowCombo_->addItem(I18n::tr("Hardware"), static_cast<int>(QSerialPort::HardwareControl));
    flowCombo_->addItem(I18n::tr("Software"), static_cast<int>(QSerialPort::SoftwareControl));
    form->addRow(I18n::tr("Flow control"), flowCombo_);
    panelLayout->addLayout(form);

    openButton_ = new QPushButton(I18n::tr("Open"), panel_);
    panelLayout->addWidget(openButton_);
    connect(openButton_, &QPushButton::clicked, this, &SessionTab::toggleOpen);

    statusLabel_ = new QLabel(I18n::tr("Disconnected"), panel_);
    statusLabel_->setObjectName(QStringLiteral("statusLabel"));
    statusLabel_->setForegroundRole(QPalette::PlaceholderText);
    statusLabel_->setWordWrap(true);
    panelLayout->addWidget(statusLabel_);
    panelLayout->addStretch(1);

    mainLayout->addWidget(panel_);

    collapseButton_ = new QToolButton(this);
    collapseButton_->setAutoRaise(true);
    collapseButton_->setFixedWidth(18);
    collapseButton_->setArrowType(Qt::LeftArrow);
    collapseButton_->setToolTip(I18n::tr("Hide panel"));
    connect(collapseButton_, &QToolButton::clicked, this,
            [this] { setPanelVisible(!panelVisible_); });
    mainLayout->addWidget(collapseButton_, 0, Qt::AlignTop);

    auto *right = new QWidget(this);
    auto *rightLayout = new QVBoxLayout(right);
    rightLayout->setContentsMargins(0, 0, 0, 0);
    rightLayout->setSpacing(0);

    terminal_ = new TerminalView(right);
    terminal_->setDarkTheme(dark_);
    terminal_->setFontPointSize(fontPointSize_);
    terminal_->setScrollbackLines(scrollbackLines_);
    connect(terminal_, &TerminalView::dataToSend, this, [this](const QByteArray &data) {
        if (worker_ != nullptr && open_ && !busy_) {
            worker_->sendData(data);
        }
    });
    rightLayout->addWidget(terminal_, 1);

    transferBar_ = new QWidget(right);
    auto *transferLayout = new QHBoxLayout(transferBar_);
    transferLayout->setContentsMargins(6, 4, 6, 4);
    transferLayout->setSpacing(8);
    transferLabel_ = new QLabel(transferBar_);
    transferLabel_->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Preferred);
    progressBar_ = new QProgressBar(transferBar_);
    progressBar_->setRange(0, 1000);
    progressBar_->setValue(0);
    progressBar_->setTextVisible(true);
    progressBar_->setFixedWidth(220);
    cancelButton_ = new QPushButton(I18n::tr("Cancel"), transferBar_);
    transferLayout->addWidget(transferLabel_, 1);
    transferLayout->addWidget(progressBar_);
    transferLayout->addWidget(cancelButton_);
    connect(cancelButton_, &QPushButton::clicked, this, [this] {
        if (worker_ != nullptr) {
            worker_->cancelTransfer();
        }
    });
    transferBar_->hide();
    rightLayout->addWidget(transferBar_);

    mainLayout->addWidget(right, 1);
}

void SessionTab::applyConfigToUi()
{
    const QSignalBlocker b1(portCombo_);
    const QSignalBlocker b2(baudCombo_);
    const QSignalBlocker b3(dataBitsCombo_);
    const QSignalBlocker b4(parityCombo_);
    const QSignalBlocker b5(stopBitsCombo_);
    const QSignalBlocker b6(flowCombo_);

    portCombo_->setEditText(config_.portName);
    baudCombo_->setEditText(QString::number(config_.baudRate));
    dataBitsCombo_->setCurrentIndex(dataBitsCombo_->findData(static_cast<int>(config_.dataBits)));
    parityCombo_->setCurrentIndex(parityCombo_->findData(static_cast<int>(config_.parity)));
    const int stop = config_.stopBits == QSerialPort::OneAndHalfStop
                         ? 15
                         : (config_.stopBits == QSerialPort::TwoStop ? 2 : 1);
    stopBitsCombo_->setCurrentIndex(stopBitsCombo_->findData(stop));
    flowCombo_->setCurrentIndex(flowCombo_->findData(static_cast<int>(config_.flowControl)));
}

SerialConfig SessionTab::configFromUi() const
{
    SerialConfig config;
    config.portName = portCombo_->currentText().trimmed();
    bool ok = false;
    const int baud = baudCombo_->currentText().toInt(&ok);
    config.baudRate = ok && baud > 0 ? baud : 115200;
    config.dataBits = static_cast<QSerialPort::DataBits>(dataBitsCombo_->currentData().toInt());
    config.parity = static_cast<QSerialPort::Parity>(parityCombo_->currentData().toInt());
    const int stop = stopBitsCombo_->currentData().toInt();
    config.stopBits = stop == 15 ? QSerialPort::OneAndHalfStop
                                 : (stop == 2 ? QSerialPort::TwoStop : QSerialPort::OneStop);
    config.flowControl =
        static_cast<QSerialPort::FlowControl>(flowCombo_->currentData().toInt());
    return config;
}

SerialConfig SessionTab::config() const
{
    return configFromUi();
}

void SessionTab::refreshPorts()
{
    if (portCombo_->hasFocus()) {
        return;
    }
    const QString current = portCombo_->currentText().trimmed();
    const QSignalBlocker blocker(portCombo_);
    portCombo_->clear();
    const auto ports = QSerialPortInfo::availablePorts();
    for (const QSerialPortInfo &info : ports) {
        portCombo_->addItem(info.portName());
        if (!info.description().isEmpty()) {
            portCombo_->setItemData(portCombo_->count() - 1, info.description(), Qt::ToolTipRole);
        }
    }
    const QString wanted = current.isEmpty() ? config_.portName : current;
    if (!wanted.isEmpty() && portCombo_->findText(wanted) < 0) {
        portCombo_->addItem(wanted);
    }
    if (!wanted.isEmpty()) {
        portCombo_->setEditText(wanted);
    }
}

void SessionTab::toggleOpen()
{
    if (worker_ != nullptr) {
        closePort();
    } else {
        openPort();
    }
}

void SessionTab::openPort()
{
    if (worker_ != nullptr) {
        return;
    }
    const SerialConfig cfg = configFromUi();
    if (cfg.portName.isEmpty()) {
        QMessageBox::warning(this, I18n::tr("Cannot open serial port"),
                             I18n::tr("Port is not selected"));
        return;
    }
    config_ = cfg;
    open_ = false;
    openButton_->setText(I18n::tr("Close"));
    openButton_->setEnabled(false);
    statusLabel_->setText(I18n::tr("Opening %1...").arg(cfg.portName));

    worker_ = new SerialWorker(cfg, this);
    connect(worker_, &SerialWorker::opened, this, [this] {
        open_ = true;
        setControlsEnabled(false);
        openButton_->setText(I18n::tr("Close"));
        openButton_->setEnabled(true);
        statusLabel_->setText(I18n::tr("Connected: %1").arg(config_.portName));
        terminal_->setFocus(Qt::OtherFocusReason);
    });
    connect(worker_, &SerialWorker::openFailed, this, [this](const QString &message) {
        open_ = false;
        statusLabel_->setText(I18n::tr("Error: %1").arg(message));
        QMessageBox::warning(this, I18n::tr("Cannot open serial port"), message);
    });
    connect(worker_, &SerialWorker::closed, this, [this](const QString &reason) {
        open_ = false;
        setBusy(false);
        setControlsEnabled(true);
        openButton_->setText(I18n::tr("Open"));
        openButton_->setEnabled(false);
        statusLabel_->setText(reason);
    });
    connect(worker_, &SerialWorker::terminalData, terminal_, &TerminalView::feed);
    connect(worker_, &SerialWorker::zmodemBusyChanged, this, &SessionTab::setBusy);
    connect(worker_, &SerialWorker::zmodemSendRequested, this, &SessionTab::onSendRequested);
    connect(worker_, &SerialWorker::zmodemFileStart, this, &SessionTab::onFileStart);
    connect(worker_, &SerialWorker::zmodemProgress, this, &SessionTab::onProgress);
    connect(worker_, &SerialWorker::zmodemFileComplete, this, [this](const QString &name) {
        statusLabel_->setText(I18n::tr("%1 complete").arg(name));
    });
    connect(worker_, &SerialWorker::zmodemReceiveComplete, this, &SessionTab::onReceiveComplete);
    connect(worker_, &SerialWorker::zmodemSendComplete, this, [this] {
        statusLabel_->setText(I18n::tr("Files sent"));
        progressBar_->setRange(0, 1000);
        progressBar_->setValue(1000);
    });
    connect(worker_, &SerialWorker::zmodemError, this, &SessionTab::onError);
    connect(worker_, &SerialWorker::zmodemCancelled, this, [this] {
        statusLabel_->setText(I18n::tr("File transfer cancelled"));
    });
    connect(worker_, &QThread::finished, this, [this, guard = QPointer<SerialWorker>(worker_)] {
        if (worker_ == guard) {
            worker_ = nullptr;
        }
        if (guard) {
            guard->deleteLater();
        }
        open_ = false;
        setControlsEnabled(true);
        openButton_->setText(I18n::tr("Open"));
        openButton_->setEnabled(true);
        setBusy(false);
    });
    worker_->start();
}

void SessionTab::closePort()
{
    if (worker_ == nullptr) {
        return;
    }
    SerialWorker *worker = worker_;
    worker_ = nullptr;
    worker->requestStop();
    worker->wait();
    worker->deleteLater();
    open_ = false;
    setBusy(false);
    setControlsEnabled(true);
    openButton_->setText(I18n::tr("Open"));
    openButton_->setEnabled(true);
    statusLabel_->setText(I18n::tr("Disconnected"));
}

void SessionTab::setControlsEnabled(bool enabled)
{
    portCombo_->setEnabled(enabled);
    baudCombo_->setEnabled(enabled);
    dataBitsCombo_->setEnabled(enabled);
    parityCombo_->setEnabled(enabled);
    stopBitsCombo_->setEnabled(enabled);
    flowCombo_->setEnabled(enabled);
    refreshButton_->setEnabled(enabled);
}

void SessionTab::setBusy(bool busy)
{
    busy_ = busy;
    terminal_->setInputBlocked(busy);
    transferBar_->setVisible(busy);
    cancelButton_->setEnabled(busy);
    openButton_->setEnabled(!busy);
    if (!busy) {
        progressBar_->setRange(0, 1000);
        progressBar_->setValue(0);
        transferLabel_->clear();
    }
}

void SessionTab::onSendRequested()
{
    if (worker_ == nullptr) {
        return;
    }
    const QString start = zmodemLastDir_.isEmpty() ? lastDir_ : zmodemLastDir_;
    const QStringList files =
        QFileDialog::getOpenFileNames(this, I18n::tr("Select files to send"), start);
    if (files.isEmpty()) {
        worker_->provideSendFiles({});
        return;
    }
    zmodemLastDir_ = QFileInfo(files.first()).absolutePath();
    lastDir_ = zmodemLastDir_;
    statusLabel_->setText(I18n::tr("Sending..."));
    worker_->provideSendFiles(files);
}

void SessionTab::onReceiveComplete(const QStringList &paths)
{
    statusLabel_->setText(I18n::tr("Received %1 file(s)").arg(paths.size()));
    saveReceivedFiles(paths);
}

void SessionTab::onFileStart(const QString &name, qint64 size, int index, int count)
{
    if (name.isEmpty()) {
        transferLabel_->setText(I18n::tr("Receiving..."));
        progressBar_->setRange(0, 0);
        return;
    }
    QString label = name;
    if (count > 0) {
        label += QStringLiteral(" (%1/%2)").arg(index + 1).arg(count);
    }
    transferLabel_->setText(label);
    if (size > 0) {
        progressBar_->setRange(0, 1000);
        progressBar_->setValue(0);
    } else {
        progressBar_->setRange(0, 0);
    }
}

void SessionTab::onProgress(const QString &name, qint64 sent, qint64 total)
{
    if (total > 0) {
        const int value = static_cast<int>(std::clamp<qint64>(sent * 1000 / total, 0, 1000));
        progressBar_->setRange(0, 1000);
        progressBar_->setValue(value);
        transferLabel_->setText(I18n::tr("%1 — %2%").arg(name).arg(value / 10));
    } else {
        transferLabel_->setText(I18n::tr("%1 — %2 bytes").arg(name).arg(sent));
    }
}

void SessionTab::onError(const QString &message)
{
    statusLabel_->setText(QStringLiteral("ZMODEM: %1").arg(message));
    QMessageBox::warning(this, I18n::tr("ZMODEM error"), message);
}

void SessionTab::saveReceivedFiles(const QStringList &paths)
{
    if (paths.isEmpty()) {
        return;
    }
    const QString tempDir = QFileInfo(paths.first()).absolutePath();
    int saved = 0;
    bool keepTemp = false;
    for (const QString &path : paths) {
        const QFileInfo info(path);
        const QString base = zmodemLastDir_.isEmpty() ? lastDir_ : zmodemLastDir_;
        const QString suggested = QDir(base).filePath(info.fileName());
        const QString destination = QFileDialog::getSaveFileName(
            this, I18n::tr("Save received file"), suggested);
        if (destination.isEmpty()) {
            QFile::remove(path);
            continue;
        }
        if (QFileInfo(destination).absoluteFilePath() == info.absoluteFilePath()) {
            ++saved;
            keepTemp = true;
            continue;
        }
        if (QFile::exists(destination)) {
            QFile::remove(destination);
        }
        bool ok = QFile::rename(path, destination);
        if (!ok) {
            ok = QFile::copy(path, destination);
            if (ok) {
                QFile::remove(path);
            }
        }
        if (!ok) {
            keepTemp = true;
            QMessageBox::warning(this, I18n::tr("ZMODEM error"),
                                 I18n::tr("Cannot save %1").arg(destination));
            continue;
        }
        ++saved;
        zmodemLastDir_ = QFileInfo(destination).absolutePath();
        lastDir_ = zmodemLastDir_;
    }
    if (!keepTemp && tempDir.startsWith(QDir::tempPath()) &&
        tempDir.contains(QStringLiteral("comtool-native-zmodem-"))) {
        QDir(tempDir).removeRecursively();
    }
    statusLabel_->setText(I18n::tr("Received %1 file(s)").arg(saved));
}

void SessionTab::setDarkTheme(bool dark)
{
    dark_ = dark;
    if (terminal_ != nullptr) {
        terminal_->setDarkTheme(dark);
    }
}

void SessionTab::setFontPointSize(int pointSize)
{
    fontPointSize_ = std::clamp(pointSize, 6, 40);
    if (terminal_ != nullptr) {
        terminal_->setFontPointSize(fontPointSize_);
    }
}

void SessionTab::setPanelVisible(bool visible)
{
    panelVisible_ = visible;
    if (panel_ != nullptr) {
        panel_->setVisible(visible);
    }
    if (collapseButton_ != nullptr) {
        collapseButton_->setArrowType(visible ? Qt::LeftArrow : Qt::RightArrow);
        collapseButton_->setToolTip(visible ? I18n::tr("Hide panel") : I18n::tr("Show panel"));
    }
}

void SessionTab::focusTerminal()
{
    if (terminal_ != nullptr) {
        terminal_->setFocus(Qt::OtherFocusReason);
    }
}
