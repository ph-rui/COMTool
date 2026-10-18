#pragma once

#include "serialconfig.h"
#include "zmodem/codec.h"

#include <QByteArray>
#include <QMutex>
#include <QStringList>
#include <QThread>
#include <QWaitCondition>

#include <atomic>

class QSerialPort;
namespace zmodem {
class ZmodemDetector;
}

class SerialWorker : public QThread
{
    Q_OBJECT
public:
    explicit SerialWorker(const SerialConfig &config, QObject *parent = nullptr);
    ~SerialWorker() override;

    void sendData(const QByteArray &data);
    void cancelTransfer();
    void provideSendFiles(const QStringList &files);
    void requestStop();

signals:
    void opened();
    void openFailed(const QString &message);
    void closed(const QString &reason);
    void terminalData(const QByteArray &data);
    void zmodemSendRequested();
    void zmodemBusyChanged(bool busy);
    void zmodemFileStart(const QString &name, qint64 size, int index, int count);
    void zmodemProgress(const QString &name, qint64 sent, qint64 total);
    void zmodemFileComplete(const QString &name);
    void zmodemReceiveComplete(const QStringList &paths);
    void zmodemSendComplete();
    void zmodemError(const QString &message);
    void zmodemCancelled();

protected:
    void run() override;

private:
    void processInput(const QByteArray &data);
    void runReceive(const zmodem::Header &header, const zmodem::Bytes &remainder);
    void runSend(const zmodem::Header &header, const zmodem::Bytes &remainder);
    void flushTerminalInput();
    void sendAbortCan();
    void emitTrailing(const zmodem::Bytes &trailing);
    QString createTempDir() const;

    SerialConfig config_;
    QSerialPort *port_ = nullptr;
    zmodem::ZmodemDetector *detector_ = nullptr;
    std::atomic<bool> stop_{false};
    std::atomic<bool> cancel_{false};
    std::atomic<bool> transferActive_{false};

    QMutex txMutex_;
    QByteArray tx_;

    QMutex pendingMutex_;
    QWaitCondition pendingCond_;
    bool pending_ = false;
    bool pendingCancelled_ = false;
    QStringList pendingFiles_;
};
