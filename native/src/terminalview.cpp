#include "terminalview.h"

#include "i18n.h"

#include <QApplication>
#include <QClipboard>
#include <QContextMenuEvent>
#include <QFontDatabase>
#include <QInputMethodEvent>
#include <QKeyEvent>
#include <QMenu>
#include <QMouseEvent>
#include <QPainter>
#include <QScrollBar>
#include <QStringList>
#include <QStyle>
#include <QTimer>
#include <QWheelEvent>

#include <algorithm>
#include <cmath>

namespace {

QString keySequenceFor(int key, bool shift)
{
    switch (key) {
    case Qt::Key_Up:
        return QStringLiteral("\x1b[A");
    case Qt::Key_Down:
        return QStringLiteral("\x1b[B");
    case Qt::Key_Right:
        return QStringLiteral("\x1b[C");
    case Qt::Key_Left:
        return QStringLiteral("\x1b[D");
    case Qt::Key_Home:
        return QStringLiteral("\x1b[H");
    case Qt::Key_End:
        return QStringLiteral("\x1b[F");
    case Qt::Key_Insert:
        return QStringLiteral("\x1b[2~");
    case Qt::Key_Delete:
        return QStringLiteral("\x1b[3~");
    case Qt::Key_PageUp:
        return QStringLiteral("\x1b[5~");
    case Qt::Key_PageDown:
        return QStringLiteral("\x1b[6~");
    case Qt::Key_F1:
        return QStringLiteral("\x1bOP");
    case Qt::Key_F2:
        return QStringLiteral("\x1bOQ");
    case Qt::Key_F3:
        return QStringLiteral("\x1bOR");
    case Qt::Key_F4:
        return QStringLiteral("\x1bOS");
    case Qt::Key_F5:
        return QStringLiteral("\x1b[15~");
    case Qt::Key_F6:
        return QStringLiteral("\x1b[17~");
    case Qt::Key_F7:
        return QStringLiteral("\x1b[18~");
    case Qt::Key_F8:
        return QStringLiteral("\x1b[19~");
    case Qt::Key_F9:
        return QStringLiteral("\x1b[20~");
    case Qt::Key_F10:
        return QStringLiteral("\x1b[21~");
    case Qt::Key_F11:
        return QStringLiteral("\x1b[23~");
    case Qt::Key_F12:
        return QStringLiteral("\x1b[24~");
    case Qt::Key_Backtab:
        return QStringLiteral("\x1b[Z");
    case Qt::Key_Tab:
        return shift ? QStringLiteral("\x1b[Z") : QStringLiteral("\t");
    case Qt::Key_Return:
    case Qt::Key_Enter:
        return QStringLiteral("\r");
    case Qt::Key_Backspace:
        return QStringLiteral("\x7f");
    case Qt::Key_Escape:
        return QStringLiteral("\x1b");
    default:
        return {};
    }
}

} // namespace

TerminalView::TerminalView(QWidget *parent) : QWidget(parent), metrics_(font_)
{
    setFocusPolicy(Qt::StrongFocus);
    setAttribute(Qt::WA_InputMethodEnabled, true);
    setAttribute(Qt::WA_OpaquePaintEvent, true);
    setAutoFillBackground(true);
    colors_ = Theme::terminalColors(false);

    font_ = QFontDatabase::systemFont(QFontDatabase::FixedFont);
    font_.setStyleHint(QFont::Monospace);
    font_.setFixedPitch(true);
    font_.setPointSize(11);
    applyFontMetrics();

    scrollBar_ = new QScrollBar(Qt::Vertical, this);
    scrollBar_->setSingleStep(1);
    connect(scrollBar_, &QScrollBar::valueChanged, this, [this](int) { update(); });

    setupVterm();
    resizeTerminal();
}

TerminalView::~TerminalView()
{
    if (vt_ != nullptr) {
        vterm_free(vt_);
        vt_ = nullptr;
        screen_ = nullptr;
    }
}

