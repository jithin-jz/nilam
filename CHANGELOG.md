# Changelog

All notable changes to Nilam are recorded here. Releases before 2.0 were
published under the name Desktop Widgets. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [2.0.0] - 2026-10-13

A major version because the project has a new name, the launcher is gone, the
settings folder moved, and updates now come from published releases rather than
from the `main` branch. Installing 2.0 over Desktop Widgets 1.x replaces it and
carries your layout, notes and weather location over.

### Added

- **Settings window**, from any card's right-click menu or the notification
  icon: city search for the weather, Celsius or Fahrenheit, 12- or 24-hour
  clock, which cards to show, the idle-picture folder, card opacity, and start
  with Windows.
- **Notification-area icon**, so the app can be reached when no card is.
- **Michroma** is bundled (SIL Open Font License) as the banner's default font.
  Anurati is still preferred when you have added your own copy.
- A log file, `widgets.log`, capped at about half a megabyte, and crash handlers
  that write to it.
- Start menu shortcut and an entry under **Settings > Apps**.
- `Nilam.exe --exit` asks a running copy to save and close.
- The exe carries a version, product name, icon and manifest.
- A test suite and CI, both using the compiler that ships in Windows.
- Open-Meteo credit on the weather card and in the settings window.

### Changed

- **Renamed to Nilam.** The app, its exe (`Nilam.exe`), its install folder
  (`%LOCALAPPDATA%\Nilam`) and its repository carry the new name. *Nilam* is
  Malayalam for the ground: the layer the widgets sit on.
- **The command-line tool is `nilam`**, replacing `dwx`. It is found through a
  `PATH` entry for the install's `bin\cli` folder.
- **Updates come from releases.** The one-line installer and `nilam update`
  install the latest published release and verify its checksum. Previously both
  ran whatever was on `main` at that moment.
- Settings, layout and notes now live in `%APPDATA%\Nilam`.
- Start with Windows is a `Run` registry value pointing at the exe.
- The installer builds in a staging folder and only replaces a working copy once
  the new one has built. It no longer asks for latitude and longitude.
- The weather card refreshes when the machine wakes or the network returns,
  shows the age of a stale reading, and shows a moon on a clear night.
- The media buttons control the player shown on the card, falling back to a
  media key only when there is no session.
- The cards' drop shadow is removed and their background set to the equivalent
  flat colour. Idle CPU use measured 0.20% of one core against 2.34% before.

### Fixed

- A failed one-line install closed the PowerShell window before the error could
  be read.
- The installer misread coordinates on systems that use a decimal comma:
  `11.2448` was stored as `112448`, or rejected.
- The clock showed 20:23 as `08:23` with no AM/PM in 24-hour locales.
- On 1080p at 150% scaling the notes card overlapped the row above it.
- An install path containing `&` stopped the app at start-up without a message.
- Updating failed with duplicate-type errors once any source file had been
  renamed or removed upstream, leaving the widgets stopped.
- The cards disappeared whenever Show Desktop (Win+D) was used, because Windows
  raises the desktop above any window that cannot be minimised.
- A card dragged to a second monitor jumped back on every start.
- Notes could be lost: an unreadable file was treated as empty and then saved
  over, and writes were not atomic.
- The banner split weekday names by UTF-16 unit, breaking Indic, Thai, Arabic
  and Hebrew names.
- The battery card said "Charging" whenever mains power was connected.
- Disk space was read from `C:` rather than the system drive; the build and
  installer assumed Windows is in `C:\Windows`.
- Cover art was lost for the rest of a track after a transient session error.
- The uninstaller would delete any folder it was pointed at.

### Removed

- The VBScript launcher (`launcher\DesktopWidgets.vbs`). VBScript is deprecated
  in Windows and the exe never needed it.
- The copy of `dwx.exe` in `%LOCALAPPDATA%\Microsoft\WindowsApps`.
- The "Kiro" name from the namespace, settings folder, mutex, window title and
  user agent.

## [1.4.2] and earlier

Published as Desktop Widgets. See the
[commit history](https://github.com/jithin-jz/nilam/commits/main).
