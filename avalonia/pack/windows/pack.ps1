#Requires -Version 5.1
<#
.SYNOPSIS
Builds the COMTool Avalonia Beta Windows release zip.

.DESCRIPTION
Publishes the self-contained single-file app unless -SkipPublish is used, then
lays out the release zip as:

  install.ps1
  uninstall.ps1
  README.txt
  app\COMTool.App.exe

Run install.ps1 from the expanded zip. The script is per-user and does not
require administrator rights.
#>
[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot "../../publish/win-x64"),
    [string]$OutputDir = (Join-Path $PSScriptRoot "../../publish/package"),
    [string]$Version = "0.01",
    [switch]$SkipPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../../.."))
$project = Join-Path $repoRoot "avalonia/src/COMTool.App/COMTool.App.csproj"

if (-not $SkipPublish) {
    # ReadyToRun removes most startup JIT; leaving single-file compression off
    # avoids decompressing the native payload on every launch. The package is
    # larger, but the startup comparison is in docs/fixtures/phase1.
    Write-Host "Publishing self-contained win-x64 app..."
    & dotnet publish $project `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=false `
        -p:PublishReadyToRun=true `
        -p:DebugType=None -p:DebugSymbols=false `
        -o $Source
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}

$Source = (Resolve-Path -LiteralPath $Source).Path
$exe = Join-Path $Source "COMTool.App.exe"
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "COMTool.App.exe was not found in '$Source'."
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).Path
$stageName = "COMTool-Avalonia-Beta-win-x64-$Version"
$stage = Join-Path $OutputDir $stageName
$zip = Join-Path $OutputDir "$stageName.zip"

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}

New-Item -ItemType Directory -Path (Join-Path $stage "app") -Force | Out-Null
Copy-Item -Path (Join-Path $Source "*") -Destination (Join-Path $stage "app") `
    -Recurse -Force
Get-ChildItem -Path (Join-Path $stage "app") -Filter "*.pdb" -File |
    Remove-Item -Force

Copy-Item -LiteralPath (Join-Path $PSScriptRoot "install.ps1") `
    -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "uninstall.ps1") `
    -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "README.txt") `
    -Destination $stage -Force

if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip `
    -CompressionLevel Optimal

$hash = Get-FileHash -LiteralPath $zip -Algorithm SHA256
$checksums = Join-Path $OutputDir "SHA256SUMS.txt"
"$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $zip)" |
    Set-Content -LiteralPath $checksums -Encoding ASCII

Write-Host "Created $zip"
Write-Host "SHA256 $($hash.Hash)"
Write-Host "Created $checksums"
