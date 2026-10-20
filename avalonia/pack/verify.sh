#!/usr/bin/env bash
# Unified COMTool Avalonia verification gate.
#
# Usage:
#   verify.sh [--app <app-executable>] [--results <directory>]
#
# Order:
#   restore -> build -> per-project tests -> App tests -> test list -> Linux E2E
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

# Unit tests are executed one project at a time so every project keeps its own
# TRX. App.Tests is deliberately excluded here and keeps app-tests.trx.
unit_projects=(
    "COMTool.Core.Tests:$repo_root/avalonia/tests/COMTool.Core.Tests/COMTool.Core.Tests.csproj"
    "COMTool.Terminal.Tests:$repo_root/avalonia/tests/COMTool.Terminal.Tests/COMTool.Terminal.Tests.csproj"
    "COMTool.Connections.Tests:$repo_root/avalonia/tests/COMTool.Connections.Tests/COMTool.Connections.Tests.csproj"
    "COMTool.Zmodem.Tests:$repo_root/avalonia/tests/COMTool.Zmodem.Tests/COMTool.Zmodem.Tests.csproj"
    "COMTool.TestGate.Tests:$repo_root/avalonia/tests/COMTool.TestGate.Tests/COMTool.TestGate.Tests.csproj"
)

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

# A reused --results directory must not retain evidence from an earlier run.
rm -f -- \
    "$results/restore.log" \
    "$results/build.log" \
    "$results/unit-tests.trx" \
    "$results/unit-tests.log" \
    "$results/app-tests.trx" \
    "$results/app-tests.log" \
    "$results/test-list.txt" \
    "$results/e2e.log" \
    "$results/testgate.log" \
    "$results/summary.txt"
rm -f -- "$results"/unit-tests-*.trx "$results"/unit-tests-*.log
rm -rf -- "$results/e2e-tmp"

platform="$(uname -s | tr '[:upper:]' '[:lower:]')"
case "$platform" in
    linux*) platform="linux" ;;
    darwin*) platform="macos" ;;
    mingw*|msys*|cygwin*) platform="windows" ;;
esac

step_names=()
step_statuses=()
unit_evidence=()
unit_total=0
app_total=0
failures=0

record_step() {
    step_names+=("$1")
    step_statuses+=("$2")
}

count_trx_results() {
    local file="$1"
    {
        grep -o '<UnitTestResult ' "$file" 2>/dev/null || true
    } | wc -l | tr -d '[:space:]'
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
    unit_failed=0
    for entry in "${unit_projects[@]}"; do
        project_name="${entry%%:*}"
        project_path="${entry#*:}"
        trx_name="unit-tests-${project_name}.trx"
        log_name="unit-tests-${project_name}.log"

        if run_step "unit-tests-${project_name}" "$results/$log_name" \
            "$dotnet" test "$project_path" -c Release --no-build \
            --results-directory "$results" \
            --logger "trx;LogFileName=${trx_name}"; then
            if [[ -s "$results/$trx_name" ]]; then
                count="$(count_trx_results "$results/$trx_name")"
                if ((count > 0)); then
                    unit_total=$((unit_total + count))
                    unit_evidence+=("$trx_name: $count results")
                else
                    unit_failed=1
                    unit_evidence+=("$trx_name: FAIL (0 results)")
                fi
            else
                unit_failed=1
                unit_evidence+=("$trx_name: FAIL (missing or empty)")
            fi
        else
            status=$?
            unit_failed=1
            unit_evidence+=("$trx_name: FAIL (test exit $status)")
        fi
    done

    if ((unit_failed == 0)); then
        record_step unit-tests "PASS ($unit_total results across ${#unit_projects[@]} projects)"
    else
        record_step unit-tests "FAIL (see unit-tests-*.log)"
        failures=$((failures + 1))
    fi

    if run_step app-tests "$results/app-tests.log" \
        "$dotnet" test "$app_tests_project" -c Release --no-build \
        --results-directory "$results" \
        --logger "trx;LogFileName=app-tests.trx"; then
        app_count=0
        if [[ -s "$results/app-tests.trx" ]]; then
            app_count="$(count_trx_results "$results/app-tests.trx")"
        fi
        if ((app_count > 0)); then
            app_total=$app_count
            record_step app-tests "PASS ($app_count results)"
        else
            record_step app-tests "FAIL (missing, empty, or no results in app-tests.trx)"
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
# Stale build outputs must never be listed after a failed build.
test_list="$results/test-list.txt"
if ((build_ok)); then
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
else
    printf '# skipped: build failed\n' >"$test_list"
    record_step test-list "SKIP (build failed)"
fi

# E2E only exists on Linux. Other platforms record an explicit skip line so
# TestGate sees a truthful log rather than a stale or empty file. A failed
# build also skips E2E so a stale binary is never exercised.
if ((build_ok)); then
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
        else
            mkdir -p "$results/e2e-tmp"
            if run_step e2e "$results/e2e.log" \
                env TMPDIR="$results/e2e-tmp" \
                bash "$e2e_script" --app "$app_path"; then
                record_step e2e "PASS"
            else
                status=$?
                record_step e2e "FAIL (exit $status)"
                failures=$((failures + 1))
            fi
        fi
    else
        printf 'E2E not applicable on %s\n' "$platform" >"$results/e2e.log"
        record_step e2e "SKIP (E2E not applicable on $platform)"
    fi
else
    printf '# skipped: build failed\n' >"$results/e2e.log"
    record_step e2e "SKIP (build failed)"
fi

# The gate is always executed after a successful build, even when an earlier
# test step failed, so the summary records the matrix verdict for this run. A
# failed build must not run a stale TestGate binary.
if ((build_ok)); then
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
else
    printf '# skipped: build failed\n' >"$results/testgate.log"
    record_step testgate "SKIP (build failed)"
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
    if ((build_ok)); then
        printf '\nTest evidence:\n'
        if ((${#unit_evidence[@]} > 0)); then
            for evidence in "${unit_evidence[@]}"; do
                printf '  %s\n' "$evidence"
            done
        fi
        if ((app_total > 0)); then
            printf '  app-tests.trx: %d results\n' "$app_total"
        fi
        printf '  total: %d results\n' "$((unit_total + app_total))"
    fi
    printf '\nTestGate output:\n'
    printf -- '----------------\n'
    if [[ -f "$results/testgate.log" ]]; then
        tail -n +2 "$results/testgate.log" || true
    else
        printf 'testgate.log is missing\n'
    fi
    if ((failures == 0)); then
        printf '\nRESULT: PASS\n'
    else
        printf '\nRESULT: FAIL (%d)\n' "$failures"
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
