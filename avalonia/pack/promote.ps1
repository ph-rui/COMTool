#Requires -Version 5.1
<#
.SYNOPSIS
Promotes a signed COMTool Avalonia candidate to the formal package directory.

.DESCRIPTION
Promotion is deliberately not forceable. The candidate archive must match the
SHA256 in candidate.json, a single manual sign-off file must cover every manual
item in the feature matrix with PASS evidence and an 8 hour soak, and the
TestGate sign-off validator must exit 0. Only then is a staging directory built
and atomically swapped into the output directory. Any failure leaves the
existing formal package untouched.

.PARAMETER CandidateDir
Candidate directory containing candidate.json, one .zip/.tar.gz archive and
SHA256SUMS.txt. Relative paths resolve from the repository root.

.PARAMETER Signoff
Manual sign-off JSON. When omitted, exactly one
docs/testing/signoffs/<windows|linux>-<version>-<sha12>*.json must exist.

.PARAMETER OutputDir
Formal package directory (default: avalonia/publish/package). Relative paths
resolve from the repository root.

.NOTES
There is no -Force switch. Formal packages only come from this script.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CandidateDir,
    [string]$Signoff = "",
    [string]$OutputDir = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
# Keep native exit codes available through $LASTEXITCODE on PowerShell 7+.
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$gateDll = Join-Path $repoRoot "avalonia/tools/COMTool.TestGate/bin/Release/net10.0/COMTool.TestGate.dll"
$matrixPath = Join-Path $repoRoot "docs/testing/feature-test-matrix.md"
$manualPath = Join-Path $repoRoot "docs/testing/manual-acceptance.md"
$signoffRoot = Join-Path $repoRoot "docs/testing/signoffs"

function Resolve-RepoPath {
    param([string]$Path)

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

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

function Get-RequiredString {
    param($Object, [string]$Name)

    $property = $Object.PSObject.Properties[$Name]
    if (-not $property -or $null -eq $property.Value) {
        throw "candidate.json is missing '$Name'."
    }

    $value = [string]$property.Value
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "candidate.json has an empty '$Name'."
    }

    return $value
}

