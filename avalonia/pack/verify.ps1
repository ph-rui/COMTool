#Requires -Version 5.1
<#
.SYNOPSIS
Runs the unified COMTool Avalonia verification gate on Windows only.

.DESCRIPTION
Order: restore -> build -> per-project tests -> App tests -> test list -> E2E
(not run on Windows) -> TestGate matrix check -> summary.txt.

This script is Windows-only. Windows never runs the Linux Xvfb E2E suite, so
e2e.log records an explicit "E2E not applicable on windows" line. On Linux or
macOS run avalonia/pack/verify.sh instead; this script exits with code 2 there.

Every step is logged under the results directory. A failing step does not stop
summary.txt or latest.txt from being written, but the script exits non-zero if
any step fails.

.PARAMETER App
Windows application executable for a future E2E step. The default is the
just-built avalonia/src/COMTool.App/bin/Release/net10.0/COMTool.App.exe.

.PARAMETER Results
Results directory. The default is
avalonia/publish/test-results/<UTC timestamp>.

Environment:
  DOTNET   dotnet executable (default: dotnet from PATH)
#>
[CmdletBinding()]
param(
    [string]$App = "",
    [string]$Results = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# This script gates Windows. Refuse to run on other platforms instead of
# silently skipping the Linux Xvfb E2E suite.
$onWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)
if (-not $onWindows) {
    $osName = if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [System.Runtime.InteropServices.OSPlatform]::OSX)) { "macOS" } else { "Linux" }
    [Console]::Error.WriteLine(
        "error: verify.ps1 is Windows-only. Run avalonia/pack/verify.sh on $osName instead.")
    exit 2
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$sln = Join-Path $repoRoot "avalonia/COMTool.Avalonia.sln"
$appTestsProject = Join-Path $repoRoot "avalonia/tests/COMTool.App.Tests/COMTool.App.Tests.csproj"
$builtApp = Join-Path $repoRoot "avalonia/src/COMTool.App/bin/Release/net10.0/COMTool.App.exe"
$gateDll = Join-Path $repoRoot "avalonia/tools/COMTool.TestGate/bin/Release/net10.0/COMTool.TestGate.dll"
$resultsRoot = Join-Path $repoRoot "avalonia/publish/test-results"

# Unit tests run one project at a time so every project keeps its own TRX.
# App.Tests is deliberately excluded here and keeps app-tests.trx.
$unitProjects = @(
    [pscustomobject]@{
        Name = "COMTool.Core.Tests"
        Path = Join-Path $repoRoot "avalonia/tests/COMTool.Core.Tests/COMTool.Core.Tests.csproj"
    },
    [pscustomobject]@{
        Name = "COMTool.Terminal.Tests"
        Path = Join-Path $repoRoot "avalonia/tests/COMTool.Terminal.Tests/COMTool.Terminal.Tests.csproj"
    },
    [pscustomobject]@{
        Name = "COMTool.Connections.Tests"
        Path = Join-Path $repoRoot "avalonia/tests/COMTool.Connections.Tests/COMTool.Connections.Tests.csproj"
    },
    [pscustomobject]@{
        Name = "COMTool.Zmodem.Tests"
        Path = Join-Path $repoRoot "avalonia/tests/COMTool.Zmodem.Tests/COMTool.Zmodem.Tests.csproj"
    },
    [pscustomobject]@{
        Name = "COMTool.TestGate.Tests"
        Path = Join-Path $repoRoot "avalonia/tests/COMTool.TestGate.Tests/COMTool.TestGate.Tests.csproj"
    }
)

