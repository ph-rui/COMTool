# Iciclecreek.Avalonia.Terminal 4.1.8 重建源码（r67.12, 2026-11-26）

本目录为 4.1.8 库的完整可编译源码存档，随 `Iciclecreek.Avalonia.Terminal.4.1.8.nupkg`
一并提交，防止源码再次丢失（4.1.7 源码仓库已确认在全部机器上不存在）。

## 来源

- 基线：线上 4.1.7 DLL（2026-11-10 构建，nuspec 依赖 Avalonia/Avalonia.Skia 12.0.4、
  Porta.Pty 2.2.3、SkiaSharp.HarfBuzz 3.119.4、Unicode.net 2.0.0、Wcwidth 4.0.1、
  XTerm.NET 3.0.0）使用 `ilspycmd -r <references>` 带引用反编译重建（38 个源文件）。
- `Themes/Generic.axaml`：按反编译产物 `TerminalGenericStyles`（x:Class 编译体）逐条
  转写重建；原文件为 x:Class + XAML 编译器生成，本重建以 `TerminalGenericStyles.cs`
  存根 + XAML 编译器重新生成，AOT 行为一致（见 PLAN r52 AOT 结论）。
- 反编译时按 XTerm.NET 3.0.0 / Avalonia 12.0.4 实际 API 修正了类型引用
  （事件参数嵌套于 `XTerm.Events.TerminalEvents`，核心类须全限定 `XTerm.Terminal`）。

## 与 4.1.7 的差异（唯一功能变更）

`Iciclecreek.Terminal/TerminalView.cs`：

1. `TryBuildGlyphRun` width-2 分支：`new GlyphInfo(glyph, 0, _charWidth * 2.0)`
   → `new GlyphInfo(glyph, 0, _charWidth * 2.0, WideGlyphVerticalOffset(glyphTypeface, glyph, c))`
   （r67.12 宽字符 ink 行内垂直居中，对齐 MobaXterm；修复前 CJK 整块偏低 ~3px）。
2. 新增私有方法 `WideGlyphVerticalOffset`：1em 探针 `BuildGeometry()` 实测 ink 包围盒，
   `offset.y = (行中心 − ink 中心) × FontSize`（行中心 = `_charHeight/2 − _baseline`，
   相对基线）；几何不可用（headless 零宽占位框）退化为 em 盒中心
   （`FontMetrics.Ascent/Descent/DesignEmHeight`）；皆不可用零偏移（不丢字）。
3. 新增缓存字段 `_wideVOffsets`（(face, glyph) → offset，上限 8192），
   `UpdateTextMetrics` 字体变更时清空。

其余 30 个源文件与 4.1.7 反编译基线一致，无改动。

## 构建

```
dotnet pack -c Release -o <output>   # 产出 Iciclecreek.Avalonia.Terminal.4.1.8.nupkg
```

COMTool 侧经 `avalonia/third_party/iciclecreek/` 本地 feed 引用（见 `avalonia/NuGet.config`）。
