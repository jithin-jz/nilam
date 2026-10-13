<#
    Installs Nilam - desktop widgets for Windows - for the current user.

    From a published release - this is what the one-liner in the README runs:
        irm https://github.com/jithin-jz/nilam/releases/latest/download/install.ps1 | iex

    From a clone or an extracted zip:
        .\install.ps1

    No admin rights, no .NET SDK and no NuGet packages are required. Everything
    it compiles against ships with Windows.

    -InstallDir <path>     where to install (default %LOCALAPPDATA%\Nilam)
    -NoStartup             do not start Nilam with Windows
    -NoLaunch              install without starting it
    -NoPath                do not put the nilam command on PATH
    -Ref <tag or branch>   download and install that ref instead of this copy
    -SourcePath <path>     install from a source folder or zip already on disk
    -SourceSha256 <hash>   refuse a -SourcePath zip that does not match
#>
# The parameters are read inside the child scope further down, which a static
# check cannot see.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSReviewUnusedParameter', '', Justification = 'Used inside the child scope.')]
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Nilam'),
    [switch]$NoStartup,
    [switch]$NoLaunch,
    [switch]$NoPath,
    [string]$Ref,
    [string]$SourcePath,
    [string]$SourceSha256
)

# Filled in by the release workflow when this file is published as a release
# asset, which ties a downloaded installer to one exact source archive. In the
# repository both are empty, meaning "an unreleased copy".
$ReleaseTag = ''
$ReleaseSourceSha256 = ''

