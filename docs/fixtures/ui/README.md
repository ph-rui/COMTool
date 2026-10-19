# UI baseline fixtures

These PNGs are 850x500 client-area crops captured at 100% scaling on Xvfb
(1280x800, light theme, English locale). They are comparison fixtures only;
fonts and DPI may differ on other machines.

- `python-empty-850x500.png`: current Python UI at baseline commit `7ae55a5`,
  no remembered sessions.
- `avalonia-empty-850x500.png`: Phase 0 Avalonia shell, no sessions.
- `avalonia-terminal-850x500.png`: Phase 0 Avalonia shell with `--demo
  --terminal`, showing sidebar groups and the Iciclecreek terminal spike.

Capture notes:

- Python was run from `COMTool/venv` with `QT_QPA_PLATFORM=xcb` on `DISPLAY=:99`.
- Avalonia was run with `LIBGL_ALWAYS_SOFTWARE=1` on the same display.
- The window client area starts at the top-left of the dark title bar for both
  captures.
- The Python capture includes the always-visible left-edge session handle.
