<#
    Removes Nilam.

    By default your layout, notes and settings are kept, so a reinstall puts the
    cards back exactly where you had them. Pass -Purge to delete those too.

    This is also what the Uninstall button under Settings > Apps runs.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Nilam'),
    [switch]$Purge
)

$ErrorActionPreference = 'Continue'
$AppId = 'Nilam'
$AppName = 'Nilam'

function Good ($m) { Write-Host "  OK    $m" -ForegroundColor Green }
function Warn ($m) { Write-Host "  WARN  $m" -ForegroundColor Yellow }

Write-Host ''
Write-Host "$AppName - uninstaller"
Write-Host '==================='

$InstallDir = [IO.Path]::GetFullPath($InstallDir)
$exe = Join-Path $InstallDir 'bin\Nilam.exe'

# This script deletes a whole folder, so make sure it is one of ours first.
# Without the check, a mistyped -InstallDir would remove whatever it named.
$looksInstalled = (Test-Path -LiteralPath $exe) -or (Test-Path -LiteralPath (Join-Path $InstallDir 'src\WidgetApp.cs'))
if ((Test-Path -LiteralPath $InstallDir) -and -not $looksInstalled) {
    Write-Host "  FAIL  $InstallDir does not look like a Nilam install (no bin\Nilam.exe). Nothing was removed." -ForegroundColor Red
    exit 1
}

# The folder being deleted must not be the current directory, and this script
# lives inside it.
Set-Location -LiteralPath ([IO.Path]::GetTempPath())

# Stop the copy running from this folder; its exe cannot be deleted while it is
# loaded. It saves and closes when asked; anything that ignores that is stopped.
$running = @(Get-Process $AppId -ErrorAction SilentlyContinue | Where-Object { $_.Path -and ($_.Path -ieq $exe) })
if ($running.Count -gt 0) {
    try { Start-Process -FilePath $exe -ArgumentList '--exit' -Wait }
    catch { Write-Verbose "could not ask the running copy to exit: $($_.Exception.Message)" }
    foreach ($p in $running) { [void]$p.WaitForExit(5000) }
    $running | Where-Object { -not $_.HasExited } | Stop-Process -Force -ErrorAction SilentlyContinue
    foreach ($p in $running) { [void]$p.WaitForExit(3000) }
    Good 'stopped the running copy'
}

# Start with Windows. Removed only if it points into this install.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$run = (Get-ItemProperty -Path $runKey -Name $AppId -ErrorAction SilentlyContinue).$AppId
if ($run -and $run.ToLowerInvariant().Contains($InstallDir.ToLowerInvariant())) {
    Remove-ItemProperty -Path $runKey -Name $AppId -ErrorAction SilentlyContinue
    Good 'removed the start-with-Windows entry'
}

# The Start menu shortcut, likewise.
$startLnk = Join-Path ([Environment]::GetFolderPath('Programs')) "$AppName.lnk"
if (Test-Path -LiteralPath $startLnk) {
    $s = (New-Object -ComObject WScript.Shell).CreateShortcut($startLnk)
    if (("$($s.TargetPath) $($s.Arguments)").ToLowerInvariant().Contains($InstallDir.ToLowerInvariant())) {
        Remove-Item -LiteralPath $startLnk -Force
        Good 'removed the Start menu shortcut'
    }
}

# The nilam command: this install's bin\cli folder on PATH.
$cli = Join-Path $InstallDir 'bin\cli'
$key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
if ($key) {
    try {
        $raw = [string]$key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        # Split without dropping anything, and write only if our own entry is
        # actually there. A PATH often holds an empty entry or a trailing ";",
        # and those are not ours to tidy away.
        $parts = @($raw -split ';')
        $others = @($parts | Where-Object { $_.TrimEnd('\') -ine $cli.TrimEnd('\') })
        if ($others.Count -ne $parts.Count) {
            # Same value type as before: rewriting PATH as a plain string would
            # stop entries such as %USERPROFILE%\bin from expanding.
            $key.SetValue('Path', ($others -join ';'), $key.GetValueKind('Path'))
            Good 'removed the nilam command from PATH'
        }
    }
    finally { $key.Close() }
}

# The entry under Settings > Apps, if it is this install's.
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppId"
$listed = (Get-ItemProperty -Path $uninstallKey -Name InstallLocation -ErrorAction SilentlyContinue).InstallLocation
if ($listed -and ($listed.TrimEnd('\') -ieq $InstallDir.TrimEnd('\'))) {
    Remove-Item -Path $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue
    Good 'removed the Settings > Apps entry'
}

if (Test-Path -LiteralPath $InstallDir) {
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
    if (Test-Path -LiteralPath $InstallDir) { Warn "some files in $InstallDir could not be deleted - close anything using them and delete the folder" }
    else { Good "deleted $InstallDir" }
}
else {
    Warn "no install found at $InstallDir"
}

# Layout, notes and settings. The second folder is where releases before 2.0,
# when the app was called Desktop Widgets, kept them.
$stateDirs = @((Join-Path $env:APPDATA $AppId), (Join-Path $env:LOCALAPPDATA 'KiroDesktopWidgets')) |
    Where-Object { Test-Path -LiteralPath $_ }
if ($Purge) {
    foreach ($dir in $stateDirs) {
        Remove-Item -LiteralPath $dir -Recurse -Force
        Good "deleted settings and notes ($dir)"
    }
}
elseif ($stateDirs) {
    Write-Host ''
    Write-Host '  Your layout, notes and settings were kept in:'
    foreach ($dir in $stateDirs) { Write-Host "      $dir" }
    Write-Host '  Run this again with -Purge to delete them as well.'
}

Write-Host ''
Write-Host 'Done.'
Write-Host ''