void TerminalView::setupVterm()
{
    vt_ = vterm_new(24, 80);
    vterm_set_utf8(vt_, 1);
    screen_ = vterm_obtain_screen(vt_);

    static VTermScreenCallbacks callbacks{};
    callbacks.damage = &TerminalView::damageCallback;
    callbacks.movecursor = &TerminalView::moveCursorCallback;
    callbacks.settermprop = &TerminalView::setTermPropCallback;
    callbacks.bell = &TerminalView::bellCallback;
    callbacks.resize = &TerminalView::resizeCallback;
    callbacks.sb_pushline = &TerminalView::pushLineCallback;
    callbacks.sb_popline = &TerminalView::popLineCallback;
    vterm_screen_set_callbacks(screen_, &callbacks, this);
    vterm_screen_set_damage_merge(screen_, VTERM_DAMAGE_ROW);
    vterm_screen_enable_altscreen(screen_, 1);
    vterm_screen_enable_reflow(screen_, true);
    vterm_screen_reset(screen_, 1);
    vterm_screen_flush_damage(screen_);
}

void TerminalView::applyFontMetrics()
{
    metrics_ = QFontMetricsF(font_);
    cellWidth_ = std::max<qreal>(1.0, std::ceil(metrics_.horizontalAdvance(QLatin1Char('M'))));
    cellHeight_ = std::max<qreal>(1.0, std::ceil(metrics_.height()));
    ascent_ = metrics_.ascent();
}

void TerminalView::setDarkTheme(bool dark)
{
    if (dark_ == dark) {
        return;
    }
    dark_ = dark;
    colors_ = Theme::terminalColors(dark);
    update();
}

void TerminalView::setFontPointSize(int pointSize)
{
    pointSize = std::clamp(pointSize, 6, 40);
    if (font_.pointSize() == pointSize) {
        return;
    }
    font_.setPointSize(pointSize);
    applyFontMetrics();
    resizeTerminal();
    update();
}

void TerminalView::setScrollbackLines(int lines)
{
    historyLimit_ = std::max(100, lines);
    while (history_.size() > historyLimit_) {
        history_.remove(0);
        hasSelection_ = false;
    }
    updateScrollBar();
    update();
}

void TerminalView::feed(const QByteArray &data)
{
    if (data.isEmpty() || vt_ == nullptr) {
        return;
    }
    const char *cursor = data.constData();
    int remaining = data.size();
    while (remaining > 0) {
        const int count = std::min(remaining, 8192);
        vterm_input_write(vt_, cursor, static_cast<size_t>(count));
        cursor += count;
        remaining -= count;
    }
    vterm_screen_flush_damage(screen_);
    scheduleRepaint();
}

void TerminalView::clear()
{
    if (screen_ == nullptr) {
        return;
    }
    clearHistory();
    vterm_screen_reset(screen_, 1);
    vterm_screen_flush_damage(screen_);
    update();
}

void TerminalView::clearHistory()
{
    history_.clear();
    hasSelection_ = false;
    scrollBar_->setRange(0, 0);
    scrollBar_->setValue(0);
}

void TerminalView::scheduleRepaint()
{
    if (repaintPending_) {
        return;
    }
    repaintPending_ = true;
    QTimer::singleShot(15, this, [this] {
        repaintPending_ = false;
        update();
    });
}

bool TerminalView::isAtBottom() const
{
    return scrollBar_->maximum() == 0 || scrollBar_->value() >= scrollBar_->maximum();
}

void TerminalView::scrollToBottom()
{
    scrollBar_->setValue(scrollBar_->maximum());
}

int TerminalView::visibleRows() const
{
    return std::max(1, screenRows_);
}

void TerminalView::updateScrollBar()
{
    const bool atBottom = isAtBottom();
    scrollBar_->setRange(0, history_.size());
    scrollBar_->setPageStep(std::max(1, visibleRows()));
    if (atBottom || history_.isEmpty()) {
        scrollBar_->setValue(scrollBar_->maximum());
    }
}

void TerminalView::resizeTerminal()
{
    if (vt_ == nullptr) {
        return;
    }
    const int scrollWidth = style()->pixelMetric(QStyle::PM_ScrollBarExtent, nullptr, this);
    scrollBar_->setGeometry(width() - scrollWidth, 0, scrollWidth, height());

    const int usableWidth = std::max(1, width() - scrollWidth);
    const int rows = std::max(2, static_cast<int>(height() / cellHeight_));
    const int cols = std::max(2, static_cast<int>(usableWidth / cellWidth_));
    if (rows != screenRows_ || cols != screenCols_) {
        screenRows_ = rows;
        screenCols_ = cols;
        vterm_set_size(vt_, rows, cols);
        vterm_screen_flush_damage(screen_);
    }
    updateScrollBar();
    update();
}

