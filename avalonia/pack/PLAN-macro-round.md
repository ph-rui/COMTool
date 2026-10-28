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

## 已知偏差（已写入 spec 附录）

1. wait 步骤导出 typeId=4 为 COMTool 自定（MobaXterm 真实编码未实测）；导入未知 typeId 跳过+警告
2. 热键 .mxtmacros 编码未实测：导出恒 0，导入非 0 忽略+警告
3. 录制间隙阈值 1000ms/封顶 1200ms 由实测两点推断，未做梯度验证（值集中在 MacroRecorder 构造参数）
4. 等待字符串超时行为 MobaXterm 未知：实现为超时跳过继续，默认 30s
5. 桌面快捷方式 P2 灰显占位；热键为应用窗口级（MobaXterm 系统级）
6. 录制中 Ctrl/Alt 组合键不记录（只记普通文本+26 种特殊键）

## 测试基线

- Core：32 项宏测试（MacroModel 4/MacroFile 9/MacroRecorder 9/MacroRunner 8/Config 2）
- App：173→192 项全绿（新增 MacroFeatureTests 22 项；删除旧 MAC01 3 项）
- 全量 gate：2026-10-28 04:01Z 全 PASS（build/328 单测/192 App/26 项 Xvfb E2E/TestGate 矩阵校验，共 520 项）
