<h1 align="center">Nilam</h1>

<p align="center">
  <b>Beautiful desktop widgets for Windows.</b><br>
  Clock, weather, music, system stats and notes — living on your desktop, beneath every window.
</p>

<p align="center">
  <a href="https://github.com/jithin-jz/nilam/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/jithin-jz/nilam/actions/workflows/ci.yml/badge.svg"></a>
  <a href="https://github.com/jithin-jz/nilam/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/jithin-jz/nilam?label=release"></a>
  <a href="LICENSE"><img alt="MIT licence" src="https://img.shields.io/badge/licence-MIT-blue"></a>
  <img alt="Windows 10 (1809+) and Windows 11" src="https://img.shields.io/badge/Windows-10%20(1809+)%20%C2%B7%2011-0078d4">
</p>

<p align="center">
  <img src="docs/images/desktop.png" width="100%"
       alt="A Windows 11 desktop with Nilam running over a dark McLaren wallpaper: TUESDAY in large spaced capitals across the top; on the right a clock, weather for Kozhikode, a now-playing card with a wave progress bar, system stats, battery and notes; the taskbar below is fully transparent.">
</p>

<p align="center">
  <a href="#install"><b>Install</b></a> ·
  <a href="#features"><b>Features</b></a> ·
  <a href="#commands"><b>Commands</b></a> ·
  <a href="docs/ARCHITECTURE.md"><b>Architecture</b></a> ·
  <a href="CHANGELOG.md"><b>Changelog</b></a>
</p>

---

*Nilam* (നിലം) is Malayalam for **"the ground"** — the layer everything else stands on. Every window draws over the widgets, so they feel like part of the desktop — never something floating over your work.

## Install

```powershell
irm https://github.com/jithin-jz/nilam/releases/latest/download/install.ps1 | iex
```

Downloads the latest release, verifies its checksum, builds Nilam with the C# compiler that ships with Windows, and starts it. **No admin rights, no runtime to install.**

<sub>Prefer a zip? Grab `nilam-source.zip` from the [latest release](https://github.com/jithin-jz/nilam/releases/latest) and run `install.cmd`. Requires Windows 10 1809+ or Windows 11 with .NET Framework 4.8 (built in).</sub>

## Features

- **Clock** — your Windows format, 12- or 24-hour
- **Weather** — current conditions, high and low, for any city
- **Now playing** — cover art, playback controls and seeking
- **System** and **Battery** — CPU, memory, free disk, charge level
- **Notes** — a scratchpad that saves as you type
- **Day banner** — the weekday in large spaced capitals
- **Brightness** — a slider for every display, including DDC/CI monitors
- **Clear taskbar** — see-through while the desktop is empty

Drag any card anywhere, on any monitor, and switch off the ones you don't need.

### Light by design

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/perf-dark.svg">
  <img src="docs/images/perf-light.svg" width="100%" alt="Idle CPU 0.20% of one core and 51 MB private memory, 91% and 20% lower than version 1.4.2.">
</picture>

No background services, no telemetry, no admin rights.

### Fits your screen

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/layout-dark.svg">
  <img src="docs/images/layout-light.svg" width="100%" alt="Default card layout: one block of four rows on a tall 1920x1200 screen, two blocks below the banner on a short 1920x1080 screen.">
</picture>

## Usage

Right-click any card, or the Nilam tray icon, to open **Settings**, lock or reset positions, or exit.

<details>
<summary><b>Settings window</b> — widgets, weather, clock, media pictures, brightness, opacity, startup</summary>
<br>
<p align="center"><img src="docs/images/settings.png" width="420" alt="The Nilam settings window in dark theme, with toggles for each widget, weather location, temperature and time format, media card pictures, display brightness, card opacity and start with Windows."></p>
</details>

<details>
<summary><b>Clear taskbar</b> — how it works</summary>
<br>

The taskbar on each monitor turns fully transparent while nothing is open there, and returns as soon as a window, Start, Search or Task View opens. Windows 11 on Intel or AMD only.

It runs a small helper inside Explorer, is **off by default**, always restores the taskbar when turned off or when Nilam exits, and starts about 45 seconds after sign-in. It can't run alongside tools like TranslucentTB.
</details>

## Commands

```powershell
nilam version            # show the installed and latest versions
nilam update             # install the latest release if newer
nilam install            # reinstall the latest release
nilam uninstall          # remove Nilam
nilam uninstall --purge  # also delete settings and notes
```

Also removable from **Settings › Apps › Installed apps**.

## Privacy

No account, no telemetry. Nilam only talks to [Open-Meteo](https://open-meteo.com) (weather for your city) and GitHub (install/update). Settings, layout and notes are plain files in `%APPDATA%\Nilam`. Details in [PRIVACY.md](PRIVACY.md).

Something wrong? Check `%APPDATA%\Nilam\widgets.log` — Settings has a link to the folder.

## Build from source

```powershell
.\build.cmd             # bin\Nilam.exe
.\build.cmd tests       # bin\Tests.exe, then run .\bin\Tests.exe
.\taskbar\build.cmd     # clear taskbar helper (needs Visual Studio C++ tools)
```

Everything except the taskbar helper builds with tools already in Windows. See [ARCHITECTURE](docs/ARCHITECTURE.md) · [CONTRIBUTING](CONTRIBUTING.md) · [SECURITY](SECURITY.md).

## Licence

[MIT](LICENSE). Banner font **Michroma** is bundled under the SIL OFL 1.1 (the banner was designed around **Anurati** by Emmeran Richard — drop your own copy in the `fonts` folder). **Weather data by [Open-Meteo.com](https://open-meteo.com/)**, CC BY 4.0, non-commercial use. See [NOTICE](NOTICE).
