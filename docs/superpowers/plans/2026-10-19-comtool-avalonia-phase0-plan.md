# COMTool Avalonia Phase 0 实施计划

对应设计：`docs/superpowers/specs/2026-10-19-comtool-avalonia-parallel-design.md`

分支：`avalonia`

## 目标

在不改动 Python 稳定版的前提下，建立 Avalonia 一比一复刻项目骨架，验证终端、SSH、串口和 Windows 自包含发布，并产出 go/no-go 结论。

Phase 0 只做可丢弃或可继续演进的验证代码。它是 Phase 1 一比一复刻的基础，不是最终产品。

## 任务

### 1. 文档与基线

- [x] 1.1 写入 Avalonia 并行重构设计 spec。
- [x] 1.2 建立 `docs/parity-matrix.md`。
- [x] 1.3 建立 Phase 0 计划。
- [x] 1.4 记录 Python 基线提交和当前 UI 截图清单。
- [ ] 1.5 从 QSS 提取颜色、字体、间距 token 清单。

### 2. 开发环境

- [x] 2.1 在构建机安装并固定 .NET SDK。
- [x] 2.2 建立 `avalonia/global.json`。
- [x] 2.3 验证 `dotnet --info`、NuGet 还原和 Avalonia 模板。
- [x] 2.4 记录 Windows/Linux 构建命令。

### 3. 解决方案骨架

- [x] 3.1 创建 `avalonia/COMTool.Avalonia.sln`。
- [x] 3.2 创建 `src/COMTool.App` Avalonia 应用。
- [x] 3.3 创建 `src/COMTool.Core`、`COMTool.Connections`、`COMTool.Terminal`、`COMTool.Zmodem` 类库。
- [x] 3.4 创建 xUnit 测试项目。
- [x] 3.5 添加 `Directory.Build.props`：可空引用、语言版本、警告级别。
- [x] 3.6 添加 `.gitignore` 规则（`bin/`、`obj/`、发布目录）。

### 4. 主窗口静态壳

- [x] 4.1 复刻自定义标题栏、Logo、窗口按钮。
- [x] 4.2 复刻左侧 `User sessions` 侧栏和串口/SSH 分组。
- [x] 4.3 复刻标签区、`+` 插件选择器入口、空状态。
- [ ] 4.4 复刻设置按钮、主题按钮、语言/帮助/功能按钮。
- [ ] 4.5 复刻浅色/深色主题 token。
- [x] 4.6 用假数据展示串口/SSH 会话，验证布局比例。
- [x] 4.7 建立 UI 基准截图对比记录。

### 5. 终端可行性

- [x] 5.1 引入 Avalonia 终端控件（优先 `Avalonia.Terminal` / `XTerm.NET`）。
- [ ] 5.2 喂入 ANSI、256 色、中文、宽字符和滚动历史。
- [ ] 5.3 验证选择、复制、粘贴、右键菜单。
- [ ] 5.4 验证窗口 resize 和光标。
- [ ] 5.5 建立 1MB/10MB 输出基准。
- [ ] 5.6 验证 ZMODEM 原始字节流接入点。

### 6. 连接可行性

- [ ] 6.1 验证 `System.IO.Ports` 在 Windows 枚举 COM 口。
- [ ] 6.2 验证 SSH.NET 密码/密钥连接和 shell 输出。
- [ ] 6.3 验证 SSH PTY resize。
- [ ] 6.4 记录连接/重连耗时。
- [ ] 6.5 设计串口/SSH 抽象接口，避免 UI 直接依赖具体库。

### 7. 配置可行性

- [x] 7.1 建立 C# 配置模型，覆盖现有 `config.json` 字段。
- [x] 7.2 定义独立 Beta 配置目录。
- [ ] 7.3 实现从 Python 配置的单向导入原型。
- [ ] 7.4 验证 DPAPI `ProtectedData` token 读取/写入。
- [x] 7.5 配置 schema 单元测试。

### 8. 打包与 CI

- [x] 8.1 Windows `win-x64` 自包含发布。
- [x] 8.2 Linux `linux-x64` 发布。
- [x] 8.3 记录包体积、启动时间、内存。
- [x] 8.4 建立 GitHub Actions `avalonia.yml`，路径过滤到 `avalonia/**`。
- [ ] 8.5 CI 运行 restore/build/test。
- [x] 8.6 不修改 Python CI。

### 9. Go/No-Go 评估

- [ ] 9.1 终端：1MB/s 输出不冻结，输入可响应。
- [ ] 9.2 终端：选择/滚动/256 色/中文可用。
- [ ] 9.3 ZMODEM：有明确接入方案，无硬阻塞。
- [ ] 9.4 SSH/串口：可连接、可收发、可 resize。
- [ ] 9.5 配置：可导入现有会话，DPAPI 可用。
- [x] 9.6 打包：Windows 自包含包可启动。
- [ ] 9.7 输出 go/no-go 报告并更新 spec。

## 验证命令

```bash
# 使用本地安装的 SDK
/tmp/opencode/dotnet/dotnet --info
/tmp/opencode/dotnet/dotnet restore avalonia/COMTool.Avalonia.sln
/tmp/opencode/dotnet/dotnet build avalonia/COMTool.Avalonia.sln -c Release
/tmp/opencode/dotnet/dotnet test avalonia/COMTool.Avalonia.sln -c Release
```

Windows 自包含发布：

```powershell
dotnet publish avalonia/src/COMTool.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## 退出条件

Phase 0 结束时必须回答：

1. Avalonia 终端控件是否足够支撑一比一复刻？
2. ZMODEM 是否能以可接受成本接入？
3. SSH/串口在 Windows 上是否可用？
4. Windows 自包含包是否可接受？
5. 是否存在必须改变架构的硬阻塞？

如果任一 P0 项无解，暂停 Phase 1，先调整方案；不允许在未验证终端和 ZMODEM 的情况下直接开始全量 UI 复刻。
