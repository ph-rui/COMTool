#Requires -Version 5.1
<#
.SYNOPSIS
Uninstalls COMTool Avalonia Beta.

.DESCRIPTION
Removes the Beta payload, shortcuts and Add/Remove Programs entry. The Beta
config in %APPDATA%\COMTool-Avalonia is kept unless -RemoveConfig is supplied.
The Python COMTool app, config and launcher are never touched.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\COMTool Avalonia"),
    [string]$SupportDir = (Join-Path $env:LOCALAPPDATA "COMTool Avalonia"),
    [switch]$RemoveConfig
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($env:OS -ne "Windows_NT") {
    throw "This uninstaller targets Windows."
}

$AppName = "COMTool Avalonia Beta"
$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\COMToolAvaloniaBeta"

function Get-SafeAppPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $full = [IO.Path]::GetFullPath($Path)
    if ($full -notmatch "(?i)COMTool Avalonia") {
        throw "Refusing to modify a path without 'COMTool Avalonia' in it: $full"
    }

    return $full.TrimEnd([char]'\')
}

$InstallDir = Get-SafeAppPath $InstallDir
$SupportDir = Get-SafeAppPath $SupportDir

if (-not $PSCmdlet.ShouldProcess($InstallDir, "Uninstall $AppName")) {
    return
}

Write-Host "Uninstalling $AppName..."
Get-Process -Name "COMTool.App" -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Path -and
        $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase)
    } |
    ForEach-Object {
        Write-Host "Stopping running $AppName (PID $($_.Id))..."
        Stop-Process -Id $_.Id -Force
    }
Start-Sleep -Milliseconds 500

$startMenu = Join-Path $env:APPDATA `
    "Microsoft\Windows\Start Menu\Programs\$AppName"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) `
    "$AppName.lnk"
Remove-Item -LiteralPath $startMenu -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $desktopShortcut -Force -ErrorAction SilentlyContinue

Remove-Item -Path $UninstallKey -Recurse -Force -ErrorAction SilentlyContinue

if ($RemoveConfig) {
    $config = Join-Path $env:APPDATA "COMTool-Avalonia"
    if ($config -notmatch "(?i)COMTool-Avalonia") {
        throw "Refusing to remove unexpected config path: $config"
    }

    Remove-Item -LiteralPath $config -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Removed Beta config $config"
}
else {
    Write-Host "Kept Beta config in $env:APPDATA\COMTool-Avalonia"
}

Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction SilentlyContinue

# The uninstall script lives under SupportDir, so Windows may still have it
# open. Schedule a delayed cleanup of the support folder and this command file.
$cleanup = Join-Path $env:TEMP `
    "comtool-avalonia-cleanup-$([Guid]::NewGuid().ToString('N')).cmd"
$command = @"
@echo off
timeout /t 2 /nobreak >nul
rmdir /s /q "$InstallDir" >nul 2>&1
rmdir /s /q "$SupportDir" >nul 2>&1
del "%~f0" >nul 2>&1
"@
Set-Content -LiteralPath $cleanup -Value $command -Encoding ASCII
Start-Process -FilePath "cmd.exe" `
    -ArgumentList "/c", "`"$cleanup`"" -WindowStyle Hidden

Write-Host "Removed $InstallDir"
Write-Host "Python COMTool was not changed."
