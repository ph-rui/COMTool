#pragma once

#include <QByteArray>
#include <QColor>
#include <QFont>
#include <QFontMetricsF>
#include <QPoint>
#include <QString>
#include <QVector>
#include <QWidget>

#include "vterm.h"
#include "theme.h"

class QScrollBar;

class TerminalView : public QWidget
{
    Q_OBJECT
public:
    explicit TerminalView(QWidget *parent = nullptr);
    ~TerminalView() override;

    void feed(const QByteArray &data);
    void clear();
    void setDarkTheme(bool dark);
    bool darkTheme() const { return dark_; }
    void setFontPointSize(int pointSize);
    int fontPointSize() const { return font_.pointSize(); }
    void setScrollbackLines(int lines);
    int scrollbackLines() const { return historyLimit_; }
    void setInputBlocked(bool blocked) { inputBlocked_ = blocked; }
    bool inputBlocked() const { return inputBlocked_; }
    void copySelection();
    void pasteClipboard();
    void selectAll();
    void sendText(const QString &text);
    QString title() const { return title_; }

signals:
    void dataToSend(const QByteArray &data);
    void titleChanged(const QString &title);

protected:
    void paintEvent(QPaintEvent *event) override;
    void resizeEvent(QResizeEvent *event) override;
    void keyPressEvent(QKeyEvent *event) override;
    void mousePressEvent(QMouseEvent *event) override;
    void mouseMoveEvent(QMouseEvent *event) override;
    void mouseReleaseEvent(QMouseEvent *event) override;
    void mouseDoubleClickEvent(QMouseEvent *event) override;
    void wheelEvent(QWheelEvent *event) override;
    void focusInEvent(QFocusEvent *event) override;
    void focusOutEvent(QFocusEvent *event) override;
    void inputMethodEvent(QInputMethodEvent *event) override;
    QVariant inputMethodQuery(Qt::InputMethodQuery query) const override;
    void contextMenuEvent(QContextMenuEvent *event) override;

private:
    struct CellColor
    {
        uint8_t type = 0;
        uint8_t index = 0;
        uint8_t red = 0;
        uint8_t green = 0;
        uint8_t blue = 0;
    };

    struct Cell
    {
        uint32_t chars[6]{};
        uint8_t width = 0;
        bool bold = false;
        bool underline = false;
        uint8_t underlineStyle = 0;
        bool italic = false;
        bool strike = false;
        bool reverse = false;
        bool conceal = false;
        CellColor fg;
        CellColor bg;
    };

    using Line = QVector<Cell>;

    static int damageCallback(VTermRect rect, void *user);
    static int moveCursorCallback(VTermPos pos, VTermPos oldpos, int visible, void *user);
    static int setTermPropCallback(VTermProp prop, VTermValue *value, void *user);
    static int bellCallback(void *user);
    static int resizeCallback(int rows, int cols, void *user);
    static int pushLineCallback(int cols, const VTermScreenCell *cells, void *user);
    static int popLineCallback(int cols, VTermScreenCell *cells, void *user);

    void setupVterm();
    void applyFontMetrics();
    void resizeTerminal();
    void scheduleRepaint();
    void scrollToBottom();
    bool isAtBottom() const;
    Line lineFor(int absoluteLine) const;
    Line screenLine(int row) const;
    Cell cellFromScreen(const VTermScreenCell &screenCell) const;
    QColor resolveFg(const Cell &cell, bool selected) const;
    QColor resolveBg(const Cell &cell) const;
    QString cellText(const Cell &cell) const;
    QPoint cellAt(const QPoint &pos) const;
    void updateSelection(const QPoint &pos);
    QString selectedText() const;
    bool isSelected(int line, int col) const;
    QRect cellRect(int absoluteLine, int col, int width = 1) const;
    void sendKeySequence(const QByteArray &sequence, bool alt = false);
    void pushLine(const Line &line);
    void clearHistory();
    void updateScrollBar();
    int visibleRows() const;

    VTerm *vt_ = nullptr;
    VTermScreen *screen_ = nullptr;
    QScrollBar *scrollBar_ = nullptr;

    QFont font_;
    QFontMetricsF metrics_;
    qreal cellWidth_ = 8.0;
    qreal cellHeight_ = 16.0;
    qreal ascent_ = 12.0;
    int screenRows_ = 24;
    int screenCols_ = 80;

    QVector<Line> history_;
    int historyLimit_ = 5000;
    bool dark_ = false;
    Theme::TerminalColors colors_;

    int cursorRow_ = 0;
    int cursorCol_ = 0;
    bool cursorVisible_ = true;
    bool repaintPending_ = false;
    QString title_;
    QString titleBuffer_;

    bool inputBlocked_ = false;
    bool dragging_ = false;
    bool hasSelection_ = false;
    int selectionAnchorLine_ = 0;
    int selectionAnchorCol_ = 0;
    int selectionStartLine_ = 0;
    int selectionStartCol_ = 0;
    int selectionEndLine_ = 0;
    int selectionEndCol_ = 0;
};
