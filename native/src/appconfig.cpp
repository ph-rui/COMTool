#include "appconfig.h"

#include <QDir>
#include <QFileInfo>
#include <QSettings>
#include <QStandardPaths>

namespace {

int toInt(QSerialPort::DataBits value)
{
    return static_cast<int>(value);
}

QSerialPort::DataBits dataBitsFromInt(int value)
{
    switch (value) {
    case 5:
        return QSerialPort::Data5;
    case 6:
        return QSerialPort::Data6;
    case 7:
        return QSerialPort::Data7;
    default:
        return QSerialPort::Data8;
    }
}

QString parityToString(QSerialPort::Parity parity)
{
    switch (parity) {
    case QSerialPort::EvenParity:
        return QStringLiteral("E");
    case QSerialPort::OddParity:
        return QStringLiteral("O");
    case QSerialPort::MarkParity:
        return QStringLiteral("M");
    case QSerialPort::SpaceParity:
        return QStringLiteral("S");
    default:
        return QStringLiteral("N");
    }
}

QSerialPort::Parity parityFromString(const QString &value)
{
    if (value == QLatin1String("E")) return QSerialPort::EvenParity;
    if (value == QLatin1String("O")) return QSerialPort::OddParity;
    if (value == QLatin1String("M")) return QSerialPort::MarkParity;
    if (value == QLatin1String("S")) return QSerialPort::SpaceParity;
    return QSerialPort::NoParity;
}

QString stopToString(QSerialPort::StopBits stop)
{
    switch (stop) {
    case QSerialPort::OneAndHalfStop:
        return QStringLiteral("1.5");
    case QSerialPort::TwoStop:
        return QStringLiteral("2");
    default:
        return QStringLiteral("1");
    }
}

QSerialPort::StopBits stopFromString(const QString &value)
{
    if (value == QLatin1String("1.5")) return QSerialPort::OneAndHalfStop;
    if (value == QLatin1String("2")) return QSerialPort::TwoStop;
    return QSerialPort::OneStop;
}

QString flowToString(QSerialPort::FlowControl flow)
{
    switch (flow) {
    case QSerialPort::HardwareControl:
        return QStringLiteral("hardware");
    case QSerialPort::SoftwareControl:
        return QStringLiteral("software");
    default:
        return QStringLiteral("none");
    }
}

QSerialPort::FlowControl flowFromString(const QString &value)
{
    if (value == QLatin1String("hardware")) return QSerialPort::HardwareControl;
    if (value == QLatin1String("software")) return QSerialPort::SoftwareControl;
    return QSerialPort::NoFlowControl;
}

QString defaultDirectory()
{
    const QString documents = QStandardPaths::writableLocation(QStandardPaths::DocumentsLocation);
    if (!documents.isEmpty() && QDir(documents).exists()) {
        return documents;
    }
    return QDir::homePath();
}

} // namespace

QString AppConfig::filePath()
{
#ifdef Q_OS_WIN
    QString base = QString::fromLocal8Bit(qgetenv("APPDATA"));
    if (base.isEmpty()) {
        base = QStandardPaths::writableLocation(QStandardPaths::AppDataLocation);
    }
    return QDir(base).filePath(QStringLiteral("comtool-native/config.ini"));
#else
    return QDir::home().filePath(QStringLiteral(".config/comtool-native/config.ini"));
#endif
}

AppConfigData AppConfig::load()
{
    AppConfigData data;
    data.lastDir = defaultDirectory();
    data.zmodemLastDir = data.lastDir;

    const QString path = filePath();
    if (!QFileInfo::exists(path)) {
        return data;
    }

    QSettings settings(path, QSettings::IniFormat);
    data.geometry = settings.value(QStringLiteral("window/geometry")).toByteArray();
    data.windowState = settings.value(QStringLiteral("window/state")).toByteArray();
    data.darkTheme = settings.value(QStringLiteral("ui/darkTheme"), false).toBool();
    data.fontPointSize = settings.value(QStringLiteral("ui/fontPointSize"), 11).toInt();
    data.scrollbackLines = settings.value(QStringLiteral("ui/scrollback"), 5000).toInt();
    data.panelVisible = settings.value(QStringLiteral("ui/panelVisible"), true).toBool();
    data.lastDir = settings.value(QStringLiteral("files/lastDir"), data.lastDir).toString();
    data.zmodemLastDir =
        settings.value(QStringLiteral("files/zmodemLastDir"), data.lastDir).toString();

    const int count = settings.value(QStringLiteral("tabs/count"), 0).toInt();
    for (int index = 0; index < count; ++index) {
        const QString prefix = QStringLiteral("tabs/%1/").arg(index);
        SerialConfig config;
        config.portName = settings.value(prefix + QStringLiteral("port")).toString();
        config.baudRate = settings.value(prefix + QStringLiteral("baud"), 115200).toInt();
        config.dataBits = dataBitsFromInt(
            settings.value(prefix + QStringLiteral("dataBits"), 8).toInt());
        config.parity =
            parityFromString(settings.value(prefix + QStringLiteral("parity"), "N").toString());
        config.stopBits =
            stopFromString(settings.value(prefix + QStringLiteral("stopBits"), "1").toString());
        config.flowControl =
            flowFromString(settings.value(prefix + QStringLiteral("flow"), "none").toString());
        data.tabs.append(config);
    }
    return data;
}

void AppConfig::save(const AppConfigData &data)
{
    const QString path = filePath();
    QDir().mkpath(QFileInfo(path).absolutePath());
    QSettings settings(path, QSettings::IniFormat);
    settings.setValue(QStringLiteral("window/geometry"), data.geometry);
    settings.setValue(QStringLiteral("window/state"), data.windowState);
    settings.setValue(QStringLiteral("ui/darkTheme"), data.darkTheme);
    settings.setValue(QStringLiteral("ui/fontPointSize"), data.fontPointSize);
    settings.setValue(QStringLiteral("ui/scrollback"), data.scrollbackLines);
    settings.setValue(QStringLiteral("ui/panelVisible"), data.panelVisible);
    settings.setValue(QStringLiteral("files/lastDir"), data.lastDir);
    settings.setValue(QStringLiteral("files/zmodemLastDir"), data.zmodemLastDir);
    settings.setValue(QStringLiteral("tabs/count"), data.tabs.size());
    for (int index = 0; index < data.tabs.size(); ++index) {
        const SerialConfig &config = data.tabs.at(index);
        const QString prefix = QStringLiteral("tabs/%1/").arg(index);
        settings.setValue(prefix + QStringLiteral("port"), config.portName);
        settings.setValue(prefix + QStringLiteral("baud"), config.baudRate);
        settings.setValue(prefix + QStringLiteral("dataBits"), toInt(config.dataBits));
        settings.setValue(prefix + QStringLiteral("parity"), parityToString(config.parity));
        settings.setValue(prefix + QStringLiteral("stopBits"), stopToString(config.stopBits));
        settings.setValue(prefix + QStringLiteral("flow"), flowToString(config.flowControl));
    }
    settings.sync();
}