$failed = $false
try {
    # Everything runs in a child scope. Piped into iex, this script executes
    # inside the user's own session, and without the scope its settings and
    # helper functions would be left behind there.
    & {
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'    # the progress bar makes downloads several times slower

        $Repo = 'jithin-jz/nilam'
        $AppId = 'Nilam'            # folder, registry and process name
        $AppName = 'Nilam'          # the name a person reads
        $Publisher = 'jithin-jz'

        function Say ($m) { Write-Host $m }
        function Good ($m) { Write-Host "  OK    $m" -ForegroundColor Green }
        function Warn ($m) { Write-Host "  WARN  $m" -ForegroundColor Yellow }

        function Get-File ($url, $path) {
            try { Invoke-WebRequest -Uri $url -OutFile $path -UseBasicParsing }
            catch { throw "download failed ($url): $($_.Exception.Message)" }
        }

        # Runs a .cmd and returns its exit code. The error preference is relaxed
        # for the call: under 'Stop', Windows PowerShell turns any line a native
        # program writes to stderr into a terminating error.
        function Invoke-Cmd ($script) {
            $previous = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try {
                & cmd.exe /c "`"$script`"" 2>&1 | ForEach-Object { Write-Host "    $_" }
                return $LASTEXITCODE
            }
            finally { $ErrorActionPreference = $previous }
        }

        function Remove-Tree ($path) {
            # A file can stay locked for a moment after its process exits.
            for ($try = 1; $try -le 5; $try++) {
                try { Remove-Item -LiteralPath $path -Recurse -Force; return }
                catch { if ($try -eq 5) { throw "could not replace $path - is something using it? ($($_.Exception.Message))" }; Start-Sleep -Milliseconds 400 }
            }
        }

        function New-Shortcut ($path, $target, $workDir, $description) {
            $shell = New-Object -ComObject WScript.Shell
            $lnk = $shell.CreateShortcut($path)
            $lnk.TargetPath = $target
            $lnk.WorkingDirectory = $workDir
            $lnk.Description = $description
            $lnk.IconLocation = "$target,0"
            $lnk.Save()
        }

        # Adds or removes one folder in the user's PATH. The registry is edited
        # directly, keeping the value's type: [Environment]::SetEnvironmentVariable
        # rewrites PATH as a plain string, which stops entries such as
        # %USERPROFILE%\bin from expanding ever again.
        function Set-UserPath ($dir, [bool]$present) {
            $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
            if (-not $key) { return $false }
            try {
                $raw = [string]$key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                $kind = [Microsoft.Win32.RegistryValueKind]::ExpandString
                if ($key.GetValueNames() -contains 'Path') { $kind = $key.GetValueKind('Path') }
                # Split without dropping anything, and write nothing unless our
                # own entry has to change. A PATH often holds an empty entry or
                # a trailing ";", and those are not ours to tidy away.
                $parts = @($raw -split ';')
                $others = @($parts | Where-Object { $_.TrimEnd('\') -ine $dir.TrimEnd('\') })
                $isThere = $others.Count -ne $parts.Count
                if ($present -eq $isThere) { return $false }
                if (-not $present) { $new = $others -join ';' }
                elseif ($raw -eq '') { $new = $dir }
                # Keep a trailing ";" trailing, so that removing the entry again
                # gives back exactly the value that was there before.
                elseif ($raw.EndsWith(';')) { $new = $raw + $dir + ';' }
                else { $new = $raw + ';' + $dir }
                $key.SetValue('Path', $new, $kind)
                return $true
            }
            finally { $key.Close() }
        }

        # Tells running programs (Explorer, and so new terminals) that the
        # environment changed. Best effort: without it the change still applies
        # from the next sign-in.
        function Send-EnvironmentChanged {
            try {
                if (-not ('NilamInstall.Native' -as [type])) {
                    Add-Type -Namespace NilamInstall -Name Native -MemberDefinition '[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);'
                }
                $result = [UIntPtr]::Zero
                [void][NilamInstall.Native]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [UIntPtr]::Zero, 'Environment', 2, 2000, [ref]$result)
            }
            catch { Write-Verbose "could not announce the PATH change: $($_.Exception.Message)" }
        }

        Say ''
        Say "$AppName - installer"
        Say '================='

        # Windows PowerShell 5.1 can still default to TLS 1.0, which GitHub refuses.
        try {
            [Net.ServicePointManager]::SecurityProtocol =
                [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        }
        catch { Write-Verbose "could not enable TLS 1.2 explicitly: $($_.Exception.Message)" }

        # ------------------------------------------------------------ requirements
        # The whole build depends on the C# compiler that ships inside Windows.
        $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
        if (-not (Test-Path $csc)) {
            throw ".NET Framework 4.x compiler not found at $csc. Enable .NET Framework 4.8 in 'Turn Windows features on or off', then run this again."
        }
        Good 'in-box C# compiler found'

        # The media card is compiled against Windows.Media.Control, which first
        # appeared in Windows 10 version 1809. On anything older the build
        # itself fails, so say so up front instead.
        $os = [Environment]::OSVersion.Version
        if ($os.Major -lt 10 -or $os.Build -lt 17763) {
            throw "Windows 10 version 1809 (build 17763) or later is required; this is build $($os.Build)."
        }
        Good "Windows build $($os.Build)"

        $InstallDir = [IO.Path]::GetFullPath($InstallDir)
        $exe = Join-Path $InstallDir 'bin\Nilam.exe'

        # Until 2.0 this app was called Desktop Widgets and lived in
        # %LOCALAPPDATA%\DesktopWidgets. Installing to the default folder
        # replaces that copy. Installing anywhere else leaves it alone, which
        # is also what keeps a test install from touching a real one.
        $oldDir = Join-Path $env:LOCALAPPDATA 'DesktopWidgets'
        $oldExe = Join-Path $oldDir 'bin\DesktopWidgets.exe'
        $isDefaultDir = $InstallDir.TrimEnd('\') -ieq (Join-Path $env:LOCALAPPDATA $AppId)
        $replaceOld = $isDefaultDir -and (Test-Path -LiteralPath $oldExe)
        $upgrade = (Test-Path -LiteralPath $exe) -or $replaceOld

        $temp = Join-Path ([IO.Path]::GetTempPath()) ('nilam-install-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Force $temp | Out-Null
        try {
            # ------------------------------------------------------------ get source
            # Works from a clone, from an extracted zip, and from the piped
            # one-liner, where $PSScriptRoot is empty because there is no script
            # file on disk.
            $srcRoot = $null
            $zip = $null
            $expected = $SourceSha256

            if ($SourcePath) {
                if (-not (Test-Path -LiteralPath $SourcePath)) { throw "-SourcePath '$SourcePath' does not exist." }
                $resolved = (Resolve-Path -LiteralPath $SourcePath).Path
                if (Test-Path -LiteralPath $resolved -PathType Container) { $srcRoot = $resolved } else { $zip = $resolved }
            }
            elseif (-not $Ref -and $PSScriptRoot -and (Test-Path (Join-Path $PSScriptRoot 'src\WidgetApp.cs'))) {
                $srcRoot = $PSScriptRoot
                Good "installing from this folder: $srcRoot"
            }
            else {
                $want = 'main'
                if ($Ref) { $want = $Ref } elseif ($ReleaseTag) { $want = $ReleaseTag }
                $zip = Join-Path $temp 'source.zip'
                Say ''
                if ($want -match '^v\d') {
                    Say "  Downloading release $want ..."
                    try {
                        Get-File "https://github.com/$Repo/releases/download/$want/nilam-source.zip" $zip
                        if (-not $expected -and $want -eq $ReleaseTag) { $expected = $ReleaseSourceSha256 }
                    }
                    catch {
                        # A tag with no release assets: made by hand, or its
                        # release is still being built.
                        Warn "release $want has no source asset; using the tag's own archive, which is not checksum-verified"
                        Get-File "https://github.com/$Repo/archive/refs/tags/$want.zip" $zip
                    }
                }
                else {
                    Warn "installing the unreleased '$want' branch rather than a published release"
                    Get-File "https://github.com/$Repo/archive/refs/heads/$want.zip" $zip
                }
                Good 'source downloaded'
            }

            if ($zip) {
                if ($expected) {
                    $actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
                    if ($actual -ine $expected) {
                        throw "the source archive does not match its published checksum (expected $expected, got $actual). Nothing was installed."
                    }
                    Good 'checksum verified'
                }
                $unzipped = Join-Path $temp 'source'
                Expand-Archive -LiteralPath $zip -DestinationPath $unzipped -Force
                if (Test-Path (Join-Path $unzipped 'src\WidgetApp.cs')) { $srcRoot = $unzipped }
                else {
                    $srcRoot = (Get-ChildItem $unzipped -Directory |
                        Where-Object { Test-Path (Join-Path $_.FullName 'src\WidgetApp.cs') } |
                        Select-Object -First 1).FullName
                }
                if (-not $srcRoot) { throw 'the archive does not contain src\WidgetApp.cs' }
            }
            elseif ($expected) {
                Warn '-SourceSha256 applies to a zip and was ignored for a folder'
            }

            # ----------------------------------------------------------------- build
            # Built in a staging folder first. The installed copy is not touched,
            # and keeps running, until there is a working exe to replace it
            # with: a build that fails must not leave the user with no widgets.
            $stage = Join-Path $temp 'stage'
            New-Item -ItemType Directory -Force $stage | Out-Null
            foreach ($item in 'src', 'cli', 'assets', 'fonts', 'build.cmd', 'app.manifest', 'check-font.ps1',
                'install.ps1', 'install.cmd', 'uninstall.ps1', 'README.md', 'LICENSE', 'NOTICE', 'VERSION') {
                $from = Join-Path $srcRoot $item
                if (Test-Path -LiteralPath $from) { Copy-Item -LiteralPath $from -Destination $stage -Recurse -Force }
            }
            foreach ($needed in 'src\WidgetApp.cs', 'build.cmd', 'VERSION', 'app.manifest', 'assets\app.ico') {
                if (-not (Test-Path (Join-Path $stage $needed))) { throw "the source is incomplete: $needed is missing" }
            }
            $version = ([string](Get-Content (Join-Path $stage 'VERSION') -TotalCount 1)).Trim()

            Say ''
            Say "  Building $version ..."
            if ((Invoke-Cmd (Join-Path $stage 'build.cmd')) -ne 0) { throw 'the build failed (output above). Nothing was changed.' }
            $built = Join-Path $stage 'bin\Nilam.exe'
            if (-not (Test-Path $built)) { throw 'the build reported success but produced no exe. Nothing was changed.' }
            Good ('built Nilam.exe ({0:N0} KB)' -f ((Get-Item $built).Length / 1KB))

            # The `nilam version` / `nilam update` command. The widgets are
            # complete without it, so a failure here is only a warning.
            $haveCli = $false
            if (Test-Path (Join-Path $stage 'cli\build.cmd')) {
                $haveCli = ((Invoke-Cmd (Join-Path $stage 'cli\build.cmd')) -eq 0) -and (Test-Path (Join-Path $stage 'bin\cli\nilam.exe'))
                if (-not $haveCli) { Warn 'the nilam command did not build - the widgets are installed without it' }
            }
            $obj = Join-Path $stage 'bin\obj'
            if (Test-Path $obj) { Remove-Item $obj -Recurse -Force }

            # ---------------------------------------------------------- stop running
            # Only the copy in the folder being installed to - not every process
            # that happens to share the name.
            $running = @(Get-Process $AppId -ErrorAction SilentlyContinue |
                Where-Object { $_.Path -and ($_.Path -ieq $exe) })
            if ($running.Count -gt 0) {
                # It saves and closes when asked; anything that ignores the
                # request is stopped.
                try { Start-Process -FilePath $exe -ArgumentList '--exit' -Wait }
                catch { Write-Verbose "could not ask the running copy to exit: $($_.Exception.Message)" }
                foreach ($p in $running) { [void]$p.WaitForExit(5000) }
                $running | Where-Object { -not $_.HasExited } | Stop-Process -Force -ErrorAction SilentlyContinue
                foreach ($p in $running) { [void]$p.WaitForExit(3000) }
                Good 'stopped the running copy'
            }
            if ($replaceOld) {
                # 1.x has no way to be asked, so it is simply stopped.
                $oldRunning = @(Get-Process DesktopWidgets -ErrorAction SilentlyContinue |
                    Where-Object { $_.Path -and ($_.Path -ieq $oldExe) })
                if ($oldRunning.Count -gt 0) {
                    $oldRunning | Stop-Process -Force -ErrorAction SilentlyContinue
                    foreach ($p in $oldRunning) { [void]$p.WaitForExit(3000) }
                    Good 'stopped Desktop Widgets 1.x'
                }
            }

            # ------------------------------------------------------------ copy files
            New-Item -ItemType Directory -Force $InstallDir | Out-Null
            # These folders only ever hold files this installer put there, and
            # are replaced whole rather than copied over. build.cmd compiles
            # src\*.cs, so a source file left behind by an older version would be
            # compiled into the new one. fonts\ is merged instead: it may hold a
            # font the user added.
            foreach ($folder in 'src', 'cli', 'assets', 'bin') {
                $target = Join-Path $InstallDir $folder
                if (Test-Path -LiteralPath $target) { Remove-Tree $target }
            }
            Copy-Item -Path (Join-Path $stage '*') -Destination $InstallDir -Recurse -Force
            Good "installed $version to $InstallDir"

            # --------------------------------------------- retire the 1.x install
            if ($replaceOld) {
                # The one thing of the user's in that folder is a font they may
                # have dropped in - their own copy of Anurati, say.
                $oldFonts = Join-Path $oldDir 'fonts'
                if (Test-Path -LiteralPath $oldFonts) {
                    Get-ChildItem -LiteralPath $oldFonts -File |
                        Where-Object { $_.Extension -in '.otf', '.ttf' } |
                        ForEach-Object {
                            $to = Join-Path (Join-Path $InstallDir 'fonts') $_.Name
                            if (-not (Test-Path -LiteralPath $to)) { Copy-Item -LiteralPath $_.FullName -Destination $to }
                        }
                }
                Remove-Tree $oldDir
                # Its `dwx` command was a copy dropped into the folder Windows
                # reserves for Store app aliases.
                $oldAlias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\dwx.exe'
                if (Test-Path -LiteralPath $oldAlias) { Remove-Item -LiteralPath $oldAlias -Force -ErrorAction SilentlyContinue }
                Good 'replaced Desktop Widgets 1.x - its layout, notes and weather location carry over on first start'
            }
            elseif (Test-Path -LiteralPath $oldExe) {
                Warn "Desktop Widgets 1.x is still installed in $oldDir. Remove it with the uninstall.ps1 in that folder, or there will be two sets of cards."
            }
        }
        finally {
            Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
        }

        # ----------------------------------------------------------------- startup
        # A per-user Run value pointing straight at the exe. 1.x used a
        # Startup-folder shortcut that ran a VBScript launcher through wscript;
        # VBScript is being retired from Windows and was never needed.
        $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        $runValue = '"' + $exe + '"'
        $hadStartup = $null -ne (Get-ItemProperty -Path $runKey -Name $AppId -ErrorAction SilentlyContinue)

        $oldLnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'DesktopWidgets.lnk'
        if ($replaceOld -and (Test-Path -LiteralPath $oldLnk)) {
            $old = (New-Object -ComObject WScript.Shell).CreateShortcut($oldLnk)
            # Only if it belonged to the install that was just replaced.
            if (("$($old.TargetPath) $($old.Arguments)").ToLowerInvariant().Contains($oldDir.ToLowerInvariant())) {
                Remove-Item -LiteralPath $oldLnk -Force
                $hadStartup = $true
            }
        }

        if ($NoStartup) {
            Warn 'not set to start with Windows (-NoStartup). It can be switched on in the settings window.'
        }
        elseif ($upgrade -and -not $hadStartup) {
            # It was turned off on purpose; an update must not turn it back on.
            Good 'start with Windows left off, as it was'
        }
        else {
            if (-not (Test-Path $runKey)) { New-Item -Path $runKey -Force | Out-Null }
            Set-ItemProperty -Path $runKey -Name $AppId -Value $runValue
            Good 'starts with Windows'
        }

        # ----------------------------------------------- start menu and apps list
        $startLnk = Join-Path ([Environment]::GetFolderPath('Programs')) "$AppName.lnk"
        New-Shortcut $startLnk $exe $InstallDir "$AppName - desktop widgets"
        Good 'added to the Start menu'

        # Makes it appear under Settings > Apps > Installed apps, with a working
        # Uninstall button. HKCU, so still no admin rights.
        $uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppId"
        $uninstaller = Join-Path $InstallDir 'uninstall.ps1'
        New-Item -Path $uninstallKey -Force | Out-Null
        Set-ItemProperty -Path $uninstallKey -Name DisplayName -Value $AppName
        Set-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value $version
        Set-ItemProperty -Path $uninstallKey -Name Publisher -Value $Publisher
        Set-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $InstallDir
        Set-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value "$exe,0"
        Set-ItemProperty -Path $uninstallKey -Name URLInfoAbout -Value "https://github.com/$Repo"
        Set-ItemProperty -Path $uninstallKey -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstaller`" -InstallDir `"$InstallDir`""
        Set-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -Type DWord
        Set-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -Type DWord
        $sizeKb = [int]((Get-ChildItem $InstallDir -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
        Set-ItemProperty -Path $uninstallKey -Name EstimatedSize -Value $sizeKb -Type DWord
        Good 'listed under Settings > Apps'

        # -------------------------------------------------------------------- path
        # bin\cli holds only the command-line tool. bin\ itself holds Nilam.exe,
        # the widgets - and since Windows file names are not case-sensitive, the
        # two cannot share a folder.
        if ($NoPath) {
            Warn 'the nilam command was not put on PATH (-NoPath)'
        }
        elseif ($haveCli) {
            if (Set-UserPath (Join-Path $InstallDir 'bin\cli') $true) { Send-EnvironmentChanged }
            Good 'nilam command installed - open a new terminal and try: nilam version'
        }

        # ------------------------------------------------------------------ launch
        if (-not $NoLaunch) {
            Start-Process -FilePath $exe -WorkingDirectory $InstallDir
            Start-Sleep -Seconds 2
            $up = @(Get-Process $AppId -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $exe })
            if ($up.Count -gt 0) { Good 'the widgets are running' }
            else { Warn "the widgets did not stay running. See widgets.log in $(Join-Path $env:APPDATA $AppId)" }
        }

        Say ''
        Say 'Done.'
        Say "  Installed to : $InstallDir"
        Say "  Settings     : right-click any card, or click the icon in the notification area"
        Say "  Your data    : $(Join-Path $env:APPDATA $AppId)"
        Say "  Uninstall    : Settings > Apps, or run uninstall.ps1 in the install folder"
        Say ''
    }
}
catch {
    Write-Host ''
    Write-Host "  FAIL  $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ''
    $failed = $true
}

# `exit` is only safe when this is running as a file. Piped into iex it would
# end the user's own PowerShell session - closing the window before the message
# above could be read.
if ($failed -and $PSCommandPath) { exit 1 }
