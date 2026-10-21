# Telnet Windows DPAPI verification (2026-10-21)

This evidence closes the Windows-only gap left by the Linux Telnet E2E. The
Python COMTool app and its config were not touched.

## Environment

- Windows 10 Pro 1909 test host `172.16.66.163`, RDP desktop at 1280x800.
- Binary: `D:\Users\zhangrui\Desktop\COMTool-Avalonia.exe`
  - source commit `adc338d`
  - SHA256 `6D6659E3251697A6D2D22ACB4843F8152FBFBDE0F521A078C2C2AC2399B5B213`
- Config: `C:\Users\zhangrui\AppData\Roaming\COMTool-Avalonia\config.json`
- Fixture: local Python Telnet server at `172.16.66.48:23231` with separate
  `login:` and `Password:` prompts.

## Checks

1. Saved a Telnet session and opened it from the sidebar. The server observed
   `LOGIN ... 'winuser'` at `login:`; the app then waited for `Password:`.
2. Entered the test password in the connection settings with **Remember
   password** enabled and clicked **OPEN**. The server observed
   `PASSWORD ... accepted=True` followed by `LOGIN_OK`.
3. Typed `PING` in the terminal. The server observed `INPUT ... 'PING'` and
   returned `PONG`.
4. Restarted the app without opening the settings dialog or retyping the
   password. Clicking the saved session produced a new `LOGIN`, `PASSWORD
   accepted=True`, and `LOGIN_OK` sequence from the DPAPI-decrypted password.
5. The saved Telnet entry had `passwd_enc` length `208`; a case-sensitive
   search of `config.json` for the test password returned no match.

Sanitized fixture log excerpt:

```text
08:34:44 CONNECT ('172.16.66.163', 9045)
08:34:44 LOGIN ('172.16.66.163', 9045) 'winuser'
08:35:09 CONNECT ('172.16.66.163', 9215)
08:35:09 LOGIN ('172.16.66.163', 9215) 'winuser'
08:35:09 PASSWORD ('172.16.66.163', 9215) accepted=True
08:35:09 LOGIN_OK ('172.16.66.163', 9215)
08:35:22 INPUT ('172.16.66.163', 9215) 'PING'
08:36:21 CONNECT ('172.16.66.163', 9850)
08:36:21 LOGIN ('172.16.66.163', 9850) 'winuser'
08:36:21 PASSWORD ('172.16.66.163', 9850) accepted=True
08:36:21 LOGIN_OK ('172.16.66.163', 9850)
```

The `08:36:21` sequence is the after-restart connection. Passwords are never
logged, and the fixture log contains only the accepted/not-accepted decision.

## Screenshots

- `telnet-autologin-windows-1280x800.png`: login, password prompt, `PONG`, and
  the sidebar Telnet entry.
- `telnet-remembered-password-restart-windows-1280x800.png`: after restarting
  the app, clicking the saved entry logged in without retyping the password.
