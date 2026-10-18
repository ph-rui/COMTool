#include "zmodem/receiver.h"
#include "zmodem/errors.h"
#include "zmodem/filename.h"

#include <QDir>
#include <QFile>

#include <algorithm>

namespace zmodem {

ZmodemReceiver::ZmodemReceiver(ZmodemIO &io, const QString &outputDir,
                               FileStartCallback onFileStart, ProgressCallback onProgress,
                               FileCompleteCallback onFileComplete, double headerTimeout,
                               double dataTimeout, int maxRetries)
    : io_(io),
      outputDir_(outputDir),
      onFileStart_(std::move(onFileStart)),
      onProgress_(std::move(onProgress)),
      onFileComplete_(std::move(onFileComplete)),
      headerTimeout_(headerTimeout),
      dataTimeout_(dataTimeout),
      maxRetries_(std::max(1, maxRetries)),
      reader_(io, dataTimeout)
{
}

void ZmodemReceiver::sendHeader(uint8_t frameType, const std::array<uint8_t, 4> &data)
{
    io_.write(encodeHexHeader(frameType, data));
}

void ZmodemReceiver::sendZrinit()
{
    const uint8_t flags = ZRINIT_CANFDX | ZRINIT_CANOVIO | ZRINIT_CANFC32;
    sendHeader(FRAME_ZRINIT, {0, 0, 0, flags});
}

void ZmodemReceiver::sendZrpos(qint64 offset)
{
    if (offset < 0 || static_cast<quint64>(offset) > 0xFFFFFFFFull) {
        throw ZmodemError("receive offset is too large");
    }
    sendHeader(FRAME_ZRPOS, packOffset(static_cast<uint32_t>(offset)));
}

void ZmodemReceiver::sendZack(qint64 offset)
{
    if (offset < 0 || static_cast<quint64>(offset) > 0xFFFFFFFFull) {
        throw ZmodemError("receive offset is too large");
    }
    sendHeader(FRAME_ZACK, packOffset(static_cast<uint32_t>(offset)));
}

ReceiveResult ZmodemReceiver::run()
{
    sendZrinit();
    while (true) {
        Header header;
        try {
            header = reader_.readHeader();
        } catch (const ZmodemTimeout &) {
            throw;
        } catch (const ZmodemAbort &) {
            throw;
        } catch (const ZmodemError &) {
            sendZrinit();
            continue;
        }

        if (header.frameType == FRAME_ZRQINIT) {
            sendZrinit();
            continue;
        }
        if (header.frameType == FRAME_ZSINIT) {
            try {
                reader_.readSubpacket(header.crcLen);
            } catch (const ZmodemError &) {
                // Fall through to the ACK; a malformed ZSINIT is rare and the
                // sender can still proceed with ZFILE.
            }
            sendZack(1);
            continue;
        }
        if (header.frameType == FRAME_ZFILE) {
            const FileResult result = receiveFile(header);
            if (result == FileResult::Finish) {
                sendHeader(FRAME_ZFIN);
                readOo();
                return {files_, reader_.drain()};
            }
            continue;
        }
        if (header.frameType == FRAME_ZFIN) {
            sendHeader(FRAME_ZFIN);
            readOo();
            return {files_, reader_.drain()};
        }
        if (header.frameType == FRAME_ZSKIP) {
            continue;
        }
        if (header.frameType == FRAME_ZABORT || header.frameType == FRAME_ZCAN) {
            throw ZmodemAbort("sender aborted the transfer");
        }
        if (header.frameType == FRAME_ZNAK) {
            sendZrinit();
            continue;
        }
    }
}

void ZmodemReceiver::readOo()
{
    try {
        const Bytes data = reader_.readRaw(2);
        if (data.size() == 2 && data[0] == 'O' && data[1] == 'O') {
            return;
        }
        reader_.prepend(data);
    } catch (const ZmodemError &) {
    }
}

ZmodemReceiver::FileResult ZmodemReceiver::receiveFile(const Header &header)
{
    Bytes metadataPacket;
    try {
        metadataPacket = reader_.readSubpacket(header.crcLen).payload;
    } catch (const ZmodemTimeout &) {
        throw;
    } catch (const ZmodemError &) {
        sendHeader(FRAME_ZNAK);
        reader_.clearBuffer();
        return FileResult::Continue;
    }

    const FileMeta meta = parseZfileMetadata(metadataPacket);
    const QString path = uniqueTempPath(outputDir_, meta.name);
    QFile file(path);
    if (!file.open(QIODevice::WriteOnly | QIODevice::Truncate)) {
        throw ZmodemError(QStringLiteral("cannot create %1: %2")
                              .arg(path, file.errorString())
                              .toStdString());
    }

    if (onFileStart_) {
        onFileStart_(FileInfo{meta.name, path, 0, meta.size});
    }

    qint64 offset = 0;
    int retries = 0;
    bool finishAfterFile = false;
    sendZrpos(0);
    try {
        while (true) {
            Header next;
            try {
                next = reader_.readHeader();
            } catch (const ZmodemTimeout &) {
                throw;
            } catch (const ZmodemAbort &) {
                throw;
            } catch (const ZmodemError &) {
                ++retries;
                if (retries > maxRetries_) {
                    throw;
                }
                reader_.clearBuffer();
                sendZrpos(offset);
                continue;
            }

            if (next.frameType == FRAME_ZDATA) {
                const qint64 dataOffset = static_cast<qint64>(unpackOffset(next.data));
                if (dataOffset != offset) {
                    ++retries;
                    if (retries > maxRetries_) {
                        throw ZmodemAbort("sender keeps sending a bad offset");
                    }
                    sendZrpos(offset);
                    continue;
                }
                const std::optional<qint64> finished =
                    receiveDataFrames(file, header.crcLen, offset, meta.name, meta.size);
                if (!finished) {
                    ++retries;
                    if (retries > maxRetries_) {
                        throw ZmodemAbort("too many data checksum failures");
                    }
                    continue;
                }
                offset = *finished;
                retries = 0;
                continue;
            }
            if (next.frameType == FRAME_ZEOF) {
                const qint64 eofOffset = static_cast<qint64>(unpackOffset(next.data));
                if (eofOffset != offset) {
                    ++retries;
                    if (retries > maxRetries_) {
                        throw ZmodemAbort("ZEOF offset does not match");
                    }
                    sendZrpos(offset);
                    continue;
                }
                break;
            }
            if (next.frameType == FRAME_ZFIN) {
                finishAfterFile = true;
                break;
            }
            if (next.frameType == FRAME_ZSKIP) {
                file.close();
                QFile::remove(path);
                return FileResult::Skip;
            }
            if (next.frameType == FRAME_ZABORT || next.frameType == FRAME_ZCAN) {
                throw ZmodemAbort("sender aborted the transfer");
            }
            if (next.frameType == FRAME_ZNAK) {
                ++retries;
                if (retries > maxRetries_) {
                    throw ZmodemAbort("too many ZNAK frames");
                }
                sendZrpos(offset);
                continue;
            }
        }
    } catch (...) {
        file.close();
        QFile::remove(path);
        throw;
    }
    file.close();

    const FileInfo info{meta.name, path, offset, meta.size};
    files_.push_back(info);
    if (onFileComplete_) {
        onFileComplete_(info);
    }
    if (finishAfterFile) {
        return FileResult::Finish;
    }
    sendZrinit();
    return FileResult::Continue;
}

std::optional<qint64> ZmodemReceiver::receiveDataFrames(QFile &file, int crcLen, qint64 offset,
                                                        const QString &name, qint64 expectedSize)
{
    while (true) {
        Subpacket packet;
        try {
            packet = reader_.readSubpacket(crcLen);
        } catch (const ZmodemTimeout &) {
            throw;
        } catch (const ZmodemAbort &) {
            throw;
        } catch (const ZmodemError &) {
            reader_.clearBuffer();
            sendZrpos(offset);
            return std::nullopt;
        }

        if (!packet.payload.empty()) {
            const QByteArray chunk(reinterpret_cast<const char *>(packet.payload.data()),
                                   static_cast<int>(packet.payload.size()));
            const qint64 written = file.write(chunk);
            if (written != chunk.size()) {
                throw ZmodemError(QStringLiteral("write failed: %1").arg(file.errorString()).toStdString());
            }
        }
        offset += static_cast<qint64>(packet.payload.size());
        if (onProgress_) {
            onProgress_(name, offset, expectedSize);
        }

        if (packet.ackExpected() && !packet.frameEndFrame()) {
            sendZack(offset);
        }
        if (packet.frameEndFrame()) {
            if (packet.frameEnd == SUBPACKET_ZCRCW) {
                sendZack(offset);
            }
            return offset;
        }
    }
}

} // namespace zmodem
