#!/usr/bin/env bash
# Builds a gated COMTool Avalonia Beta Linux x64 candidate package.
#
# The candidate is created only after the unified verification gate and a real
# launch smoke test pass. Any failure removes the staging directory and leaves
# the previous publish/candidate untouched. The release package directory
# (publish/package) is never written by this script.
#
# Usage:
#   pack.sh [--candidate-dir <directory>]
#
# Environment:
#   VERSION=0.01            package version
#   SKIP_PUBLISH=1          reuse publish/linux-x64 instead of publishing again
#                           (verification, smoke and the package gate still run)
#   DOTNET=/path/to/dotnet  dotnet executable (default: dotnet)
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../../.." && pwd)"
project="$repo_root/avalonia/src/COMTool.App/COMTool.App.csproj"
verify_script="$repo_root/avalonia/pack/verify.sh"
e2e_script="$repo_root/avalonia/tests/e2e/linux/run-ui-e2e.sh"
publish_root="$repo_root/avalonia/publish"
results_root="$publish_root/test-results"
staging_root="$publish_root/staging"
source_dir="$publish_root/linux-x64"
candidate_dir="$publish_root/candidate"
gate_dll="$repo_root/avalonia/tools/COMTool.TestGate/bin/Release/net10.0/COMTool.TestGate.dll"
parity_path="$repo_root/docs/parity-matrix.md"
matrix_path="$repo_root/docs/testing/feature-test-matrix.md"
manual_path="$repo_root/docs/testing/manual-acceptance.md"
version="${VERSION:-0.01}"
platform="linux"
platform_rid="linux-x64"
package_name="COMTool-Avalonia-linux-x64-$version"
dotnet="${DOTNET:-dotnet}"

usage() {
    cat <<EOF
Usage: $0 [--candidate-dir <directory>]

  --candidate-dir <dir>  Candidate output directory
                         (default: avalonia/publish/candidate).
  -h, --help             Show this help.

Environment:
  VERSION                Package version (default: 0.01).
  SKIP_PUBLISH=1         Reuse publish/linux-x64. Verification, smoke and the
                         package gate still run.
  DOTNET                 dotnet executable (default: dotnet from PATH).
EOF
}

