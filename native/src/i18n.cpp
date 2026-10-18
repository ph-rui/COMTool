#include "i18n.h"

#include <QHash>
#include <QLocale>

namespace I18n {
namespace {

const QHash<QString, QString> &chinese()
{
    static const QHash<QString, QString> map = {
        {QStringLiteral("Serial Port"), QStringLiteral("串口")},
        {QStringLiteral("Port"), QStringLiteral("端口")},
        {QStringLiteral("Refresh"), QStringLiteral("刷新")},
        {QStringLiteral("Baud rate"), QStringLiteral("波特率")},
        {QStringLiteral("Data bits"), QStringLiteral("数据位")},
        {QStringLiteral("Parity"), QStringLiteral("校验")},
        {QStringLiteral("Stop bits"), QStringLiteral("停止位")},
        {QStringLiteral("Flow control"), QStringLiteral("流控")},
        {QStringLiteral("None"), QStringLiteral("无")},
        {QStringLiteral("Hardware"), QStringLiteral("硬件")},
        {QStringLiteral("Software"), QStringLiteral("软件")},
        {QStringLiteral("Open"), QStringLiteral("打开")},
        {QStringLiteral("Close"), QStringLiteral("关闭")},
        {QStringLiteral("Disconnected"), QStringLiteral("未连接")},
        {QStringLiteral("Connected"), QStringLiteral("已连接")},
        {QStringLiteral("Hide panel"), QStringLiteral("隐藏面板")},
        {QStringLiteral("Show panel"), QStringLiteral("显示面板")},
        {QStringLiteral("New Session"), QStringLiteral("新建会话")},
        {QStringLiteral("New Tab"), QStringLiteral("新建标签")},
        {QStringLiteral("Close Tab"), QStringLiteral("关闭标签")},
        {QStringLiteral("Quit"), QStringLiteral("退出")},
        {QStringLiteral("File"), QStringLiteral("文件")},
        {QStringLiteral("View"), QStringLiteral("视图")},
        {QStringLiteral("Help"), QStringLiteral("帮助")},
        {QStringLiteral("Dark Theme"), QStringLiteral("深色主题")},
        {QStringLiteral("Increase Font"), QStringLiteral("增大字体")},
        {QStringLiteral("Decrease Font"), QStringLiteral("减小字体")},
        {QStringLiteral("About"), QStringLiteral("关于")},
        {QStringLiteral("About COMTool Native"), QStringLiteral("关于 COMTool Native")},
        {QStringLiteral("Cancel"), QStringLiteral("取消")},
        {QStringLiteral("Sending..."), QStringLiteral("发送中...")},
        {QStringLiteral("Receiving..."), QStringLiteral("接收中...")},
        {QStringLiteral("Opening %1..."), QStringLiteral("正在打开 %1...")},
        {QStringLiteral("Connected: %1"), QStringLiteral("已连接：%1")},
        {QStringLiteral("Error: %1"), QStringLiteral("错误：%1")},
        {QStringLiteral("Port closed"), QStringLiteral("端口已关闭")},
        {QStringLiteral("%1 complete"), QStringLiteral("%1 完成")},
        {QStringLiteral("Received %1 file(s)"), QStringLiteral("已接收 %1 个文件")},
        {QStringLiteral("%1 — %2%"), QStringLiteral("%1 — %2%")},
        {QStringLiteral("%1 — %2 bytes"), QStringLiteral("%1 — %2 字节")},
        {QStringLiteral("Cannot save %1"), QStringLiteral("无法保存 %1")},
        {QStringLiteral("ZMODEM transfer aborted"), QStringLiteral("ZMODEM 传输已中止")},
        {QStringLiteral("Select files to send"), QStringLiteral("选择要发送的文件")},
        {QStringLiteral("Save received file"), QStringLiteral("保存接收文件")},
        {QStringLiteral("Files sent"), QStringLiteral("文件已发送")},
        {QStringLiteral("File transfer cancelled"), QStringLiteral("文件传输已取消")},
        {QStringLiteral("ZMODEM error"), QStringLiteral("ZMODEM 错误")},
        {QStringLiteral("Serial error"), QStringLiteral("串口错误")},
        {QStringLiteral("Serial port"), QStringLiteral("串口")},
        {QStringLiteral("Port is not selected"), QStringLiteral("未选择端口")},
        {QStringLiteral("The serial port is already open in another session."),
         QStringLiteral("该串口已在另一个会话中打开。")},
        {QStringLiteral("Close this session while a file transfer is active?"),
         QStringLiteral("文件传输进行中，仍要关闭此会话吗？")},
        {QStringLiteral("Close COMTool Native while a file transfer is active?"),
         QStringLiteral("文件传输进行中，仍要退出 COMTool Native 吗？")},
        {QStringLiteral("Copy"), QStringLiteral("复制")},
        {QStringLiteral("Paste"), QStringLiteral("粘贴")},
        {QStringLiteral("Select All"), QStringLiteral("全选")},
        {QStringLiteral("Clear"), QStringLiteral("清屏")},
        {QStringLiteral("Receive"), QStringLiteral("接收")},
        {QStringLiteral("Send"), QStringLiteral("发送")},
        {QStringLiteral("Error"), QStringLiteral("错误")},
        {QStringLiteral("Cannot open serial port"), QStringLiteral("无法打开串口")},
        {QStringLiteral("Serial device disconnected"), QStringLiteral("串口设备已断开")},
        {QStringLiteral("Ready"), QStringLiteral("就绪")},
        {QStringLiteral("Session"), QStringLiteral("会话")},
    };
    return map;
}

} // namespace

bool isChinese()
{
    return QLocale::system().language() == QLocale::Chinese;
}

QString tr(const char *text)
{
    const QString key = QString::fromUtf8(text);
    if (!isChinese()) {
        return key;
    }
    return chinese().value(key, key);
}

} // namespace I18n
