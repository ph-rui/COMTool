#Requires -Version 5.1
<#
.SYNOPSIS
Builds a gated COMTool Avalonia Beta Windows x64 candidate package.

.DESCRIPTION
The unified verification gate always runs first; there is no skip or force
switch. The app is then published into a unique staging directory, launched
with --demo under isolated config directories, closed through CloseMainWindow,
and only after the smoke test and the package-phase TestGate pass is
publish/candidate replaced. Any failure removes staging and leaves the previous
candidate and publish/package untouched.

Candidate layout:
  COMTool-Avalonia-Beta-win-x64-<version>.zip
  COMTool-Avalonia-Beta-win-x64-<version>/
  SHA256SUMS.txt
  candidate.json

.PARAMETER CandidateDir
Candidate output directory (default: ../../publish/candidate).

.PARAMETER SkipPublish
Reuse -Source instead of running dotnet publish. Verification, the smoke test
and the package gate still run.
#>
[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot "../../publish/win-x64"),
    [string]$CandidateDir = (Join-Path $PSScriptRoot "../../publish/candidate"),
    [string]$Version = "0.01",
    [switch]$SkipPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../.."))
$project = Join-Path $repoRoot "avalonia/src/COMTool.App/COMTool.App.csproj"
$verifyScript = Join-Path $repoRoot "avalonia/pack/verify.ps1"
$publishRoot = Join-Path $repoRoot "avalonia/publish"
$resultsRoot = Join-Path $publishRoot "test-results"
$stagingRoot = Join-Path $publishRoot "staging"
$gateDll = Join-Path $repoRoot "avalonia/tools/COMTool.TestGate/bin/Release/net10.0/COMTool.TestGate.dll"
$parityPath = Join-Path $repoRoot "docs/parity-matrix.md"
$matrixPath = Join-Path $repoRoot "docs/testing/feature-test-matrix.md"
$manualPath = Join-Path $repoRoot "docs/testing/manual-acceptance.md"
$packageName = "COMTool-Avalonia-Beta-win-x64-$Version"
$platform = "windows"
$platformRid = "win-x64"

