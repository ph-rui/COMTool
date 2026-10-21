#!/usr/bin/env bash
# Shared helpers for the COMTool Avalonia Linux Xvfb E2E suite.
#
# This file is sourced by run-ui-e2e.sh. It deliberately owns only processes
# that it started itself; the caller's desktop, RDP server and any pre-existing
# X displays are left alone.

set -euo pipefail

TMP_ROOT="${TMP_ROOT:-}"
LOG_FILE="${LOG_FILE:-}"
CONFIG_ROOT="${CONFIG_ROOT:-}"
E2E_RUNTIME_DIR="${E2E_RUNTIME_DIR:-}"
APP="${APP:-}"
XVFB_BIN="${XVFB_BIN:-}"
WM_BIN="${WM_BIN:-}"
XVFB_PID="${XVFB_PID:-}"
XVFB_STARTED="${XVFB_STARTED:-0}"
WM_PID="${WM_PID:-}"
APP_PID="${APP_PID:-}"
SSHD_PID="${SSHD_PID:-}"
SSHD_BIN="${SSHD_BIN:-}"
SSH_FIXTURE_DIR="${SSH_FIXTURE_DIR:-}"
SSH_PORT="${SSH_PORT:-}"
SSH_USER="${SSH_USER:-}"
SSH_CLIENT_KEY="${SSH_CLIENT_KEY:-}"
SSH_HOST_KEY="${SSH_HOST_KEY:-}"
TELNET_PID="${TELNET_PID:-}"
TELNET_PORT="${TELNET_PORT:-}"
TELNET_FIXTURE_DIR="${TELNET_FIXTURE_DIR:-}"
TELNET_LOGIN_MARKER="${TELNET_LOGIN_MARKER:-}"
TELNET_INPUT_MARKER="${TELNET_INPUT_MARKER:-}"
TELNET_DROPPED_MARKER="${TELNET_DROPPED_MARKER:-}"
TELNET_RECONNECT_MARKER="${TELNET_RECONNECT_MARKER:-}"
DISPLAY_NUM="${DISPLAY_NUM:-}"
WINDOW_ID="${WINDOW_ID:-}"
KEEP_TEMP="${KEEP_TEMP:-0}"
DEFAULT_WINDOW_TIMEOUT="${DEFAULT_WINDOW_TIMEOUT:-20}"
E2E_CAPTURED="${E2E_CAPTURED:-0}"

# Window-relative coordinates for the 1000x650 E2E window (no decorations).
# The sidebar starts at x=16 inside BodyGrid and is 220px wide. Its first SSH
# row sits below the 24px sidebar header, 5px gap, 26px "SSH" section header
# and 2px list padding, so the 40px row spans y=98..138.
SIDEBAR_SESSION_X="${SIDEBAR_SESSION_X:-80}"
SIDEBAR_SESSION_Y="${SIDEBAR_SESSION_Y:-118}"

# The tab strip is the first 36px row of MainStack. UI05 collapses the sidebar
# first: MainStack then starts at x=32 (16px body margin + 16px content
# margin), the settings button is 27px, the tab list has a 10px left margin and
# each tab is at least 112px wide with a 2px right margin. Keeping the sidebar
# collapsed makes these coordinates stable; leaving it expanded lets auto-hide
# shift every tab 223px left while the test is running.
FIRST_TAB_X="${FIRST_TAB_X:-125}"
SECOND_TAB_X="${SECOND_TAB_X:-239}"
TAB_STRIP_Y="${TAB_STRIP_Y:-53}"
SESSIONS_TOGGLE_X="${SESSIONS_TOGGLE_X:-201}"
SESSIONS_TOGGLE_Y="${SESSIONS_TOGGLE_Y:-18}"
# Calibration probe: this point is the white sidebar list when expanded and
# the dark terminal background when collapsed.
SIDEBAR_PROBE_X="${SIDEBAR_PROBE_X:-200}"
SIDEBAR_PROBE_Y="${SIDEBAR_PROBE_Y:-300}"

log() {
    local line
    line="[e2e $(date +%H:%M:%S)] $*"
    printf '%s\n' "$line" >&2
    if [[ -n "${LOG_FILE:-}" ]]; then
        printf '%s\n' "$line" >>"$LOG_FILE" 2>/dev/null || true
    fi
}