# Resolve dotnet before creating results so a missing tool fails clearly.
$dotnetInput = if ($env:DOTNET) { $env:DOTNET } else { "dotnet" }
if ($dotnetInput -match '[\\/]') {
    $resolvedDotnet = Resolve-Path -LiteralPath $dotnetInput -ErrorAction SilentlyContinue
    if (-not $resolvedDotnet -or
        -not (Test-Path -LiteralPath $resolvedDotnet.Path -PathType Leaf)) {
        [Console]::Error.WriteLine(
            "error: dotnet executable not found: $dotnetInput. Set DOTNET or add dotnet to PATH.")
        exit 2
    }
    $dotnet = $resolvedDotnet.Path
}
else {
    $dotnetCommand = Get-Command -Name $dotnetInput -CommandType Application `
        -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $dotnetCommand) {
        [Console]::Error.WriteLine(
            "error: dotnet executable not found: $dotnetInput. Set DOTNET or add dotnet to PATH.")
        exit 2
    }
    $dotnet = $dotnetCommand.Source
}

New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null
if ($Results) {
    if ([IO.Path]::IsPathRooted($Results)) {
        $resultsDir = $Results
    }
    else {
        $resultsDir = Join-Path (Get-Location).Path $Results
    }
    New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null
    $resultsDir = (Resolve-Path -LiteralPath $resultsDir).Path
}
else {
    $stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
    $resultsDir = Join-Path $resultsRoot $stamp
    $suffix = 1
    while (Test-Path -LiteralPath $resultsDir) {
        $resultsDir = Join-Path $resultsRoot ("{0}-{1}" -f $stamp, $suffix)
        $suffix++
    }
    New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null
}
Set-Content -LiteralPath (Join-Path $resultsRoot "latest.txt") -Value $resultsDir

# A reused -Results directory must not retain evidence from an earlier run.
$staleNames = @(
    "restore.log",
    "build.log",
    "unit-tests.trx",
    "unit-tests.log",
    "app-tests.trx",
    "app-tests.log",
    "test-list.txt",
    "e2e.log",
    "testgate.log",
    "summary.txt"
)
foreach ($staleName in $staleNames) {
    Remove-Item -LiteralPath (Join-Path $resultsDir $staleName) `
        -Force -ErrorAction SilentlyContinue
}
Get-ChildItem -Path (Join-Path $resultsDir "unit-tests-*") -File `
    -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $resultsDir "e2e-tmp") `
    -Recurse -Force -ErrorAction SilentlyContinue

$currentPlatform = "windows"
$e2eApp = if ($App) {
    [IO.Path]::GetFullPath($App)
}
else {
    $builtApp
}

$stepNames = New-Object System.Collections.Generic.List[string]
$stepStatuses = New-Object System.Collections.Generic.List[string]
$trxEvidence = New-Object System.Collections.Generic.List[string]
$unitTotal = 0
$appTotal = 0
$failures = 0

function Add-StepResult {
    param(
        [string]$Name,
        [string]$Status
    )
    $stepNames.Add($Name)
    $stepStatuses.Add($Status)
}

function Get-TrxResultCount {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return 0
    }
    $raw = Get-Content -LiteralPath $Path -Raw
    if (-not $raw) {
        return 0
    }
    return ([regex]::Matches($raw, '<UnitTestResult ')).Count
}

# Runs one native command, writing combined output to $LogPath. Returns the
# native exit code from $LASTEXITCODE so the caller can check it immediately.
function Invoke-Logged {
    param(
        [string]$Name,
        [string]$LogPath,
        [scriptblock]$Action
    )

    Write-Host "[verify] $Name"
    Set-Content -LiteralPath $LogPath -Value "# command: $Name"
    & $Action 2>&1 | Tee-Object -FilePath $LogPath -Append | Out-Host
    return $LASTEXITCODE
}

$LASTEXITCODE = 0
$restoreOk = $false
$buildOk = $false

$restoreStatus = Invoke-Logged -Name "restore" `
    -LogPath (Join-Path $resultsDir "restore.log") `
    -Action { & $dotnet restore $sln }
if ($restoreStatus -eq 0) {
    Add-StepResult "restore" "PASS"
    $restoreOk = $true
}
else {
    Add-StepResult "restore" "FAIL (exit $restoreStatus)"
    $failures++
}

if ($restoreOk) {
    $buildStatus = Invoke-Logged -Name "build" `
        -LogPath (Join-Path $resultsDir "build.log") `
        -Action { & $dotnet build $sln -c Release --no-restore }
    if ($buildStatus -eq 0) {
        Add-StepResult "build" "PASS"
        $buildOk = $true
    }
    else {
        Add-StepResult "build" "FAIL (exit $buildStatus)"
        $failures++
    }
}
else {
    Write-Host "[verify] build (skipped: restore failed)"
    Set-Content -LiteralPath (Join-Path $resultsDir "build.log") -Value "# skipped: restore failed"
    Add-StepResult "build" "SKIP (restore failed)"
}

