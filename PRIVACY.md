# Privacy

Nilam has no account, no telemetry, no analytics and no crash
reporting. This page lists everything it sends, reads and stores.

## What leaves your machine

| When | Where | What |
|---|---|---|
| Every 15 minutes, once a city is set (and when the network returns or the machine wakes) | `api.open-meteo.com` | the latitude and longitude of your chosen city, and a user-agent naming the app and its version |
| When you press Search in Settings | `geocoding-api.open-meteo.com` | the city name you typed and your Windows display language |
| When you run the installer, or `nilam version` / `nilam update` | `github.com` | an ordinary download request |

As with any web request, the service on the other end also sees your IP
address. [Open-Meteo's own terms](https://open-meteo.com/en/terms) describe what
it does with requests.

Nothing else is ever sent. In particular: your notes, the names of the tracks
you play, your pictures, and your system statistics never leave the machine.

If you do not choose a city, the app makes no network requests at all.

## What it reads on your machine

- **What is playing** - title, artist, cover art and position - from the Windows
  media session, the same source the volume flyout shows. It is displayed and
  not stored.
- **CPU, memory, free disk space and battery level**, displayed and not stored.
- **Pictures from one folder**, shown on the media card while nothing is
  playing. By default this is `Pictures\wallpaper`, used only if it exists. The
  files are opened read-only. You can switch this off or choose another folder
  in Settings.

## What it stores

Everything is in `%APPDATA%\Nilam`, as plain files you can open:

| File | Contents |
|---|---|
| `settings.json` | your settings, including the chosen city and its coordinates |
| `layout.json` | where each card is |
| `notes.txt` | the text of the notes card, unencrypted |
| `widgets.log` | start and stop times, and errors; no personal content |

Uninstalling leaves this folder in place so a reinstall restores your layout.
Run `uninstall.ps1 -Purge` to delete it too.
