# 本轮任务清单（2026-10-28 宏 1:1 复刻 MobaXterm 22.1）

范围（用户以"实施"确认全量 1:1；spec/plan 已批准）：

- [x] Task 1 Core：MacroStep/MacroDefinition/MacroKeys(26 键)/MacroFile(.mxtmacros 解析导出，实机样本验证)/MacroMigrator(旧 text+appendEnter→steps)；MacroConfig 加 steps[]/hotkey，Version 保持 3（commit 0a28378）
- [x] Task 2 Core：MacroRecorder（间隙>1000ms→SLEEP 封顶 1200ms，字符合并，Key 断行）（commit 0a28378）
- [x] Task 3 Core：MacroRunner（文本逐字延迟/按键转义/睡眠/等待字符串 50ms 轮询超时跳过、取消）（commit 0a28378）
- [x] Task 4 App：侧栏宏页 MobaXterm 化（Record 按钮/已保存的宏/◎ 列表+9 项右键菜单）、录制（tunnel 捕获不吞键）、执行（红色横幅+取消）、旧配置迁移、命名框/删除确认、Ctrl+Space 浮层、顶栏"宏"菜单、热键触发（commit af14361）
- [x] Task 5 App：MacroEditorWindow("Macro 编辑" 4 按钮+热键下拉) + MacroStepEditorDialog(4 radio 类型)；Edit/Assign hotkey 均开编辑器；工作副本语义（commit af14361/62cbc9a 部分）
- [x] Task 6 App：快捷键分配校验（保留/重复拒绝）+ Ctrl+Space 浮层 + 顶栏"宏"按钮测试补齐（commit 62cbc9a 前）
- [x] Task 7 App：.mxtmacros 导入/导出（明文警告原文、默认文件名、重名 (imported)、IMacroFilePickers+Scripted 环境变量）（commit a780cd8）
- [x] 修复：Avalonia Key 枚举非 ASCII（(Key)'M'=77 ≠ Key.M=56）导致字母热键永不触发（commit 3356d6e）
- [x] 修复：标题栏加"宏"列后 min/max/close 的 Grid.Column 未后移（5/6/7→6/7/8），关闭按钮落空列导致 E2E APP01 点关闭不退出（commit b72cebd）
- [x] Task 8 文档：parity-matrix MAC-01~08 拆条、feature-test-matrix 登记 22 App+32 Core 测试（MAC-01~08 八行）、manual-acceptance WIN-27、本文件
- [x] 全量 gate（pack/verify.sh，2026-10-28 04:01Z：build/328 单测/192 App/26 项 Xvfb E2E/TestGate 全 PASS，520 项）
- [x] 发布 win-x64 单文件 R2R（154,887,681 B，SHA256 4A39AF2C…）+ deploy_r3.py 双份部署
  （C:\Users\devuser\COMTool-Avalonia.exe 与 D:\Users\devuser\Desktop\ 双份校验 OK）
- [x] SSH 冒烟：进程起来（Session 0 存活 180s+，WS 240MB）、config.json 可读写
  （run.log starting+Config loaded，config 内容完好 v3）、无崩溃日志
  （仅 Skia swap chain 0x887A0022 刷屏 = Session 0 非交互桌面 D3D 设备丢失，
  phase0-report L129 已有同类记录；交互桌面不受影响）
- [ ] 用户实机目验（WIN-27 清单；对照 MobaXterm 22.1 并排）
- [ ] 用户确认后 push（本轮不 push）
- [x] 追加：应用户反馈移除顶栏"宏"按钮（标题栏恢复原 8 列，min/max/close 回 5/6/7；
  宏入口保留左侧栏宏页 + Ctrl+Space + 热键；commit cc9cff4）；gate 第三轮
  （2026-10-28 06:18Z）全 PASS；重新发布（SHA256 FF36D195…）双份部署 + SSH 冒烟通过
- [x] 追加：用户目验反馈（截图）——① 修复"录制新宏"按钮有终端后不亮起的 bug：
  SelectedTab 变化时未通知 CanToggleRecording（补 OnPropertyChanged），新增
  MAC21_RecordButtonEnablesWithActiveTerminal；② 宏 UI 全面汉化：面板标题"宏"、
  "录制新宏/停止录制宏"、9 项右键菜单（重命名/编辑/复制/删除/分配快捷键/执行…）、
  编辑器"宏编辑"+4 按钮+提示、命名框"请为此宏输入一个名称"、导出警告/成功提示改中文；
  文档同步（parity MAC-01/04/05/08、FTM、WIN-27）

