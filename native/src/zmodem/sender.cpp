#include "zmodem/sender.h"
#include "zmodem/errors.h"

#include <QDateTime>
#include <QFile>
#include <QFileInfo>

#include <algorithm>

#ifdef Q_OS_UNIX
#include <sys/stat.h>
#endif

namespace zmodem {
namespace {

int unixFileMode(const QString &path)
{
#ifdef Q_OS_UNIX
    struct stat status {};
    const QByteArray encoded = QFile::encodeName(path);
    if (::stat(encoded.constData(), &status) == 0) {
        return static_cast<int>(status.st_mode & 07777);
    }
#else
    Q_UNUSED(path)
#endif
    return 0644;
}

} // namespace

ZmodemSender::ZmodemSender(ZmodemIO &io, const QStringList &files,
                           std::optional<Header> initialZrinit, FileStartCallback onFileStart,
                           ProgressCallback onProgress, FileCompleteCallback onFileComplete,
                           double headerTimeout, double dataTimeout, int chunkSize, int maxRetries,
                           int windowSize, double ackTimeout)
    : io_(io),
      files_(files),
      initialZrinit_(std::move(initialZrinit)),
      onFileStart_(std::move(onFileStart)),
      onProgress_(std::move(onProgress)),
      onFileComplete_(std::move(onFileComplete)),
      headerTimeout_(headerTimeout),
      dataTimeout_(dataTimeout),
      chunkSize_(std::max(1, chunkSize)),
      maxRetries_(std::max(1, maxRetries)),
      windowSize_(std::max(chunkSize_, windowSize)),
      ackTimeout_(ackTimeout),
      reader_(io, headerTimeout)
{
}

void ZmodemSender::sendHeader(uint8_t frameType, const std::array<uint8_t, 4> &data)
{
    io_.write(encodeHexHeader(frameType, data));
}

void ZmodemSender::sendBinaryHeader(uint8_t frameType, const std::array<uint8_t, 4> &data,
                                    int crcLen)
{
    io_.write(encodeBinaryHeader(frameType, data, crcLen));
}

void ZmodemSender::sendSubpacket(const Bytes &payload, uint8_t frameEnd, int crcLen)
{
    io_.write(encodeSubpacket(payload, frameEnd, crcLen));
    if (frameEnd == SUBPACKET_ZCRCW) {
        io_.write(Bytes{XON});
    }
}

Header ZmodemSender::waitForZrinit()
{
    if (initialZrinit_) {
        return *initialZrinit_;
    }
    sendHeader(FRAME_ZRQINIT);
    for (int attempt = 0; attempt <= maxRetries_; ++attempt) {
        try {
            Header header = reader_.readHeader();
            if (header.frameType == FRAME_ZRINIT) {
                return header;
            }
            if (header.frameType == FRAME_ZABORT || header.frameType == FRAME_ZCAN) {
                throw ZmodemAbort("receiver aborted the transfer");
            }
        } catch (const ZmodemTimeout &) {
            sendHeader(FRAME_ZRQINIT);
        }
    }
    throw ZmodemTimeout("receiver did not send ZRINIT");
}

Bytes ZmodemSender::run()
{
    const Header zrinit = waitForZrinit();
    const int crcLen = (zrinit.data[3] & ZRINIT_CANFC32) ? 32 : 16;
    for (const QString &path : files_) {
        sendFile(path, crcLen);
    }
    finish();
    trailing_ = reader_.drain();
    return trailing_;
}

void ZmodemSender::sendFile(const QString &path, int crcLen)
{
    const QFileInfo info(path);
    if (!info.isFile()) {
        throw ZmodemError(QStringLiteral("file not found: %1").arg(path).toStdString());
    }
    const qint64 size = info.size();
    if (size < 0 || static_cast<quint64>(size) > 0xFFFFFFFFull) {
        throw ZmodemError("file is too large for ZMODEM");
    }

    const QByteArray nameBytes = info.fileName().toUtf8();
    Bytes metadata(nameBytes.begin(), nameBytes.end());
    metadata.push_back(0);
    const QDateTime modified = info.lastModified();
    const qint64 mtime = modified.isValid() ? modified.toSecsSinceEpoch() : 0;
    const QString attributes = QStringLiteral("%1 %2 %3 0 0 0")
                                   .arg(size)
                                   .arg(mtime, 0, 8)
                                   .arg(unixFileMode(path), 0, 8);
    const QByteArray attrs = attributes.toLatin1();
    metadata.insert(metadata.end(), attrs.begin(), attrs.end());

    sendZfile(metadata, crcLen);
    const std::optional<qint64> requested = waitForZrpos(metadata, crcLen);
    if (!requested) {
        return;
    }
    qint64 offset = *requested;
    if (offset < 0 || offset > size) {
        throw ZmodemAbort("receiver requested an invalid offset");
    }

    if (onFileStart_) {
        onFileStart_(FileInfo{info.fileName(), path, size, size});
    }

    int retries = 0;
    qint64 lastOffset = offset;
    while (true) {
        const std::optional<qint64> resume = sendData(path, offset, size, crcLen);
        if (resume && *resume >= 0 && *resume < size) {
            if (*resume > lastOffset) {
                retries = 0;
                lastOffset = *resume;
            } else {
                ++retries;
                if (retries > maxRetries_) {
                    throw ZmodemAbort("too many receiver retransmit requests");
                }
            }
            offset = *resume;
            reader_.clearBuffer();
            io_.drain();
            continue;
        }

        AfterEof result;
        try {
            result = waitAfterEof(size);
        } catch (const ZmodemTimeout &) {
            ++retries;
            if (retries > maxRetries_) {
                throw;
            }
            continue;
        }

        if (result.kind == AfterEof::Kind::Ok) {
            break;
        }
        if (result.kind == AfterEof::Kind::Skip) {
            return;
        }
        if (result.offset >= size) {
            break;
        }
        offset = result.offset;
        ++retries;
        if (retries > maxRetries_) {
            throw ZmodemAbort("too many receiver retransmit requests");
        }
    }

    if (onFileComplete_) {
        onFileComplete_(FileInfo{info.fileName(), path, size, size});
    }
}

void ZmodemSender::sendZfile(const Bytes &metadata, int crcLen)
{
    sendBinaryHeader(FRAME_ZFILE, {}, crcLen);
    sendSubpacket(metadata, SUBPACKET_ZCRCW, crcLen);
}

std::optional<qint64> ZmodemSender::waitForZrpos(const Bytes &metadata, int crcLen)
{
    int retries = 0;
    while (true) {
        try {
            const Header header = reader_.readHeader();
            if (header.frameType == FRAME_ZRPOS) {
                return static_cast<qint64>(unpackOffset(header.data));
            }
            if (header.frameType == FRAME_ZSKIP) {
                return std::nullopt;
            }
            if (header.frameType == FRAME_ZABORT || header.frameType == FRAME_ZCAN) {
                throw ZmodemAbort("receiver aborted the transfer");
            }
            if (header.frameType == FRAME_ZNAK) {
                ++retries;
                if (retries > maxRetries_) {
                    throw ZmodemAbort("receiver rejected the file header");
                }
                sendZfile(metadata, crcLen);
                continue;
            }
        } catch (const ZmodemTimeout &) {
            ++retries;
            if (retries > maxRetries_) {
                throw;
            }
            sendZfile(metadata, crcLen);
        }
    }
}

std::optional<qint64> ZmodemSender::waitForAck(qint64 sent)
{
    while (true) {
        try {
            const Header header = reader_.readHeader(ackTimeout_);
            const uint8_t frameType = header.frameType & 0x7F;
            if (frameType == FRAME_ZACK) {
                const qint64 acked = static_cast<qint64>(unpackOffset(header.data));
                return acked < sent ? std::make_optional(acked) : std::nullopt;
            }
            if (frameType == FRAME_ZRPOS) {
                return static_cast<qint64>(unpackOffset(header.data));
            }
            if (frameType == FRAME_ZNAK) {
                return sent;
            }
            if (frameType == FRAME_ZABORT || frameType == FRAME_ZCAN) {
                throw ZmodemAbort("receiver aborted the transfer");
            }
        } catch (const ZmodemTimeout &) {
            return sent;
        }
    }
}

std::optional<qint64> ZmodemSender::sendData(const QString &path, qint64 offset,
                                             qint64 expectedSize, int crcLen)
{
    QFile file(path);
    if (!file.open(QIODevice::ReadOnly)) {
        throw ZmodemError(file.errorString().toStdString());
    }
    const qint64 size = file.size();
    if (size != expectedSize) {
        throw ZmodemError("source file changed during transfer");
    }
    if (!file.seek(offset)) {
        throw ZmodemError("cannot seek source file");
    }

    qint64 sent = offset;
    if (size == 0) {
        sendBinaryHeader(FRAME_ZDATA, packOffset(0), crcLen);
        sendSubpacket({}, SUBPACKET_ZCRCE, crcLen);
        return std::nullopt;
    }

    while (sent < size) {
        if (sent > 0xFFFFFFFFll) {
            throw ZmodemError("file offset is too large for ZMODEM");
        }
        sendBinaryHeader(FRAME_ZDATA, packOffset(static_cast<uint32_t>(sent)), crcLen);
        const qint64 windowEnd = std::min(sent + windowSize_, size);
        while (sent < windowEnd) {
            const qint64 remaining = windowEnd - sent;
            const QByteArray chunk = file.read(std::min<qint64>(chunkSize_, remaining));
            if (chunk.isEmpty()) {
                break;
            }
            const Bytes payload(chunk.begin(), chunk.end());
            sent += chunk.size();
            if (sent >= size) {
                sendSubpacket(payload, SUBPACKET_ZCRCE, crcLen);
                reportProgress(path, sent, size);
                return std::nullopt;
            }
            if (sent >= windowEnd) {
                sendSubpacket(payload, SUBPACKET_ZCRCW, crcLen);
            } else {
                sendSubpacket(payload, SUBPACKET_ZCRCG, crcLen);
            }
            reportProgress(path, sent, size);
        }
        const std::optional<qint64> resume = waitForAck(sent);
        if (resume) {
            return resume;
        }
    }
    return std::nullopt;
}

ZmodemSender::AfterEof ZmodemSender::waitAfterEof(qint64 size)
{
    if (size < 0 || static_cast<quint64>(size) > 0xFFFFFFFFull) {
        throw ZmodemError("file is too large for ZMODEM");
    }
    sendHeader(FRAME_ZEOF, packOffset(static_cast<uint32_t>(size)));
    while (true) {
        const Header header = reader_.readHeader();
        if (header.frameType == FRAME_ZRINIT) {
            return {AfterEof::Kind::Ok, 0};
        }
        if (header.frameType == FRAME_ZSKIP) {
            return {AfterEof::Kind::Skip, 0};
        }
        if (header.frameType == FRAME_ZRPOS) {
            return {AfterEof::Kind::Offset, static_cast<qint64>(unpackOffset(header.data))};
        }
        if (header.frameType == FRAME_ZABORT || header.frameType == FRAME_ZCAN) {
            throw ZmodemAbort("receiver aborted the transfer");
        }
        if (header.frameType == FRAME_ZNAK) {
            return {AfterEof::Kind::Offset, 0};
        }
    }
}

void ZmodemSender::finish()
{
    sendHeader(FRAME_ZFIN);
    for (int attempt = 0; attempt <= maxRetries_; ++attempt) {
        try {
            const Header header = reader_.readHeader();
            if (header.frameType == FRAME_ZFIN) {
                io_.write(Bytes{'O', 'O'});
                return;
            }
            if (header.frameType == FRAME_ZABORT || header.frameType == FRAME_ZCAN) {
                throw ZmodemAbort("receiver aborted the transfer");
            }
        } catch (const ZmodemTimeout &) {
            sendHeader(FRAME_ZFIN);
        }
    }
    throw ZmodemTimeout("receiver did not acknowledge ZFIN");
}

void ZmodemSender::reportProgress(const QString &path, qint64 sent, qint64 size)
{
    if (onProgress_) {
        onProgress_(QFileInfo(path).fileName(), sent, size);
    }
}

} // namespace zmodem
