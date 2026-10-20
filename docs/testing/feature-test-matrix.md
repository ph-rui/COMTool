# COMTool Avalonia 功能测试矩阵

本表以 `docs/parity-matrix.md` 的功能 ID 为主键，逐行登记自动化测试、门禁阶段和人工验收证据。
状态与 `docs/parity-matrix.md` 保持一致（`完成（Avalonia 增强）` 归一化为 `完成`）；尚未补齐的自动化测试显式写 `-`，不得用人工验收替代。
门禁用法见 `docs/superpowers/specs/2026-10-20-comtool-avalonia-test-gate-design.md` 5.1/5.2 节。

| Feature | Status | Automated | Phase | Platform | Command | Manual | Evidence |
|---|---|---|---|---|---|---|---|
| UI-01 | 完成 | - | verify | all | - | - | 待补测试 |
| UI-02 | 完成 | - | verify | all | - | - | 待补测试 |
| UI-03 | 完成 | - | verify | all | - | - | 待补测试 |
| UI-04 | 完成 | - | verify | all | - | - | 待补测试 |
| UI-05 | 完成 | - | verify | all | - | - | 待补测试 |
| UI-06 | 完成 | - | verify | all | - | - | 待补测试 |
| UI-07 | 完成 | - | verify | all | - | - | 待补测试 |
| UI-08 | 完成 | - | verify | all | - | - | 待补测试 |
| UI-09 | 完成 | - | verify | all | - | - | 待补测试 |
| UI-10 | 未开始 | - | manual | all | - | - | 未开始 |
| UI-11 | 部分 | - | manual | all | - | WIN-10 | Windows 真实桌面 About/帮助入口与插件帮助、更新提示的视觉和交互核对；插件帮助和更新检测尚未实现，不能自动化 |
| UI-12 | 完成 | - | verify | all | - | - | 待补测试 |
| UI-13 | 完成 | - | verify | all | - | - | 待补测试 |
| UI-14 | 部分 | - | manual | all | - | WIN-02 | Windows 150%/200% DPI 需要真实桌面和显示器，裁切属于视觉判断，无法自动化 |
| UI-15 | 完成 | - | verify | all | - | - | 待补测试 |
| TERM-01 | 完成 | - | verify | all | - | - | 待补测试 |
| TERM-02 | 完成 | - | verify | all | - | - | 待补测试 |
| TERM-03 | 完成 | - | verify | all | - | - | 待补测试 |
| TERM-04 | 完成 | - | verify | all | - | - | 待补测试 |
| TERM-05 | 完成 | - | verify | all | - | - | 待补测试 |
| TERM-06 | 完成 | - | verify | all | - | - | 待补测试 |
| TERM-07 | 完成 | - | verify | all | - | - | 待补测试 |
| TERM-08 | 完成 | - | verify | all | - | - | 待补测试 |
| TERM-09 | 完成 | - | verify | all | - | - | 待补测试 |
| TERM-10 | 完成 | - | verify | all | - | - | 待补测试 |
| TERM-11 | 完成 | - | verify | all | - | - | 待补测试 |
| TERM-12 | 完成 | - | verify | all | - | - | 待补测试 |
| TERM-13 | 完成 | - | verify | all | - | - | 待补测试 |
| TERM-14 | 完成 | - | verify | all | - | - | 待补测试 |
| TERM-15 | 完成 | - | verify | all | - | - | 待补测试 |
| SER-01 | 完成 | - | verify | all | - | - | 待补测试 |
| SER-02 | 完成 | - | verify | all | - | - | 待补测试 |
| SER-03 | 部分 | - | manual | all | - | WIN-03 | 串口热插拔依赖真实硬件设备，无法自动化 |
| SER-04 | 完成 | - | verify | all | - | - | 待补测试 |
| SER-05 | 部分 | - | manual | all | - | WIN-11 | DSR/DTR 流控依赖真实串口硬件和引脚接线，无法自动化 |
| SER-06 | 部分 | - | manual | all | - | WIN-04 | 端口占用需要真实串口硬件，无法自动化 |
| SER-07 | 部分 | - | manual | all | - | WIN-05 | Windows 串口回环收发依赖真实硬件回路，无法自动化 |
| SER-08 | 不适用 | - | manual | all | - | - | 不适用 |
| SER-09 | 部分 | - | manual | all | - | WIN-12 | Windows 配置内串口 UI 收发依赖真实串口硬件，无法自动化 |
| SER-10 | 部分 | - | manual | all | - | WIN-13 | 设备消失场景依赖真实串口硬件拔插，无法自动化 |
| SSH-01 | 完成 | - | verify | all | - | - | 待补测试 |
| SSH-02 | 完成 | - | verify | all | - | - | 待补测试 |
| SSH-03 | 完成 | - | verify | all | - | - | 待补测试 |
| SSH-04 | 完成 | - | verify | all | - | - | 待补测试 |
| SSH-05 | 完成 | - | verify | all | - | - | 待补测试 |
| SSH-06 | 完成 | - | verify | all | - | - | 待补测试 |
| SSH-07 | 不适用 | - | manual | all | - | - | 不适用 |
| SSH-08 | 完成 | - | verify | all | - | - | 待补测试 |
| SSH-09 | 完成 | - | verify | all | - | - | 待补测试 |
| SSH-10 | 完成 | - | verify | all | - | - | 待补测试 |
| NET-01 | 未开始 | - | manual | all | - | - | 未开始 |
| NET-02 | 未开始 | - | manual | all | - | - | 未开始 |
| NET-03 | 未开始 | - | manual | all | - | - | 未开始 |
| NET-04 | 未开始 | - | manual | all | - | - | 未开始 |
| ZM-01 | 完成 | - | verify | all | - | - | 待补测试 |
| ZM-02 | 完成 | - | verify | all | - | - | 待补测试 |
| ZM-03 | 完成 | - | verify | all | - | - | 待补测试 |
| ZM-04 | 完成 | - | verify | all | - | - | 待补测试 |
| ZM-05 | 完成 | - | verify | all | - | - | 待补测试 |
| ZM-06 | 完成 | - | verify | all | - | - | 待补测试 |
| ZM-07 | 部分 | - | manual | all | - | WIN-14 | 长时间重复传输的内存曲线需要真实桌面长时间运行观察，无法自动化 |
| PLG-01 | 未开始 | - | manual | all | - | - | 未开始 |
| PLG-02 | 完成 | - | verify | all | - | - | 待补测试 |
| PLG-03 | 未开始 | - | manual | all | - | - | 未开始 |
| PLG-04 | 未开始 | - | manual | all | - | - | 未开始 |
| PLG-05 | 未开始 | - | manual | all | - | - | 未开始 |
| PLG-06 | 未开始 | - | manual | all | - | - | 未开始 |
| PLG-07 | 未开始 | - | manual | all | - | - | 未开始 |
| CFG-01 | 完成 | - | verify | all | - | - | 待补测试 |
| CFG-02 | 完成 | - | verify | all | - | - | 待补测试 |
| CFG-03 | 完成 | - | verify | all | - | - | 待补测试 |
| CFG-04 | 完成 | - | verify | all | - | - | 待补测试 |
| CFG-05 | 完成 | - | verify | all | - | - | 待补测试 |
| CFG-06 | 完成 | - | verify | all | - | - | 待补测试 |
| CFG-07 | 未开始 | - | manual | all | - | - | 未开始 |
| CFG-08 | 未开始 | - | manual | all | - | - | 未开始 |
| PKG-01 | 完成 | - | verify | all | - | - | 待补测试 |
| PKG-02 | 部分 | - | manual | all | - | WIN-15 | Wayland 与 AppImage 需要真实桌面会话验证，无法自动化 |
| PKG-03 | 未开始 | - | manual | all | - | - | 未开始 |
| PKG-04 | 完成 | - | verify | all | - | - | 待补测试 |
| PKG-05 | 完成 | - | verify | all | - | - | 待补测试 |
| PKG-06 | 部分 | - | manual | all | - | WIN-16 | CI 流水线需在托管 CI 实际运行并核对日志，本地门禁无法自动化 |
| TST-01 | 完成 | - | verify | all | - | - | 待补测试 |
| TST-02 | 完成 | - | verify | all | - | - | 待补测试 |
| TST-03 | 完成 | - | verify | all | - | - | 待补测试 |
| TST-04 | 部分 | - | manual | all | - | WIN-17 | Windows 实机 SSH/串口集成依赖真实硬件，无法自动化 |
| TST-05 | 部分 | - | manual | all | - | WIN-18 | 最终逐页像素复核属于视觉判断，无法自动化 |
| TST-06 | 未开始 | - | manual | all | - | - | 未开始 |
| TST-07 | 部分 | - | manual | all | - | WIN-19 | 默认版本切换与回退需要真实桌面安装环境演练，无法自动化 |