while (($# > 0)); do
    case "$1" in
        --candidate-dir)
            (($# >= 2)) || { printf 'Missing value for --candidate-dir\n' >&2; usage >&2; exit 2; }
            candidate_dir="$2"
            shift 2
            ;;
        --candidate-dir=*)
            candidate_dir="${1#*=}"
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

if [[ "$candidate_dir" != /* ]]; then
    candidate_dir="$repo_root/$candidate_dir"
fi

staging=""
promoted=0

cleanup() {
    local status=$?
    trap - EXIT INT TERM HUP
    if [[ "$promoted" != "1" && -n "$staging" && -d "$staging" ]]; then
        rm -rf -- "$staging" || true
    fi
    exit "$status"
}

trap cleanup EXIT
trap 'exit 130' INT TERM HUP

fail() {
    printf 'error: %s\n' "$*" >&2
    exit 1
}

json_escape() {
    local value="$1"
    value="${value//\\/\\\\}"
    value="${value//\"/\\\"}"
    printf '%s' "$value"
}

if [[ "$dotnet" == */* ]]; then
    [[ -x "$dotnet" ]] || fail "dotnet executable not found or not executable: $dotnet"
else
    dotnet="$(command -v -- "$dotnet")" || fail "dotnet executable not found: $dotnet"
fi

[[ -f "$verify_script" ]] || fail "verification gate not found: $verify_script"
[[ -f "$e2e_script" ]] || fail "Linux E2E entry point not found: $e2e_script"

commit="$(git -C "$repo_root" rev-parse HEAD 2>/dev/null)" \
    || fail "could not read the source commit"

# Step 1: the unified gate always runs first. There is deliberately no skip
# or force option. The results directory from verify is the only evidence
# source used by the candidate metadata and the package-phase gate.
printf '[pack] Running unified verification gate...\n'
if ! bash "$verify_script"; then
    fail "verification failed; no candidate package was created"
fi

latest_file="$results_root/latest.txt"
[[ -s "$latest_file" ]] || fail "verify did not write $latest_file"
results="$(head -n 1 "$latest_file" | tr -d '\r')"
[[ -n "$results" ]] || fail "verify results directory is empty in $latest_file"
[[ -d "$results" ]] || fail "verify results directory does not exist: $results"
[[ -s "$results/test-list.txt" ]] || fail "verify did not produce $results/test-list.txt"
[[ -f "$results/app-tests.trx" ]] || fail "verify did not produce $results/app-tests.trx"

mapfile -t unit_trx < <(find "$results" -maxdepth 1 -type f -name 'unit-tests-*.trx' -print | sort)
if ((${#unit_trx[@]} != 5)); then
    fail "expected 5 unit TRX files in $results, found ${#unit_trx[@]}"
fi
trx_files=("${unit_trx[@]}" "$results/app-tests.trx")

# Step 2: publish into a unique staging directory. The previous
# publish/linux-x64 and publish/candidate are never touched.
mkdir -p "$staging_root"
staging_id="$(date -u +%Y%m%dT%H%M%SZ)-$$"
staging="$staging_root/candidate-$staging_id"
[[ ! -e "$staging" ]] || fail "staging directory already exists: $staging"
mkdir -p "$staging"
publish_source="$staging/.publish"

if [[ "${SKIP_PUBLISH:-0}" == "1" ]]; then
    printf '[pack] Reusing published application from %s\n' "$source_dir"
    marker="$source_dir/.candidate-commit"
    [[ -f "$marker" ]] \
        || fail "SKIP_PUBLISH=1 requires $marker; run a real publish on this commit first"
    marked_commit="$(head -n 1 "$marker" | tr -d '\r')"
    [[ "$marked_commit" == "$commit" ]] \
        || fail "publish source $source_dir was built from '${marked_commit:-unknown}', but current HEAD is $commit"
    [[ -x "$source_dir/COMTool.App" ]] || fail "SKIP_PUBLISH=1 but $source_dir/COMTool.App is missing"
    mkdir -p "$publish_source"
    cp -a "$source_dir/." "$publish_source/"
else
    # ReadyToRun removes most startup JIT; leaving single-file compression off
    # avoids decompressing the native payload on every launch. The package is
    # larger, but the startup comparison is in docs/fixtures/phase1.
    printf '[pack] Publishing self-contained %s app...\n' "$platform_rid"
    if ! "$dotnet" publish "$project" \
        -c Release -r "$platform_rid" --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=false \
        -p:PublishReadyToRun=true \
        -p:DebugType=None -p:DebugSymbols=false \
        -o "$publish_source"; then
        fail "dotnet publish failed; no candidate package was created"
    fi
    printf '%s\n' "$commit" >"$publish_source/.candidate-commit"
fi

staged_app="$publish_source/COMTool.App"
[[ -f "$staged_app" ]] || fail "staged application not found: $staged_app"
chmod 0755 "$staged_app"

# Step 3: smoke the staged application itself. run-ui-e2e.sh owns Xvfb, the
# window manager and its temporary config directories; it starts its own
# display when DISPLAY is unset, so this works both locally and in CI.
smoke_log="$results/candidate-smoke.log"
smoke_tmp="$results/candidate-smoke-tmp"
: >"$smoke_log"
mkdir -p "$smoke_tmp"

printf '[pack] Running staged candidate smoke test...\n'
set +e
TMPDIR="$smoke_tmp" bash "$e2e_script" \
    --app "$staged_app" \
    --filter '^APP01_SmokeLaunchE2E$' 2>&1 | tee "$smoke_log"
smoke_status=${PIPESTATUS[0]}
set -e

if ((smoke_status != 0)); then
    fail "candidate smoke test failed; no candidate package was created (see $smoke_log)"
fi
if ! grep -qx 'PASS APP01_SmokeLaunchE2E' "$smoke_log"; then
    fail "candidate smoke test did not report PASS APP01_SmokeLaunchE2E (see $smoke_log)"
fi

# TestGate consumes the package-phase PASS line.
printf 'PASS PKG01_CandidateSmoke\n' >>"$smoke_log"
rm -rf -- "$smoke_tmp"

# Step 4: build the archive and folder entirely inside staging.
package_dir="$staging/$package_name"
archive_name="$package_name.tar.gz"
archive="$staging/$archive_name"
mkdir -p "$package_dir/app"
install -m 0644 "$staged_app" "$package_dir/app/COMTool.App"
chmod 0755 "$package_dir/app/COMTool.App"
install -m 0644 "$repo_root/avalonia/src/COMTool.App/Assets/logo.png" \
    "$package_dir/app/comtool-avalonia.png"
install -m 0755 "$script_dir/install.sh" "$package_dir/install.sh"
install -m 0755 "$script_dir/uninstall.sh" "$package_dir/uninstall.sh"
install -m 0644 "$script_dir/README.txt" "$package_dir/README.txt"

(
    cd -- "$staging"
    tar -czf "$archive_name" "$package_name"
)
[[ -s "$archive" ]] || fail "candidate archive was not created: $archive"

if ! archive_hash="$(sha256sum "$archive" | awk '{print $1}')"; then
    fail "could not calculate SHA256 for $archive"
fi
[[ "$archive_hash" =~ ^[0-9a-f]{64}$ ]] || fail "invalid SHA256 for $archive: $archive_hash"

(
    cd -- "$staging"
    printf '%s  %s\n' "$archive_hash" "$archive_name" >SHA256SUMS.txt
)

# Count real test results from the verify TRX files. Never hardcode totals.
trx_counter() {
    local file="$1"
    local name="$2"
    local counters
    local value

    counters="$(grep -m1 -o '<Counters[^>]*>' "$file" || true)"
    [[ -n "$counters" ]] || { printf '0\n'; return 0; }
    value="$(printf '%s\n' "$counters" \
        | sed -n "s/.*[[:space:]]${name}=\"\\([0-9][0-9]*\\)\".*/\\1/p")"
    [[ -n "$value" ]] || value=0
    printf '%s\n' "$value"
}

passed=0
failed=0
skipped=0
for trx in "${trx_files[@]}"; do
    [[ -s "$trx" ]] || fail "TRX result file is missing or empty: $trx"
    passed=$((passed + $(trx_counter "$trx" passed)))
    failed=$((failed + $(trx_counter "$trx" failed)))
    skipped=$((skipped + $(trx_counter "$trx" notExecuted)))
done

built_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

cat >"$staging/candidate.json" <<EOF
{
  "version": "$(json_escape "$version")",
  "platform": "$(json_escape "$platform_rid")",
  "sha256": "$(json_escape "$archive_hash")",
  "builtAt": "$(json_escape "$built_at")",
  "commit": "$(json_escape "$commit")",
  "resultsDir": "$(json_escape "$results")",
  "passed": $passed,
  "failed": $failed,
  "skipped": $skipped
}
EOF

# Step 5: enforce the package-phase coverage rules before promotion.
[[ -f "$gate_dll" ]] || fail "TestGate build output not found: $gate_dll"
package_log="$results/testgate-package.log"
printf '[pack] Running package-phase test gate...\n'
set +e
"$dotnet" "$gate_dll" \
    --phase package \
    --platform "$platform" \
    --parity "$parity_path" \
    --matrix "$matrix_path" \
    --manual "$manual_path" \
    --tests "$results/test-list.txt" \
    --e2e "$results/e2e.log" \
    --package "$smoke_log" 2>&1 | tee "$package_log"
gate_status=${PIPESTATUS[0]}
set -e

if ((gate_status != 0)); then
    fail "package-phase test gate failed; staging was removed and the previous candidate was kept"
fi

# Step 6: promotion preconditions. Directory replacement is only atomic on the
# same filesystem, so refuse before moving anything when the candidate output
# is on another volume.
candidate_parent="$(dirname -- "$candidate_dir")"
mkdir -p "$candidate_parent"
staging_device="$(stat -c '%d' -- "$staging")" \
    || fail "could not determine the filesystem of $staging"
candidate_device="$(stat -c '%d' -- "$candidate_parent")" \
    || fail "could not determine the filesystem of $candidate_parent"
if [[ "$staging_device" != "$candidate_device" ]]; then
    fail "refusing to promote: staging '$staging' and candidate directory '$candidate_parent' are on different filesystems"
fi

# A real publish also refreshes the reusable publish source, including its
# .candidate-commit provenance marker. Doing this only after the package gate
# keeps a later SKIP_PUBLISH=1 safe on the same commit.
if [[ "${SKIP_PUBLISH:-0}" != "1" ]]; then
    source_backup=""
    if [[ -e "$source_dir" ]]; then
        source_backup="${source_dir}.previous-$staging_id"
        mv -- "$source_dir" "$source_backup" \
            || fail "could not move the previous publish source aside"
    fi

    if ! mv -- "$publish_source" "$source_dir"; then
        if [[ -e "$source_dir" ]]; then
            rm -rf -- "$source_dir" || true
        fi
        if [[ -n "$source_backup" ]]; then
            if ! mv -- "$source_backup" "$source_dir"; then
                fail "could not update $source_dir and could not restore $source_backup; the candidate was not promoted"
            fi
        fi
        fail "could not update the reusable publish source $source_dir; the candidate was not promoted"
    fi

    if [[ -n "$source_backup" && -e "$source_backup" ]]; then
        rm -rf -- "$source_backup" || true
    fi
else
    rm -rf -- "$publish_source"
fi

# Step 7: atomically replace the candidate only after every gate passed.
backup=""
if [[ -e "$candidate_dir" ]]; then
    backup="${candidate_dir}.previous-$staging_id"
    mv -- "$candidate_dir" "$backup" || fail "could not move the previous candidate aside"
fi

if ! mv -- "$staging" "$candidate_dir"; then
    if [[ -n "$backup" ]]; then
        # Remove any partial target before restoring, and do not claim a
        # restore that did not happen.
        rm -rf -- "$candidate_dir" || true
        if ! mv -- "$backup" "$candidate_dir"; then
            fail "candidate promotion failed and the previous candidate could not be restored from $backup"
        fi
    fi
    fail "could not promote the staged candidate; the previous candidate was restored"
fi

promoted=1
staging=""
if [[ -n "$backup" && -e "$backup" ]]; then
    rm -rf -- "$backup" || true
fi

printf 'Created candidate:\n'
printf '  %s\n' "$candidate_dir/$archive_name"
printf '  %s\n' "$candidate_dir/candidate.json"
printf '  %s\n' "$candidate_dir/SHA256SUMS.txt"
printf 'SHA256 %s\n' "$archive_hash"
printf 'Tests passed=%d failed=%d skipped=%d\n' "$passed" "$failed" "$skipped"
printf 'Results: %s\n' "$results"
