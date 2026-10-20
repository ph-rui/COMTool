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
DISPLAY_NUM="${DISPLAY_NUM:-}"
WINDOW_ID="${WINDOW_ID:-}"
KEEP_TEMP="${KEEP_TEMP:-0}"
DEFAULT_WINDOW_TIMEOUT="${DEFAULT_WINDOW_TIMEOUT:-20}"
E2E_CAPTURED="${E2E_CAPTURED:-0}"

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
            printf '%s\n' "sudo apt-get update && sudo apt-get install -y xvfb xdotool openbox openssh-server lrzsz netcat-openbsd x11-utils imagemagick"
            ;;
        *fedora*|*rhel*|*centos*)
            printf '%s\n' "sudo dnf install -y xorg-x11-server-Xvfb xdotool openbox openssh-server lrzsz nmap-ncat xorg-x11-utils ImageMagick"
            ;;
        *arch*)
            printf '%s\n' "sudo pacman -S --needed xorg-server-xvfb xdotool openbox openssh lrzsz gnu-netcat xorg-xdpyinfo xorg-xprop imagemagick"
            ;;
        *suse*)
            printf '%s\n' "sudo zypper install -y xvfb xdotool openbox openssh lrzsz netcat-openbsd xdpyinfo xprop ImageMagick"
            ;;
        *)
            printf '%s\n' "Install Xvfb, xdotool, openbox (or mutter), OpenSSH server/client, lrzsz, netcat, xdpyinfo, xprop, and ImageMagick or xwd."
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

    for command_name in xdotool xdpyinfo xprop timeout sshd ssh-keygen nc rz sz; do
        if ! command -v "$command_name" >/dev/null 2>&1; then
            missing+=("$command_name")
        fi
    done

    if ! command -v import >/dev/null 2>&1 && ! command -v xwd >/dev/null 2>&1; then
        missing+=("import (ImageMagick) or xwd (x11-apps)")
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
    xdotool mousemove --window "$window" --sync "$x" "$y" click 1
}

type_text() {
    xdotool type --clearmodifiers --delay 20 "$1"
}

press_enter() {
    xdotool key --clearmodifiers Return
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

stop_sshd() {
    stop_pid "${SSHD_PID:-}" "sshd"
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
