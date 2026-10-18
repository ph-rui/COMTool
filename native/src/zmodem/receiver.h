#pragma once

#include "zmodem/codec.h"
#include "zmodem/io.h"
#include "zmodem/reader.h"
#include "zmodem/types.h"

#include <QFile>

#include <functional>
#include <vector>

namespace zmodem {

struct ReceiveResult
{
    std::vector<FileInfo> files;
    Bytes trailing;
};

class ZmodemReceiver
{
public:
    using FileStartCallback = std::function<void(const FileInfo &)>;
    using ProgressCallback = std::function<void(const QString &, qint64, qint64)>;
    using FileCompleteCallback = std::function<void(const FileInfo &)>;

    ZmodemReceiver(ZmodemIO &io, const QString &outputDir, FileStartCallback onFileStart = {},
                   ProgressCallback onProgress = {}, FileCompleteCallback onFileComplete = {},
                   double headerTimeout = 30.0, double dataTimeout = 30.0, int maxRetries = 5);

    ReceiveResult run();

private:
    enum class FileResult
    {
        Continue,
        Finish,
        Skip,
    };

    void sendHeader(uint8_t frameType, const std::array<uint8_t, 4> &data = {});
    void sendZrinit();
    void sendZrpos(qint64 offset);
    void sendZack(qint64 offset);
    void readOo();
    FileResult receiveFile(const Header &header);
    std::optional<qint64> receiveDataFrames(QFile &file, int crcLen, qint64 offset,
                                            const QString &name, qint64 expectedSize);

    ZmodemIO &io_;
    QString outputDir_;
    FileStartCallback onFileStart_;
    ProgressCallback onProgress_;
    FileCompleteCallback onFileComplete_;
    double headerTimeout_;
    double dataTimeout_;
    int maxRetries_;
    ZmodemReader reader_;
    std::vector<FileInfo> files_;
};

} // namespace zmodem