- [x] 追加：用户第三次反馈（UI 与 MobaXterm 差异 + "用起来比较卡"）
  - UI 对齐（参照 mx-state3/mx-del-menu/mx-export-dlg 基准图，FA6 码点已验证存在）：
    行图标 f202(⊠)→f110 circle-dot(◎) 蓝 #2f7ed8；录制按钮改双色图标
    （空闲=灰环 f05c + 小红点 f111 常显，录制中=大红点 f111）；9 项右键菜单加图标
    （f044 橙/灰、f24d、f1f8 红、f11b、f144 蓝、f005 金、f305 红、f306 蓝）；
    对话框标题统一 "COMTool"（对齐 MobaXterm 用应用名做标题）；命名框加铅笔 f303
    图标 + 底部居中 ✓确定(f00c 绿)/✗取消(f00d 红) 图标按钮（新增 IconFont()/
    DialogButtonContent() helper）；导出警告/成功提示加蓝色 info 图标 f05a
  - 测试：MacroFeatureTests 按图标+文字按钮更新断言（ButtonByLabel helper），23/23 + 全量 193/193 绿
  - 卡顿诊断：远程机 = SANGFOR VDI（i440FX VM + 2×Sangfor TitanGPU 虚拟显卡，
    driver 11.4.8.914，Win10 Pro）。Session 0 冒烟复现 Skia D3D11 交换链
    0x887A0022 device lost 刷屏（~120 次/秒）→ 头号嫌疑 = 虚拟 GPU 上 D3D11 渲染不稳。
    代码审查：本轮无新增周期负载（仅旧 2s 端口扫描/1s 服务器定时器；MacroRunner
    50ms 轮询仅 Wait 步骤执行时）
  - 软件渲染 A/B 开关：Program.cs BuildAvaloniaApp 增加 COMTOOL_SW_RENDER 环境变量，
    =1 时 With(new Win32PlatformOptions { RenderingMode = { Software } })，
    绕开 AngleEgl(D3D11) 走 CPU framebuffer（Avalonia 12.1.4 API 已用元数据核验：
    Win32PlatformOptions.RenderingMode / Win32RenderingMode.Software 均存在；
    默认行为 { AngleEgl, Software } 按序回退不受影响）
   - gate 第 6 轮（2026-10-28 08:21Z）全 PASS（build/328 单测/193 App/26 E2E/TestGate）
   - 发布 win-x64（154,887,681 B，SHA256 F3C4C987…）+ deploy_r3 双份部署校验 OK；
     用户目录另放 "COMTool-软件渲染.bat"（set COMTOOL_SW_RENDER=1 后启动）
   - Session 0 冒烟 A/B（120s，smoke-ab.ps1）：默认 D3D 路径 CPU 18.98s
     （~15.8% 单核持续占用，虚拟 GPU 上 D3D 心跳/交换链空转）；
     COMTOOL_SW_RENDER=1 时 CPU 2.67s（~2.2%）、日志干净 → 开关生效，
     卡顿主嫌疑锁定虚拟 GPU 的 D3D11 渲染路径；待用户交互桌面实测确认
   - 提交 3445771
   - 追加（2026-10-29）：RDP 实机核对 MobaXterm 22.1 后三项修正：
     ① 执行宏不显示任何横幅（MobaXterm 执行时无 UI，录制横幅保留）；
     ② 单击行即执行宏（RDP 实测 MobaXterm 单击即触发）；
     ③ 行图标改为灰环+蓝色播放三角（实机像素采样 #6e6e6e/#2196f3）；
     f10c 在 solid 字体中是实心盘非环 → 改用矢量 Border 圆环；
     MAC17 改写 + 新增 MAC24/MAC25，197/197，gate 第 8/9 轮全 PASS；
     提交 c215ad2 + 图标修复；发布 15cd67aa 双份部署。
     另：已从用户 MobaXterm.ini 删除 RDP 测试宏 echo moba_test（保持其安全净净）；
     录制文本丢失 bug 修复见 970d1f4（MAC22/23）。
   - 追加（2026-10-29）：宏对话框 UI 优化（用户截图反馈）：新增 CreateMacroDialog
     工厂（COMTool 标题/不显示任务栏/居中/不可缩放）+ Windows 剥离 min/max 按钮
     （SetWindowLongPtr 去 WS_MINIMIZEBOX|WS_MAXIMIZEBOX；CanResize=false 在 VDI 上去不掉）；4 个宏对话框统一（命名/删除/导出警告/成功提示）：f05a info 图标
     + DialogButtonContent 图标按钮（是(Y)✓/否(N)✗，IsDefault/IsCancel）；修复导出警告按钮竖排 bug；
     MAC15/导出警告测试改 ButtonByLabel；197/197，gate 第 10 轮全 PASS；
     提交 efabcab，发布部署双份。
   - 追加（2026-10-29）：收起侧栏时 rail 图标再保持"选中"白底（用户截图反馈：
     收起后星形图标发白你被点住）： VM 增 RailSessions/Tools/Macros/SshBrowserActive
     （=IsSidebarVisible&&对应 Tab）， IsSidebarVisible  ApplySidebarMode  _sidebarVisible； 4 个 rail 按钮 Classes.active ¹ Rail*Active；
     新增 UI16_RailActiveClearedWhenDockCollapsed； 198/198， gate 第 10c 轮全 PASS；
     提交 c1abecb，发布部署双份。

## 已知偏差（已写入 spec 附录）

1. wait 步骤导出 typeId=4 为 COMTool 自定（MobaXterm 真实编码未实测）；导入未知 typeId 跳过+警告
2. 热键 .mxtmacros 编码未实测：导出恒 0，导入非 0 忽略+警告
3. 录制间隙阈值 1000ms/封顶 1200ms 由实测两点推断，未做梯度验证（值集中在 MacroRecorder 构造参数）
4. 等待字符串超时行为 MobaXterm 未知：实现为超时跳过继续，默认 30s
5. 桌面快捷方式 P2 灰显占位；热键为应用窗口级（MobaXterm 系统级）
6. 录制中 Ctrl/Alt 组合键不记录（只记普通文本+26 种特殊键）

## 测试基线

- Core：32 项宏测试（MacroModel 4/MacroFile 9/MacroRecorder 9/MacroRunner 8/Config 2）
- App：173→193 项全绿（新增 MacroFeatureTests 23 项，含 MAC21；删除旧 MAC01 3 项）
- 全量 gate：2026-10-28 06:53Z 第四轮全 PASS（build/328 单测/193 App/26 项 Xvfb E2E/TestGate，共 521 项）