if ($buildOk) {
    $unitFailed = $false
    foreach ($project in $unitProjects) {
        $trxName = "unit-tests-$($project.Name).trx"
        $logName = "unit-tests-$($project.Name).log"
        $trxPath = Join-Path $resultsDir $trxName
        $status = Invoke-Logged -Name "unit-tests-$($project.Name)" `
            -LogPath (Join-Path $resultsDir $logName) `
            -Action {
                & $dotnet test $project.Path -c Release --no-build `
                    --results-directory $resultsDir `
                    --logger "trx;LogFileName=$trxName"
            }

        if ($status -ne 0) {
            $unitFailed = $true
            $trxEvidence.Add("${trxName}: FAIL (test exit $status)")
        }
        else {
            $count = Get-TrxResultCount -Path $trxPath
            if ($count -gt 0) {
                $unitTotal += $count
                $trxEvidence.Add("${trxName}: $count results")
            }
            else {
                $unitFailed = $true
                $trxEvidence.Add("${trxName}: FAIL (missing, empty, or no results)")
            }
        }
    }

    if ($unitFailed) {
        Add-StepResult "unit-tests" "FAIL (see unit-tests-*.log)"
        $failures++
    }
    else {
        Add-StepResult "unit-tests" `
            "PASS ($unitTotal results across $($unitProjects.Count) projects)"
    }

    $appStatus = Invoke-Logged -Name "app-tests" `
        -LogPath (Join-Path $resultsDir "app-tests.log") `
        -Action {
            & $dotnet test $appTestsProject -c Release --no-build `
                --results-directory $resultsDir `
                --logger "trx;LogFileName=app-tests.trx"
        }
    $appTrx = Join-Path $resultsDir "app-tests.trx"
    if ($appStatus -ne 0) {
        Add-StepResult "app-tests" "FAIL (exit $appStatus)"
        $failures++
    }
    else {
        $appCount = Get-TrxResultCount -Path $appTrx
        if ($appCount -gt 0) {
            $appTotal = $appCount
            Add-StepResult "app-tests" "PASS ($appCount results)"
        }
        else {
            Add-StepResult "app-tests" `
                "FAIL (missing, empty, or no results in app-tests.trx)"
            $failures++
        }
    }
}
else {
    Write-Host "[verify] unit-tests (skipped: build failed)"
    Write-Host "[verify] app-tests (skipped: build failed)"
    Set-Content -LiteralPath (Join-Path $resultsDir "unit-tests.log") -Value "# skipped: build failed"
    Set-Content -LiteralPath (Join-Path $resultsDir "app-tests.log") -Value "# skipped: build failed"
    Add-StepResult "unit-tests" "SKIP (build failed)"
    Add-StepResult "app-tests" "SKIP (build failed)"
}

# The list stays solution-wide so TestGate sees the headless App tests too.
# Never list stale build outputs after a failed build.
$testList = Join-Path $resultsDir "test-list.txt"
if ($buildOk) {
    Write-Host "[verify] test-list"
    # vstest.console can occasionally return 1 on Windows after a successful
    # discovery run (notably when the Terminal test assembly was just used).
    # Retry once; the list file is replaced on each attempt and still has to
    # contain App tests before the step passes.
    $listStatus = 1
    $listAttempts = 0
    for ($listAttempts = 1; $listAttempts -le 2; $listAttempts++) {
        & $dotnet test $sln -c Release --no-build --list-tests 2>&1 |
            Tee-Object -FilePath $testList | Out-Host
        $listStatus = $LASTEXITCODE
        if ($listStatus -eq 0) {
            break
        }
        Write-Host "[verify] test-list attempt $listAttempts failed (exit $listStatus); retrying"
        Start-Sleep -Seconds 1
    }

    $appTestCount = 0
    if (Test-Path -LiteralPath $testList -PathType Leaf) {
        $appTestCount = @(
            Get-Content -LiteralPath $testList |
                Select-String -Pattern '^\s+COMTool\.App\.Tests\.'
        ).Count
    }
    if ($listStatus -ne 0) {
        Add-StepResult "test-list" "FAIL (exit $listStatus)"
        $failures++
    }
    elseif ($appTestCount -eq 0) {
        Add-StepResult "test-list" "FAIL (no COMTool.App.Tests entries)"
        $failures++
    }
    else {
        Add-StepResult "test-list" "PASS ($appTestCount App tests listed)"
    }
}
else {
    Set-Content -LiteralPath $testList -Value "# skipped: build failed"
    Add-StepResult "test-list" "SKIP (build failed)"
}

