# COMTool 实时串口列表实现计划

对应设计：`docs/superpowers/specs/2026-10-17-comtool-live-port-refresh-design.md`

分支：`feature/live-port-refresh`

## 任务

- [x] 1. 新增 `COMTool/conn/port_utils.py`：`PortInfo`、格式化、自然排序、macOS 去重、集合差异、选择索引、tooltip。
- [x] 2. 新增 `tests/test_port_utils.py`，先写失败用例，再实现到通过。
- [x] 3. 修改 `COMTool/Combobox.py`：`popupShown` / `popupHidden` 信号与可见状态。
- [x] 4. 修改 `COMTool/conn/conn_serial.py`：500ms 单飞扫描、弹窗期间刷新、占位项、`UserRole`、选择保持、停止定时器。
- [x] 5. 修改 `COMTool/autoUpdate.py`：指向 fork，跳过 neucrack 更新源。
- [x] 6. 补占位文案翻译：`messages.pot`、`zh_CN`、`zh_TW`。
- [x] 7. 新增 `NOTICE`，README 增加 fork 说明、运行与打包命令。
- [x] 8. 新增 `.github/workflows/test.yml`，跑纯逻辑单测。
- [x] 9. 本地验证：`pytest`、`py_compile`、无头 Qt 冒烟测试（可用时）。
- [ ] 10. 提交并推送到 `origin/feature/live-port-refresh`。

## 验证命令

```bash
python -m pytest tests -q
python -m py_compile COMTool/Combobox.py COMTool/conn/conn_serial.py COMTool/conn/port_utils.py
QT_QPA_PLATFORM=offscreen python -m pytest tests -q
```
