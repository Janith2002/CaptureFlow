# CaptureFlow

Windows recording and editing with a focused WPF workspace and timeline tools.

![CaptureFlow branding](assets/branding/CaptureFlow-New-Logo.png)

**Proposed 0.2.0-beta.1 · prerelease documentation · no approved public installer.**

## Features

- Six modes: Screen Only, Webcam Only, Screen + Webcam, Microphone Only, System Audio Only, Microphone + System Audio.
- Screen/window/region sources, camera/PiP controls and collapsible workspace panels.
- Timeline trimming/splitting/cropping, transforms, supported transitions/keyframes and audio mixing.
- Project save/load, best-effort recovery, recordings library and MP4/MP3/WAV export.
- Retained export progress, completion paths/actions and error details.

These are implemented capabilities, not completed physical acceptance. See [features](docs/FEATURES.md) and [known issues](docs/KNOWN-ISSUES.md).

## Download and installation

**No verified public installer is available.** The old 0.1.0-preview setup predates repairs and must not be presented as the current candidate. No installer download link is provided.

Intended platform: supported Windows 11 x64, self-contained .NET 10. ARM64/32-bit Windows are excluded. FFmpeg/FFprobe are external prerequisites, not bundled. Clean-machine and OS-version acceptance remain pending. See [installation](docs/INSTALLATION.md).

## Documentation

- [Complete user guide](docs/CaptureFlow-Complete-User-Guide.md)
- [Downloadable PDF manual](artifacts/release/docs/CaptureFlow-Complete-User-Guide.pdf)
- [Troubleshooting](docs/TROUBLESHOOTING.md) · [Privacy](docs/PRIVACY.md)
- [Change log](docs/CHANGELOG.md) · [Third-party notices](docs/THIRD-PARTY-NOTICES.md)
- [Draft release notes](artifacts/release/RELEASE-NOTES-DRAFT.md)

## Verification and limitations

Latest development evidence: **893 automated tests passed; clean build 0 warnings/errors**. Formal manual QA: **20 PASS / 26 Not Run**. Physical webcam/PiP recording/playback, A/V synchronization, final export playback evidence, performance/resource acceptance, signing and clean-machine deployment remain incomplete. Lag/high Windows memory usage were reported; causation is not established.

## Support and contributions

Use [GitHub Issues](https://github.com/Janith2002/CaptureFlow/issues) for non-sensitive reports. Include version, Windows version, mode, steps and exact error. Redact private paths/identifiers; do not upload credentials, recordings or personal projects. See [contributing](docs/CONTRIBUTING.md).

This publication contains documentation, notices and approved branding. It contains no application source/binaries. No open-source license or legal publisher identity is implied; product licensing remains an owner decision.
