# Code signing policy

## Current status

Veyro releases are currently unsigned. An application to SignPath Foundation's free open-source signing program was
submitted on 2026-11-17. Acceptance and a certificate have not been granted. No release should be described as signed
until its Authenticode signature has been verified.

## Maintainer roles

- Author / committer: [officialFengi](https://github.com/officialFengi).
- Reviewer: officialFengi reviews contributions before merging.
- Signing approver: officialFengi (planned, subject to SignPath onboarding).

All maintainers must enable multi-factor authentication on GitHub and SignPath
before joining the signing process. Every signing request requires manual
approval. Secrets must never be committed or exposed to pull-request builds.

## Release process

The Windows workflow builds the application, uninstaller and installer from the
public source. It currently uploads unsigned artifacts only. It does not publish
GitHub releases or request signatures.

After approval, configure SignPath's trusted build integration and artifact
metadata restrictions. Sign Veyro.exe and Uninstall-Veyro.exe before embedding
them in the installer; then sign Veyro-Setup.exe. Keep the product name Veyro and
use the same version for all three binaries. Verify signatures and timestamping
before publishing. Do not modify signed binaries.

Only after SignPath approval, add the following attribution to the product home
page and release pages: “Free code signing provided by SignPath.io, certificate
by SignPath Foundation”, with links to https://signpath.io and https://signpath.org.

## Privacy

Veyro does not implement telemetry, an account system or automatic update checks.
It reads local Windows audio sessions and changes their volume/output settings.
Preferences are stored locally at `%LOCALAPPDATA%\Veyro\settings.json`.
The installer optionally registers Windows startup and desktop shortcuts and
provides an uninstaller. Downloads from GitHub and visits to the product website
are subject to those services' privacy policies.

## References

- https://signpath.org/terms
- https://signpath.org/apply
