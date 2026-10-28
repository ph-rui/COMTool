# 本轮任务清单（2026-10-26）

用户反馈 5 项 + 确认范围：

- [x] 调查白边根因：Win10 18363 不支持 DWMWA_BORDER_COLOR；BorderOnly 的 DWM 边框（蓝+白）无法用该属性去除
- [x] 实测：Aero Snap 需要 WS_THICKFRAME；WS_POPUP|WS_THICKFRAME + NCCALCSIZE 全客户区 → 边框 0 像素
- [ ] 1. 白边修复：全平台 WindowDecorations.None；Windows 上 P/Invoke 给样式加 WS_THICKFRAME（Opened/Activated/WindowState 变化时重设）；删除 DWMWA_COLOR_NONE P/Invoke
- [ ] 2. Ctrl+滚轮调终端字号：MainWindow tunnel PointerWheelChanged，Ctrl + 指针在 TerminalControl 上，FontSize ±1（8..48），不持久化
- [ ] 3. 服务器引擎（COMTool.Connections/Servers）：
  - [ ] FtpServer：匿名 FTP，PASV，CWD/CDUP/PWD/LIST/NLST/RETR/STOR/SIZE/MKD/RMD/DELE/QUIT，路径穿越防护
  - [ ] TelnetServer：TCP 监听 + 回显 + 日志
  - [ ] SshServerHost：Universal.Common.Ssh.Server（NuGet，CC0），内置 ed25519 主机密钥（AvaloniaResource），接受任意凭据，sftp 子系统映射到根目录
  - [ ] CronServer：5 字段 cron 解析（纯函数）+ 定时执行命令 + 输出日志
  - [ ] IperfServer：PATH 查找 iperf3/iperf 子进程 -s -p PORT，流式输出；未找到 → 明确提示
  - [ ] StubHost（NFS/VNC）：启动时在服务器输出显示未实现说明
- [ ] 4. 配置持久化（COMTool.Core/Config/ServerSettings.cs）：ftp/telnet/ssh/cron/iperf 各自 port/root/cron 字段
- [ ] 5. 服务器窗口 UI 1:1 复刻 MobaXterm：经典灰底、3D 按钮（选中行 ⚙ 蓝框）、双线 groupbox、居中「XX 服务器设置」、经典复选框/微调框、底部绿勾「确定」；9 行全部可点；TFTP/HTTP 根目录输入框加宽
- [ ] 6. 测试：
  - [ ] Connections：FtpServer 原始 socket 集成测试、TelnetServer 回显、CronParser 单测、IperfServer 未找到提示、SshServerHost 用 SSH.NET SftpClient 集成测试
  - [ ] App：WindowSnapTests 更新（None + 样式位逻辑）、Ctrl+滚轮字号、服务器行/面板/占位启动提示、配置持久化
- [ ] 7. 文档：feature-test-matrix SRV 行、parity matrix、manual-acceptance WIN-26、packaging 记录
- [ ] 8. 全量 gate PASS
- [ ] 9. 部署 Windows + RDP 实机验证：顶边无白边、贴墙分屏、最大化还原、Ctrl+滚轮、TFTP 路径完整、服务器窗口外观、FTP/Telnet/SSH/Cron/Iperf 逐个起停
- [ ] 10. 用户确认后 push
