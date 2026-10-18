#pragma once

#include "zmodem/codec.h"
#include "zmodem/io.h"
#include "zmodem/reader.h"
#include "zmodem/types.h"

#include <QStringList>

#include <functional>
#include <optional>

namespace zmodem {

class ZmodemSender
{
public:
    using FileStartCallback = std::function<void(const FileInfo &)>;
    using ProgressCallback = std::function<void(const QString &, qint64, qint64)>;
    using FileCompleteCallback = std::function<void(const FileInfo &)>;

    ZmodemSender(ZmodemIO &io, const QStringList &files,
                 std::optional<Header> initialZrinit = std::nullopt,
                 FileStartCallback onFileStart = {}, ProgressCallback onProgress = {},
                 FileCompleteCallback onFileComplete = {}, double headerTimeout = 30.0,
                 double dataTimeout = 30.0, int chunkSize = 1024, int maxRetries = 5,
                 int windowSize = 1024, double ackTimeout = 10.0);

    Bytes run();
    const Bytes &trailing() const { return trailing_; }

private:
    void sendHeader(uint8_t frameType, const std::array<uint8_t, 4> &data = {});
    void sendBinaryHeader(uint8_t frameType, const std::array<uint8_t, 4> &data, int crcLen);
    void sendSubpacket(const Bytes &payload, uint8_t frameEnd, int crcLen);
    Header waitForZrinit();
    void sendFile(const QString &path, int crcLen);
    void sendZfile(const Bytes &metadata, int crcLen);
    std::optional<qint64> waitForZrpos(const Bytes &metadata, int crcLen);
    std::optional<qint64> waitForAck(qint64 sent);
    std::optional<qint64> sendData(const QString &path, qint64 offset, qint64 expectedSize,
                                   int crcLen);
    struct AfterEof
    {
        enum class Kind
        {
            Ok,
            Skip,
            Offset,
        } kind = Kind::Ok;
        qint64 offset = 0;
    };
    AfterEof waitAfterEof(qint64 size);
    void finish();
    void reportProgress(const QString &path, qint64 sent, qint64 size);

    ZmodemIO &io_;
    QStringList files_;
    std::optional<Header> initialZrinit_;
    FileStartCallback onFileStart_;
    ProgressCallback onProgress_;
    FileCompleteCallback onFileComplete_;
    double headerTimeout_;
    double dataTimeout_;
    int chunkSize_;
    int maxRetries_;
    int windowSize_;
    double ackTimeout_;
    ZmodemReader reader_;
    Bytes trailing_;
};

} // namespace zmodem
