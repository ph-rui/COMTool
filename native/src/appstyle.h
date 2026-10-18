#pragma once

#include <QProxyStyle>

// Small style adjustments that keep standard controls legible with the
// palette-only themes.  In particular the platform close icon is almost
// invisible on a dark palette, so draw it from the current text colour.
class AppStyle : public QProxyStyle
{
public:
    using QProxyStyle::QProxyStyle;

    void drawPrimitive(PrimitiveElement element, const QStyleOption *option,
                       QPainter *painter, const QWidget *widget = nullptr) const override;
};
