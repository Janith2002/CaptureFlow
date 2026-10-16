# Troubleshooting

## Troubleshooting

For startup failure, preserve the exact error and diagnostic reference. The read-only export-progress binding failure was corrected and has a real-WPF regression. An old executable can retain an old defect; check the actual build.

For disabled Start, check mode/source selection, readiness, active work and validation. Camera modes need appropriate camera state; PiP also needs a screen source. Do not bypass validation.

For export errors, retain status, format, actual path and details. Check approved FFmpeg/FFprobe, codecs, source presence and writable destination. For stale edits use Update preview; distinguish source audition from edited playback.

For lag, stop preview and record workload, app CPU/private memory/handles/threads and system memory. High system load alone does not establish causation. Do not terminate Windows-owned media threads. For an Application Control block, retain diagnostics and contact the maintainer/administrator; do not disable controls or rename binaries to evade policy.

Reference: [Smart App Control](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview).

## Updates and support

Use [GitHub Issues](https://github.com/Janith2002/CaptureFlow/issues) for non-sensitive reports. Include actual app/build version, Windows version, mode, steps, expected/actual behavior and exact error. Redact private paths/identifiers and never post credentials or personal recordings. No private vulnerability channel is approved; avoid sensitive exploit details in public issues.

Before critical use, select an approved candidate, check sources/permissions/destination, allow disk space and make a short authorized test. Play/listen before relying on a long recording. Save projects and preserve originals/backups.

Export acceptance needs generated inputs, unique new outputs, stream/format/duration validation and human visible/audible playback. Performance acceptance needs the same process, exactly three preview lifecycle cycles and cleanup/settle sampling, not unrelated snapshots.

Future release notes must identify the actual installer, SHA-256, signer, tested Windows versions and remaining issues. This guide is proposed-beta documentation, not release approval.

See the [complete guide](CaptureFlow-Complete-User-Guide.md).
