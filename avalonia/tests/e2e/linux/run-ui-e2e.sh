#!/usr/bin/env bash
# COMTool Avalonia Linux Xvfb end-to-end test entry point.
#
# Output contract consumed by COMTool.TestGate:
#   PASS <TestId>
#   FAIL <TestId>: <reason>
#
# Exit status is 0 only when every selected test passes.

set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib.sh
source "$SCRIPT_DIR/lib.sh"

APP=""
FILTER=""
KEEP_TEMP=0
WINDOW_TITLE="COMTool v0.01"
E2E_TESTS=(
    APP01_SmokeLaunchE2E
)

usage() {
    cat <<EOF
Usage: $0 --app <path> [--filter <regex>] [--keep]

  --app <path>       Avalonia application executable to test.
  --filter <regex>   Run only test ids matching the Bash regex.
  --keep             Keep the temporary HOME/config/artifact directory even
                     when the run succeeds.
  -h, --help         Show this help.

Environment:
  XVFB_BIN             Xvfb executable (default: Xvfb from PATH).
  WM_BIN               openbox or mutter (default: first one from PATH).
  E2E_DISPLAY_START    First display number to try (default: 100).
  COMTOOL_E2E_DISPLAY  Explicit free display, for example :100.
  TMPDIR               Parent directory for the temporary test root.
EOF
}

matches_filter() {
    local test_id="$1"
    [[ -z "$FILTER" ]] && return 0
    [[ "$test_id" =~ $FILTER ]]
}

while (($# > 0)); do
    case "$1" in
        --app)
            (($# >= 2)) || { printf 'Missing value for --app\n' >&2; usage >&2; exit 2; }
            APP="$2"
            shift 2
            ;;
        --app=*)
            APP="${1#*=}"
            shift
            ;;
        --filter)
            (($# >= 2)) || { printf 'Missing value for --filter\n' >&2; usage >&2; exit 2; }
            FILTER="$2"
            shift 2
            ;;
        --filter=*)
            FILTER="${1#*=}"
            shift
            ;;
        --keep)
            # Read by cleanup() in lib.sh.
            # shellcheck disable=SC2034
            KEEP_TEMP=1
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            printf 'Unknown argument: %s\n' "$1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

if [[ -z "$APP" ]]; then
    printf 'Missing required --app <path>\n' >&2
    usage >&2
    exit 2
fi
if [[ ! -f "$APP" ]]; then
    printf 'Application not found: %s\n' "$APP" >&2
    exit 2
fi
if [[ ! -x "$APP" ]]; then
    printf 'Application is not executable: %s\n' "$APP" >&2
    exit 2
fi
APP="$(cd -- "$(dirname -- "$APP")" && pwd)/$(basename -- "$APP")"

selected=0
for test_id in "${E2E_TESTS[@]}"; do
    if matches_filter "$test_id"; then
        selected=$((selected + 1))
    fi
done
if ((selected == 0)); then
    printf 'FAIL filter: no E2E test matched %s\n' "$FILTER" >&2
    exit 1
fi

if ! require_commands; then
    exit 1
fi

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/comtool-e2e.XXXXXX")"
LOG_FILE="$TMP_ROOT/run.log"
CONFIG_ROOT="$TMP_ROOT/config"
E2E_RUNTIME_DIR="$TMP_ROOT/runtime"
: >"$LOG_FILE"
mkdir -p "$TMP_ROOT/home" "$E2E_RUNTIME_DIR" "$CONFIG_ROOT/avalonia" "$CONFIG_ROOT/python"
chmod 700 "$E2E_RUNTIME_DIR" 2>/dev/null || true

trap cleanup EXIT
trap 'exit 130' INT TERM HUP

log "Temporary E2E root: $TMP_ROOT"
log "Application under test: $APP"

DISPLAY_NUM="$(find_free_display)" || fail "APP01_SmokeLaunchE2E" "no free X display available"
log "Selected free display $DISPLAY_NUM"

if ! start_xvfb "$DISPLAY_NUM"; then
    fail "APP01_SmokeLaunchE2E" "Xvfb failed to start on $DISPLAY_NUM (see $TMP_ROOT/xvfb.log)"
fi

if ! start_wm "$DISPLAY_NUM"; then
    fail "APP01_SmokeLaunchE2E" "window manager failed to start on $DISPLAY_NUM (see $TMP_ROOT/wm.log)"
fi

seed_empty_config "$CONFIG_ROOT/avalonia"

if ! start_app "$CONFIG_ROOT"; then
    fail "APP01_SmokeLaunchE2E" "application failed to launch"
fi

run_app01_smoke() {
    local test_id="APP01_SmokeLaunchE2E"
    local geometry
    local width
    local height
    local close_x

    log "Running $test_id"
    if ! WINDOW_ID="$(wait_for_window "$WINDOW_TITLE" 20)"; then
        fail "$test_id" "window '$WINDOW_TITLE' did not appear within 20s"
    fi

    if ! kill -0 "$APP_PID" 2>/dev/null; then
        fail "$test_id" "application exited before the window check completed"
    fi

    if ! activate_window "$WINDOW_ID"; then
        fail "$test_id" "could not activate window $WINDOW_ID"
    fi

    if ! capture_screenshot "$TMP_ROOT/smoke.png"; then
        fail "$test_id" "could not capture the smoke screenshot"
    fi
    log "Smoke screenshot: $TMP_ROOT/smoke.png"

    if ! geometry="$(xdotool getwindowgeometry --shell "$WINDOW_ID" 2>/dev/null)"; then
        fail "$test_id" "could not read the window geometry"
    fi
    width="$(printf '%s\n' "$geometry" | sed -n 's/^WIDTH=//p' | sed -n '1p')"
    height="$(printf '%s\n' "$geometry" | sed -n 's/^HEIGHT=//p' | sed -n '1p')"
    if [[ ! "$width" =~ ^[0-9]+$ || ! "$height" =~ ^[0-9]+$ ]]; then
        fail "$test_id" "invalid window geometry: ${width}x${height}"
    fi
    width=$((10#$width))
    height=$((10#$height))
    if ((width <= 0 || height <= 0)); then
        fail "$test_id" "invalid window geometry: ${width}x${height}"
    fi
    log "Window geometry: ${width}x${height}"

    # The window uses WindowDecorations=None. The close button is the final
    # 35px-wide title-bar column, so its center is at (width - 17, 17).
    close_x=$((width - 17))
    if ! click_window "$close_x" 17; then
        fail "$test_id" "could not click the title-bar close button at ($close_x,17)"
    fi

    if ! wait_for_app_exit 10; then
        fail "$test_id" "application did not exit within 10s of clicking close"
    fi
    wait "$APP_PID" 2>/dev/null || true
    APP_PID=""

    log "Application exited cleanly after the close click"
    pass "$test_id"
}

if matches_filter "APP01_SmokeLaunchE2E"; then
    run_app01_smoke
fi

exit 0