executable_path() {
    local candidate="$1"
    if [[ "$candidate" == */* ]]; then
        [[ -x "$candidate" ]] || return 1
        printf '%s\n' "$candidate"
        return 0
    fi
    command -v -- "$candidate"
}

resolve_xvfb_bin() {
    local resolved
    if [[ -n "${XVFB_BIN:-}" ]]; then
        resolved="$(executable_path "$XVFB_BIN")" || return 1
        XVFB_BIN="$resolved"
        return 0
    fi
    resolved="$(command -v Xvfb || true)"
    [[ -n "$resolved" ]] || return 1
    XVFB_BIN="$resolved"
}

resolve_wm_bin() {
    local resolved
    if [[ -n "${WM_BIN:-}" ]]; then
        resolved="$(executable_path "$WM_BIN")" || return 1
        WM_BIN="$resolved"
        return 0
    fi
    if resolved="$(command -v openbox || true)" && [[ -n "$resolved" ]]; then
        WM_BIN="$resolved"
        return 0
    fi
    if resolved="$(command -v mutter || true)" && [[ -n "$resolved" ]]; then
        WM_BIN="$resolved"
        return 0
    fi
    return 1
}

install_hint() {
    local os_release=""
    if [[ -r /etc/os-release ]]; then
        os_release="$(. /etc/os-release 2>/dev/null; printf '%s %s' "${ID:-}" "${ID_LIKE:-}")"
    fi

    case " $os_release " in
        *debian*|*ubuntu*)
            printf '%s\n' "sudo apt-get update && sudo apt-get install -y xvfb xdotool openbox openssh-server lrzsz netcat-openbsd x11-utils imagemagick python3"
            ;;
        *fedora*|*rhel*|*centos*)
            printf '%s\n' "sudo dnf install -y xorg-x11-server-Xvfb xdotool openbox openssh-server lrzsz nmap-ncat xorg-x11-utils ImageMagick python3"
            ;;
        *arch*)
            printf '%s\n' "sudo pacman -S --needed xorg-server-xvfb xdotool openbox openssh lrzsz gnu-netcat xorg-xdpyinfo xorg-xprop imagemagick python"
            ;;
        *suse*)
            printf '%s\n' "sudo zypper install -y xvfb xdotool openbox openssh lrzsz netcat-openbsd xdpyinfo xprop ImageMagick python3"
            ;;
        *)
            printf '%s\n' "Install Xvfb, xdotool, openbox (or mutter), OpenSSH server/client, lrzsz, netcat, xdpyinfo, xprop, ImageMagick or xwd, and Python 3."
            ;;
    esac
}

require_commands() {
    local missing=()
    local command_name

    if ! resolve_xvfb_bin; then
        missing+=("Xvfb (or set XVFB_BIN)")
    fi
    if ! resolve_wm_bin; then
        missing+=("openbox or mutter (or set WM_BIN)")
    fi

    for command_name in xdotool xdpyinfo xprop timeout sshd ssh-keygen nc ss pgrep rz sz python3; do
        if ! command -v "$command_name" >/dev/null 2>&1; then
            missing+=("$command_name")
        fi
    done

    if ! command -v import >/dev/null 2>&1 && ! command -v xwd >/dev/null 2>&1; then
        missing+=("import (ImageMagick) or xwd (x11-apps)")
    fi

    if ! command -v convert >/dev/null 2>&1; then
        missing+=("convert (ImageMagick)")
    fi

    if ((${#missing[@]} > 0)); then
        printf 'Missing Linux E2E dependencies: %s\n' "${missing[*]}" >&2
        printf 'Install hint for %s:\n' "$(uname -s)" >&2
        install_hint | sed 's/^/  /' >&2
        printf 'Alternatively set XVFB_BIN=/path/to/Xvfb and WM_BIN=/path/to/openbox-or-mutter.\n' >&2
        return 1
    fi
}

# Normalize a decimal string without letting bash treat leading zeroes as
# octal. Values such as "099" must become 99, not an arithmetic error.
normalize_decimal() {
    local value="$1"
    [[ "$value" =~ ^[0-9]+$ ]] || return 1
    printf '%s\n' "$((10#$value))"
}

display_in_use() {
    local display="$1"
    local number

    number="$(normalize_decimal "${display#:}")" || return 1
    if [[ -e "/tmp/.X${number}-lock" || -e "/tmp/.X11-unix/X${number}" ]]; then
        return 0
    fi
    if timeout 2 xdpyinfo -display ":$number" >/dev/null 2>&1; then
        return 0
    fi
    return 1
}

# Pick a free display. Display :99 is deliberately never used: it is reserved
# for the user's RDP session. COMTOOL_E2E_DISPLAY / E2E_DISPLAY_START can pin a
# different range for controlled environments.
find_free_display() {
    local requested="${COMTOOL_E2E_DISPLAY:-}"
    local start="${E2E_DISPLAY_START:-100}"
    local requested_number
    local start_number
    local number

    if [[ -n "$requested" ]]; then
        if [[ ! "$requested" =~ ^:[0-9]+$ ]]; then
            log "Invalid COMTOOL_E2E_DISPLAY value: $requested"
            return 1
        fi
        if ! requested_number="$(normalize_decimal "${requested#:}")"; then
            log "Invalid COMTOOL_E2E_DISPLAY value: $requested"
            return 1
        fi
        if ((requested_number == 99)); then
            log "Refusing display :$requested_number; it belongs to the user's RDP session"
            return 1
        fi
        requested=":$requested_number"
        if display_in_use "$requested"; then
            log "Requested display $requested is already in use"
            return 1
        fi
        printf '%s\n' "$requested"
        return 0
    fi

    if ! start_number="$(normalize_decimal "$start")"; then
        log "Invalid E2E_DISPLAY_START value: $start"
        return 1
    fi

    for ((number = start_number; number < start_number + 100; number++)); do
        if ((number == 99)); then
            continue
        fi
        if ! display_in_use ":$number"; then
            printf ':%s\n' "$number"
            return 0
        fi
    done

    log "No free X display found starting at :$start_number"
    return 1
}

start_xvfb() {
    local display="$1"
    local attempt

    [[ -n "${XVFB_BIN:-}" ]] || resolve_xvfb_bin || return 1
    [[ -n "${TMP_ROOT:-}" ]] || { log "TMP_ROOT is not set"; return 1; }

    log "Starting Xvfb on $display ($XVFB_BIN)"
    : >"$TMP_ROOT/xvfb.log"
    "$XVFB_BIN" "$display" -screen 0 1280x800x24 -ac +extension RANDR -nolisten tcp \
        >>"$TMP_ROOT/xvfb.log" 2>&1 &
    XVFB_PID=$!

    for ((attempt = 0; attempt < 100; attempt++)); do
        if ! kill -0 "$XVFB_PID" 2>/dev/null; then
            log "Xvfb exited before becoming ready"
            tail -n 50 "$TMP_ROOT/xvfb.log" >&2 2>/dev/null || true
            XVFB_PID=""
            return 1
        fi
        if timeout 2 xdpyinfo -display "$display" >/dev/null 2>&1; then
            DISPLAY_NUM="$display"
            export DISPLAY="$display"
            XVFB_STARTED=1
            log "Xvfb ready on $display (pid $XVFB_PID)"
            return 0
        fi
        sleep 0.1
    done

    log "Xvfb did not become ready within 10s"
    kill "$XVFB_PID" 2>/dev/null || true
    wait "$XVFB_PID" 2>/dev/null || true
    XVFB_PID=""
    return 1
}

start_wm() {
    local display="$1"
    local wm_name
    local attempt
    local -a wm_args=()

    [[ -n "${WM_BIN:-}" ]] || resolve_wm_bin || { log "No window manager available"; return 1; }
    wm_name="$(basename -- "$WM_BIN")"

    case "$wm_name" in
        openbox)
            wm_args+=(--sm-disable)
            ;;
        mutter)
            wm_args+=(--x11 --replace)
            ;;
        *)
            if [[ -n "${WM_ARGS:-}" ]]; then
                # WM_ARGS is an explicit escape hatch for unusual WMs.
                read -r -a wm_args <<<"$WM_ARGS"
            fi
            ;;
    esac

    log "Starting window manager $wm_name on $display"
    : >"$TMP_ROOT/wm.log"
    DISPLAY="$display" "$WM_BIN" "${wm_args[@]}" >>"$TMP_ROOT/wm.log" 2>&1 &
    WM_PID=$!

    for ((attempt = 0; attempt < 100; attempt++)); do
        if ! kill -0 "$WM_PID" 2>/dev/null; then
            log "$wm_name exited before becoming ready"
            tail -n 50 "$TMP_ROOT/wm.log" >&2 2>/dev/null || true
            WM_PID=""
            return 1
        fi
        if timeout 2 xprop -display "$display" -root _NET_SUPPORTING_WM_CHECK 2>/dev/null \
            | grep -q 'window id'; then
            log "$wm_name ready on $display (pid $WM_PID)"
            return 0
        fi
        sleep 0.1
    done

    log "Warning: $wm_name is running but did not advertise _NET_SUPPORTING_WM_CHECK within 10s"
    return 0
}

# Seed a standalone version 3 Avalonia config. This intentionally lives in the
# test temp root and never touches the user's real config.
seed_empty_config() {
    local config_dir="$1"
    mkdir -p "$config_dir"
    cat >"$config_dir/config.json" <<'JSON'
{
  "version": 3,
  "skin": "light",
  "locale": "en",
  "encoding": "UTF-8",
  "serialSessions": [],
  "sshSessions": [],
  "sessionSidebar": { "visible": true, "width": 220 },
  "window": { "width": 1000, "height": 650 }
}
JSON
}

# Pick an unprivileged, currently free TCP port. The 20000..60000 range keeps
# the fixture well away from the user's existing sshd on 2222 and from
# privileged ports. sshd startup is verified separately, so a race that grabs
# the port between this probe and sshd's bind is reported as a fixture failure
# rather than silently reusing another server.
find_free_tcp_port() {
    local attempt
    local port

    for ((attempt = 0; attempt < 100; attempt++)); do
        port="$(shuf -i 20000-60000 -n 1 2>/dev/null || true)"
        if [[ ! "$port" =~ ^[0-9]+$ ]]; then
            port=$((20000 + RANDOM % 40000))
        fi
        if ((port < 1024 || port > 65535)); then
            continue
        fi
        if ! timeout 1 nc -z 127.0.0.1 "$port" >/dev/null 2>&1; then
            printf '%s\n' "$port"
            return 0
        fi
    done

    log "Could not find a free TCP port for the E2E fixture"
    return 1
}

ssh_connection_count() {
    local output

    [[ -n "${SSH_PORT:-}" ]] || { printf '0\n'; return 0; }
    output="$(ss -Htn state established "sport = :$SSH_PORT" 2>/dev/null || true)"
    printf '%s\n' "$output" | sed '/^[[:space:]]*$/d' | wc -l
}

wait_for_ssh_connections() {
    local expected="$1"
    local timeout_s="${2:-20}"
    local attempt
    local iterations=$((timeout_s * 10))
    local count=0

    for ((attempt = 0; attempt < iterations; attempt++)); do
        count="$(ssh_connection_count)"
        if ((count >= expected)); then
            return 0
        fi
        sleep 0.1
    done

    log "Expected at least $expected SSH connection(s) on port ${SSH_PORT:-?}, saw $count"
    return 1
}

# Run one ephemeral sshd owned by the current Unix user. The fixture never
# touches the user's 2222 server: it has its own port, keys, config, log and
# PidFile under TMP_ROOT, and cleanup stops only the tracked listener PID (and
# descendants it created for active sessions).
start_sshd_fixture() {
    local attempt
    local resolved

    [[ -n "${TMP_ROOT:-}" ]] || { log "TMP_ROOT is not set"; return 1; }
    resolved="$(command -v sshd || true)"
    [[ -n "$resolved" ]] || { log "sshd not found"; return 1; }
    SSHD_BIN="$resolved"
    SSH_USER="$(id -un)"
    SSH_FIXTURE_DIR="$TMP_ROOT/sshd"
    mkdir -p "$SSH_FIXTURE_DIR"
    chmod 700 "$SSH_FIXTURE_DIR"

    SSH_PORT="$(find_free_tcp_port)" || return 1
    SSH_HOST_KEY="$SSH_FIXTURE_DIR/host_key"
    SSH_CLIENT_KEY="$SSH_FIXTURE_DIR/client_key"

    log "Creating SSH fixture keys under $SSH_FIXTURE_DIR"
    ssh-keygen -q -t ed25519 -N '' -C comtool-e2e-host -f "$SSH_HOST_KEY" || return 1
    ssh-keygen -q -t ed25519 -N '' -C comtool-e2e-client -f "$SSH_CLIENT_KEY" || return 1
    cp -- "$SSH_CLIENT_KEY.pub" "$SSH_FIXTURE_DIR/authorized_keys"
    chmod 600 "$SSH_FIXTURE_DIR/authorized_keys"
    chmod 600 "$SSH_HOST_KEY" "$SSH_CLIENT_KEY"

    cat >"$SSH_FIXTURE_DIR/sshd_config" <<EOF
Port $SSH_PORT
ListenAddress 127.0.0.1
HostKey $SSH_HOST_KEY
PidFile $SSH_FIXTURE_DIR/sshd.pid
AuthorizedKeysFile $SSH_FIXTURE_DIR/authorized_keys
PubkeyAuthentication yes
PasswordAuthentication no
KbdInteractiveAuthentication no
ChallengeResponseAuthentication no
PermitRootLogin no
UsePAM no
StrictModes no
LogLevel VERBOSE
AllowUsers $SSH_USER
PrintMotd no
X11Forwarding no
EOF

    log "Starting SSH fixture on 127.0.0.1:$SSH_PORT as $SSH_USER"
    : >"$SSH_FIXTURE_DIR/sshd.log"
    "$SSHD_BIN" -D -f "$SSH_FIXTURE_DIR/sshd_config" -E "$SSH_FIXTURE_DIR/sshd.log" \
        >>"$SSH_FIXTURE_DIR/sshd.stderr.log" 2>&1 &
    SSHD_PID=$!

    for ((attempt = 0; attempt < 100; attempt++)); do
        if ! kill -0 "$SSHD_PID" 2>/dev/null; then
            log "SSH fixture exited before accepting connections"
            tail -n 50 "$SSH_FIXTURE_DIR/sshd.log" >&2 2>/dev/null || true
            tail -n 50 "$SSH_FIXTURE_DIR/sshd.stderr.log" >&2 2>/dev/null || true
            SSHD_PID=""
            return 1
        fi
        if timeout 1 nc -z 127.0.0.1 "$SSH_PORT" >/dev/null 2>&1; then
            log "SSH fixture ready on 127.0.0.1:$SSH_PORT (pid $SSHD_PID)"
            return 0
        fi
        sleep 0.1
    done

    log "SSH fixture did not listen on 127.0.0.1:$SSH_PORT within 10s"
    tail -n 50 "$SSH_FIXTURE_DIR/sshd.log" >&2 2>/dev/null || true
    stop_pid "$SSHD_PID" "sshd fixture"
    SSHD_PID=""
    return 1
}

# Start a local Telnet fixture. It deliberately does not use sshd or port
# 2222: the Python server prompts for a user name, records the auto-login,
# then records a PING sent through the terminal.
start_telnet_fixture() {
    local attempt

    [[ -n "${TMP_ROOT:-}" ]] || { log "TMP_ROOT is not set"; return 1; }
    command -v python3 >/dev/null 2>&1 || { log "python3 not found"; return 1; }
    TELNET_FIXTURE_DIR="$TMP_ROOT/telnet"
    mkdir -p "$TELNET_FIXTURE_DIR"
    TELNET_PORT="$(find_free_tcp_port)" || return 1
    TELNET_LOGIN_MARKER="$TMP_ROOT/telnet-login.ready"
    TELNET_INPUT_MARKER="$TMP_ROOT/telnet-input.ready"
    TELNET_DROPPED_MARKER="$TMP_ROOT/telnet-dropped.ready"
    TELNET_RECONNECT_MARKER="$TMP_ROOT/telnet-reconnect.ready"
    rm -f -- "$TELNET_LOGIN_MARKER" "$TELNET_INPUT_MARKER" \
        "$TELNET_DROPPED_MARKER" "$TELNET_RECONNECT_MARKER"

    cat >"$TELNET_FIXTURE_DIR/server.py" <<'PY'
import pathlib
import socket
import sys

port = int(sys.argv[1])
login_marker = pathlib.Path(sys.argv[2])
input_marker = pathlib.Path(sys.argv[3])
dropped_marker = pathlib.Path(sys.argv[4])
reconnect_marker = pathlib.Path(sys.argv[5])

logins = 0

def serve(conn):
    global logins
    conn.settimeout(60)
    conn.sendall(b"Welcome to COMTool Telnet E2E\r\nlogin: ")
    buffer = b""
    logged_in = False
    while True:
        data = conn.recv(4096)
        if not data:
            return
        buffer += data
        while b"\n" in buffer:
            line, buffer = buffer.split(b"\n", 1)
            if b"DROP" in line:
                # Hang up on the client to simulate a lost connection.
                dropped_marker.write_text("TELNET_DROPPED")
                return
            if not logged_in and b"e2e" in line:
                logins += 1
                if logins >= 2:
                    # The app reconnected and logged in again.
                    reconnect_marker.write_text("TELNET_RECONNECT_READY")
                login_marker.write_text("TELNET_E2E_READY")
                conn.sendall(b"\r\nTELNET_E2E_READY\r\n$ ")
                logged_in = True
            elif logged_in and b"PING" in line:
                input_marker.write_text("TELNET_INPUT_READY")
                conn.sendall(b"PONG\r\n$ ")

server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
server.bind(("127.0.0.1", port))
server.listen(5)
while True:
    conn, _ = server.accept()
    try:
        serve(conn)
    except Exception:
        pass
    finally:
        conn.close()
PY

    : >"$TELNET_FIXTURE_DIR/server.log"
    python3 "$TELNET_FIXTURE_DIR/server.py" "$TELNET_PORT" \
        "$TELNET_LOGIN_MARKER" "$TELNET_INPUT_MARKER" \
        "$TELNET_DROPPED_MARKER" "$TELNET_RECONNECT_MARKER" \
        >>"$TELNET_FIXTURE_DIR/server.log" 2>&1 &
    TELNET_PID=$!

    for ((attempt = 0; attempt < 100; attempt++)); do
        if ! kill -0 "$TELNET_PID" 2>/dev/null; then
            log "Telnet fixture exited before becoming ready"
            tail -n 50 "$TELNET_FIXTURE_DIR/server.log" >&2 2>/dev/null || true
            TELNET_PID=""
            return 1
        fi
        # A TCP connect probe is fine: the server ignores connections that
        # close before sending a line and keeps accepting.
        if timeout 1 nc -z 127.0.0.1 "$TELNET_PORT" >/dev/null 2>&1; then
            log "Telnet fixture ready on 127.0.0.1:$TELNET_PORT (pid $TELNET_PID)"
            return 0
        fi
        sleep 0.1
    done

    log "Telnet fixture did not listen on 127.0.0.1:$TELNET_PORT within 10s"
    tail -n 50 "$TELNET_FIXTURE_DIR/server.log" >&2 2>/dev/null || true
    stop_pid "$TELNET_PID" "telnet fixture"
    TELNET_PID=""
    return 1
}

# Seed one Telnet session with a user name but no plaintext password. The
# auto-login marker proves the app sent the user name after the prompt.
seed_telnet_config() {
    local config_dir="$1"
    local port="$2"
    local key="telnet:e2e@127.0.0.1:${port}"

    [[ -n "$config_dir" && -n "$port" ]] || return 1
    mkdir -p "$config_dir"
    cat >"$config_dir/config.json" <<EOF
{
  "version": 3,
  "skin": "light",
  "encoding": "UTF-8",
  "serialSessions": [],
  "sshSessions": [],
  "telnetSessions": [
    {
      "transport": "telnet",
      "key": "$key",
      "host": "127.0.0.1",
      "port": $port,
      "user": "e2e",
      "alias": "telnet-e2e",
      "passwd_enc": ""
    }
  ],
  "sessionSidebar": { "visible": true, "width": 220 },
  "window": { "width": 1000, "height": 650 }
}
EOF
}

# Seed one SSH session for the fixture. The private key is referenced by its
# absolute path; no plaintext password is written anywhere in the config.
seed_ssh_config() {
    local config_dir="$1"
    local port="$2"
    local key_file="$3"
    local user="${SSH_USER:-$(id -un)}"
    local key="${user}@127.0.0.1:${port}"

    [[ -n "$config_dir" && -n "$port" && -n "$key_file" ]] || return 1
    [[ -f "$key_file" ]] || { log "SSH client key not found: $key_file"; return 1; }

    mkdir -p "$config_dir"
    cat >"$config_dir/config.json" <<EOF
{
  "version": 3,
  "skin": "light",
  "encoding": "UTF-8",
  "serialSessions": [],
  "sshSessions": [
    {
      "key": "$key",
      "host": "127.0.0.1",
      "port": $port,
      "user": "$user",
      "alias": "e2e",
      "ssh_key": "$key_file",
      "passwd_enc": ""
    }
  ],
  "sshSessionsMigrated": true,
  "sessionSidebar": { "visible": false, "width": 220 },
  "window": { "width": 1000, "height": 650 }
}
EOF
}

start_app() {
    local config_dir="$1"
    local app_dir
    local home_dir
    local avalonia_dir
    local python_dir
    local runtime_dir

    [[ -n "${APP:-}" ]] || { log "APP is not set"; return 1; }
    [[ -n "${TMP_ROOT:-}" ]] || { log "TMP_ROOT is not set"; return 1; }
    [[ -n "${DISPLAY_NUM:-}" ]] || { log "DISPLAY_NUM is not set"; return 1; }

    app_dir="$(cd -- "$(dirname -- "$APP")" && pwd)"
    home_dir="$TMP_ROOT/home"
    avalonia_dir="$config_dir/avalonia"
    python_dir="$config_dir/python"
    runtime_dir="${E2E_RUNTIME_DIR:-$TMP_ROOT/runtime}"

    mkdir -p "$home_dir" "$avalonia_dir" "$python_dir" "$runtime_dir"
    chmod 700 "$runtime_dir" 2>/dev/null || true

    log "Starting application $APP"
    : >"$TMP_ROOT/app.stdout.log"
    (
        cd -- "$app_dir"
        # exec env keeps the background PID identical to the real application
        # PID, so cleanup kills the app rather than an intermediate subshell.
        exec env \
            HOME="$home_dir" \
            XDG_CONFIG_HOME="$home_dir/.config" \
            XDG_RUNTIME_DIR="$runtime_dir" \
            COMTOOL_AVALONIA_CONFIG_DIR="$avalonia_dir" \
            COMTOOL_PYTHON_CONFIG_DIR="$python_dir" \
            DISPLAY="$DISPLAY_NUM" \
            NO_AT_BRIDGE=1 \
            GSETTINGS_BACKEND=memory \
            "$APP"
    ) >>"$TMP_ROOT/app.stdout.log" 2>&1 &
    APP_PID=$!

    log "Application started (pid $APP_PID)"
}

wait_for_window() {
    local title="$1"
    local timeout_s="${2:-$DEFAULT_WINDOW_TIMEOUT}"
    local output
    local status=0
    local window

    log "Waiting up to ${timeout_s}s for window '$title'"
    output="$(timeout "${timeout_s}s" xdotool search --sync --onlyvisible --name "$title" 2>/dev/null)" || status=$?
    if ((status != 0)); then
        log "Window '$title' was not found (xdotool status $status)"
        return 1
    fi

    window="$(printf '%s\n' "$output" | sed -n '1p')"
    if [[ -z "$window" ]]; then
        log "Window '$title' search returned no window id"
        return 1
    fi

    WINDOW_ID="$window"
    printf '%s\n' "$window"
}

activate_window() {
    local window="${1:-$WINDOW_ID}"
    local attempt

    [[ -n "$window" ]] || return 1
    for ((attempt = 0; attempt < 10; attempt++)); do
        if timeout 2 xdotool windowactivate --sync "$window" >/dev/null 2>&1; then
            return 0
        fi
        sleep 0.1
    done
    return 1
}

click_window() {
    local x="$1"
    local y="$2"
    local window="${3:-$WINDOW_ID}"

    [[ -n "$window" ]] || return 1
    # Do not use --sync: moving to the coordinates the pointer already occupies
    # waits for a motion event that never arrives. X processes the ordered
    # move and click requests in sequence.
    xdotool mousemove --window "$window" "$x" "$y" || return 1
    xdotool click 1
}

double_click_window() {
    local x="$1"
    local y="$2"
    local window="${3:-$WINDOW_ID}"

    [[ -n "$window" ]] || return 1
    xdotool mousemove --window "$window" "$x" "$y" || return 1
    # Two presses inside the platform double-click interval (100ms apart).
    xdotool click --repeat 2 --delay 100 1
}

# Print "<width> <height>" for a window, normalized without leading zeroes.
read_window_dimensions() {
    local window="${1:-$WINDOW_ID}"
    local geometry
    local width
    local height

    [[ -n "$window" ]] || return 1
    geometry="$(xdotool getwindowgeometry --shell "$window" 2>/dev/null)" || return 1
    width="$(printf '%s\n' "$geometry" | sed -n 's/^WIDTH=//p' | sed -n '1p')"
    height="$(printf '%s\n' "$geometry" | sed -n 's/^HEIGHT=//p' | sed -n '1p')"
    [[ "$width" =~ ^[0-9]+$ && "$height" =~ ^[0-9]+$ ]] || return 1
    printf '%s %s\n' "$((10#$width))" "$((10#$height))"
}

# Bounded poll for a geometry change. Modes:
#   grow <width> <height>   both dimensions strictly greater than the targets
#   exact <width> <height>  both dimensions equal to the targets
wait_for_window_size() {
    local mode="$1"
    local window="$2"
    local target_width="$3"
    local target_height="$4"
    local timeout_s="${5:-5}"
    local attempt
    local iterations=$((timeout_s * 10))
    local size
    local width
    local height

    for ((attempt = 0; attempt < iterations; attempt++)); do
        if size="$(read_window_dimensions "$window")"; then
            read -r width height <<<"$size"
            case "$mode" in
                grow)
                    if ((width > target_width && height > target_height)); then
                        printf '%s\n' "$size"
                        return 0
                    fi
                    ;;
                exact)
                    if ((width == target_width && height == target_height)); then
                        printf '%s\n' "$size"
                        return 0
                    fi
                    ;;
                *)
                    return 1
                    ;;
            esac
        fi
        sleep 0.1
    done
    return 1
}

type_text() {
    xdotool type --clearmodifiers --delay 20 "$1"
}

press_enter() {
    xdotool key --clearmodifiers Return
}

clear_input_line() {
    xdotool key --clearmodifiers ctrl+u >/dev/null 2>&1 || true
}

click_sidebar_session() {
    click_window "$SIDEBAR_SESSION_X" "$SIDEBAR_SESSION_Y"
}

# Read one window-relative pixel. This is only used to choose the correct tab
# coordinates: expanding and collapsing the sidebar shifts the tab strip by
# 223px. Test PASS/FAIL decisions never use screenshots.
window_pixel_is_dark() {
    local rel_x="$1"
    local rel_y="$2"
    local geometry
    local win_x
    local win_y
    local image="$TMP_ROOT/pixel-probe.png"
    local pixel
    local hex
    local r
    local g
    local b

    geometry="$(xdotool getwindowgeometry --shell "$WINDOW_ID" 2>/dev/null)" || return 2
    win_x="$(printf '%s\n' "$geometry" | sed -n 's/^X=//p' | sed -n '1p')"
    win_y="$(printf '%s\n' "$geometry" | sed -n 's/^Y=//p' | sed -n '1p')"
    [[ "$win_x" =~ ^-?[0-9]+$ && "$win_y" =~ ^-?[0-9]+$ ]] || return 2

    capture_screenshot "$image" || return 2
    pixel="$(convert "$image" -crop "1x1+$((win_x + rel_x))+$((win_y + rel_y))" +repage txt:- 2>/dev/null \
        | sed -n 's/.*#\([0-9A-Fa-f]\{6\}\).*/\1/p' | sed -n '1p')"
    [[ "$pixel" =~ ^[0-9A-Fa-f]{6}$ ]] || return 2

    r=$((16#${pixel:0:2}))
    g=$((16#${pixel:2:2}))
    b=$((16#${pixel:4:2}))
    ((r + g + b < 300))
}

# Wait until the sidebar probe reports the collapsed (dark terminal) state.
wait_for_sidebar_hidden() {
    local timeout_s="${1:-5}"
    local attempt
    local iterations=$((timeout_s * 10))
    local status

    for ((attempt = 0; attempt < iterations; attempt++)); do
        if window_pixel_is_dark "$SIDEBAR_PROBE_X" "$SIDEBAR_PROBE_Y"; then
            return 0
        else
            status=$?
        fi
        if ((status == 2)); then
            return 2
        fi
        sleep 0.1
    done
    return 1
}

# Wait until the sidebar probe reports the expanded (white list) state.
wait_for_sidebar_visible() {
    local timeout_s="${1:-5}"
    local attempt
    local iterations=$((timeout_s * 10))
    local status

    for ((attempt = 0; attempt < iterations; attempt++)); do
        if window_pixel_is_dark "$SIDEBAR_PROBE_X" "$SIDEBAR_PROBE_Y"; then
            sleep 0.1
            continue
        else
            status=$?
        fi
        if ((status == 2)); then
            return 2
        fi
        return 0
    done
    return 1
}

# The sidebar auto-hides as soon as the pointer moves right towards the tabs,
# which shifts the whole tab strip 223px left mid-test. Collapse it explicitly
# through the title-bar sessions button so tab coordinates are deterministic.
# The probe tolerates the panel already being collapsed (auto-hide or a prior
# run) and retries a missed button click.
collapse_sidebar_for_tabs() {
    local attempt
    local probe_status

    for ((attempt = 0; attempt < 4; attempt++)); do
        if window_pixel_is_dark "$SIDEBAR_PROBE_X" "$SIDEBAR_PROBE_Y"; then
            probe_status=0
        else
            probe_status=$?
        fi
        if ((probe_status == 0)); then
            return 0
        fi
        if ((probe_status == 2)); then
            log "Could not calibrate sidebar state from a screenshot"
            return 1
        fi
        click_window "$SESSIONS_TOGGLE_X" "$SESSIONS_TOGGLE_Y" || return 1
        sleep 0.3
    done

    window_pixel_is_dark "$SIDEBAR_PROBE_X" "$SIDEBAR_PROBE_Y"
}

click_first_tab() {
    click_window "$FIRST_TAB_X" "$TAB_STRIP_Y"
}

click_second_tab() {
    click_window "$SECOND_TAB_X" "$TAB_STRIP_Y"
}

# Type a command until its marker file appears. A fresh SSH tab may not have
# attached/focused its terminal when the click returns, so a lost keystroke is
# retried after clearing the line. The marker remains the assertion; a timeout
# is always a failure. Commands used here are idempotent.
run_command_until_marker() {
    local command="$1"
    local marker="$2"
    local timeout_s="${3:-20}"
    local attempt
    local wait_attempt
    local iterations=$((timeout_s * 2))

    for ((attempt = 0; attempt < iterations; attempt++)); do
        if [[ -e "$marker" ]]; then
            return 0
        fi
        clear_input_line
        type_text "$command" || true
        press_enter || true
        for ((wait_attempt = 0; wait_attempt < 5; wait_attempt++)); do
            if [[ -e "$marker" ]]; then
                return 0
            fi
            sleep 0.1
        done
    done

    return 1
}

wait_for_file_content() {
    local path="$1"
    local expected="$2"
    local timeout_s="${3:-20}"
    local attempt
    local iterations=$((timeout_s * 10))
    local actual=""

    for ((attempt = 0; attempt < iterations; attempt++)); do
        if [[ -s "$path" ]]; then
            actual="$(cat -- "$path" 2>/dev/null || true)"
            if [[ "$actual" == "$expected" ]]; then
                return 0
            fi
        fi
        sleep 0.1
    done
    log "File $path did not contain '$expected' (last value: '${actual}')"
    return 1
}

# After a tab click, prove keystrokes reach the expected shell. This also
# warms the terminal focus before a relative-path assertion. It retries because
# Avalonia posts terminal focus asynchronously after the content host swap.
wait_for_shell_focus() {
    local expected_pid="$1"
    local tag="$2"
    local timeout_s="${3:-15}"
    local probe="$TMP_ROOT/${tag}.shell-pid"
    local attempt
    local wait_attempt
    local iterations=$((timeout_s * 2))
    local actual=""

    for ((attempt = 0; attempt < iterations; attempt++)); do
        rm -f -- "$probe"
        clear_input_line
        type_text "echo \$\$ > $probe" || true
        press_enter || true
        for ((wait_attempt = 0; wait_attempt < 5; wait_attempt++)); do
            if [[ -s "$probe" ]]; then
                actual="$(cat -- "$probe" 2>/dev/null || true)"
                if [[ "$actual" == "$expected_pid" ]]; then
                    return 0
                fi
                break
            fi
            sleep 0.1
        done
    done

    log "Keystrokes reached shell '${actual:-<none>}', expected '$expected_pid'"
    return 1
}

# Click the saved SSH row until the requested number of independent transport
# connections is live. A row may not be populated the instant the window maps,
# so a missed click is retried; this is still an assertion on the fixture's
# established sockets, not a fixed sleep.
ensure_ssh_connections() {
    local expected="$1"
    local timeout_s="${2:-20}"
    local count
    local click_attempt
    local max_clicks=$((timeout_s * 2))

    count="$(ssh_connection_count)"
    if ((count >= expected)); then
        return 0
    fi
    for ((click_attempt = 0; count < expected && click_attempt < max_clicks; click_attempt++)); do
        click_sidebar_session || true
        if wait_for_ssh_connections "$expected" 8; then
            count="$(ssh_connection_count)"
            if ((count >= expected)); then
                return 0
            fi
        fi
        count="$(ssh_connection_count)"
    done

    log "Could not open SSH connection $expected (current count: $count)"
    return 1
}

descendant_pids() {
    local parent="$1"
    local child

    for child in $(pgrep -P "$parent" 2>/dev/null || true); do
        printf '%s\n' "$child"
        descendant_pids "$child"
    done
}

wait_for_file() {
    local path="$1"
    local timeout_s="${2:-20}"
    local attempt
    local iterations=$((timeout_s * 10))

    for ((attempt = 0; attempt < iterations; attempt++)); do
        if [[ -e "$path" ]]; then
            return 0
        fi
        sleep 0.1
    done
    return 1
}

wait_for_app_exit() {
    local timeout_s="${1:-10}"
    local attempt
    local iterations=$((timeout_s * 10))

    [[ -n "${APP_PID:-}" ]] || return 0
    for ((attempt = 0; attempt < iterations; attempt++)); do
        if ! kill -0 "$APP_PID" 2>/dev/null; then
            return 0
        fi
        sleep 0.1
    done
    return 1
}

capture_screenshot() {
    local path="$1"
    local raw

    [[ -n "${DISPLAY_NUM:-}" ]] || return 1

    if command -v import >/dev/null 2>&1; then
        if DISPLAY="$DISPLAY_NUM" import -window root "$path" >/dev/null 2>&1; then
            return 0
        fi
    fi

    if command -v xwd >/dev/null 2>&1; then
        raw="${path%.*}.xwd"
        if DISPLAY="$DISPLAY_NUM" xwd -root -silent -out "$raw" >/dev/null 2>&1; then
            if command -v convert >/dev/null 2>&1 \
                && convert "$raw" "$path" >/dev/null 2>&1; then
                rm -f -- "$raw"
                return 0
            fi
            if [[ "$raw" != "$path" ]]; then
                log "Saved XWD screenshot to $raw"
            fi
            return 0
        fi
    fi

    return 1
}

capture_failure() {
    local test_id="${1:-unknown}"
    local reason="${2:-}"
    local app_log

    [[ -n "${TMP_ROOT:-}" ]] || return 0
    if [[ "${E2E_CAPTURED:-0}" == "1" ]]; then
        return 0
    fi
    E2E_CAPTURED=1

    mkdir -p "$TMP_ROOT"
    app_log="${CONFIG_ROOT:-}/avalonia/run.log"

    {
        printf '\n===== FAILURE EVIDENCE =====\n'
        printf 'Test: %s\n' "$test_id"
        printf 'Reason: %s\n' "$reason"
        printf 'Display: %s\n' "${DISPLAY_NUM:-<none>}"
        printf 'Time: %s\n' "$(date -Is)"
        printf '\n--- process snapshot ---\n'
        ps -eo pid,ppid,stat,etime,cmd --sort=pid \
            | grep -E 'Xvfb|mutter|openbox|COMTool\.App|sshd' \
            | grep -v grep || true
        printf '\n--- window tree ---\n'
        if [[ -n "${DISPLAY_NUM:-}" ]] && command -v xwininfo >/dev/null 2>&1; then
            DISPLAY="$DISPLAY_NUM" xwininfo -root -tree 2>&1 || true
        fi
        printf '\n--- application stdout/stderr (tail) ---\n'
        tail -n 300 "$TMP_ROOT/app.stdout.log" 2>/dev/null || true
        printf '\n--- application run.log (tail) ---\n'
        tail -n 300 "$app_log" 2>/dev/null || true
    } >>"${LOG_FILE:-$TMP_ROOT/run.log}" 2>&1 || true

    if capture_screenshot "$TMP_ROOT/failure.png"; then
        log "Failure screenshot: $TMP_ROOT/failure.png"
    else
        log "Could not capture a failure screenshot"
    fi
    log "Failure evidence saved in ${LOG_FILE:-$TMP_ROOT/run.log}"
}

pass() {
    local test_id="$1"
    log "PASS $test_id"
    printf 'PASS %s\n' "$test_id"
}

fail() {
    local test_id="$1"
    shift
    local reason="${*:-unspecified failure}"

    capture_failure "$test_id" "$reason"
    log "FAIL $test_id: $reason"
    printf 'FAIL %s: %s\n' "$test_id" "$reason"
    exit 1
}

stop_pid() {
    local pid="$1"
    local label="$2"
    local attempt

    [[ -n "$pid" ]] || return 0
    if kill -0 "$pid" 2>/dev/null; then
        log "Stopping $label (pid $pid)"
        kill "$pid" 2>/dev/null || true
        for ((attempt = 0; attempt < 50; attempt++)); do
            if ! kill -0 "$pid" 2>/dev/null; then
                break
            fi
            sleep 0.1
        done
        if kill -0 "$pid" 2>/dev/null; then
            kill -KILL "$pid" 2>/dev/null || true
        fi
    fi
    wait "$pid" 2>/dev/null || true
}

stop_app() {
    stop_pid "${APP_PID:-}" "COMTool.App"
    APP_PID=""
}

stop_telnet_fixture() {
    stop_pid "${TELNET_PID:-}" "telnet fixture"
    TELNET_PID=""
}

stop_sshd() {
    local pid="${SSHD_PID:-}"
    local children=""

    if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
        # Collect this fixture's session children before the listener exits and
        # they are reparented. Never search by name: the user's own sshd on
        # 2222 is a different process tree.
        children="$(descendant_pids "$pid" || true)"
    fi

    stop_pid "$pid" "sshd fixture"
    if [[ -n "$children" ]]; then
        while read -r child; do
            [[ -n "$child" ]] || continue
            stop_pid "$child" "sshd fixture session"
        done <<<"$children"
    fi
    SSHD_PID=""
}

stop_wm() {
    stop_pid "${WM_PID:-}" "window manager"
    WM_PID=""
}

stop_xvfb() {
    local display="${DISPLAY_NUM:-}"
    local started="${XVFB_STARTED:-0}"
    local number

    stop_pid "${XVFB_PID:-}" "Xvfb"
    XVFB_PID=""

    # Only remove display files when this run launched Xvfb itself. A failed
    # startup or a display owned by another desktop/server must not be touched.
    if [[ "$started" == "1" && -n "$display" ]]; then
        if number="$(normalize_decimal "${display#:}")"; then
            rm -f -- "/tmp/.X${number}-lock" "/tmp/.X11-unix/X${number}" 2>/dev/null || true
        fi
    fi
    XVFB_STARTED=0
    DISPLAY_NUM=""
    WINDOW_ID=""
}

cleanup() {
    local status=$?

    trap - EXIT INT TERM HUP

    if ((status != 0)) && [[ -n "${TMP_ROOT:-}" ]] \
        && [[ "${E2E_CAPTURED:-0}" != "1" ]]; then
        capture_failure "unexpected" "command failed with exit status $status"
    fi

    stop_app || true
    stop_telnet_fixture || true
    stop_sshd || true
    stop_wm || true
    stop_xvfb || true

    if [[ -n "${TMP_ROOT:-}" ]]; then
        if ((status == 0)) && [[ "${KEEP_TEMP:-0}" != "1" ]]; then
            rm -rf -- "$TMP_ROOT"
        else
            printf 'E2E artifacts kept at %s\n' "$TMP_ROOT" >&2
        fi
    fi

    exit "$status"
}