TerminalView::Line TerminalView::screenLine(int row) const
{
    Line line;
    line.resize(screenCols_);
    if (screen_ == nullptr || row < 0 || row >= screenRows_) {
        return line;
    }
    for (int col = 0; col < screenCols_; ++col) {
        VTermScreenCell cell{};
        const VTermPos pos{row, col};
        if (vterm_screen_get_cell(screen_, pos, &cell)) {
            line[col] = cellFromScreen(cell);
        }
    }
    return line;
}

TerminalView::Line TerminalView::lineFor(int absoluteLine) const
{
    if (absoluteLine < 0) {
        return {};
    }
    if (absoluteLine < history_.size()) {
        return history_.at(absoluteLine);
    }
    return screenLine(absoluteLine - history_.size());
}

TerminalView::Cell TerminalView::cellFromScreen(const VTermScreenCell &source) const
{
    Cell cell;
    std::copy(std::begin(source.chars), std::end(source.chars), std::begin(cell.chars));
    cell.width = static_cast<uint8_t>(source.width);
    cell.bold = source.attrs.bold;
    cell.underline = source.attrs.underline != VTERM_UNDERLINE_OFF;
    cell.underlineStyle = static_cast<uint8_t>(source.attrs.underline);
    cell.italic = source.attrs.italic;
    cell.strike = source.attrs.strike;
    cell.reverse = source.attrs.reverse;
    cell.conceal = source.attrs.conceal;
    cell.fg.type = source.fg.type;
    cell.fg.index = source.fg.indexed.idx;
    cell.fg.red = source.fg.rgb.red;
    cell.fg.green = source.fg.rgb.green;
    cell.fg.blue = source.fg.rgb.blue;
    cell.bg.type = source.bg.type;
    cell.bg.index = source.bg.indexed.idx;
    cell.bg.red = source.bg.rgb.red;
    cell.bg.green = source.bg.rgb.green;
    cell.bg.blue = source.bg.rgb.blue;
    return cell;
}

QColor TerminalView::resolveFg(const Cell &cell, bool selected) const
{
    if (selected) {
        return colors_.selectionFg;
    }
    if (cell.fg.type & VTERM_COLOR_DEFAULT_FG) {
        return colors_.fg;
    }
    if (cell.fg.type & VTERM_COLOR_TYPE_MASK) {
        return colors_.ansi.value(cell.fg.index, colors_.fg);
    }
    return QColor(cell.fg.red, cell.fg.green, cell.fg.blue);
}

QColor TerminalView::resolveBg(const Cell &cell) const
{
    if (cell.bg.type & VTERM_COLOR_DEFAULT_BG) {
        return colors_.bg;
    }
    if (cell.bg.type & VTERM_COLOR_TYPE_MASK) {
        return colors_.ansi.value(cell.bg.index, colors_.bg);
    }
    return QColor(cell.bg.red, cell.bg.green, cell.bg.blue);
}

QString TerminalView::cellText(const Cell &cell) const
{
    uint count = 0;
    while (count < 6 && cell.chars[count] != 0) {
        ++count;
    }
    if (count == 0) {
        return {};
    }
    return QString::fromUcs4(cell.chars, static_cast<int>(count));
}

QRect TerminalView::cellRect(int absoluteLine, int col, int width) const
{
    const int top = scrollBar_->value();
    return QRectF(col * cellWidth_, (absoluteLine - top) * cellHeight_,
                  width * cellWidth_, cellHeight_)
        .toRect();
}

bool TerminalView::isSelected(int line, int col) const
{
    if (!hasSelection_) {
        return false;
    }
    int startLine = selectionStartLine_;
    int startCol = selectionStartCol_;
    int endLine = selectionEndLine_;
    int endCol = selectionEndCol_;
    if (startLine > endLine || (startLine == endLine && startCol > endCol)) {
        std::swap(startLine, endLine);
        std::swap(startCol, endCol);
    }
    if (line < startLine || line > endLine) {
        return false;
    }
    if (line == startLine && col < startCol) {
        return false;
    }
    if (line == endLine && col > endCol) {
        return false;
    }
    return true;
}

