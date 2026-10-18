#include "appstyle.h"

#include <QPainter>
#include <QStyleOption>
#include <QPalette>

void AppStyle::drawPrimitive(PrimitiveElement element, const QStyleOption *option,
                             QPainter *painter, const QWidget *widget) const
{
    if (element == PE_IndicatorTabClose && option != nullptr && painter != nullptr) {
        painter->save();
        painter->setRenderHint(QPainter::Antialiasing, true);

        const bool enabled = (option->state & State_Enabled) != 0;
        QColor color = option->palette.color(enabled ? QPalette::Active : QPalette::Disabled,
                                             QPalette::WindowText);
        if ((option->state & State_MouseOver) != 0) {
            QColor background = option->palette.color(QPalette::Highlight);
            background.setAlpha(110);
            painter->setPen(Qt::NoPen);
            painter->setBrush(background);
            painter->drawRoundedRect(option->rect.adjusted(1, 1, -1, -1), 3, 3);
            color = option->palette.color(QPalette::HighlightedText);
        }

        QPen pen(color);
        pen.setWidthF(1.35);
        pen.setCapStyle(Qt::RoundCap);
        painter->setPen(pen);
        painter->setBrush(Qt::NoBrush);
        const QRect mark = option->rect.adjusted(4, 4, -4, -4);
        painter->drawLine(mark.topLeft(), mark.bottomRight());
        painter->drawLine(mark.topRight(), mark.bottomLeft());

        painter->restore();
        return;
    }

    QProxyStyle::drawPrimitive(element, option, painter, widget);
}
