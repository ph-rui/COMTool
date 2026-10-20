# COMTool Avalonia Telnet Session Design

## Goal

Add the first post-session-selector MobaXterm session type: a minimal usable
Telnet client with optional prompt-driven auto-login.

## Scope

Included:

- Telnet client session with host, port (default 23), user, alias, optional
  password, and remember-password.
- Telnet IAC negotiation for TTYPE, NAWS, SGA, ECHO, and BINARY; LINEMODE is
  refused.
- Terminal send/receive through the existing `PtyStreamConnection` and
  terminal control.
- Prompt-driven auto-login for user name and password.
- Saved `telnetSessions` in the Avalonia-only config, sidebar section, new
  session menu entry, settings page, remove/share/load support.
- No plaintext password persistence; only the existing DPAPI `passwd_enc`
  mechanism. On non-Windows platforms the password remains process-memory only,
  matching the SSH implementation.

Excluded from this phase:

- Automatic reconnect.
- TLS or proxy support.
- Login macros or arbitrary expect scripts.
- Sending credentials before a recognizable prompt.
- Python-side config or plugin changes.

## Connection Flow

`TelnetConnection.ConnectAsync` opens a `TcpClient`, creates a `TelnetStream`,
sends the initial handshake, and starts a receive loop. Decoded terminal bytes
are written to a `ByteQueueStream` consumed by `PtyStreamConnection`. Terminal
writes go through a write stream that applies Telnet escaping and newline
normalization. Remote close calls `Pty.Close()`, which raises the normal
`ProcessExited` event and leaves the tab with a `Connection lose` status.

## Negotiation Rules

Initial client negotiation:

- `IAC WILL TTYPE`
- `IAC WILL NAWS`
- `IAC WILL SGA`
- `IAC DO SGA`

Accepted local options: TTYPE, NAWS, SGA, BINARY. Accepted remote options:
ECHO, SGA, BINARY. Unsupported options are refused with WONT/DONT. TERMINAL-TYPE
IS is `XTERM-256COLOR`. NAWS is sent when the server enables it and on every
terminal resize. Outgoing data escapes `0xFF` as `0xFF 0xFF`; in non-binary
mode Enter/newlines are normalized to `CR LF`.

## Auto-Login

Auto-login is enabled when the saved user name is non-empty. The connection
observes decoded output, strips ANSI control sequences, and keeps a bounded
tail. When a prompt is recognized at the end of the tail:

- User prompt: `login:`, `username:`, `user:`, or `login as:`, with ASCII or
  full-width colon.
- Password prompt: `password:` or `passwd:`, optionally followed by
  `for <name>:`.

The user name is sent at most once. A non-empty password is sent at most once,
after the user name has been sent and a password prompt appears. If no prompt
appears, nothing is sent and the user can type manually. Passwords are never
logged.

## Configuration

New `TelnetSession` records are stored under `telnetSessions` in the Avalonia
config. The config version remains 3. The stable key is
`telnet:{user}@{normalized-host}:{port}`. Display name prefers `alias`, then
host. Detail is `user@host:port`, or `host:port` when user is empty.

The share/load flow includes Telnet but a shared payload never contains
`passwd_enc`.

## UI

The `+` menu and settings type selector use the order:

`Serial`, `SSH`, `Telnet`, `TCP`, `Local terminal`.

The Telnet settings panel contains Host, Port, User, Alias, Password, Remember
password, and OPEN. The sidebar gets a `Telnet` section with connected-state
coloring. Clicking a saved Telnet session opens an independent connection.
Removing a sidebar session removes its config entry and in-memory password.

## Tests

- Core: key normalization, deduplication, password preservation, share payload
  sanitization, config round-trip.
- Connections: loopback fake Telnet server covering handshake, TTYPE, NAWS,
  data and newline translation, escaped IAC, auto-login one-shot behavior, and
  close/EOF.
- App headless: menu contents, settings preselection, settings save + route,
  sidebar state, remove behavior.
- Linux E2E: seeded Telnet session against a local fake server, verifying the
  session opens and the user name auto-login marker is written.
