@echo off
rem ---------------------------------------------------------------------------
rem Double-click installer.
rem
rem This exists so nobody has to type or paste a command. Pasting a PowerShell
rem one-liner loses its `irm` prefix and `| iex` suffix easily - especially when
rem the link is copied out of a chat app - and PowerShell then reports the URL
rem itself as an unknown command. A batch file has no such failure mode, and it
rem is also not subject to PowerShell's execution policy.
rem
rem Works two ways:
rem   - next to install.ps1 (extracted zip or a clone): runs the local copy
rem   - on its own: fetches install.ps1 from the latest published release
rem
rem Arguments are passed straight through to install.ps1, so switches like
rem -NoStartup or -NoLaunch work here exactly as they do on the script.
rem ---------------------------------------------------------------------------

setlocal
cd /d "%~dp0"

if exist "%~dp0install.ps1" goto :local

echo Fetching the installer from the latest release...
echo.
rem The script is saved to a file and run with -File rather than piped into
rem Invoke-Expression: download-and-execute-in-memory from a script host is the
rem shape antivirus heuristics are trained to flag. Tls12 (3072) is forced
rem because this download happens before install.ps1 can do it itself.
set "DWTEMP=%TEMP%\Nilam-install.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072; try { (New-Object Net.WebClient).DownloadFile('https://github.com/jithin-jz/nilam/releases/latest/download/install.ps1', $env:DWTEMP) } catch { Write-Host ('Could not download the installer: ' + $_.Exception.Message) -ForegroundColor Red; exit 1 }"
if errorlevel 1 goto :done
powershell -NoProfile -ExecutionPolicy Bypass -File "%DWTEMP%" %*
del "%DWTEMP%" >nul 2>&1
goto :done

:local
echo Installing from this folder...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*

:done
echo.
echo ---------------------------------------------------------------------------
pause
