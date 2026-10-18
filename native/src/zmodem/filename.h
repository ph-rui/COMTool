#pragma once

#include "zmodem/codec.h"

#include <QString>

namespace zmodem {

struct FileMeta
{
    QString name;
    qint64 size = -1;
    qint64 mtime = -1;
    qint64 mode = -1;
    qint64 serial = -1;
};

QString sanitizeBasename(const QString &name);
QString uniqueTempPath(const QString &directory, const QString &name);
FileMeta parseZfileMetadata(const Bytes &payload);

} // namespace zmodem