function Get-RepoRelativePath {
    param([string]$Path)

    $full = [IO.Path]::GetFullPath($Path)
    $separator = [IO.Path]::DirectorySeparatorChar
    $root = $repoRoot.TrimEnd([char[]]@('\', '/'))
    if ($full.StartsWith($root + $separator, [StringComparison]::OrdinalIgnoreCase)) {
        return ($full.Substring($root.Length + 1) -replace '\\', '/')
    }

    return $full
}

$staging = $null
$backup = $null

try {
    $dotnet = Get-Dotnet
    $CandidateDir = Resolve-RepoPath $CandidateDir
    if (-not (Test-Path -LiteralPath $CandidateDir -PathType Container)) {
        throw "candidate directory does not exist: $CandidateDir"
    }

    $candidateJsonPath = Join-Path $CandidateDir "candidate.json"
    if (-not (Test-Path -LiteralPath $candidateJsonPath -PathType Leaf)) {
        throw "candidate.json does not exist: $candidateJsonPath"
    }

    try {
        $candidate = Get-Content -LiteralPath $candidateJsonPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "candidate.json is not valid JSON: $candidateJsonPath ($($_.Exception.Message))"
    }

    if ($null -eq $candidate -or $candidate -isnot [pscustomobject]) {
        throw "candidate.json must contain a JSON object: $candidateJsonPath"
    }

    $version = Get-RequiredString -Object $candidate -Name "version"
    $platform = Get-RequiredString -Object $candidate -Name "platform"
    $candidateSha = (Get-RequiredString -Object $candidate -Name "sha256").ToLowerInvariant()
    if ($candidateSha -notmatch '^[0-9a-f]{64}$') {
        throw "candidate.json sha256 must be exactly 64 hexadecimal characters."
    }

    $archives = @(Get-ChildItem -LiteralPath $CandidateDir -File | Where-Object {
            $_.Name -like '*.zip' -or $_.Name -like '*.tar.gz'
        })
    if ($archives.Count -ne 1) {
        throw "expected exactly one candidate archive (.zip or .tar.gz) in ${CandidateDir}, found $($archives.Count)."
    }
    $archive = $archives[0]

    $checksumFiles = @(Get-ChildItem -LiteralPath $CandidateDir -File |
        Where-Object { $_.Name -eq 'SHA256SUMS.txt' })
    if ($checksumFiles.Count -ne 1) {
        throw "expected exactly one SHA256SUMS.txt in ${CandidateDir}, found $($checksumFiles.Count)."
    }
    $checksumFile = $checksumFiles[0]

    $archiveHash = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($archiveHash -ne $candidateSha) {
        throw "candidate SHA256 mismatch: candidate.json declares ${candidateSha}, but $($archive.Name) is ${archiveHash}."
    }

    if ([string]::IsNullOrWhiteSpace($Signoff)) {
        if ($platform -like 'windows*') {
            $os = "windows"
        }
        elseif ($platform -like 'linux*') {
            $os = "linux"
        }
        else {
            throw "cannot infer a sign-off platform from candidate platform '$platform'."
        }

        if (-not (Test-Path -LiteralPath $signoffRoot -PathType Container)) {
            throw "sign-off directory does not exist: $signoffRoot"
        }

        $sha12 = $candidateSha.Substring(0, 12)
        $pattern = "$os-$version-$sha12*.json"
        $signoffMatches = @(Get-ChildItem -LiteralPath $signoffRoot -File |
            Where-Object { $_.Name -like $pattern })
        if ($signoffMatches.Count -eq 0) {
            throw "no sign-off file matches docs/testing/signoffs/$pattern; run with -Signoff <json> after manual acceptance."
        }
        if ($signoffMatches.Count -gt 1) {
            throw "multiple sign-off files match docs/testing/signoffs/$pattern; keep exactly one."
        }
        $Signoff = $signoffMatches[0].FullName
    }
    else {
        $Signoff = Resolve-RepoPath $Signoff
    }

    if (-not (Test-Path -LiteralPath $Signoff -PathType Leaf)) {
        throw "sign-off file does not exist: $Signoff"
    }

    if (-not (Test-Path -LiteralPath $gateDll -PathType Leaf)) {
        throw "TestGate build output not found: $gateDll"
    }

    Write-Host "[promote] Validating sign-off against the compiled TestGate..."
    & $dotnet $gateDll `
        --validate-signoff `
        --candidate $candidateJsonPath `
        --signoff $Signoff `
        --manual $manualPath `
        --matrix $matrixPath
    $gateStatus = $LASTEXITCODE
    if ($gateStatus -ne 0) {
        throw "sign-off validation failed with exit code $gateStatus; no formal package was created."
    }

    if ([string]::IsNullOrWhiteSpace($OutputDir)) {
        $OutputDir = Join-Path $repoRoot "avalonia/publish/package"
    }
    else {
        $OutputDir = Resolve-RepoPath $OutputDir
    }

    $outputParent = Split-Path -Parent $OutputDir
    if ([string]::IsNullOrWhiteSpace($outputParent)) {
        throw "output directory must have a parent: $OutputDir"
    }

    if ([string]::Equals($OutputDir, $CandidateDir, [StringComparison]::OrdinalIgnoreCase)) {
        throw "output directory must differ from the candidate directory."
    }

    New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
    $staging = Join-Path $outputParent (".promote-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $staging | Out-Null

    $stagingVolume = [IO.Path]::GetPathRoot($staging)
    $outputVolume = [IO.Path]::GetPathRoot($OutputDir)
    if (-not [string]::Equals($stagingVolume, $outputVolume, [StringComparison]::OrdinalIgnoreCase)) {
        throw "refusing to promote: staging '$staging' and output '$OutputDir' are on different filesystems."
    }

    $stagedArchive = Join-Path $staging $archive.Name
    $stagedChecksums = Join-Path $staging "SHA256SUMS.txt"
    Copy-Item -LiteralPath $archive.FullName -Destination $stagedArchive -Force
    Copy-Item -LiteralPath $checksumFile.FullName -Destination $stagedChecksums -Force

    $releaseHash = (Get-FileHash -LiteralPath $stagedArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($releaseHash -ne $candidateSha) {
        throw "release archive SHA256 mismatch: staged ${releaseHash}, candidate ${candidateSha}."
    }

    $promotedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $release = [ordered]@{
        candidateSha256 = $candidateSha
        releaseSha256   = $releaseHash
        signoffPath     = Get-RepoRelativePath $Signoff
        promotedAt      = $promotedAt
    }
    $release | ConvertTo-Json -Depth 3 |
        Set-Content -LiteralPath (Join-Path $staging "release.json") -Encoding UTF8

    if (Test-Path -LiteralPath $OutputDir) {
        $backup = "$OutputDir.previous-" + [Guid]::NewGuid().ToString("N")
        Move-Item -LiteralPath $OutputDir -Destination $backup -Force
    }

    try {
        Move-Item -LiteralPath $staging -Destination $OutputDir -Force
    }
    catch {
        $moveError = $_.Exception.Message
        if ($backup) {
            if (Test-Path -LiteralPath $OutputDir) {
                Remove-Item -LiteralPath $OutputDir -Recurse -Force -ErrorAction SilentlyContinue
            }
            try {
                Move-Item -LiteralPath $backup -Destination $OutputDir -Force
                $backup = $null
            }
            catch {
                throw "could not replace $OutputDir ($moveError) and could not restore the previous output from $backup : $($_.Exception.Message)"
            }
        }
        throw "could not replace $OutputDir ($moveError); the previous output was restored."
    }

    $staging = $null
    if ($backup -and (Test-Path -LiteralPath $backup)) {
        Remove-Item -LiteralPath $backup -Recurse -Force
    }

    Write-Host "Promoted signed candidate:"
    Write-Host "  $(Join-Path $OutputDir $archive.Name)"
    Write-Host "  $(Join-Path $OutputDir 'SHA256SUMS.txt')"
    Write-Host "  $(Join-Path $OutputDir 'release.json')"
    Write-Host "SHA256 $releaseHash"
    Write-Host "Signoff $(Get-RepoRelativePath $Signoff)"
    Write-Host "PromotedAt $promotedAt"
}
catch {
    [Console]::Error.WriteLine("ERROR: $($_.Exception.Message)")
    if ($staging -and (Test-Path -LiteralPath $staging)) {
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
    exit 1
}
