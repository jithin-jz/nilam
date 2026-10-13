# Security

## Reporting a vulnerability

Please do not open a public issue for a security problem.

Use **Report a vulnerability** on the repository's
[Security tab](https://github.com/jithin-jz/nilam/security/advisories/new).
That opens a private report only the maintainers can see. Include what you
found, how to reproduce it, and which version you saw it in.

You should hear back within a week. Fixes are released as a new version; there
are no patches for older ones, so the supported version is always the latest
release.

## What this software does and does not promise

It helps to be plain about the trust model, because it is unusual.

- **Nothing is code-signed.** The exe is compiled on your own machine from the
  source of a release, by the C# compiler that ships with Windows. That avoids
  downloading an unsigned binary, but it does mean the installer runs a build
  script with the PowerShell execution policy bypassed.
- **Installs come from tagged releases**, not from the development branch. The
  one-line installer and `nilam update` both fetch from `releases/latest`.
- **Checksums are verified, and they are not signatures.** A release's installer
  is stamped with the SHA-256 of that release's source archive and refuses
  anything else; `nilam update` checks the installer against the release's
  `SHA256SUMS.txt`. This catches a corrupted or swapped download. It does not
  protect you if the release itself was published by someone who had taken over
  the repository.
- **Releases carry build provenance.** You can check that a file was produced by
  this repository's release workflow:

  ```
  gh attestation verify install.ps1 --repo jithin-jz/nilam
  ```

- **It needs no admin rights** and asks for none. Everything it writes is under
  your own user profile and `HKEY_CURRENT_USER`.
- **Network access is limited** to Open-Meteo (weather and city search) and, for
  the installer and `nilam`, GitHub. See [PRIVACY.md](PRIVACY.md).

If you would rather not trust a script at all, clone the repository at a release
tag, read it, and run `build.cmd` yourself.
