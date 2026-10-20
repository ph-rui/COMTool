# 人工验收签收文件

本目录保存每个候选包的人工验收签收记录。签收文件由执行人手工创建，并由提升脚本校验；校验不通过不得生成正式包。

## 命名规则

- 文件名：`windows-0.01-<sha12>.json`。
- `<sha12>` 为候选包 SHA256 的前 12 个十六进制字符，与 `candidate.json` 对应。
- 每个候选包只允许一份签收文件；重新验收时覆盖同名文件并更新日期。

## 格式

```json
{
  "product": "COMTool-Avalonia",
  "version": "0.01",
  "platform": "windows-x64",
  "candidateSha256": "完整的 64 位 SHA256",
  "tester": "姓名",
  "date": "2026-10-20",
  "windowsVersion": "Windows 11 23H2",
  "hardware": ["COM97 CH344", "COM99 CH344"],
  "items": [
    {
      "id": "WIN-01",
      "result": "PASS",
      "evidence": "docs/testing/evidence/..."
    }
  ],
  "soak": {
    "hours": 8,
    "result": "PASS",
    "evidence": "docs/testing/evidence/..."
  }
}
```

## 校验规则

- `candidateSha256` 必须是完整的 64 个十六进制字符，并与候选包实际 SHA256 完全一致。
- `items` 必须包含 `docs/testing/feature-test-matrix.md` 中 `Manual` 列用到的每一个 `WIN-*` ID，不能遗漏。
- 每个 `items` 条目必须为 `PASS`，不允许出现 `FAIL`。
- 每条 `evidence` 必须非空，且指向真实存在的文件或链接。
- `soak.hours` 必须不小于 8，`soak.result` 必须为 `PASS`，`soak.evidence` 不得为空。
- 任一条件不满足时，`avalonia/pack/promote.ps1` 必须拒绝提升并输出原因。
