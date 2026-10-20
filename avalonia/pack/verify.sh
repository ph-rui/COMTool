#!/usr/bin/env bash
# Unified COMTool Avalonia verification gate.
#
# Usage:
#   verify.sh [--app <app-executable>] [--results <directory>]
#
# Order:
#   restore -> build -> solution tests -> App tests -> test list -> Linux E2E
#   -> TestGate matrix check -> summary.txt
#
# Every step is logged under the results directory. A failing step does not
# prevent the summary or latest.txt from being written, but this script exits
# non-zero if any step fails. E2E is only executed on Linux; on other platforms
# e2e.log records that it does not apply.
#
# Environment:
#   DOTNET   dotnet executable (default: dotnet from PATH)
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"

app_arg=""
results_arg=""

usage() {
    cat <<EOF
Usage: $0 [--app <app-executable>] [--results <directory>]

  --app <path>       Application executable for Linux E2E
                     (default: avalonia/src/COMTool.App/bin/Release/net10.0/COMTool.App).
  --results <dir>    Results directory (default: avalonia/publish/test-results/<UTC timestamp>).
  -h, --help         Show this help.

Environment:
  DOTNET             dotnet executable (default: dotnet from PATH).
EOF
}

while (($# > 0)); do
    case "$1" in
        --app)
            (($# >= 2)) || { printf 'Missing value for --app\n' >&2; usage >&2; exit 2; }
            app_arg="$2"
            shift 2
            ;;
        --app=*)
            app_arg="${1#*=}"
            shift
            ;;
        --results)
            (($# >= 2)) || { printf 'Missing value for --results\n' >&2; usage >&2; exit 2; }
            results_arg="$2"
            shift 2
            ;;
        --results=*)
            results_arg="${1#*=}"
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

# Resolve dotnet before creating results so a missing tool fails clearly.
dotnet_input="${DOTNET:-dotnet}"
if [[ "$dotnet_input" == */* ]]; then
    if [[ ! -x "$dotnet_input" ]]; then
        printf 'error: dotnet executable not found or not executable: %s\n' "$dotnet_input" >&2
        printf 'Set DOTNET to the dotnet executable or add dotnet to PATH.\n' >&2
        exit 2
    fi
    dotnet="$(cd -- "$(dirname -- "$dotnet_input")" && pwd)/$(basename -- "$dotnet_input")"
else
    if ! dotnet="$(command -v -- "$dotnet_input")"; then
        printf 'error: dotnet executable not found: %s\n' "$dotnet_input" >&2
        printf 'Set DOTNET to the dotnet executable or add dotnet to PATH.\n' >&2
        exit 2
    fi
fi
if [[ ! -x "$dotnet" ]]; then
    printf 'error: dotnet executable is not executable: %s\n' "$dotnet" >&2
    exit 2
fi

sln="$repo_root/avalonia/COMTool.Avalonia.sln"
app_tests_project="$repo_root/avalonia/tests/COMTool.App.Tests/COMTool.App.Tests.csproj"
built_app="$repo_root/avalonia/src/COMTool.App/bin/Release/net10.0/COMTool.App"
gate_dll="$repo_root/avalonia/tools/COMTool.TestGate/bin/Release/net10.0/COMTool.TestGate.dll"
e2e_script="$repo_root/avalonia/tests/e2e/linux/run-ui-e2e.sh"
results_root="$repo_root/avalonia/publish/test-results"

mkdir -p "$results_root"
if [[ -n "$results_arg" ]]; then
    if [[ "$results_arg" == /* ]]; then
        results="$results_arg"
    else
        results="$PWD/$results_arg"
    fi
    mkdir -p "$results"
    results="$(cd -- "$results" && pwd)"
else
    timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
    results="$results_root/$timestamp"
    suffix=1
    while [[ -e "$results" ]]; do
        results="$results_root/${timestamp}-${suffix}"
        suffix=$((suffix + 1))
    done
    mkdir -p "$results"
fi
printf '%s\n' "$results" >"$results_root/latest.txt"

platform="$(uname -s | tr '[:upper:]' '[:lower:]')"
case "$platform" in
    linux*) platform="linux" ;;
    darwin*) platform="macos" ;;
    mingw*|msys*|cygwin*) platform="windows" ;;
esac

step_names=()
step_statuses=()
failures=0

record_step() {
    step_names+=("$1")
    step_statuses+=("$2")
}

# Run one native command, writing its combined output to <log>. The native
# command's exit status is returned so callers can record it and continue.
run_step() {
    local name="$1"
    local log="$2"
    shift 2

    {
        printf '# command:'
        printf ' %q' "$@"
        printf '\n'
    } >"$log"

    printf '[verify] %s\n' "$name"
    set +e
    "$@" 2>&1 | tee -a "$log"
    local status=${PIPESTATUS[0]}
    set -e
    return "$status"
}

restore_ok=0
build_ok=0

if run_step restore "$results/restore.log" "$dotnet" restore "$sln"; then
    record_step restore "PASS"
    restore_ok=1
else
    status=$?
    record_step restore "FAIL (exit $status)"
    failures=$((failures + 1))
fi

if ((restore_ok)); then
    if run_step build "$results/build.log" "$dotnet" build "$sln" -c Release --no-restore; then
        record_step build "PASS"
        build_ok=1
    else
        status=$?
        record_step build "FAIL (exit $status)"
        failures=$((failures + 1))
    fi
else
    printf '[verify] build (skipped: restore failed)\n'
    printf '# skipped: restore failed\n' >"$results/build.log"
    record_step build "SKIP (restore failed)"
fi

if ((build_ok)); then
    if run_step unit-tests "$results/unit-tests.log" \
        "$dotnet" test "$sln" -c Release --no-build \
        --results-directory "$results" \
        --logger "trx;LogFileName=unit-tests.trx"; then
        if [[ -s "$results/unit-tests.trx" ]]; then
            record_step unit-tests "PASS"
        else
            record_step unit-tests "FAIL (missing unit-tests.trx)"
            failures=$((failures + 1))
        fi
    else
        status=$?
        record_step unit-tests "FAIL (exit $status)"
        failures=$((failures + 1))
    fi

    if run_step app-tests "$results/app-tests.log" \
        "$dotnet" test "$app_tests_project" -c Release --no-build \
        --results-directory "$results" \
        --logger "trx;LogFileName=app-tests.trx"; then
        if [[ -s "$results/app-tests.trx" ]]; then
            record_step app-tests "PASS"
        else
            record_step app-tests "FAIL (missing app-tests.trx)"
            failures=$((failures + 1))
        fi
    else
        status=$?
        record_step app-tests "FAIL (exit $status)"
        failures=$((failures + 1))
    fi
else
    printf '[verify] unit-tests (skipped: build failed)\n'
    printf '[verify] app-tests (skipped: build failed)\n'
    printf '# skipped: build failed\n' >"$results/unit-tests.log"
    printf '# skipped: build failed\n' >"$results/app-tests.log"
    record_step unit-tests "SKIP (build failed)"
    record_step app-tests "SKIP (build failed)"
fi

# Produce the test list consumed by TestGate. This remains solution-wide so
# headless App tests are included alongside core/terminal/connections tests.
test_list="$results/test-list.txt"
printf '[verify] test-list\n'
set +e
"$dotnet" test "$sln" -c Release --no-build --list-tests >"$test_list" 2>&1
list_status=$?
set -e
app_test_count=0
if [[ -s "$test_list" ]]; then
    app_test_count="$(grep -cE '^[[:space:]]+COMTool\.App\.Tests\.' "$test_list" || true)"
fi
if ((list_status != 0)); then
    record_step test-list "FAIL (exit $list_status)"
    failures=$((failures + 1))
elif ((app_test_count == 0)); then
    record_step test-list "FAIL (no COMTool.App.Tests entries)"
    failures=$((failures + 1))
else
    record_step test-list "PASS ($app_test_count App tests listed)"
fi

# E2E only exists on Linux. Other platforms record an explicit skip line so
# TestGate sees a truthful log rather than a stale or empty file.
if [[ "$platform" == "linux" ]]; then
    if [[ -n "$app_arg" ]]; then
        if [[ "$app_arg" == /* ]]; then
            app_path="$app_arg"
        else
            app_path="$PWD/$app_arg"
        fi
    else
        app_path="$built_app"
    fi

    if [[ ! -x "$app_path" ]]; then
        printf 'FAIL E2E: application not found or not executable: %s\n' "$app_path" \
            >"$results/e2e.log"
        record_step e2e "FAIL (app not found: $app_path)"
        failures=$((failures + 1))
    elif run_step e2e "$results/e2e.log" bash "$e2e_script" --app "$app_path"; then
        record_step e2e "PASS"
    else
        status=$?
        record_step e2e "FAIL (exit $status)"
        failures=$((failures + 1))
    fi
else
    printf 'E2E not applicable on %s\n' "$platform" >"$results/e2e.log"
    record_step e2e "SKIP (E2E not applicable on $platform)"
fi

# The gate is always executed, even when earlier steps failed, so the summary
# records the matrix verdict for this run instead of hiding it behind an abort.
if [[ -f "$gate_dll" ]]; then
    if run_step testgate "$results/testgate.log" \
        "$dotnet" "$gate_dll" \
        --phase verify \
        --platform "$platform" \
        --parity "$repo_root/docs/parity-matrix.md" \
        --matrix "$repo_root/docs/testing/feature-test-matrix.md" \
        --manual "$repo_root/docs/testing/manual-acceptance.md" \
        --tests "$test_list" \
        --e2e "$results/e2e.log"; then
        record_step testgate "PASS"
    else
        status=$?
        record_step testgate "FAIL (exit $status)"
        failures=$((failures + 1))
    fi
else
    printf 'TestGate build output not found: %s\n' "$gate_dll" >"$results/testgate.log"
    record_step testgate "FAIL (missing TestGate)"
    failures=$((failures + 1))
fi

summary="$results/summary.txt"
{
    printf 'COMTool Avalonia verification\n'
    printf 'Timestamp (UTC): %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    printf 'Platform: %s\n' "$platform"
    printf 'Repository: %s\n' "$repo_root"
    printf 'Results: %s\n' "$results"
    printf 'dotnet: %s\n' "$dotnet"
    printf '\nSteps:\n'
    for index in "${!step_names[@]}"; do
        printf '  %-12s %s\n' "${step_names[$index]}" "${step_statuses[$index]}"
    done
    printf '\nTestGate output:\n'
    printf -- '----------------\n'
    if [[ -f "$results/testgate.log" ]]; then
        tail -n +2 "$results/testgate.log" || true
    else
        printf 'testgate.log is missing\n'
    fi
} >"$summary"

if ((failures == 0)); then
    printf 'RESULT: PASS\n'
    printf 'Results: %s\n' "$results"
    exit 0
fi

printf 'RESULT: FAIL (%d)\n' "$failures"
printf 'Results: %s\n' "$results"
exit 1
