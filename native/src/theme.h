#pragma once

#include <QColor>
#include <QVector>

class QApplication;

namespace Theme {

struct TerminalColors
{
    QColor fg;
    QColor bg;
    QColor selectionFg;
    QColor selectionBg;
    QColor cursor;
    QVector<QColor> ansi;
};

TerminalColors terminalColors(bool dark);
void apply(QApplication &app, bool dark);

} // namespace Theme