QPoint TerminalView::cellAt(const QPoint &pos) const
{
    const int top = scrollBar_->value();
    const int line = top + static_cast<int>(pos.y() / cellHeight_);
    const int col = static_cast<int>(pos.x() / cellWidth_);
    const int maxLine = history_.size() + screenRows_ - 1;
    return QPoint(std::clamp(col, 0, std::max(0, screenCols_ - 1)),
                  std::clamp(line, 0, std::max(0, maxLine)));
}

void TerminalView::paintEvent(QPaintEvent *event)
{
    Q_UNUSED(event)
    QPainter painter(this);
    painter.fillRect(rect(), colors_.bg);
    painter.setFont(font_);
    painter.setRenderHint(QPainter::TextAntialiasing, true);

    const int top = scrollBar_->value();
    const int rows = std::max(1, static_cast<int>(height() / cellHeight_));
    const int maxCol = screenCols_;
    const bool focused = hasFocus();

    for (int row = 0; row < rows; ++row) {
        const int absoluteLine = top + row;
        const Line line = lineFor(absoluteLine);
        if (line.isEmpty()) {
            continue;
        }
        const qreal rowY = row * cellHeight_;

        // Background: merge adjacent cells with the same background colour.
        int runStart = 0;
        QColor runColor = colors_.bg;
        bool runOpen = false;
        for (int col = 0; col < line.size() && col < maxCol;) {
            const Cell &cell = line.at(col);
            const int width = std::max<int>(1, cell.width);
            QColor background = resolveBg(cell);
            if (cell.reverse) {
                background = resolveFg(cell, false);
            }
            if (isSelected(absoluteLine, col)) {
                background = colors_.selectionBg;
            }
            if (!runOpen) {
                runStart = col;
                runColor = background;
                runOpen = true;
            } else if (background != runColor) {
                painter.fillRect(QRectF(runStart * cellWidth_, rowY,
                                        (col - runStart) * cellWidth_, cellHeight_),
                                 runColor);
                runStart = col;
                runColor = background;
            }
            col += width;
        }
        if (runOpen) {
            const int endCol = std::min<int>(maxCol, line.size());
            painter.fillRect(QRectF(runStart * cellWidth_, rowY,
                                    (endCol - runStart) * cellWidth_, cellHeight_),
                             runColor);
        }

        // Text: draw cell by cell so wide glyphs and combining characters
        // keep their exact positions.  The background pass above is already
        // run-length merged.
        for (int col = 0; col < line.size() && col < maxCol;) {
            const Cell &cell = line.at(col);
            const int width = std::max<int>(1, cell.width);
            const bool selected = isSelected(absoluteLine, col);
            QColor foreground = resolveFg(cell, selected);
            if (cell.reverse) {
                foreground = resolveBg(cell);
            }
            if (cell.conceal) {
                foreground = resolveBg(cell);
            }

            const QString text = cellText(cell);
            if (!text.isEmpty()) {
                QFont font = font_;
                font.setBold(cell.bold);
                font.setItalic(cell.italic);
                painter.setFont(font);
                painter.setPen(foreground);
                painter.drawText(QPointF(col * cellWidth_, rowY + ascent_), text);

                if (cell.underline) {
                    const qreal y = rowY + ascent_ + 2;
                    painter.drawLine(QPointF(col * cellWidth_, y),
                                     QPointF((col + width) * cellWidth_, y));
                    if (cell.underlineStyle == VTERM_UNDERLINE_DOUBLE) {
                        painter.drawLine(QPointF(col * cellWidth_, y + 2),
                                         QPointF((col + width) * cellWidth_, y + 2));
                    }
                }
                if (cell.strike) {
                    const qreal y = rowY + ascent_ - ascent_ * 0.35;
                    painter.drawLine(QPointF(col * cellWidth_, y),
                                     QPointF((col + width) * cellWidth_, y));
                }
            }
            col += width;
        }
    }

    const int cursorLine = history_.size() + cursorRow_;
    if (cursorVisible_ && focused && cursorLine >= top && cursorLine < top + rows &&
        cursorCol_ >= 0 && cursorCol_ < maxCol) {
        QColor cursor = colors_.cursor;
        cursor.setAlpha(90);
        const QRectF rect(cursorCol_ * cellWidth_, (cursorLine - top) * cellHeight_,
                          cellWidth_, cellHeight_);
        painter.fillRect(rect, cursor);
        painter.setPen(colors_.cursor);
        painter.drawRect(rect.adjusted(0.5, 0.5, -0.5, -0.5));
    }
}

