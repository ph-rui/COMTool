#pragma once

#include "appconfig.h"

#include <QMainWindow>

class QTabWidget;
class SessionTab;

class MainWindow : public QMainWindow
{
    Q_OBJECT
public:
    explicit MainWindow(QWidget *parent = nullptr);
    ~MainWindow() override;

protected:
    void changeEvent(QEvent *event) override;
    void closeEvent(QCloseEvent *event) override;

private:
    void newTab(const SerialConfig &config = {});
    void closeTab(int index);
    void saveConfig();
    void applyTheme(bool dark);
    void updateWindowTitle();
    void setFontSize(int pointSize);
    SessionTab *currentTab() const;

    AppConfigData config_;
    QTabWidget *tabs_ = nullptr;
    bool dark_ = false;
    bool initialFocusDone_ = false;
};
