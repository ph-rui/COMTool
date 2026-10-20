# 人工验收签收文件

本目录保存每个候选包的人工验收签收记录。签收文件由执行人手工创建，并由提升脚本校验；校验不通过不得生成正式包。

## 命名规则

- 文件名：`<windows|linux>-<version>-<sha12>.json`，例如 `windows-0.01-0123456789ab.json`。
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
- 每条 `evidence` 必须非空；本地文件暂不存在时只记录警告，但仍应在发布前补齐，链接形式可直接填写。
- `soak.hours` 必须不小于 8，`soak.result` 必须为 `PASS`，`soak.evidence` 不得为空。
- 任一条件不满足时，`avalonia/pack/promote.ps1` 必须拒绝提升并输出原因。

## 提升正式包

候选包完成人工验收后运行：

```bash
pwsh -NoProfile -File avalonia/pack/promote.ps1 \
  -CandidateDir avalonia/publish/candidate \
  -Signoff docs/testing/signoffs/windows-0.01-<sha12>.json
```

省略 `-Signoff` 时，脚本只接受唯一匹配的
`docs/testing/signoffs/<windows|linux>-<version>-<sha12>*.json`；零个或多个都拒绝提升。
`-OutputDir` 默认为 `avalonia/publish/package`，可用绝对路径或相对仓库根目录的路径覆盖。
脚本没有 `-Force`。

提升会重新校验：候选包实际 SHA256 与 `candidate.json` 完全一致、签收文件绑定同一 SHA256、
矩阵 `Manual` 列全部人工项恰好出现一次且均为 `PASS`、证据非空、8 小时稳定性为 `PASS`。
任一项失败都不写入输出目录，已有正式包保持不变。全部通过后才在输出目录旁构建临时目录并原子替换，
同时写入 `release.json`：

- `candidateSha256`：被提升候选包的 SHA256；
- `releaseSha256`：正式包归档的 SHA256（必须与候选包一致）；
- `signoffPath`：签收文件路径（仓库内为相对路径）；
- `promotedAt`：UTC 提升时间。

正式包只能由 `promote.ps1` 在精确 SHA256、全部人工项和 8 小时稳定性均通过后生成；
不得手工复制候选包到 `avalonia/publish/package`。