QString TerminalView::selectedText() const
{
    if (!hasSelection_) {
        return {};
    }
    int startLine = selectionStartLine_;
    int startCol = selectionStartCol_;
    int endLine = selectionEndLine_;
    int endCol = selectionEndCol_;
    if (startLine > endLine || (startLine == endLine && startCol > endCol)) {
        std::swap(startLine, endLine);
        std::swap(startCol, endCol);
    }

    QStringList lines;
    for (int lineIndex = startLine; lineIndex <= endLine; ++lineIndex) {
        const Line line = lineFor(lineIndex);
        if (line.isEmpty()) {
            lines << QString();
            continue;
        }
        const int first = lineIndex == startLine ? std::max(0, startCol) : 0;
        const int last = lineIndex == endLine ? std::min<int>(line.size() - 1, endCol)
                                               : static_cast<int>(line.size()) - 1;
        QString text;
        for (int col = first; col <= last && col < line.size();) {
            const Cell &cell = line.at(col);
            const int width = std::max<int>(1, cell.width);
            const QString glyph = cellText(cell);
            text += glyph.isEmpty() ? QStringLiteral(" ") : glyph;
            col += width;
        }
        while (text.endsWith(QLatin1Char(' '))) {
            text.chop(1);
        }
        lines << text;
    }
    while (!lines.isEmpty() && lines.last().isEmpty()) {
        lines.removeLast();
    }
    return lines.join(QLatin1Char('\n'));
}

void TerminalView::copySelection()
{
    const QString text = selectedText();
    if (!text.isEmpty()) {
        QApplication::clipboard()->setText(text);
    }
}

void TerminalView::pasteClipboard()
{
    if (inputBlocked_) {
        return;
    }
    const QString text = QApplication::clipboard()->text();
    if (!text.isEmpty()) {
        sendText(text);
    }
}

void TerminalView::selectAll()
{
    selectionAnchorLine_ = 0;
    selectionAnchorCol_ = 0;
    selectionStartLine_ = 0;
    selectionStartCol_ = 0;
    selectionEndLine_ = history_.size() + screenRows_ - 1;
    selectionEndCol_ = screenCols_ - 1;
    hasSelection_ = true;
    update();
}

void TerminalView::sendText(const QString &text)
{
    if (!inputBlocked_ && !text.isEmpty()) {
        emit dataToSend(text.toUtf8());
    }
}

void TerminalView::sendKeySequence(const QByteArray &sequence, bool alt)
{
    if (sequence.isEmpty()) {
        return;
    }
    if (alt) {
        QByteArray prefixed("\x1b", 1);
        prefixed += sequence;
        emit dataToSend(prefixed);
    } else {
        emit dataToSend(sequence);
    }
}

void TerminalView::mousePressEvent(QMouseEvent *event)
{
    if (event->button() == Qt::LeftButton) {
        setFocus(Qt::MouseFocusReason);
        const QPoint cell = cellAt(event->pos());
        selectionAnchorLine_ = cell.y();
        selectionAnchorCol_ = cell.x();
        selectionStartLine_ = cell.y();
        selectionStartCol_ = cell.x();
        selectionEndLine_ = cell.y();
        selectionEndCol_ = cell.x();
        hasSelection_ = false;
        dragging_ = true;
        update();
        event->accept();
        return;
    }
    QWidget::mousePressEvent(event);
}

void TerminalView::mouseMoveEvent(QMouseEvent *event)
{
    if (dragging_ && (event->buttons() & Qt::LeftButton)) {
        updateSelection(event->pos());
        event->accept();
        return;
    }
    QWidget::mouseMoveEvent(event);
}

void TerminalView::mouseReleaseEvent(QMouseEvent *event)
{
    if (event->button() == Qt::LeftButton && dragging_) {
        dragging_ = false;
        updateSelection(event->pos());
        event->accept();
        return;
    }
    QWidget::mouseReleaseEvent(event);
}

