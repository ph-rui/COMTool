#include "serialworker.h"

#include "i18n.h"
#include "zmodem/detector.h"
#include "zmodem/errors.h"
#include "zmodem/io.h"
#include "zmodem/receiver.h"
#include "zmodem/sender.h"

#include <QDir>
#include <QElapsedTimer>
#include <QMutexLocker>
#include <QSerialPort>
#include <QUuid>

#include <algorithm>

namespace {

class SerialIO : public zmodem::ZmodemIO
{
public:
    SerialIO(QSerialPort *port, std::atomic<bool> *cancel, zmodem::Bytes initial)
        : port_(port), cancel_(cancel), initial_(std::move(initial))
    {
    }

    void write(const zmodem::Bytes &data) override
    {
        if (cancelled()) {
            throw zmodem::ZmodemAbort("ZMODEM transfer cancelled");
        }
        qint64 offset = 0;
        while (offset < static_cast<qint64>(data.size())) {
            if (cancelled()) {
                throw zmodem::ZmodemAbort("ZMODEM transfer cancelled");
            }
            const qint64 accepted = port_->write(
                reinterpret_cast<const char *>(data.data()) + offset,
                static_cast<qint64>(data.size()) - offset);
            if (accepted < 0) {
                throw zmodem::ZmodemError(port_->errorString().toStdString());
            }
            offset += accepted;
            while (port_->bytesToWrite() > 0) {
                if (!port_->waitForBytesWritten(1000)) {
                    if (cancelled()) {
                        throw zmodem::ZmodemAbort("ZMODEM transfer cancelled");
                    }
                    throw zmodem::ZmodemTimeout("serial write timed out");
                }
            }
        }
    }

    zmodem::Bytes readSome(size_t maxBytes, double timeoutSeconds) override
    {
        if (!initial_.empty()) {
            const size_t count = std::min(maxBytes, initial_.size());
            zmodem::Bytes result(initial_.begin(),
                                 initial_.begin() + static_cast<std::ptrdiff_t>(count));
            initial_.erase(initial_.begin(),
                           initial_.begin() + static_cast<std::ptrdiff_t>(count));
            return result;
        }

        QElapsedTimer timer;
        timer.start();
        const qint64 timeoutMs =
            timeoutSeconds < 0 ? -1 : std::max<qint64>(1, static_cast<qint64>(timeoutSeconds * 1000));
        while (true) {
            if (cancelled()) {
                throw zmodem::ZmodemAbort("ZMODEM transfer cancelled");
            }
            if (port_->bytesAvailable() > 0) {
                const qint64 wanted =
                    std::min<qint64>(static_cast<qint64>(maxBytes), port_->bytesAvailable());
                const QByteArray data = port_->read(wanted);
                if (!data.isEmpty()) {
                    return zmodem::Bytes(data.begin(), data.end());
                }
            }

            if (port_->error() != QSerialPort::NoError &&
                port_->error() != QSerialPort::TimeoutError) {
                throw zmodem::ZmodemError(port_->errorString().toStdString());
            }

            qint64 slice = 50;
            if (timeoutMs >= 0) {
                const qint64 remaining = timeoutMs - timer.elapsed();
                if (remaining <= 0) {
                    throw zmodem::ZmodemTimeout("timed out waiting for ZMODEM data");
                }
                slice = std::min(slice, remaining);
            }
            port_->waitForReadyRead(static_cast<int>(slice));
        }
    }

    bool cancelled() const override { return cancel_->load(); }

    zmodem::Bytes drain() override
    {
        zmodem::Bytes result = std::move(initial_);
        initial_.clear();
        const QByteArray data = port_->readAll();
        result.insert(result.end(), data.begin(), data.end());
        return result;
    }

private:
    QSerialPort *port_;
    std::atomic<bool> *cancel_;
    zmodem::Bytes initial_;
};

} // namespace

SerialWorker::SerialWorker(const SerialConfig &config, QObject *parent)
    : QThread(parent), config_(config), detector_(new zmodem::ZmodemDetector)
{
}

SerialWorker::~SerialWorker()
{
    requestStop();
    wait();
    delete detector_;
    detector_ = nullptr;
    delete port_;
    port_ = nullptr;
}

