# CaptureFlow 0.2.0-beta.1 — unpublished prerelease notes

CaptureFlow's audited original source and documentation are available under MIT. This is not an installer or a verified binary release. The version remains a proposed beta; no installer is attached or approved.

## Implemented repairs

- Mode-aware webcam preview action in the main toolbar.
- Persistent export Preparing/Exporting/Finalizing state, reliable or indeterminate progress, elapsed time, output paths and completion/error actions.
- Safe retry/cancel and collision prevention.
- Read-only WPF export-progress startup binding correction with a real-binding regression.

## Verification and known issues

Last verified development suite: 893 passed / 0 failed / 0 skipped / 0 blocked. Build: 0 warnings/errors. Formal QA stays 20 PASS / 26 Not Run. No new binary is certified by source publication.

Physical webcam/PiP playback, A/V synchronization, complete export playback evidence, lag/resource retention, signing and clean-machine deployment remain open. Historical security restrictions were not bypassed. Compatible external FFmpeg/FFprobe are required and not included.

See [installation](../../docs/INSTALLATION.md), [known issues](../../docs/KNOWN-ISSUES.md), [license](../../LICENSE) and the [PDF manual](docs/CaptureFlow-Complete-User-Guide.pdf). Future installer build/signing/distribution and release publication require separate approval. Do not attach the outdated 0.1.0-preview setup.