void TerminalView::mouseDoubleClickEvent(QMouseEvent *event)
{
    if (event->button() == Qt::LeftButton) {
        const QPoint cell = cellAt(event->pos());
        selectionStartLine_ = selectionEndLine_ = cell.y();
        selectionStartCol_ = selectionEndCol_ = cell.x();
        hasSelection_ = true;
        update();
        event->accept();
        return;
    }
    QWidget::mouseDoubleClickEvent(event);
}

void TerminalView::updateSelection(const QPoint &pos)
{
    const QPoint cell = cellAt(pos);
    selectionEndLine_ = cell.y();
    selectionEndCol_ = cell.x();
    hasSelection_ = selectionStartLine_ != selectionEndLine_ ||
                    selectionStartCol_ != selectionEndCol_;
    update();
}

void TerminalView::wheelEvent(QWheelEvent *event)
{
    if (event->modifiers() & Qt::ControlModifier) {
        setFontPointSize(font_.pointSize() + (event->angleDelta().y() > 0 ? 1 : -1));
        event->accept();
        return;
    }
    const int steps = event->angleDelta().y() / 120;
    scrollBar_->setValue(scrollBar_->value() - steps * 3);
    event->accept();
}

void TerminalView::focusInEvent(QFocusEvent *event)
{
    QWidget::focusInEvent(event);
    update();
}

void TerminalView::focusOutEvent(QFocusEvent *event)
{
    QWidget::focusOutEvent(event);
    update();
}

void TerminalView::keyPressEvent(QKeyEvent *event)
{
    const int key = event->key();
    const Qt::KeyboardModifiers modifiers = event->modifiers();
    const bool control = modifiers & Qt::ControlModifier;
    const bool shift = modifiers & Qt::ShiftModifier;
    const bool alt = modifiers & Qt::AltModifier;

    if (control && shift && key == Qt::Key_C) {
        copySelection();
        event->accept();
        return;
    }
    if (control && shift && key == Qt::Key_V) {
        pasteClipboard();
        event->accept();
        return;
    }
    if (control && shift && key == Qt::Key_A) {
        selectAll();
        event->accept();
        return;
    }
    if (control && (key == Qt::Key_Plus || key == Qt::Key_Equal)) {
        setFontPointSize(font_.pointSize() + 1);
        event->accept();
        return;
    }
    if (control && key == Qt::Key_Minus) {
        setFontPointSize(font_.pointSize() - 1);
        event->accept();
        return;
    }
    if (inputBlocked_) {
        event->accept();
        return;
    }

    if (control) {
        if (key >= Qt::Key_A && key <= Qt::Key_Z) {
            emit dataToSend(QByteArray(1, static_cast<char>(key - Qt::Key_A + 1)));
            event->accept();
            return;
        }
        switch (key) {
        case Qt::Key_Space:
            emit dataToSend(QByteArray(1, '\0'));
            event->accept();
            return;
        case Qt::Key_BracketLeft:
            emit dataToSend(QByteArray(1, '\x1b'));
            event->accept();
            return;
        case Qt::Key_Backslash:
            emit dataToSend(QByteArray(1, '\x1c'));
            event->accept();
            return;
        case Qt::Key_BracketRight:
            emit dataToSend(QByteArray(1, '\x1d'));
            event->accept();
            return;
        case Qt::Key_AsciiCircum:
            emit dataToSend(QByteArray(1, '\x1e'));
            event->accept();
            return;
        case Qt::Key_Underscore:
            emit dataToSend(QByteArray(1, '\x1f'));
            event->accept();
            return;
        default:
            break;
        }
    }

    const QString sequence = keySequenceFor(key, shift);
    if (!sequence.isEmpty()) {
        sendKeySequence(sequence.toUtf8(), alt);
        event->accept();
        return;
    }

    const QString text = event->text();
    if (!text.isEmpty() && !text.at(0).isNull()) {
        if (alt) {
            emit dataToSend(QByteArray("\x1b", 1) + text.toUtf8());
        } else {
            sendText(text);
        }
        event->accept();
        return;
    }

    QWidget::keyPressEvent(event);
}

void TerminalView::inputMethodEvent(QInputMethodEvent *event)
{
    if (!event->commitString().isEmpty()) {
        sendText(event->commitString());
    }
    event->accept();
}