void SerialWorker::sendData(const QByteArray &data)
{
    if (data.isEmpty() || transferActive_.load() || !isRunning()) {
        return;
    }
    QMutexLocker locker(&txMutex_);
    if (tx_.size() > 1024 * 1024) {
        return;
    }
    tx_ += data;
}

void SerialWorker::cancelTransfer()
{
    cancel_.store(true);
    QMutexLocker locker(&pendingMutex_);
    pendingCancelled_ = true;
    pending_ = false;
    pendingCond_.wakeAll();
}

void SerialWorker::provideSendFiles(const QStringList &files)
{
    QMutexLocker locker(&pendingMutex_);
    pendingFiles_ = files;
    pending_ = false;
    pendingCond_.wakeAll();
}

void SerialWorker::requestStop()
{
    stop_.store(true);
    cancelTransfer();
}

void SerialWorker::run()
{
    port_ = new QSerialPort;
    port_->setPortName(config_.portName);
    port_->setBaudRate(config_.baudRate);
    port_->setDataBits(config_.dataBits);
    port_->setParity(config_.parity);
    port_->setStopBits(config_.stopBits);
    port_->setFlowControl(config_.flowControl);

    if (!port_->open(QIODevice::ReadWrite)) {
        const QString message = port_->errorString();
        delete port_;
        port_ = nullptr;
        emit openFailed(message);
        return;
    }
    port_->clear(QSerialPort::AllDirections);
    detector_->reset();
    emit opened();

    bool disconnected = false;
    while (!stop_.load()) {
        if (port_->waitForReadyRead(10)) {
            const QByteArray data = port_->readAll();
            if (!data.isEmpty()) {
                processInput(data);
            }
        }
        flushTerminalInput();

        if (!port_->isOpen() || port_->error() == QSerialPort::ResourceError ||
            port_->error() == QSerialPort::DeviceNotFoundError ||
            port_->error() == QSerialPort::PermissionError) {
            disconnected = true;
            break;
        }
    }

    if (port_ != nullptr) {
        port_->close();
        delete port_;
        port_ = nullptr;
    }
    emit closed(disconnected ? I18n::tr("Serial device disconnected")
                             : I18n::tr("Port closed"));
}

void SerialWorker::processInput(const QByteArray &data)
{
    const zmodem::Bytes bytes(data.begin(), data.end());
    const zmodem::DetectionResult result = detector_->feed(bytes);
    if (!result.terminalData.empty()) {
        emit terminalData(QByteArray(reinterpret_cast<const char *>(result.terminalData.data()),
                                     static_cast<int>(result.terminalData.size())));
    }
    if (!result.hasDetection) {
        return;
    }

    const zmodem::Detection &detection = result.detection;
    if (detection.direction == zmodem::Detection::Direction::Receive) {
        runReceive(detection.header, detection.remainder);
    } else {
        runSend(detection.header, detection.remainder);
    }
    detector_->reset();
}

void SerialWorker::flushTerminalInput()
{
    if (port_ == nullptr || !port_->isOpen() || transferActive_.load()) {
        return;
    }
    QByteArray data;
    {
        QMutexLocker locker(&txMutex_);
        data.swap(tx_);
    }
    if (!data.isEmpty()) {
        port_->write(data);
    }
}

void SerialWorker::emitTrailing(const zmodem::Bytes &trailing)
{
    if (trailing.empty()) {
        return;
    }
    const size_t count = std::min<size_t>(trailing.size(), 4096);
    emit terminalData(QByteArray(
        reinterpret_cast<const char *>(trailing.data() + trailing.size() - count),
        static_cast<int>(count)));
}

QString SerialWorker::createTempDir() const
{
    const QString path = QDir::temp().filePath(
        QStringLiteral("comtool-native-zmodem-%1")
            .arg(QUuid::createUuid().toString(QUuid::WithoutBraces)));
    QDir().mkpath(path);
    return path;
}

void SerialWorker::sendAbortCan()
{
    if (port_ == nullptr || !port_->isOpen()) {
        return;
    }
    port_->clear(QSerialPort::Output);
    QByteArray can(8, static_cast<char>(zmodem::ZDLE));
    can += QByteArray(8, '\x08');
    port_->write(can);
    port_->waitForBytesWritten(500);
    port_->clear(QSerialPort::Input);
}

