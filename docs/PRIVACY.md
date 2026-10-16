# Privacy and permissions

## Privacy and permissions

Screen capture can expose passwords, notifications and windows. Microphones can include nearby conversations; loopback can contain calls; webcams can reveal people/surroundings. Preview may access a device before recording. Obtain consent and prepare a safe scene.

The reviewed implementation stores media, project references, preferences, recovery metadata and library data locally, and invokes external FFmpeg/FFprobe. Source review found no application telemetry/cloud-upload implementation; this is not an independent security certification or a guarantee about Windows, third-party tools or synced destinations.

Diagnostics/metadata may contain filenames, paths, device identifiers and exception details. Redact before sharing. Prefer a synthetic reproduction instead of uploading private recordings/projects.

Use Windows device permissions deliberately. Access denial is a valid condition, not a reason for bypass. This guide does not authorize capture or policy changes. Review behavior of external tools and any applications opening the resulting files separately.

See the [complete guide](CaptureFlow-Complete-User-Guide.md).
