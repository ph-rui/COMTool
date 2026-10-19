# Phase 0 evidence screenshots

Captured on Xvfb `:99` at 100% scaling while running the Avalonia app from
`avalonia/` on Linux. These are functional evidence, not pixel baselines; the
850x500 UI baselines live in `docs/fixtures/ui/`.

| File | What it proves |
| --- | --- |
| `avalonia-terminal-clear.png` | Terminal tab opens and accepts a clear/echo command. |
| `avalonia-terminal-input-cjk.png` | Keyboard input, command echo, and CJK output (`中文`) render correctly. |
| `avalonia-terminal-256-color.png` | ANSI 256-color foreground (196/46) and background (21) render correctly. |
| `avalonia-terminal-resize-pty.png` | Window resize changes the terminal size; `stty size` reports `22 41` then `27 50`. |
| `avalonia-terminal-right-click-paste.png` | Right-click pastes `PASTE-中文-TEST` when no selection exists; left-drag selection and right-click copy were also exercised with `xclip`. |

The terminal uses `xterm-256color`, the Python dark background `#212121`, and
the Python cursor color `#4caf50`. The clipboard behavior follows the Python
terminal: left-drag selects, right-click copies when selected and pastes when
not, and `Ctrl+V`/`Ctrl+Shift+V` paste.