# Windows does not run the Linux Xvfb E2E suite. The log line is explicit so
# TestGate never mistakes a missing file for a silently skipped gate. A failed
# build also skips E2E so a stale binary is never exercised.
$e2eLog = Join-Path $resultsDir "e2e.log"
if ($buildOk) {
    Set-Content -LiteralPath $e2eLog -Value "E2E not applicable on windows"
    Add-StepResult "e2e" "SKIP (E2E not applicable on windows)"
}
else {
    Set-Content -LiteralPath $e2eLog -Value "# skipped: build failed"
    Add-StepResult "e2e" "SKIP (build failed)"
}

# Always execute the gate after a successful build, even after an earlier test
# failure, so summary.txt records the matrix verdict for this run.
$gateLog = Join-Path $resultsDir "testgate.log"
if ($buildOk) {
    if (Test-Path -LiteralPath $gateDll -PathType Leaf) {
        $gateStatus = Invoke-Logged -Name "testgate" -LogPath $gateLog -Action {
            & $dotnet $gateDll `
                --phase verify `
                --platform $currentPlatform `
                --parity (Join-Path $repoRoot "docs/parity-matrix.md") `
                --matrix (Join-Path $repoRoot "docs/testing/feature-test-matrix.md") `
                --manual (Join-Path $repoRoot "docs/testing/manual-acceptance.md") `
                --tests $testList `
                --e2e $e2eLog
        }
        if ($gateStatus -eq 0) {
            Add-StepResult "testgate" "PASS"
        }
        else {
            Add-StepResult "testgate" "FAIL (exit $gateStatus)"
            $failures++
        }
    }
    else {
        Set-Content -LiteralPath $gateLog -Value "TestGate build output not found: $gateDll"
        Add-StepResult "testgate" "FAIL (missing TestGate)"
        $failures++
    }
}
else {
    Set-Content -LiteralPath $gateLog -Value "# skipped: build failed"
    Add-StepResult "testgate" "SKIP (build failed)"
}

$summaryLines = New-Object System.Collections.Generic.List[string]
$summaryLines.Add("COMTool Avalonia verification")
$summaryLines.Add(("Timestamp (UTC): {0}" -f (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")))
$summaryLines.Add(("Platform: {0}" -f $currentPlatform))
$summaryLines.Add(("Repository: {0}" -f $repoRoot))
$summaryLines.Add(("Results: {0}" -f $resultsDir))
$summaryLines.Add(("dotnet: {0}" -f $dotnet))
$summaryLines.Add(("App: {0}" -f $e2eApp))
$summaryLines.Add("")
$summaryLines.Add("Steps:")
for ($index = 0; $index -lt $stepNames.Count; $index++) {
    $summaryLines.Add(("  {0,-12} {1}" -f $stepNames[$index], $stepStatuses[$index]))
}
if ($buildOk) {
    $summaryLines.Add("")
    $summaryLines.Add("Test evidence:")
    foreach ($evidence in $trxEvidence) {
        $summaryLines.Add("  $evidence")
    }
    if ($appTotal -gt 0) {
        $summaryLines.Add("  app-tests.trx: $appTotal results")
    }
    $summaryLines.Add("  total: $($unitTotal + $appTotal) results")
}
$summaryLines.Add("")
$summaryLines.Add("TestGate output:")
$summaryLines.Add("----------------")
if (Test-Path -LiteralPath $gateLog -PathType Leaf) {
    foreach ($line in (Get-Content -LiteralPath $gateLog | Select-Object -Skip 1)) {
        $summaryLines.Add([string]$line)
    }
}
else {
    $summaryLines.Add("testgate.log is missing")
}
$summaryLines.Add("")
if ($failures -eq 0) {
    $summaryLines.Add("RESULT: PASS")
}
else {
    $summaryLines.Add("RESULT: FAIL ($failures)")
}
Set-Content -LiteralPath (Join-Path $resultsDir "summary.txt") -Value $summaryLines -Encoding UTF8

if ($failures -eq 0) {
    Write-Host "RESULT: PASS"
    Write-Host "Results: $resultsDir"
    exit 0
}

Write-Host "RESULT: FAIL ($failures)"
Write-Host "Results: $resultsDir"
exit 1
