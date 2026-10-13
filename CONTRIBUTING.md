# Contributing

Thanks for wanting to improve Nilam. This page is the short version of
what you need; [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) explains why the
code is shaped the way it is.

## Build and test

Nothing to install. On Windows 10 1809 or later:

```
.\build.cmd            builds bin\Nilam.exe
.\cli\build.cmd        builds bin\cli\nilam.exe
.\build.cmd tests      builds bin\Tests.exe
.\bin\Tests.exe        runs the tests; the exit code is the number of failures
```

The leading `.\` makes the same lines work in PowerShell and in `cmd`.

CI runs exactly these, plus PSScriptAnalyzer over the PowerShell scripts and an
install / uninstall round trip.

## Try a change without touching your own widgets

Point the build at a scratch state folder. It becomes a separate profile with
its own single-instance lock, so it runs beside your installed copy and never
reads or writes your real layout and notes:

```powershell
$env:NILAM_STATE_DIR = "$env:TEMP\dw-scratch"
.\bin\Nilam.exe
.\bin\Nilam.exe --exit      # asks that copy to save and close
```

## Rules of the house

- **C# 5 only.** The build uses the compiler that ships inside Windows. No `?.`,
  `$""`, `nameof`, expression-bodied members, `out var` or pattern matching.
- **No new dependencies.** No NuGet packages, and no assembly that is not
  already in the .NET Framework that ships with Windows. That constraint is the
  point of the project.
- **Plain ASCII in `.cs`, `.cmd` and `.ps1` files.** Use `°`-style escapes
  for anything else. The scripts are read by hosts that guess encodings.
- **No silent failures.** A `catch` either handles the error or logs it with
  `Log`. If swallowing one really is correct, say why in a comment.
- **Logic that can be tested goes where it can be tested.** Layout, clock
  formatting, the playback clock, parsing: these are plain classes with no
  window attached, and each has tests. Add to them when you change behaviour.
- **Comments explain why**, not what. Much of this code is the way it is because
  of something measured or something that broke; keep that knowledge next to it.
- **Markup stays in `Markup.cs`**, and only values the code owns are substituted
  into it - never a path or other text from the machine.

## Before you open a pull request

- `.\build.cmd` finishes with no warnings
- `.\bin\Tests.exe` reports no failures
- if you touched a script, it passes
  `Invoke-ScriptAnalyzer -Path . -Recurse -Settings ./PSScriptAnalyzerSettings.psd1`
- user-visible changes have a line under an `Unreleased` heading in
  `CHANGELOG.md`

## Releasing (maintainers)

1. Set the new number in `VERSION`.
2. In `CHANGELOG.md`, turn the notes into a `## [x.y.z] - date` section.
3. Commit, then tag that commit and push the tag:

   ```
   git tag v2.0.1
   git push origin main v2.0.1
   ```

The release workflow refuses to publish unless the tag matches `VERSION`, the
build is clean and the tests pass. It needs a changelog section for the version,
which becomes the release notes.