void SerialWorker::runReceive(const zmodem::Header &header, const zmodem::Bytes &remainder)
{
    Q_UNUSED(header)
    transferActive_.store(true);
    cancel_.store(false);
    emit zmodemBusyChanged(true);
    emit zmodemFileStart(QString(), -1, 0, 0);

    const QString tempDir = createTempDir();
    SerialIO io(port_, &cancel_, remainder);
    bool success = false;
    try {
        zmodem::ZmodemReceiver receiver(
            io, tempDir,
            [this](const zmodem::FileInfo &file) {
                emit zmodemFileStart(file.name, file.expectedSize, 0, 0);
            },
            [this](const QString &name, qint64 sent, qint64 total) {
                emit zmodemProgress(name, sent, total);
            },
            [this](const zmodem::FileInfo &file) { emit zmodemFileComplete(file.name); });
        const zmodem::ReceiveResult result = receiver.run();
        QStringList paths;
        for (const zmodem::FileInfo &file : result.files) {
            paths << file.path;
        }
        emitTrailing(result.trailing);
        success = true;
        emit zmodemReceiveComplete(paths);
    } catch (const zmodem::ZmodemAbort &) {
        if (cancel_.load()) {
            emit zmodemCancelled();
        } else {
            emit zmodemError(I18n::tr("ZMODEM transfer aborted"));
        }
    } catch (const zmodem::ZmodemTimeout &error) {
        emit zmodemError(QString::fromUtf8(error.what()));
    } catch (const std::exception &error) {
        emit zmodemError(QString::fromUtf8(error.what()));
    }

    if (!success) {
        sendAbortCan();
        QDir(tempDir).removeRecursively();
    }
    transferActive_.store(false);
    cancel_.store(false);
    emit zmodemBusyChanged(false);
}

void SerialWorker::runSend(const zmodem::Header &header, const zmodem::Bytes &remainder)
{
    transferActive_.store(true);
    cancel_.store(false);

    {
        QMutexLocker locker(&pendingMutex_);
        pending_ = true;
        pendingCancelled_ = false;
        pendingFiles_.clear();
    }
    emit zmodemBusyChanged(true);
    emit zmodemSendRequested();

    QStringList files;
    bool cancelled = false;
    {
        QMutexLocker locker(&pendingMutex_);
        while (pending_ && !stop_.load() && !cancel_.load()) {
            pendingCond_.wait(&pendingMutex_, 200);
        }
        files = pendingFiles_;
        pendingFiles_.clear();
        cancelled = pendingCancelled_ || stop_.load() || cancel_.load();
        pending_ = false;
    }

    if (cancelled || files.isEmpty()) {
        sendAbortCan();
        emit zmodemCancelled();
        transferActive_.store(false);
        cancel_.store(false);
        emit zmodemBusyChanged(false);
        return;
    }

    SerialIO io(port_, &cancel_, remainder);
    bool success = false;
    try {
        int index = 0;
        const int count = files.size();
        zmodem::ZmodemSender sender(
            io, files, header,
            [this, &index, count](const zmodem::FileInfo &file) {
                emit zmodemFileStart(file.name, file.size, index, count);
                ++index;
            },
            [this](const QString &name, qint64 sent, qint64 total) {
                emit zmodemProgress(name, sent, total);
            },
            [this](const zmodem::FileInfo &file) { emit zmodemFileComplete(file.name); });
        const zmodem::Bytes trailing = sender.run();
        emitTrailing(trailing);
        success = true;
        emit zmodemSendComplete();
    } catch (const zmodem::ZmodemAbort &) {
        if (cancel_.load()) {
            emit zmodemCancelled();
        } else {
            emit zmodemError(I18n::tr("ZMODEM transfer aborted"));
        }
    } catch (const zmodem::ZmodemTimeout &error) {
        emit zmodemError(QString::fromUtf8(error.what()));
    } catch (const std::exception &error) {
        emit zmodemError(QString::fromUtf8(error.what()));
    }

    if (!success) {
        sendAbortCan();
    }
    transferActive_.store(false);
    cancel_.store(false);
    emit zmodemBusyChanged(false);
}
