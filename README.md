<h1 align="center">Nilam</h1>

<p align="center">
  <b>Desktop widgets for Windows.</b><br>
  Clock, weather, music, system stats and notes - on your desktop, beneath every window.
</p>

<p align="center">
  <a href="https://github.com/jithin-jz/nilam/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/jithin-jz/nilam/actions/workflows/ci.yml/badge.svg"></a>
  <a href="https://github.com/jithin-jz/nilam/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/jithin-jz/nilam?label=release"></a>
  <a href="LICENSE"><img alt="MIT licence" src="https://img.shields.io/badge/licence-MIT-blue"></a>
  <img alt="Windows 10 version 1809 or later, and Windows 11" src="https://img.shields.io/badge/Windows-10%20(1809+)%20%C2%B7%2011-0078d4">
</p>

<p align="center">
  <img src="docs/images/desktop.png" width="100%"
       alt="A Windows 11 desktop with Nilam running on a dark wallpaper: the word TUESDAY in large spaced capitals across the top, and on the right a clock, weather for Kozhikode, a now-playing card with a wave progress bar, system stats, battery and notes.">
</p>

*Nilam* (നിലം) is Malayalam for "the ground" - the layer everything else stands
on. The widgets live there: every app window draws over them, so they feel like
part of the desktop rather than something floating above your work.

## Features

| | |
|---|---|
| **Clock** | Time and date in your Windows format, 12- or 24-hour |
| **Weather** | Current conditions and today's high and low for any city |
| **Now playing** | Track, artist and cover art from any media player, with playback controls and seeking |
| **System** | CPU, memory and free disk space |
| **Battery** | Charge level and charging state, on laptops |
| **Notes** | A scratchpad that saves as you type |
| **Day banner** | The weekday in large spaced capitals |
| **Brightness** | A slider for every display, including external monitors that support DDC/CI |
| **Clear taskbar** | Optional: a see-through taskbar while the desktop is empty (Windows 11) |

Drag any card anywhere, on any monitor, and switch off the ones you don't need.
Nilam is light by design: no background services, no runtime to install, no
admin rights and no telemetry.

## Install

Run this in PowerShell:

```powershell
irm https://github.com/jithin-jz/nilam/releases/latest/download/install.ps1 | iex
```

The installer downloads the latest release, verifies its checksum, builds Nilam
with the C# compiler that ships with Windows, and starts it. No admin rights are
needed.

Alternatively, download `nilam-source.zip` from the
[latest release](https://github.com/jithin-jz/nilam/releases/latest), extract it
and run `install.cmd`.

**Requirements:** Windows 10 version 1809 or later, or Windows 11, with .NET
Framework 4.8 (included in Windows).

## Usage

Right-click any card, or click the Nilam icon in the notification area, to open
**Settings**, lock or reset card positions, or exit.

Settings covers the weather location and units, clock format, which cards to
show, pictures for the idle media card, display brightness, card opacity, the
clear taskbar, and starting with Windows.

### Clear taskbar

When switched on, the taskbar on each monitor becomes fully transparent while
nothing is open there, and returns to normal as soon as a window, Start, Search
or Task View opens. It requires Windows 11 on an Intel or AMD processor.

Because Windows 11 draws the taskbar inside Explorer, this option runs a small
helper inside Explorer. It is off by default, the taskbar is always restored when
you turn it off or Nilam exits, and it takes about 45 seconds to start after you
sign in. It cannot run alongside other taskbar tools such as TranslucentTB.

## Commands

The installer adds a `nilam` command:

| Command | Description |
|---|---|
| `nilam version` | Show the installed and latest versions |
| `nilam update` | Install the latest release if it is newer |
| `nilam install` | Reinstall the latest release |
| `nilam uninstall` | Remove Nilam (add `--purge` to also delete your settings and notes) |

Nilam can also be removed from **Settings > Apps > Installed apps**.

## Privacy

Nilam has no account and no telemetry. It connects only to
[Open-Meteo](https://open-meteo.com), for the weather of the city you choose, and
to GitHub, when installing or updating. Your settings, layout and notes are kept
as plain files in `%APPDATA%\Nilam`. See [PRIVACY.md](PRIVACY.md) for details.

## Troubleshooting

If something isn't working, check `%APPDATA%\Nilam\widgets.log`. Settings has a
link that opens the folder.

## Building from source

```
.\build.cmd            build bin\Nilam.exe
.\build.cmd tests      build bin\Tests.exe
.\bin\Tests.exe        run the tests
.\taskbar\build.cmd    build the clear taskbar helper (needs Visual Studio C++ tools)
```

Everything except the clear taskbar helper builds with tools already in Windows.

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) - how Nilam is built, and why
- [CONTRIBUTING.md](CONTRIBUTING.md) - guidelines for changes
- [SECURITY.md](SECURITY.md) - reporting vulnerabilities, and the trust model
- [CHANGELOG.md](CHANGELOG.md) - release history

## Licence

Nilam is released under the [MIT licence](LICENSE).

- The banner font, **Michroma**, is included under the SIL Open Font License 1.1.
  The banner was designed around **Anurati** by Emmeran Richard, which cannot be
  redistributed; if you own a copy, place it in the install's `fonts` folder.
- **Weather data by [Open-Meteo.com](https://open-meteo.com/)**, under CC BY 4.0.
  Its free API is for non-commercial use.

See [NOTICE](NOTICE) for details.
