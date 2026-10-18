#include "zmodem/filename.h"

#include <QDir>
#include <QFileInfo>
#include <QRegularExpression>
#include <QStringList>

#include <algorithm>

namespace zmodem {
namespace {

const QStringList &reservedNames()
{
    static const QStringList names = [] {
        QStringList list{"CON", "PRN", "AUX", "NUL"};
        for (int i = 1; i <= 9; ++i) {
            list << QStringLiteral("COM%1").arg(i);
            list << QStringLiteral("LPT%1").arg(i);
        }
        return list;
    }();
    return names;
}

qint64 parseInteger(const QString &value, int base)
{
    bool ok = false;
    const qint64 parsed = value.toLongLong(&ok, base);
    return ok && parsed >= 0 ? parsed : -1;
}

} // namespace

QString sanitizeBasename(const QString &input)
{
    QString name = input.normalized(QString::NormalizationForm_C);
    name.replace(QLatin1Char('\\'), QLatin1Char('/'));
    name = name.section(QLatin1Char('/'), -1);

    QString cleaned;
    cleaned.reserve(name.size());
    for (const QChar ch : name) {
        const ushort code = ch.unicode();
        if (code < 0x20 || code == 0x7F) {
            continue;
        }
        if (QStringLiteral("<>:\"|?*/\\").contains(ch)) {
            cleaned.append(QLatin1Char('_'));
        } else {
            cleaned.append(ch);
        }
    }
    name = cleaned.trimmed();
    while (name.endsWith(QLatin1Char('.')) || name.endsWith(QLatin1Char(' '))) {
        name.chop(1);
    }
    if (name.isEmpty() || name == QLatin1String(".") || name == QLatin1String("..")) {
        return QStringLiteral("download");
    }

    const QFileInfo info(name);
    const QString root = info.completeBaseName();
    const QString suffix = info.suffix();
    for (const QString &reserved : reservedNames()) {
        if (root.compare(reserved, Qt::CaseInsensitive) == 0) {
            name += QLatin1Char('_');
            break;
        }
    }

    if (name.size() > 200) {
        const int keep = qMax(1, 200 - suffix.size() - (suffix.isEmpty() ? 0 : 1));
        name = name.left(keep);
        if (!suffix.isEmpty()) {
            name += QLatin1Char('.') + suffix;
        }
    }
    return name.isEmpty() ? QStringLiteral("download") : name;
}

QString uniqueTempPath(const QString &directory, const QString &name)
{
    const QString base = sanitizeBasename(name);
    const QString candidate = QDir(directory).filePath(base);
    if (!QFileInfo::exists(candidate)) {
        return candidate;
    }
    const QFileInfo info(base);
    const QString root = info.completeBaseName();
    const QString suffix = info.suffix();
    for (int index = 1;; ++index) {
        QString next = QStringLiteral("%1 (%2)").arg(root).arg(index);
        if (!suffix.isEmpty()) {
            next += QLatin1Char('.') + suffix;
        }
        const QString path = QDir(directory).filePath(next);
        if (!QFileInfo::exists(path)) {
            return path;
        }
    }
}

FileMeta parseZfileMetadata(const Bytes &payload)
{
    FileMeta meta;
    const auto zero = std::find(payload.begin(), payload.end(), static_cast<uint8_t>(0));
    const size_t nameLength = static_cast<size_t>(zero - payload.begin());
    const QByteArray nameBytes(reinterpret_cast<const char *>(payload.data()),
                               static_cast<int>(nameLength));
    meta.name = sanitizeBasename(QString::fromUtf8(nameBytes));

    if (zero == payload.end()) {
        return meta;
    }
    const auto restStart = static_cast<size_t>(zero - payload.begin()) + 1;
    const QByteArray rest(reinterpret_cast<const char *>(payload.data() + restStart),
                          static_cast<int>(payload.size() - restStart));
    const QStringList parts = QString::fromLatin1(rest).split(
        QRegularExpression(QStringLiteral("\\s+")), Qt::SkipEmptyParts);
    if (!parts.isEmpty()) {
        meta.size = parseInteger(parts.at(0), 10);
    }
    if (parts.size() > 1) {
        meta.mtime = parseInteger(parts.at(1), 8);
    }
    if (parts.size() > 2) {
        meta.mode = parseInteger(parts.at(2), 8);
    }
    if (parts.size() > 3) {
        meta.serial = parseInteger(parts.at(3), 10);
    }
    return meta;
}

} // namespace zmodem