QVariant TerminalView::inputMethodQuery(Qt::InputMethodQuery query) const
{
    if (query == Qt::ImCursorRectangle) {
        const int line = history_.size() + cursorRow_;
        return cellRect(line, cursorCol_);
    }
    if (query == Qt::ImCursorPosition) {
        return 0;
    }
    if (query == Qt::ImSurroundingText) {
        return QString();
    }
    if (query == Qt::ImCurrentSelection) {
        return selectedText();
    }
    return QWidget::inputMethodQuery(query);
}

void TerminalView::contextMenuEvent(QContextMenuEvent *event)
{
    QMenu menu(this);
    QAction *copyAction = menu.addAction(I18n::tr("Copy"));
    copyAction->setEnabled(hasSelection_);
    QAction *pasteAction = menu.addAction(I18n::tr("Paste"));
    QAction *selectAllAction = menu.addAction(I18n::tr("Select All"));
    menu.addSeparator();
    QAction *clearAction = menu.addAction(I18n::tr("Clear"));
    QAction *chosen = menu.exec(event->globalPos());
    if (chosen == copyAction) {
        copySelection();
    } else if (chosen == pasteAction) {
        pasteClipboard();
    } else if (chosen == selectAllAction) {
        selectAll();
    } else if (chosen == clearAction) {
        clear();
    }
    event->accept();
}

void TerminalView::resizeEvent(QResizeEvent *event)
{
    QWidget::resizeEvent(event);
    resizeTerminal();
}

// libvterm callbacks -------------------------------------------------------

int TerminalView::damageCallback(VTermRect rect, void *user)
{
    Q_UNUSED(rect)
    auto *view = static_cast<TerminalView *>(user);
    view->scheduleRepaint();
    return 1;
}

int TerminalView::moveCursorCallback(VTermPos pos, VTermPos oldpos, int visible, void *user)
{
    Q_UNUSED(oldpos)
    auto *view = static_cast<TerminalView *>(user);
    view->cursorRow_ = pos.row;
    view->cursorCol_ = pos.col;
    view->cursorVisible_ = visible != 0;
    return 1;
}

int TerminalView::setTermPropCallback(VTermProp prop, VTermValue *value, void *user)
{
    auto *view = static_cast<TerminalView *>(user);
    if (prop == VTERM_PROP_CURSORVISIBLE) {
        view->cursorVisible_ = value->boolean != 0;
        return 1;
    }
    if (prop == VTERM_PROP_TITLE) {
        const QByteArray piece(value->string.str, static_cast<int>(value->string.len));
        if (value->string.initial) {
            view->titleBuffer_.clear();
        }
        view->titleBuffer_ += QString::fromUtf8(piece);
        if (value->string.final) {
            view->title_ = view->titleBuffer_;
            emit view->titleChanged(view->title_);
        }
        return 1;
    }
    return 1;
}

int TerminalView::bellCallback(void *user)
{
    Q_UNUSED(user)
    QApplication::beep();
    return 1;
}

int TerminalView::resizeCallback(int rows, int cols, void *user)
{
    Q_UNUSED(rows)
    Q_UNUSED(cols)
    Q_UNUSED(user)
    return 1;
}

int TerminalView::pushLineCallback(int cols, const VTermScreenCell *cells, void *user)
{
    auto *view = static_cast<TerminalView *>(user);
    Line line;
    line.resize(cols);
    for (int col = 0; col < cols; ++col) {
        line[col] = view->cellFromScreen(cells[col]);
    }
    view->pushLine(line);
    return 1;
}

int TerminalView::popLineCallback(int cols, VTermScreenCell *cells, void *user)
{
    Q_UNUSED(cols)
    Q_UNUSED(cells)
    Q_UNUSED(user)
    return 0;
}

void TerminalView::pushLine(const Line &line)
{
    const bool atBottom = isAtBottom();
    history_.append(line);
    if (history_.size() > historyLimit_) {
        history_.remove(0);
        hasSelection_ = false;
    }
    scrollBar_->setRange(0, history_.size());
    scrollBar_->setPageStep(std::max(1, visibleRows()));
    if (atBottom) {
        scrollBar_->setValue(scrollBar_->maximum());
    }
    update();
}
