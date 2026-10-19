#Requires -Version 5.1
<#
.SYNOPSIS
Installs the COMTool Avalonia Beta beside the Python COMTool installation.

.DESCRIPTION
Per-user installation; no administrator rights are required. The Python app,
its config (%APPDATA%\comtool) and its launcher are never modified. The Beta
keeps its own config in %APPDATA%\COMTool-Avalonia.

.PARAMETER Source
Directory containing COMTool.App.exe. Defaults to the app\ folder next to this
script, which is how the release zip is laid out.

.PARAMETER InstallDir
Application payload directory. Defaults to
%LOCALAPPDATA%\Programs\COMTool Avalonia.

.PARAMETER SupportDir
Directory that keeps the uninstaller and install metadata. Defaults to
%LOCALAPPDATA%\COMTool Avalonia.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Source = (Join-Path $PSScriptRoot "app"),
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\COMTool Avalonia"),
    [string]$SupportDir = (Join-Path $env:LOCALAPPDATA "COMTool Avalonia"),
    [string]$Version = "0.01",
    [switch]$NoShortcuts,
    [switch]$NoLaunch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($env:OS -ne "Windows_NT") {
    throw "This installer targets Windows."
}

$AppName = "COMTool Avalonia Beta"
$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\COMToolAvaloniaBeta"

function Get-SafeAppPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $full = [IO.Path]::GetFullPath($Path)
    if ($full -notmatch "(?i)COMTool Avalonia") {
        throw "Refusing to modify a path without 'COMTool Avalonia' in it: $full"
    }

    return $full.TrimEnd("\")
}

function Stop-InstalledApp {
    param([Parameter(Mandatory = $true)][string]$Root)

    Get-Process -Name "COMTool.App" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Path -and
            $_.Path.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)
        } |
        ForEach-Object {
            Write-Host "Stopping running $AppName (PID $($_.Id))..."
            Stop-Process -Id $_.Id -Force
        }
    Start-Sleep -Milliseconds 500
}

function New-AppShortcut {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $parent = Split-Path -Parent $ShortcutPath
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = "$TargetPath,0"
    $shortcut.Description = $AppName
    $shortcut.Save()
}

$Source = (Resolve-Path -LiteralPath $Source).Path
$InstallDir = Get-SafeAppPath $InstallDir
$SupportDir = Get-SafeAppPath $SupportDir

$sourceExe = Join-Path $Source "COMTool.App.exe"
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw "COMTool.App.exe was not found in '$Source'."
}

if (-not $PSCmdlet.ShouldProcess($InstallDir, "Install $AppName $Version")) {
    return
}

Write-Host "Installing $AppName $Version..."
Stop-InstalledApp $InstallDir

$staging = "$InstallDir.staging-$([Guid]::NewGuid().ToString('N'))"
$backup = "$InstallDir.old-$([Guid]::NewGuid().ToString('N'))"
$hadExisting = Test-Path -LiteralPath $InstallDir

try {
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    Copy-Item -Path (Join-Path $Source "*") -Destination $staging -Recurse -Force

    if ($hadExisting) {
        Move-Item -LiteralPath $InstallDir -Destination $backup
    }

    Move-Item -LiteralPath $staging -Destination $InstallDir

    if ($hadExisting) {
        Remove-Item -LiteralPath $backup -Recurse -Force
    }
}
catch {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    }

    if ($hadExisting -and (Test-Path -LiteralPath $backup) -and
        -not (Test-Path -LiteralPath $InstallDir)) {
        Move-Item -LiteralPath $backup -Destination $InstallDir
    }

    throw
}

New-Item -ItemType Directory -Path $SupportDir -Force | Out-Null
$uninstaller = Join-Path $SupportDir "uninstall.ps1"
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "uninstall.ps1") `
    -Destination $uninstaller -Force

@{
    name = $AppName
    version = $Version
    installDir = $InstallDir
    source = $Source
    installedAt = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $SupportDir "install.json") `
    -Encoding UTF8

if (-not $NoShortcuts) {
    $startMenu = Join-Path $env:APPDATA `
        "Microsoft\Windows\Start Menu\Programs\$AppName\$AppName.lnk"
    $desktop = [Environment]::GetFolderPath("Desktop")
    $installedExe = Join-Path $InstallDir "COMTool.App.exe"
    New-AppShortcut -ShortcutPath $startMenu -TargetPath $installedExe `
        -WorkingDirectory $InstallDir
    New-AppShortcut -ShortcutPath (Join-Path $desktop "$AppName.lnk") `
        -TargetPath (Join-Path $InstallDir "COMTool.App.exe") `
        -WorkingDirectory $InstallDir
}

$uninstallCommand = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}" -InstallDir "{1}" -SupportDir "{2}"' -f `
    $uninstaller, $InstallDir, $SupportDir
New-Item -Path $UninstallKey -Force | Out-Null
New-ItemProperty -Path $UninstallKey -Name "DisplayName" -Value $AppName `
    -PropertyType String -Force | Out-Null
New-ItemProperty -Path $UninstallKey -Name "DisplayVersion" -Value $Version `
    -PropertyType String -Force | Out-Null
New-ItemProperty -Path $UninstallKey -Name "Publisher" -Value "COMTool" `
    -PropertyType String -Force | Out-Null
New-ItemProperty -Path $UninstallKey -Name "InstallLocation" -Value $InstallDir `
    -PropertyType String -Force | Out-Null
New-ItemProperty -Path $UninstallKey -Name "DisplayIcon" `
    -Value (Join-Path $InstallDir "COMTool.App.exe") -PropertyType String -Force |
    Out-Null
New-ItemProperty -Path $UninstallKey -Name "UninstallString" `
    -Value $uninstallCommand -PropertyType String -Force | Out-Null
New-ItemProperty -Path $UninstallKey -Name "NoModify" -Value 1 `
    -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $UninstallKey -Name "NoRepair" -Value 1 `
    -PropertyType DWord -Force | Out-Null

Write-Host "Installed to $InstallDir"
Write-Host "Config stays in $env:APPDATA\COMTool-Avalonia and Python is untouched."

if (-not $NoLaunch) {
    Start-Process -FilePath (Join-Path $InstallDir "COMTool.App.exe")
}
