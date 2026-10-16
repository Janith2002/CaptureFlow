# CaptureFlow 0.2.0-beta.1 — draft prerelease notes

Proposed beta, not an approved public installer release. A GitHub draft created for this milestone targets the documentation commit only and contains no application binary. Its tag must not be represented as a reproducible application-source revision.

## Implementation changes

- Mode-aware webcam preview action in the main toolbar.
- Persistent export Preparing/Exporting/Finalizing state, reliable or indeterminate progress, elapsed time and completion path/actions.
- Retained failure details, safe retry/cancel behavior and output collision protection.
- Read-only WPF export-progress startup binding correction with a real-binding regression.

## Evidence and limitations

Latest development regression: 893 passed / 0 failed / 0 skipped / 0 blocked. Build: 0 errors/warnings. Formal QA: 20 PASS / 26 Not Run. This is not verification of a newly packaged installer.

Physical webcam/PiP playback, A/V synchronization, complete export playback evidence, lag/resource retention, clean-machine deployment, publisher/signing and licensing remain open. Historical Application Control restrictions were not bypassed; the current full suite did execute successfully.

## Requirements and installation

Intended Windows 11 x64, self-contained .NET 10; compatible external FFmpeg/FFprobe required. No approved installer, checksum or signer exists for this version. Do not attach the old preview. See [installation](../../docs/INSTALLATION.md), [known issues](../../docs/KNOWN-ISSUES.md) and [PDF guide](docs/CaptureFlow-Complete-User-Guide.pdf).

Before publication, approve actual application build identity, version/publisher/terms and external-tool policy; authorize a new candidate; verify exports/recording/performance and clean-machine installation; approve signing where applicable; update final hashes/notices and obtain separate publication approval. Documentation publication is not binary-release approval.
