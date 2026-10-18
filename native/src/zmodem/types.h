#pragma once

#include <QString>

namespace zmodem {

struct FileInfo
{
    QString name;
    QString path;
    qint64 size = 0;
    qint64 expectedSize = -1;
};

} // namespace zmodem
