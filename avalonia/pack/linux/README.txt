COMTool Avalonia Beta - Linux package
====================================

This is a portable, self-contained Linux x64 build of the Avalonia Beta. It is
separate from the Python COMTool app and uses its own configuration directory
(~/.config/comtool-avalonia by default).

Requirements
------------
- Linux x64
- X11 or Wayland (through the normal Avalonia/X11 backend)

Install (current user, no sudo)
-------------------------------
    ./install.sh

The app is installed under ~/.local/opt/comtool-avalonia, a launcher is created
at ~/.local/bin/comtool-avalonia, and a desktop entry is written to
~/.local/share/applications.

Custom prefix:

    PREFIX=/opt/my-prefix ./install.sh

Run
---
    ~/.local/bin/comtool-avalonia

or start "COMTool Avalonia Beta" from the desktop application menu.

Uninstall
---------
    ./uninstall.sh

This removes the app, launcher, icon and desktop entry. It intentionally keeps
the configuration at ~/.config/comtool-avalonia so remembered sessions are not
lost. Delete that directory manually to remove all Beta state.

Rollback
--------
The Python COMTool installation and ~/.config/comtool are untouched. To roll
back, simply keep using or restore the Python app; uninstalling this Beta does
not change the stable app.
