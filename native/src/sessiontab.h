#pragma once

#include "serialconfig.h"

#include <QStringList>
#include <QWidget>

class QComboBox;
class QLabel;
class QProgressBar;
class QPushButton;
class QTimer;
class QToolButton;
class SerialWorker;
class TerminalView;

class SessionTab : public QWidget
{
    Q_OBJECT
public:
    SessionTab(const SerialConfig &config, bool darkTheme, int fontPointSize,
               int scrollbackLines, bool panelVisible, const QString &lastDir,
               const QString &zmodemLastDir, QWidget *parent = nullptr);
    ~SessionTab() override;

    SerialConfig config() const;
    QString lastDir() const { return lastDir_; }
    QString zmodemLastDir() const { return zmodemLastDir_; }
    bool isOpen() const { return open_; }
    bool isBusy() const { return busy_; }
    bool panelVisible() const { return panelVisible_; }
    int fontPointSize() const { return fontPointSize_; }
    int scrollbackLinesForConfig() const { return scrollbackLines_; }

    void setDarkTheme(bool dark);
    void setFontPointSize(int pointSize);
    void setPanelVisible(bool visible);
    void focusTerminal();
    void closePort();

private:
    void buildUi();
    void applyConfigToUi();
    void refreshPorts();
    void toggleOpen();
    void openPort();
    void setControlsEnabled(bool enabled);
    void setBusy(bool busy);
    void onSendRequested();
    void onReceiveComplete(const QStringList &paths);
    void onFileStart(const QString &name, qint64 size, int index, int count);
    void onProgress(const QString &name, qint64 sent, qint64 total);
    void onError(const QString &message);
    void saveReceivedFiles(const QStringList &paths);
    SerialConfig configFromUi() const;

    SerialConfig config_;
    bool dark_ = false;
    int fontPointSize_ = 11;
    int scrollbackLines_ = 5000;
    bool panelVisible_ = true;
    QString lastDir_;
    QString zmodemLastDir_;
    bool open_ = false;
    bool busy_ = false;

    TerminalView *terminal_ = nullptr;
    QWidget *panel_ = nullptr;
    QToolButton *collapseButton_ = nullptr;
    QComboBox *portCombo_ = nullptr;
    QComboBox *baudCombo_ = nullptr;
    QComboBox *dataBitsCombo_ = nullptr;
    QComboBox *parityCombo_ = nullptr;
    QComboBox *stopBitsCombo_ = nullptr;
    QComboBox *flowCombo_ = nullptr;
    QPushButton *refreshButton_ = nullptr;
    QPushButton *openButton_ = nullptr;
    QLabel *statusLabel_ = nullptr;
    QWidget *transferBar_ = nullptr;
    QLabel *transferLabel_ = nullptr;
    QProgressBar *progressBar_ = nullptr;
    QPushButton *cancelButton_ = nullptr;
    QTimer *portTimer_ = nullptr;
    SerialWorker *worker_ = nullptr;
};
