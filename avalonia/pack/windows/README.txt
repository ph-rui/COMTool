COMTool Avalonia Beta
=====================

This package installs the Avalonia rewrite as a separate per-user application.
It does not replace, modify, or remove the Python COMTool installation.

Install
-------
1. Extract the whole zip to a temporary folder.
2. Right-click install.ps1 and choose "Run with PowerShell".
   Alternatively, run:

   powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1

The installer writes to:

   %LOCALAPPDATA%\Programs\COMTool Avalonia
   %LOCALAPPDATA%\COMTool Avalonia

It creates Start Menu and Desktop shortcuts and an entry in
Settings > Apps > Installed apps. No administrator rights are required.

The Beta config is separate:

   %APPDATA%\COMTool-Avalonia

On the first run it imports the Python config one way. Later Python config
changes are not imported automatically.

Uninstall
---------
Use Settings > Apps > Installed apps, or run the uninstaller from:

   %LOCALAPPDATA%\COMTool Avalonia\uninstall.ps1

The Beta config is kept by default. Pass -RemoveConfig to delete it:

   powershell -NoProfile -ExecutionPolicy Bypass -File uninstall.ps1 -RemoveConfig

The Python app and %APPDATA%\comtool are never touched.

Rollback
--------
The Python COMTool installation remains the default and is not changed by this
package. To roll back, uninstall COMTool Avalonia Beta and continue using the
existing Python launcher.

Notes
-----
- The executable is currently unsigned, so Windows SmartScreen may warn on the
  first run. Choose "More info" and verify the publisher/source before running.
- The app is a compressed self-contained single-file executable. First start
  may take a little longer while Windows extracts it.
