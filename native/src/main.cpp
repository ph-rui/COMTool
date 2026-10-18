#include "appstyle.h"
#include "mainwindow.h"
#include "i18n.h"
#include "theme.h"

#include <QApplication>
#include <QCommandLineParser>
#include <QElapsedTimer>
#include <QLibraryInfo>
#include <QTranslator>
#include <QTextStream>
#include <QTimer>

namespace {

// Qt's own catalogs translate most standard dialog labels, but the
// platform-theme provided buttons (notably Cancel) are not present in the
// Qt 5.15 Chinese catalog.  Fill those few gaps without pulling in a second
// application-wide translation mechanism.
class StandardButtonTranslator : public QTranslator
{
public:
    using QTranslator::QTranslator;

    QString translate(const char *context, const char *sourceText,
                      const char *disambiguation = nullptr, int n = -1) const override
    {
        if (I18n::isChinese() && sourceText != nullptr &&
            qstrcmp(context, "QPlatformTheme") == 0) {
            const QByteArray source(sourceText);
            if (source == "Cancel" || source == "&Cancel") {
                return QStringLiteral("取消");
            }
            if (source == "OK" || source == "&OK") {
                return QStringLiteral("确定");
            }
            if (source == "Yes" || source == "&Yes") {
                return QStringLiteral("是");
            }
            if (source == "No" || source == "&No") {
                return QStringLiteral("否");
            }
            if (source == "Save" || source == "&Save") {
                return QStringLiteral("保存");
            }
            if (source == "Open" || source == "&Open") {
                return QStringLiteral("打开");
            }
            if (source == "Close" || source == "&Close") {
                return QStringLiteral("关闭");
            }
        }
        return QTranslator::translate(context, sourceText, disambiguation, n);
    }
};

} // namespace

int main(int argc, char *argv[])
{
    QElapsedTimer startup;
    startup.start();

    QApplication::setAttribute(Qt::AA_EnableHighDpiScaling);
    QApplication app(argc, argv);
    const qint64 appReadyMs = startup.elapsed();
    QCoreApplication::setOrganizationName(QStringLiteral("COMTool"));
    QCoreApplication::setApplicationName(QStringLiteral("comtool-native"));
    QCoreApplication::setApplicationVersion(QStringLiteral("0.1.0"));

    QTranslator qtTranslator;
    if (I18n::isChinese() &&
        qtTranslator.load(QStringLiteral("qt_zh_CN"),
                          QLibraryInfo::location(QLibraryInfo::TranslationsPath))) {
        app.installTranslator(&qtTranslator);
    }
    StandardButtonTranslator buttonTranslator;
    app.installTranslator(&buttonTranslator);

#ifdef Q_OS_WIN
    QApplication::setStyle(new AppStyle(QStringLiteral("windowsvista")));
#else
    QApplication::setStyle(new AppStyle(QStringLiteral("Fusion")));
#endif

    QCommandLineParser parser;
    parser.setApplicationDescription(QStringLiteral("Native serial terminal with ZMODEM"));
    parser.addHelpOption();
    parser.addVersionOption();
    const QCommandLineOption benchmarkOption(
        QStringLiteral("benchmark-startup"),
        QStringLiteral("Print the time from process start to the first event-loop turn and exit."));
    parser.addOption(benchmarkOption);
    parser.process(app);

    MainWindow window;
    const qint64 windowConstructedMs = startup.elapsed();
    window.show();
    const qint64 windowShownMs = startup.elapsed();

    if (parser.isSet(benchmarkOption)) {
        QTimer::singleShot(0, &app, [&startup, &app, appReadyMs, windowConstructedMs,
                                     windowShownMs] {
            QCoreApplication::processEvents();
            QTextStream out(stdout);
            out << "startup_ms=" << startup.elapsed()
                << " app_ms=" << appReadyMs
                << " window_ms=" << windowConstructedMs - appReadyMs
                << " show_ms=" << windowShownMs - windowConstructedMs
                << "\n";
            out.flush();
            app.quit();
        });
    }

    return app.exec();
}
