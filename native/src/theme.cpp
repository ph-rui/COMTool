#include "theme.h"

#include <QApplication>
#include <QPalette>

namespace Theme {

namespace {

QColor blend(const QColor &first, const QColor &second, int percent)
{
    const int inverse = 100 - percent;
    return QColor((first.red() * inverse + second.red() * percent) / 100,
                  (first.green() * inverse + second.green() * percent) / 100,
                  (first.blue() * inverse + second.blue() * percent) / 100);
}

} // namespace

TerminalColors terminalColors(bool dark)
{
    TerminalColors colors;
    if (dark) {
        colors.fg = QColor(0xD8, 0xDE, 0xE9);
        colors.bg = QColor(0x10, 0x14, 0x18);
        colors.selectionFg = QColor(0xF5, 0xF7, 0xFA);
        colors.selectionBg = QColor(0x2D, 0x4F, 0x7A);
        colors.cursor = QColor(0xE8, 0xEE, 0xF7);
        colors.ansi = {
            QColor(0x1A, 0x1D, 0x22), QColor(0xE0, 0x6C, 0x75), QColor(0x98, 0xC3, 0x79),
            QColor(0xE5, 0xC0, 0x7B), QColor(0x61, 0xAF, 0xEF), QColor(0xC6, 0x78, 0xDD),
            QColor(0x56, 0xB6, 0xC2), QColor(0xAB, 0xB2, 0xBF), QColor(0x5C, 0x63, 0x70),
            QColor(0xFF, 0x7B, 0x86), QColor(0xB5, 0xE8, 0x90), QColor(0xFF, 0xD6, 0x8A),
            QColor(0x7C, 0xC7, 0xFF), QColor(0xE0, 0x94, 0xF5), QColor(0x7A, 0xDB, 0xE8),
            QColor(0xF0, 0xF3, 0xF8),
        };
    } else {
        colors.fg = QColor(0x1F, 0x23, 0x28);
        colors.bg = QColor(0xFA, 0xFB, 0xFC);
        colors.selectionFg = QColor(0x11, 0x18, 0x20);
        colors.selectionBg = QColor(0xBF, 0xD8, 0xF5);
        colors.cursor = QColor(0x1F, 0x23, 0x28);
        colors.ansi = {
            QColor(0x24, 0x29, 0x2E), QColor(0xC0, 0x39, 0x2B), QColor(0x2E, 0x7D, 0x32),
            QColor(0x9A, 0x67, 0x00), QColor(0x1A, 0x5F, 0xB4), QColor(0x8E, 0x24, 0xAA),
            QColor(0x00, 0x7A, 0x8A), QColor(0x6A, 0x73, 0x7D), QColor(0x57, 0x60, 0x6A),
            QColor(0xD3, 0x3A, 0x2C), QColor(0x2E, 0x8B, 0x36), QColor(0xA8, 0x74, 0x00),
            QColor(0x1E, 0x6F, 0xC8), QColor(0xA0, 0x2C, 0xC0), QColor(0x00, 0x8C, 0x9E),
            QColor(0x3A, 0x41, 0x49),
        };
    }
    for (int index = 16; index < 256; ++index) {
        if (index < 232) {
            const int value = index - 16;
            const int r = value / 36;
            const int g = (value % 36) / 6;
            const int b = value % 6;
            auto component = [](int level) {
                return level == 0 ? 0 : 55 + level * 40;
            };
            colors.ansi.append(QColor(component(r), component(g), component(b)));
        } else {
            const int level = 8 + (index - 232) * 10;
            colors.ansi.append(QColor(level, level, level));
        }
    }
    return colors;
}

void apply(QApplication &app, bool dark)
{
    const QColor window = dark ? QColor(0x14, 0x19, 0x1f) : QColor(0xf7, 0xf8, 0xfa);
    const QColor panel = dark ? QColor(0x1b, 0x22, 0x2b) : QColor(0xff, 0xff, 0xff);
    const QColor field = dark ? QColor(0x10, 0x15, 0x1b) : QColor(0xff, 0xff, 0xff);
    const QColor border = dark ? QColor(0x2b, 0x35, 0x42) : QColor(0xc9, 0xd0, 0xd8);
    const QColor text = dark ? QColor(0xd8, 0xde, 0xe9) : QColor(0x1f, 0x23, 0x28);
    const QColor muted = dark ? QColor(0x8b, 0x94, 0x9e) : QColor(0x6a, 0x73, 0x7d);
    const QColor accent = dark ? QColor(0x5b, 0x9b, 0xd5) : QColor(0x1a, 0x73, 0xe8);

    QPalette palette;
    palette.setColor(QPalette::Window, window);
    palette.setColor(QPalette::WindowText, text);
    palette.setColor(QPalette::Base, field);
    palette.setColor(QPalette::AlternateBase, panel);
    palette.setColor(QPalette::Text, text);
    palette.setColor(QPalette::Button, panel);
    palette.setColor(QPalette::ButtonText, text);
    palette.setColor(QPalette::BrightText, QColor(0xff, 0xff, 0xff));
    palette.setColor(QPalette::Highlight, dark ? QColor(0x2d, 0x4f, 0x7a)
                                               : QColor(0xbf, 0xd8, 0xf5));
    palette.setColor(QPalette::HighlightedText, dark ? QColor(0xf5, 0xf7, 0xfa)
                                                     : QColor(0x11, 0x18, 0x20));
    palette.setColor(QPalette::ToolTipBase, panel);
    palette.setColor(QPalette::ToolTipText, text);
    palette.setColor(QPalette::PlaceholderText, muted);
    palette.setColor(QPalette::Link, accent);
    palette.setColor(QPalette::LinkVisited, accent.darker(115));
    palette.setColor(QPalette::Light, blend(window, panel, 35));
    palette.setColor(QPalette::Midlight, blend(window, panel, 20));
    palette.setColor(QPalette::Mid, blend(panel, border, 60));
    palette.setColor(QPalette::Dark, border);
    palette.setColor(QPalette::Shadow, dark ? QColor(0x05, 0x08, 0x0b)
                                            : QColor(0x8f, 0x98, 0xa3));

    palette.setColor(QPalette::Disabled, QPalette::Text, muted);
    palette.setColor(QPalette::Disabled, QPalette::ButtonText, muted);
    palette.setColor(QPalette::Disabled, QPalette::WindowText, muted);
    palette.setColor(QPalette::Disabled, QPalette::HighlightedText, muted);

    app.setPalette(palette);
}

} // namespace Theme
