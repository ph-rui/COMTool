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
    [string]$Source = "",
    [string]$CandidateDir = "",
    [string]$Version = "0.01",
    [switch]$SkipPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Defaults are resolved in the script body. PowerShell evaluates parameter
# defaults in the caller's scope, where $PSScriptRoot is empty when the script
# is started with `powershell.exe -File`.
if (-not $Source) {
    $Source = Join-Path $PSScriptRoot "../../publish/win-x64"
}
if (-not $CandidateDir) {
    $CandidateDir = Join-Path $PSScriptRoot "../../publish/candidate"
}

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
$gatePlatform = "windows"
$candidatePlatform = "windows-x64"
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
$Source = [IO.Path]::GetFullPath($Source)
$CandidateDir = [IO.Path]::GetFullPath($CandidateDir)

$staging = $null
$promoted = $false
$backup = $null

try {
    $commitOutput = & git -C $repoRoot rev-parse HEAD 2>$null
    $gitStatus = $LASTEXITCODE
    if ($gitStatus -ne 0) {
        throw "could not read the source commit (git exit code $gitStatus)."
    }
    if (-not $commitOutput) {
        throw "could not read the source commit (git produced no output)."
    }
    $commit = ([string]($commitOutput | Select-Object -First 1)).Trim()
    if (-not $commit) {
        throw "could not read the source commit."
    }

    if (-not (Test-Path -LiteralPath $verifyScript -PathType Leaf)) {
        throw "verification gate not found: $verifyScript"
    }

    # Step 1: run verify in a child host. verify.ps1 ends with Exit, so call it
    # through a child process. The call operator passes the path as a distinct
    # argument, so repository paths containing spaces survive intact.
    Write-Host "[pack] Running unified verification gate..."
    $hostName = if ($PSVersionTable.PSEdition -eq "Core") { "pwsh" } else { "powershell.exe" }
    $hostExe = Join-Path $PSHOME $hostName
    if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
        $hostExe = (Get-Process -Id $PID).Path
    }
    & $hostExe -NoProfile -ExecutionPolicy Bypass -File $verifyScript
    $verifyStatus = $LASTEXITCODE
    if ($verifyStatus -ne 0) {
        throw "verification failed with exit code $verifyStatus; no candidate package was created."
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
        $markerPath = Join-Path $Source ".candidate-commit"
        if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
            throw "-SkipPublish requires $markerPath; run a real publish on this commit first."
        }
        $markedCommit = (Get-Content -LiteralPath $markerPath -Raw).Trim()
        if ($markedCommit -ne $commit) {
            throw "publish source $Source was built from '$markedCommit', but current HEAD is '$commit'."
        }
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
        Set-Content -LiteralPath (Join-Path $publishSource ".candidate-commit") `
            -Value $commit -Encoding ASCII
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
    # Build provenance belongs next to the publish source, not in the package.
    Remove-Item -LiteralPath (Join-Path $packageDir "app/.candidate-commit") `
        -Force -ErrorAction SilentlyContinue

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

    $builtAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

    $candidate = [ordered]@{
        version    = $Version
        platform   = $candidatePlatform
        sha256     = $archiveHash
        builtAt    = $builtAt
        commit     = $commit
        resultsDir = $resultsDir
        passed     = $passed
        failed     = $failed
        skipped    = $skipped
    }
    $candidate | ConvertTo-Json -Depth 3 |
        Set-Content -LiteralPath (Join-Path $staging "candidate.json") -Encoding UTF8

    # Step 5: enforce package-phase coverage rules before promotion.
    if (-not (Test-Path -LiteralPath $gateDll -PathType Leaf)) {
        throw "TestGate build output not found: $gateDll"
    }
    Write-Host "[pack] Running package-phase test gate..."
    $gateLog = Join-Path $resultsDir "testgate-package.log"
    & $dotnet $gateDll `
        --phase package `
        --platform $gatePlatform `
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

    # Step 6: promotion preconditions. Directory replacement is only atomic on
    # the same volume, so refuse before moving anything across filesystems.
    $candidateParent = [IO.Path]::GetFullPath((Split-Path -Parent $CandidateDir))
    New-Item -ItemType Directory -Path $candidateParent -Force | Out-Null
    $stagingVolume = [IO.Path]::GetPathRoot($staging)
    $candidateVolume = [IO.Path]::GetPathRoot($candidateParent)
    if (-not [string]::Equals($stagingVolume, $candidateVolume, [StringComparison]::OrdinalIgnoreCase)) {
        throw "refusing to promote: staging '$staging' and candidate directory '$candidateParent' are on different filesystems."
    }

    # A real publish also refreshes the reusable publish source, including its
    # .candidate-commit marker. Only do this after the package gate so a failed
    # run cannot make a later -SkipPublish look provenance-clean.
    if ($SkipPublish) {
        Remove-Item -LiteralPath $publishSource -Recurse -Force
    }
    else {
        $sourceBackup = "$Source.previous-$stagingId"
        if (Test-Path -LiteralPath $Source) {
            Move-Item -LiteralPath $Source -Destination $sourceBackup -Force
        }
        try {
            Move-Item -LiteralPath $publishSource -Destination $Source -Force
        }
        catch {
            $syncError = $_.Exception.Message
            if (Test-Path -LiteralPath $Source) {
                Remove-Item -LiteralPath $Source -Recurse -Force -ErrorAction SilentlyContinue
            }
            if (Test-Path -LiteralPath $sourceBackup) {
                try {
                    Move-Item -LiteralPath $sourceBackup -Destination $Source -Force
                }
                catch {
                    throw "could not update $Source ($syncError) and could not restore $sourceBackup; the candidate was not promoted."
                }
            }
            throw "could not update the reusable publish source $Source ($syncError); the candidate was not promoted."
        }
        if (Test-Path -LiteralPath $sourceBackup) {
            Remove-Item -LiteralPath $sourceBackup -Recurse -Force
        }
    }

    # Step 7: atomically replace the candidate only after every gate passed.
    if (Test-Path -LiteralPath $CandidateDir) {
        $backup = "$CandidateDir.previous-$stagingId"
        Move-Item -LiteralPath $CandidateDir -Destination $backup -Force
    }

    try {
        Move-Item -LiteralPath $staging -Destination $CandidateDir -Force
    }
    catch {
        $moveError = $_.Exception.Message
        if ($backup) {
            # Remove any partial target, then verify the restore actually
            # happened before claiming the previous candidate was kept.
            if (Test-Path -LiteralPath $CandidateDir) {
                Remove-Item -LiteralPath $CandidateDir -Recurse -Force -ErrorAction SilentlyContinue
            }
            try {
                Move-Item -LiteralPath $backup -Destination $CandidateDir -Force
            }
            catch {
                throw "candidate promotion failed ($moveError) and the previous candidate could not be restored from $backup : $($_.Exception.Message)"
            }
            throw "could not promote the staged candidate ($moveError); the previous candidate was restored."
        }
        throw "could not promote the staged candidate: $moveError"
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
