#pragma once

#include "serialconfig.h"

#include <QByteArray>
#include <QString>
#include <QVector>

struct AppConfigData
{
    QByteArray geometry;
    QByteArray windowState;
    bool darkTheme = false;
    int fontPointSize = 11;
    int scrollbackLines = 5000;
    bool panelVisible = true;
    QString lastDir;
    QString zmodemLastDir;
    QVector<SerialConfig> tabs;
};

class AppConfig
{
public:
    static QString filePath();
    static AppConfigData load();
    static void save(const AppConfigData &data);
};
