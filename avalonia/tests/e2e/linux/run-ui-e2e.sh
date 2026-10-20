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
    UI01_TitleBarDoubleClickMaximizeE2E
    UI02_SidebarExpandsOnLaunchE2E
    E2E01_OpenSshSession
    SSH06_MultipleConnectionsE2E
    UI05_TabSwitchTypingBothDirections
    TERM15_DoubleClickMaximizeE2E
)
SSH_TEST_IDS=(
    UI01_TitleBarDoubleClickMaximizeE2E
    UI02_SidebarExpandsOnLaunchE2E
    E2E01_OpenSshSession
    SSH06_MultipleConnectionsE2E
    UI05_TabSwitchTypingBothDirections
    TERM15_DoubleClickMaximizeE2E
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

# Create the diagnostics root before validating the app path so that even a
# launch-configuration failure leaves a run.log and a printed artifact path.
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

selected=0
PRIMARY_TEST=""
SSH_TEST_ID=""
for test_id in "${E2E_TESTS[@]}"; do
    if matches_filter "$test_id"; then
        selected=$((selected + 1))
        if [[ -z "$PRIMARY_TEST" ]]; then
            PRIMARY_TEST="$test_id"
        fi
    fi
done
for test_id in "${SSH_TEST_IDS[@]}"; do
    if matches_filter "$test_id"; then
        SSH_TEST_ID="$test_id"
        break
    fi
done
if ((selected == 0)); then
    printf 'FAIL filter: no E2E test matched %s\n' "$FILTER" >&2
    exit 1
fi

if [[ ! -f "$APP" ]]; then
    fail "$PRIMARY_TEST" "Application not found: $APP"
fi
if [[ ! -x "$APP" ]]; then
    fail "$PRIMARY_TEST" "Application is not executable: $APP"
fi
APP="$(cd -- "$(dirname -- "$APP")" && pwd)/$(basename -- "$APP")"

if ! require_commands; then
    fail "$PRIMARY_TEST" "required Linux E2E dependencies are missing"
fi

log "Application under test: $APP"

DISPLAY_NUM="$(find_free_display)" || fail "$PRIMARY_TEST" "no free X display available"
log "Selected free display $DISPLAY_NUM"

if ! start_xvfb "$DISPLAY_NUM"; then
    fail "$PRIMARY_TEST" "Xvfb failed to start on $DISPLAY_NUM (see $TMP_ROOT/xvfb.log)"
fi

if ! start_wm "$DISPLAY_NUM"; then
    fail "$PRIMARY_TEST" "window manager failed to start on $DISPLAY_NUM (see $TMP_ROOT/wm.log)"
fi

SHELL_A_PID_FILE="$TMP_ROOT/shell-a.pid"
SHELL_B_PID_FILE="$TMP_ROOT/shell-b.pid"
SHELL_A_PID=""
SHELL_B_PID=""
DIR_A="$TMP_ROOT/dir-a"
DIR_B="$TMP_ROOT/dir-b"

wait_for_comtool_window() {
    local test_id="$1"

    if ! WINDOW_ID="$(wait_for_window "$WINDOW_TITLE" 20)"; then
        fail "$test_id" "window '$WINDOW_TITLE' did not appear within 20s"
    fi

    if ! kill -0 "$APP_PID" 2>/dev/null; then
        fail "$test_id" "application exited before the window check completed"
    fi

    if ! activate_window "$WINDOW_ID"; then
        fail "$test_id" "could not activate window $WINDOW_ID"
    fi
}

close_app_window() {
    local test_id="$1"
    local geometry
    local width
    local close_x

    [[ -n "${WINDOW_ID:-}" ]] || { stop_app; return 0; }
    if ! geometry="$(xdotool getwindowgeometry --shell "$WINDOW_ID" 2>/dev/null)"; then
        log "$test_id: could not read geometry before close; terminating the app"
        stop_app
        return 0
    fi
    width="$(printf '%s\n' "$geometry" | sed -n 's/^WIDTH=//p' | sed -n '1p')"
    if [[ ! "$width" =~ ^[0-9]+$ ]]; then
        log "$test_id: invalid width before close; terminating the app"
        stop_app
        return 0
    fi
    width=$((10#$width))
    close_x=$((width - 17))
    if ! click_window "$close_x" 17; then
        log "$test_id: could not click the close button; terminating the app"
        stop_app
        return 0
    fi

    if wait_for_app_exit 10; then
        wait "$APP_PID" 2>/dev/null || true
        APP_PID=""
        log "Application exited cleanly after the close click"
    else
        log "$test_id: application did not exit after the close click; terminating it"
        stop_app
    fi
}

run_app01_smoke() {
    local test_id="APP01_SmokeLaunchE2E"
    local geometry
    local width
    local height
    local close_x

    log "Running $test_id"
    wait_for_comtool_window "$test_id"

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

read_pid_file() {
    local path="$1"
    [[ -s "$path" ]] || return 1
    cat -- "$path"
}

ensure_first_shell() {
    local test_id="$1"

    if ! ensure_ssh_connections 1 20; then
        fail "$test_id" "clicking the sidebar SSH row did not open a connection"
    fi

    if [[ -s "$SHELL_A_PID_FILE" ]]; then
        SHELL_A_PID="$(read_pid_file "$SHELL_A_PID_FILE")"
        [[ -n "$SHELL_A_PID" ]] && return 0
    fi

    if ! run_command_until_marker \
        "echo \$\$ > $SHELL_A_PID_FILE && touch $SHELL_A_PID_FILE.ready" \
        "$SHELL_A_PID_FILE.ready" 20; then
        fail "$test_id" "first SSH shell did not accept the PID marker command"
    fi
    SHELL_A_PID="$(read_pid_file "$SHELL_A_PID_FILE")" \
        || fail "$test_id" "first SSH shell PID marker is empty"
    [[ -n "$SHELL_A_PID" ]] || fail "$test_id" "first SSH shell PID marker is empty"
}

ensure_second_shell() {
    local test_id="$1"
    local attempt
    local candidate

    if ! ensure_ssh_connections 2 20; then
        fail "$test_id" "clicking the SSH row a second time did not open a second connection"
    fi

    for ((attempt = 0; attempt < 40; attempt++)); do
        if [[ -s "$SHELL_B_PID_FILE" ]]; then
            candidate="$(read_pid_file "$SHELL_B_PID_FILE")"
            if [[ -n "$candidate" && "$candidate" != "$SHELL_A_PID" ]]; then
                SHELL_B_PID="$candidate"
                return 0
            fi
        fi
        rm -f -- "$SHELL_B_PID_FILE" "$SHELL_B_PID_FILE.ready"
        run_command_until_marker \
            "echo \$\$ > $SHELL_B_PID_FILE && touch $SHELL_B_PID_FILE.ready" \
            "$SHELL_B_PID_FILE.ready" 2 || true
        if [[ -s "$SHELL_B_PID_FILE" ]]; then
            candidate="$(read_pid_file "$SHELL_B_PID_FILE")"
            if [[ -n "$candidate" && "$candidate" != "$SHELL_A_PID" ]]; then
                SHELL_B_PID="$candidate"
                return 0
            fi
        fi
    done

    fail "$test_id" "second SSH shell did not get an independent shell PID (first: ${SHELL_A_PID:-unknown})"
}

run_ui02_sidebar_expands() {
    local test_id="UI02_SidebarExpandsOnLaunchE2E"
    local config="$CONFIG_ROOT/avalonia/config.json"

    log "Running $test_id"
    if ! grep -q '"visible": false' "$config" 2>/dev/null; then
        fail "$test_id" "seeded config did not request a collapsed sidebar"
    fi

    ensure_first_shell "$test_id"

    if ! run_command_until_marker \
        "printf UI02_READY > $TMP_ROOT/ui02-ready" \
        "$TMP_ROOT/ui02-ready" 20; then
        fail "$test_id" "the first SSH tab did not accept keyboard input"
    fi
    if ! wait_for_file_content "$TMP_ROOT/ui02-ready" "UI02_READY" 5; then
        fail "$test_id" "first SSH tab readiness marker had the wrong contents"
    fi

    pass "$test_id"
}

run_e2e01_open_ssh() {
    local test_id="E2E01_OpenSshSession"
    local marker="$TMP_ROOT/e2e01"

    log "Running $test_id"
    ensure_first_shell "$test_id"

    if ! run_command_until_marker \
        "printf E2E01_READY > $marker" \
        "$marker" 20; then
        fail "$test_id" "SSH shell did not create $marker"
    fi
    if ! wait_for_file_content "$marker" "E2E01_READY" 5; then
        fail "$test_id" "marker $marker did not contain E2E01_READY"
    fi

    pass "$test_id"
}

run_ssh06_multiple_connections() {
    local test_id="SSH06_MultipleConnectionsE2E"
    local shell_a
    local shell_b

    log "Running $test_id"
    ensure_first_shell "$test_id"
    ensure_second_shell "$test_id"

    [[ -s "$SHELL_A_PID_FILE" ]] || fail "$test_id" "$SHELL_A_PID_FILE is missing"
    [[ -s "$SHELL_B_PID_FILE" ]] || fail "$test_id" "$SHELL_B_PID_FILE is missing"
    shell_a="$(read_pid_file "$SHELL_A_PID_FILE")"
    shell_b="$(read_pid_file "$SHELL_B_PID_FILE")"
    [[ -n "$shell_a" ]] || fail "$test_id" "first shell PID is empty"
    [[ -n "$shell_b" ]] || fail "$test_id" "second shell PID is empty"
    [[ "$shell_a" != "$shell_b" ]] \
        || fail "$test_id" "both sidebar clicks reused shell PID $shell_a"

    pass "$test_id"
}

run_ui05_tab_switch() {
    local test_id="UI05_TabSwitchTypingBothDirections"
    local shell_pid

    log "Running $test_id"
    ensure_first_shell "$test_id"
    ensure_second_shell "$test_id"

    mkdir -p "$DIR_A" "$DIR_B"
    rm -f -- "$DIR_A"/* "$DIR_B"/* 2>/dev/null || true
    collapse_sidebar_for_tabs \
        || fail "$test_id" "could not collapse the sidebar before tab switching"

    click_first_tab || fail "$test_id" "could not click the first tab"
    wait_for_shell_focus "$SHELL_A_PID" "ui05-focus-a" 15 \
        || fail "$test_id" "first tab did not receive keyboard focus (expected shell $SHELL_A_PID)"
    if ! run_command_until_marker \
        "cd $DIR_A && echo \$\$ > $DIR_A/shell.pid && touch $DIR_A/setup.ready" \
        "$DIR_A/setup.ready" 15; then
        fail "$test_id" "first tab did not accept the working-directory setup"
    fi
    shell_pid="$(read_pid_file "$DIR_A/shell.pid")"
    [[ "$shell_pid" == "$SHELL_A_PID" ]] \
        || fail "$test_id" "first tab setup ran in shell ${shell_pid:-unknown}, expected $SHELL_A_PID"

    click_second_tab || fail "$test_id" "could not click the second tab"
    wait_for_shell_focus "$SHELL_B_PID" "ui05-focus-b" 15 \
        || fail "$test_id" "second tab did not receive keyboard focus (expected shell $SHELL_B_PID)"
    if ! run_command_until_marker \
        "cd $DIR_B && echo \$\$ > $DIR_B/shell.pid && touch $DIR_B/setup.ready" \
        "$DIR_B/setup.ready" 15; then
        fail "$test_id" "second tab did not accept the working-directory setup"
    fi
    shell_pid="$(read_pid_file "$DIR_B/shell.pid")"
    [[ "$shell_pid" == "$SHELL_B_PID" ]] \
        || fail "$test_id" "second tab setup ran in shell ${shell_pid:-unknown}, expected $SHELL_B_PID"

    # Do not click inside the terminal: after each tab click the app must
    # restore terminal focus itself. Relative marker names prove the keystroke
    # reached the shell whose working directory matches that tab.
    click_first_tab || fail "$test_id" "could not click the first tab"
    wait_for_shell_focus "$SHELL_A_PID" "ui05-switch-a1" 15 \
        || fail "$test_id" "switch to first tab lost terminal focus"
    if ! run_command_until_marker "touch after-switch-1" "$DIR_A/after-switch-1" 15; then
        fail "$test_id" "touch after-switch-1 did not create $DIR_A/after-switch-1"
    fi
    [[ ! -e "$DIR_B/after-switch-1" ]] \
        || fail "$test_id" "after-switch-1 appeared in the second tab's directory"

    click_second_tab || fail "$test_id" "could not click the second tab"
    wait_for_shell_focus "$SHELL_B_PID" "ui05-switch-b2" 15 \
        || fail "$test_id" "switch to second tab lost terminal focus"
    if ! run_command_until_marker "touch after-switch-2" "$DIR_B/after-switch-2" 15; then
        fail "$test_id" "touch after-switch-2 did not create $DIR_B/after-switch-2"
    fi
    [[ ! -e "$DIR_A/after-switch-2" ]] \
        || fail "$test_id" "after-switch-2 appeared in the first tab's directory"

    click_first_tab || fail "$test_id" "could not click the first tab"
    wait_for_shell_focus "$SHELL_A_PID" "ui05-switch-a3" 15 \
        || fail "$test_id" "second switch to first tab lost terminal focus"
    if ! run_command_until_marker "touch after-switch-3" "$DIR_A/after-switch-3" 15; then
        fail "$test_id" "touch after-switch-3 did not create $DIR_A/after-switch-3"
    fi
    [[ ! -e "$DIR_B/after-switch-3" ]] \
        || fail "$test_id" "after-switch-3 appeared in the second tab's directory"

    pass "$test_id"
}

run_ui01_title_bar_double_click_maximize() {
    local test_id="UI01_TitleBarDoubleClickMaximizeE2E"
    local before
    local before_w
    local before_h
    local maximized
    local max_w
    local max_h
    local restored
    local actual

    log "Running $test_id"
    if ! before="$(read_window_dimensions "$WINDOW_ID")"; then
        fail "$test_id" "could not read window geometry before maximize"
    fi
    read -r before_w before_h <<<"$before"
    log "$test_id: geometry before maximize: ${before_w}x${before_h}"

    if ! double_click_window 600 17; then
        fail "$test_id" "could not double-click the title bar"
    fi
    if ! maximized="$(wait_for_window_size grow "$WINDOW_ID" "$before_w" "$before_h" 5)"; then
        actual="$(read_window_dimensions "$WINDOW_ID" 2>/dev/null || printf 'unknown')"
        fail "$test_id" "title-bar double-click did not maximize (before ${before_w}x${before_h}, now $actual)"
    fi
    read -r max_w max_h <<<"$maximized"
    log "$test_id: geometry after maximize: ${max_w}x${max_h}"

    if ! double_click_window 600 17; then
        fail "$test_id" "could not double-click the title bar again to restore"
    fi
    if ! restored="$(wait_for_window_size exact "$WINDOW_ID" "$before_w" "$before_h" 5)"; then
        actual="$(read_window_dimensions "$WINDOW_ID" 2>/dev/null || printf 'unknown')"
        fail "$test_id" "second title-bar double-click did not restore ${before_w}x${before_h} (now $actual)"
    fi
    log "$test_id: geometry after restore: ${restored}"

    pass "$test_id"
}

run_term15_double_click_maximize() {
    local test_id="TERM15_DoubleClickMaximizeE2E"
    local before
    local before_w
    local before_h
    local maximized
    local max_w
    local max_h
    local restored
    local restored_w
    local restored_h
    local actual

    log "Running $test_id"
    # Reuse the SSH fixture session. ensure_first_shell types into the current
    # terminal, which proves a terminal tab is selected without clicking the
    # sidebar or tab strip again.
    ensure_first_shell "$test_id"

    wait_for_shell_focus "$SHELL_A_PID" "term15-focus-a" 15 \
        || fail "$test_id" "first tab did not receive keyboard focus before maximize"

    if ! before="$(read_window_dimensions "$WINDOW_ID")"; then
        fail "$test_id" "could not read window geometry before maximize"
    fi
    read -r before_w before_h <<<"$before"
    log "$test_id: geometry before maximize: ${before_w}x${before_h}"

    if ! double_click_window 600 300; then
        fail "$test_id" "could not double-click the terminal center at (600,300)"
    fi
    if ! maximized="$(wait_for_window_size grow "$WINDOW_ID" "$before_w" "$before_h" 5)"; then
        actual="$(read_window_dimensions "$WINDOW_ID" 2>/dev/null || printf 'unknown')"
        fail "$test_id" "double-click did not maximize the window (before ${before_w}x${before_h}, now $actual)"
    fi
    read -r max_w max_h <<<"$maximized"
    log "$test_id: geometry after maximize: ${max_w}x${max_h}"

    if ! double_click_window 600 300; then
        fail "$test_id" "could not double-click the terminal center again to restore"
    fi
    if ! restored="$(wait_for_window_size exact "$WINDOW_ID" "$before_w" "$before_h" 5)"; then
        actual="$(read_window_dimensions "$WINDOW_ID" 2>/dev/null || printf 'unknown')"
        fail "$test_id" "second double-click did not restore ${before_w}x${before_h} (now $actual)"
    fi
    read -r restored_w restored_h <<<"$restored"
    log "$test_id: geometry after restore: ${restored_w}x${restored_h}"

    pass "$test_id"
}

if matches_filter "APP01_SmokeLaunchE2E"; then
    seed_empty_config "$CONFIG_ROOT/avalonia"
    if ! start_app "$CONFIG_ROOT"; then
        fail "APP01_SmokeLaunchE2E" "application failed to launch"
    fi
    run_app01_smoke
fi

if [[ -n "$SSH_TEST_ID" ]]; then
    if ! start_sshd_fixture; then
        fail "$SSH_TEST_ID" "ephemeral SSH fixture failed to start"
    fi

    seed_ssh_config "$CONFIG_ROOT/avalonia" "$SSH_PORT" "$SSH_CLIENT_KEY"
    if ! start_app "$CONFIG_ROOT"; then
        fail "$SSH_TEST_ID" "application failed to launch with the SSH fixture config"
    fi
    wait_for_comtool_window "$SSH_TEST_ID"

    if matches_filter "UI01_TitleBarDoubleClickMaximizeE2E"; then
        run_ui01_title_bar_double_click_maximize
    fi
    if matches_filter "UI02_SidebarExpandsOnLaunchE2E"; then
        run_ui02_sidebar_expands
    fi
    if matches_filter "E2E01_OpenSshSession"; then
        run_e2e01_open_ssh
    fi
    if matches_filter "SSH06_MultipleConnectionsE2E"; then
        run_ssh06_multiple_connections
    fi
    if matches_filter "UI05_TabSwitchTypingBothDirections"; then
        run_ui05_tab_switch
    fi
    if matches_filter "TERM15_DoubleClickMaximizeE2E"; then
        run_term15_double_click_maximize
    fi

    close_app_window "$SSH_TEST_ID"
fi

exit 0