function Get-Dotnet {
    $input = if ($env:DOTNET) { $env:DOTNET } else { "dotnet" }
    if ($input -match '[\\/]') {
        $resolved = Resolve-Path -LiteralPath $input -ErrorAction SilentlyContinue
        if (-not $resolved -or -not (Test-Path -LiteralPath $resolved.Path -PathType Leaf)) {
            throw "dotnet executable not found: $input. Set DOTNET or add dotnet to PATH."
        }
        return $resolved.Path
    }

    $command = Get-Command -Name $input -CommandType Application `
        -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $command) {
        throw "dotnet executable not found: $input. Set DOTNET or add dotnet to PATH."
    }
    return $command.Source
}

function Get-TrxCounters {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "TRX result file is missing: $Path"
    }
    $raw = Get-Content -LiteralPath $Path -Raw
    if (-not $raw) {
        throw "TRX result file is empty: $Path"
    }

    [xml]$document = $raw
    $counters = $document.TestRun.ResultSummary.Counters
    if (-not $counters) {
        throw "TRX result file has no Counters element: $Path"
    }

    return [pscustomobject]@{
        Passed  = [int]$counters.passed
        Failed  = [int]$counters.failed
        Skipped = [int]$counters.notExecuted
    }
}

function Stop-StagedProcess {
    param($Process)

    if (-not $Process) {
        return
    }
    try {
        if (-not $Process.HasExited) {
            $Process.Kill()
        }
    }
    catch {
        # Best effort only; the original failure is reported by the caller.
    }
    try {
        $null = $Process.WaitForExit(5000)
    }
    catch {
    }
}

$dotnet = Get-Dotnet
$CandidateDir = [IO.Path]::GetFullPath($CandidateDir)
$staging = $null
$promoted = $false
$backup = $null

try {
    if (-not (Test-Path -LiteralPath $verifyScript -PathType Leaf)) {
        throw "verification gate not found: $verifyScript"
    }

    # Step 1: run verify in a child host. verify.ps1 ends with Exit, so calling
    # it with & would terminate this pack process before packaging.
    Write-Host "[pack] Running unified verification gate..."
    $hostExe = (Get-Process -Id $PID).Path
    $verifyArguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $verifyScript
    )
    $verifyProcess = Start-Process -FilePath $hostExe -ArgumentList $verifyArguments `
        -WorkingDirectory $repoRoot -NoNewWindow -Wait -PassThru
    if ($verifyProcess.ExitCode -ne 0) {
        throw "verification failed with exit code $($verifyProcess.ExitCode); no candidate package was created."
    }

    $latestFile = Join-Path $resultsRoot "latest.txt"
    if (-not (Test-Path -LiteralPath $latestFile -PathType Leaf)) {
        throw "verify did not write $latestFile."
    }
    $resultsDir = (Get-Content -LiteralPath $latestFile -Raw).Trim()
    if (-not $resultsDir -or -not (Test-Path -LiteralPath $resultsDir -PathType Container)) {
        throw "verify results directory does not exist: $resultsDir"
    }
    $testList = Join-Path $resultsDir "test-list.txt"
    if (-not (Test-Path -LiteralPath $testList -PathType Leaf)) {
        throw "verify did not produce $testList."
    }

    $unitTrx = @(Get-ChildItem -Path $resultsDir -Filter "unit-tests-*.trx" -File)
    if ($unitTrx.Count -ne 5) {
        throw "expected 5 unit TRX files in $resultsDir, found $($unitTrx.Count)."
    }
    $appTrx = Join-Path $resultsDir "app-tests.trx"
    if (-not (Test-Path -LiteralPath $appTrx -PathType Leaf)) {
        throw "verify did not produce $appTrx."
    }
    $trxFiles = @($unitTrx | ForEach-Object { $_.FullName }) + @($appTrx)

    # Step 2: publish into a unique staging directory. The previous publish
    # source and candidate are not modified.
    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
    $stagingId = "{0}-{1}" -f (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ"), $PID
    $staging = Join-Path $stagingRoot "candidate-$stagingId"
    if (Test-Path -LiteralPath $staging) {
        throw "staging directory already exists: $staging"
    }
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    $publishSource = Join-Path $staging ".publish"

    if ($SkipPublish) {
        Write-Host "[pack] Reusing published application from $Source"
        if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
            throw "-SkipPublish was used but the publish source does not exist: $Source"
        }
        $Source = (Resolve-Path -LiteralPath $Source).Path
        New-Item -ItemType Directory -Path $publishSource -Force | Out-Null
        Copy-Item -Path (Join-Path $Source "*") -Destination $publishSource -Recurse -Force
    }
    else {
        # ReadyToRun removes most startup JIT; leaving single-file compression
        # off avoids decompressing the native payload on every launch.
        Write-Host "[pack] Publishing self-contained $platformRid app..."
        & $dotnet publish $project `
            -c Release -r $platformRid --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:EnableCompressionInSingleFile=false `
            -p:PublishReadyToRun=true `
            -p:DebugType=None -p:DebugSymbols=false `
            -o $publishSource
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE; no candidate package was created."
        }
    }

    $stagedExe = Join-Path $publishSource "COMTool.App.exe"
    if (-not (Test-Path -LiteralPath $stagedExe -PathType Leaf)) {
        throw "staged application not found: $stagedExe"
    }

    # Step 3: launch the staged executable itself under isolated config
    # directories and require a real main window and a clean close.
    $smokeLog = Join-Path $resultsDir "candidate-smoke.log"
    $smokeRoot = Join-Path $resultsDir "candidate-smoke-tmp"
    $smokeConfig = Join-Path $smokeRoot "avalonia"
    $smokePython = Join-Path $smokeRoot "python"
    $smokeHome = Join-Path $smokeRoot "home"
    New-Item -ItemType Directory -Path $smokeConfig, $smokePython, $smokeHome -Force | Out-Null
    Set-Content -LiteralPath $smokeLog -Encoding UTF8 `
        -Value "Candidate smoke: $stagedExe"

    $previousAvalonia = $env:COMTOOL_AVALONIA_CONFIG_DIR
    $previousPython = $env:COMTOOL_PYTHON_CONFIG_DIR
    $previousHome = $env:HOME
    $smokeProcess = $null
    try {
        $env:COMTOOL_AVALONIA_CONFIG_DIR = $smokeConfig
        $env:COMTOOL_PYTHON_CONFIG_DIR = $smokePython
        $env:HOME = $smokeHome

        Write-Host "[pack] Running staged candidate smoke test..."
        $smokeProcess = Start-Process -FilePath $stagedExe -ArgumentList "--demo" `
            -WorkingDirectory $publishSource -PassThru

        $deadline = (Get-Date).AddSeconds(30)
        $windowReady = $false
        while ((Get-Date) -lt $deadline) {
            $smokeProcess.Refresh()
            if ($smokeProcess.HasExited) {
                throw "staged application exited before showing a window (exit code $($smokeProcess.ExitCode))."
            }
            if ($smokeProcess.MainWindowHandle -ne [IntPtr]::Zero) {
                $windowReady = $true
                break
            }
            Start-Sleep -Milliseconds 200
        }
        if (-not $windowReady) {
            throw "staged application did not show a main window within 30s."
        }

        if (-not $smokeProcess.CloseMainWindow()) {
            throw "CloseMainWindow() failed for the staged application."
        }
        if (-not $smokeProcess.WaitForExit(15000)) {
            throw "staged application did not exit within 15s of CloseMainWindow()."
        }
        if ($smokeProcess.ExitCode -ne 0) {
            throw "staged application exited with code $($smokeProcess.ExitCode)."
        }
    }
    catch {
        $reason = $_.Exception.Message
        Add-Content -LiteralPath $smokeLog -Encoding UTF8 -Value "FAIL PKG01_CandidateSmoke: $reason"
        Stop-StagedProcess -Process $smokeProcess
        throw
    }
    finally {
        $env:COMTOOL_AVALONIA_CONFIG_DIR = $previousAvalonia
        $env:COMTOOL_PYTHON_CONFIG_DIR = $previousPython
        $env:HOME = $previousHome
    }

    # TestGate consumes the package-phase PASS line; it is written only after
    # the real process launch and clean exit above.
    Add-Content -LiteralPath $smokeLog -Encoding UTF8 -Value "PASS PKG01_CandidateSmoke"

    # Step 4: build the folder and zip entirely inside staging.
    $packageDir = Join-Path $staging $packageName
    $archive = Join-Path $staging "$packageName.zip"
    New-Item -ItemType Directory -Path (Join-Path $packageDir "app") -Force | Out-Null
    Copy-Item -Path (Join-Path $publishSource "*") -Destination (Join-Path $packageDir "app") `
        -Recurse -Force
    Get-ChildItem -Path (Join-Path $packageDir "app") -Filter "*.pdb" -File |
        Remove-Item -Force

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "install.ps1") -Destination $packageDir -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "uninstall.ps1") -Destination $packageDir -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "README.txt") -Destination $packageDir -Force

    Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $archive `
        -CompressionLevel Optimal
    if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
        throw "candidate archive was not created: $archive"
    }

    $archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($archiveHash -notmatch '^[0-9a-f]{64}$') {
        throw "invalid SHA256 for ${archive}: $archiveHash"
    }
    "$archiveHash  $($packageName).zip" |
        Set-Content -LiteralPath (Join-Path $staging "SHA256SUMS.txt") -Encoding ASCII

    # Count real results from verify TRX files; totals are never hardcoded.
    $passed = 0
    $failed = 0
    $skipped = 0
    foreach ($trx in $trxFiles) {
        $counters = Get-TrxCounters -Path $trx
        $passed += $counters.Passed
        $failed += $counters.Failed
        $skipped += $counters.Skipped
    }

    $commit = (& git -C $repoRoot rev-parse HEAD 2>$null | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or -not $commit) {
        throw "could not read the source commit."
    }
    $builtAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

    $candidate = [ordered]@{
        version    = $Version
        platform   = $platformRid
        sha256     = $archiveHash
        builtAt    = $builtAt
        commit     = $commit.Trim()
        resultsDir = $resultsDir
        passed     = $passed
        failed     = $failed
        skipped    = $skipped
    }
    $candidate | ConvertTo-Json -Depth 3 |
        Set-Content -LiteralPath (Join-Path $staging "candidate.json") -Encoding UTF8

    # The staging-only publish copy must not become part of the candidate.
    Remove-Item -LiteralPath $publishSource -Recurse -Force

    # Step 5: enforce package-phase coverage rules before promotion.
    if (-not (Test-Path -LiteralPath $gateDll -PathType Leaf)) {
        throw "TestGate build output not found: $gateDll"
    }
    Write-Host "[pack] Running package-phase test gate..."
    $gateLog = Join-Path $resultsDir "testgate-package.log"
    & $dotnet $gateDll `
        --phase package `
        --platform $platform `
        --parity $parityPath `
        --matrix $matrixPath `
        --manual $manualPath `
        --tests $testList `
        --e2e (Join-Path $resultsDir "e2e.log") `
        --package $smokeLog 2>&1 |
        Tee-Object -FilePath $gateLog | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "package-phase test gate failed with exit code $LASTEXITCODE; staging was removed and the previous candidate was kept."
    }

    # Step 6: atomically replace the candidate only after every gate passed.
    $candidateParent = Split-Path -Parent $CandidateDir
    New-Item -ItemType Directory -Path $candidateParent -Force | Out-Null
    if (Test-Path -LiteralPath $CandidateDir) {
        $backup = "$CandidateDir.previous-$stagingId"
        Move-Item -LiteralPath $CandidateDir -Destination $backup -Force
    }

    try {
        Move-Item -LiteralPath $staging -Destination $CandidateDir -Force
    }
    catch {
        if ($backup -and (Test-Path -LiteralPath $backup)) {
            Move-Item -LiteralPath $backup -Destination $CandidateDir -Force
        }
        throw
    }

    $promoted = $true
    $staging = $null
    if ($backup -and (Test-Path -LiteralPath $backup)) {
        Remove-Item -LiteralPath $backup -Recurse -Force
    }

    Write-Host "Created candidate:"
    Write-Host "  $CandidateDir\$packageName.zip"
    Write-Host "  $CandidateDir\candidate.json"
    Write-Host "  $CandidateDir\SHA256SUMS.txt"
    Write-Host "SHA256 $archiveHash"
    Write-Host "Tests passed=$passed failed=$failed skipped=$skipped"
    Write-Host "Results: $resultsDir"
}
catch {
    [Console]::Error.WriteLine("error: $($_.Exception.Message)")
    if (-not $promoted -and $staging -and (Test-Path -LiteralPath $staging)) {
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
    exit 1
}
