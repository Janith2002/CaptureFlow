# CaptureFlow User Guide

**Record. Edit. Export.**

Edition: 16 October 2026 · Proposed 0.2.0-beta.1 · MIT-licensed original material.

This source follows the approved redesigned guide. The [publication PDF](../artifacts/release/docs/CaptureFlow-Complete-User-Guide.pdf) preserves its 17-page design and adds a build page and two genuine screenshot plates. No public installer is approved.

## Contents

1. [Start here](#start-here)
2. [Requirements & setup](#requirements--setup)
3. [The workspace](#the-workspace)
4. [Recording modes & screen sources](#recording-modes--screen-sources)
5. [Webcam & picture-in-picture](#webcam--picture-in-picture)
6. [Audio capture & recording settings](#audio-capture--recording-settings)
7. [Shortcuts & background behavior](#shortcuts--background-behavior)
8. [Editor & timeline basics](#editor--timeline-basics)
9. [Trim, split, crop & transform](#trim-split-crop--transform)
10. [Transitions, animation & mixing](#transitions-animation--mixing)
11. [Projects, recovery & library](#projects-recovery--library)
12. [Export & verify](#export--verify)
13. [Troubleshooting & privacy](#troubleshooting--privacy)
14. [Beta status & limitations](#beta-status--limitations)
15. [Technical reference & support](#technical-reference--support)

## Start here

An efficient first session, without connecting a camera or microphone.

CaptureFlow is a Windows desktop workspace for screen, webcam and audio capture, timeline editing and media export. The fastest way to learn its editor is to start with non-sensitive generated media.

1. Import media

2. Add to timeline

3. Update preview

4. Export & verify

### Your first edit

1. Open Video Editor. Choose Project > New, then import a generated video or audio clip.

2. Build a timeline. Select media in the bin and choose Add to timeline. Make a small trim or property change.

3. Review the edit. Choose Update preview to render the current timeline, then play the result.

4. Save and export. Save a disposable .cfedit project, export to a new folder and personally check the saved file.

**KEEP YOUR SOURCES**

A saved project references source media; it is not a rendered video and does not embed the original files. Keep source media available at its expected location.

## Requirements & setup

What you need for a development build and a future installer.

- **Platform:** Windows 11 x64. The planned installer excludes ARM64 and 32-bit Windows; clean-machine acceptance is pending.

- **Development:** A .NET 10 SDK is required for building from source. The planned self-contained installer is not yet approved.

- **Media tools:** External ffmpeg.exe and matching ffprobe.exe; documented workflows require libx264, AAC and libmp3lame.

- **Devices:** Webcam, microphone and output device only for recording modes that use them.

- **Performance:** No validated minimum RAM, CPU, GPU or disk specification has been established.

### Configure FFmpeg and FFprobe

1. Obtain compatible external tools. Keep matching ffmpeg.exe and ffprobe.exe together in one directory.

2. Open settings. Navigate to Settings > External tools and select ffmpeg.exe.

3. Validate. Choose Validate and Save and resolve any missing codecs or capability errors.

**INSTALLER AVAILABILITY**

No approved installer is available. The historical unsigned 0.1.0-preview setup is outdated. Do not treat it as the current beta candidate. Future installers should be obtained from the official GitHub Releases page after approval.

### Build from source

Use Windows 11 x64, Git and the .NET 10 SDK. Media operations additionally require compatible external FFmpeg and matching FFprobe.

```powershell
git clone https://github.com/Janith2002/CaptureFlow.git
cd CaptureFlow
dotnet restore CaptureFlow.slnx
dotnet build CaptureFlow.slnx
dotnet test CaptureFlow.slnx --no-build
dotnet run --project src/CaptureFlow.Desktop/CaptureFlow.Desktop.csproj
```

Start with generated media in Video Editor. Running the application is not permission to initialize a camera, microphone or screen preview. Device-mode selection and global recording shortcuts can activate physical sources. Save work and close a development instance yourself before rebuilding locked output files. No new test run or device acceptance is claimed by this guide.

[Installation and external tools](INSTALLATION.md) · [Contribution guide](CONTRIBUTING.md)

## The workspace

A clear map of Recording Studio, Video Editor and their shared controls.

- **Recording Studio:** Large monitor with compact toolbar, Sources, Inspector and optional Mixer. Collapse panels for a larger preview.

- **Video Editor:** Media bin, rendered-preview monitor, Inspector and multitrack timeline.

- **Project menu:** New, Open project, Save project and Import media.

- **Edit / View:** Clip commands, Media bin and Inspector visibility, and Reset layout.

- **Monitor:** Preview update, fullscreen, mute, source audition and cropping controls.

### Understand the preview

The editor preview is a rendered timeline result played through WPF; it is not a realtime compositor. After changing a clip, use Update preview before judging the edit. Source audition is not the same as listening to the edited mix.

### A typical recording-to-export path

1. Configure

2. Capture

3. Edit

4. Export

DEVICE ACCESS

Choosing a camera mode may initialize a physical device before recording begins. For a device-free walkthrough, stay in Video Editor and use generated media.

### Recording Studio reference

![Genuine CaptureFlow Recording Studio QA screenshot](images/recording-studio.png)

Real M69.2 QA screenshot. Preview and recording are stopped; generic hardware labels are visible. Historical workspace view, not physical capture acceptance or proof of later toolbar repairs. Zoom in for control labels.

## Recording modes & screen sources

Six implemented capture modes, with mode-specific readiness checks.

SCREEN ONLY

Screen / window / region

MICROPHONE ONLY

Selected input device

WEBCAM ONLY

Selected camera

SYSTEM AUDIO ONLY

Output loopback

SCREEN + WEBCAM

Picture-in-picture

MIC + SYSTEM AUDIO

Combined audio sources

### Choose the correct screen source

For screen recording, select Entire Screen, Application Window or Custom Region. Recheck the target after display changes or when a window closes. Fit shows the whole preview; 1:1 shows source pixels and may require scrolling. Neither viewing control sets output resolution.

### Start, pause and stop

Check the source, output destination and capture permissions. Use Start, Pause, Resume and Stop only when recording is intended, and allow finalization to finish before opening the output. A clickable Start control is not evidence that physical capture has succeeded.

PERFORMANCE AND VERIFICATION

The UI supports 24, 30 and 60 FPS choices. Actual frame delivery depends on capture and encoder load; overload can repeat or skip frames. Physical mode-by-mode playback and synchronization remain unaccepted.

## Webcam & picture-in-picture

Select the intended device, confirm its format and preview deliberately.

### Webcam Only

Select the camera and inspect its reported capture format. Device resolution, frame rate and pixel format must be supported. The chosen recording output size is separate from the camera capture format.

### Screen + Webcam

Select a valid screen source and camera, then configure the picture-in-picture layout. The main Studio toolbar exposes mode-aware preview controls; the Inspector is not required for basic preview access.

### When Start is unavailable

-  Read the actual validation message and check the selected mode.

-  Confirm camera permissions, device readiness and supported format.

-  For PiP, confirm the screen source is valid and still available.

-  Close competing camera applications where appropriate; do not bypass security or readiness checks.

CURRENT VERIFICATION BOUNDARY

Synthetic regression tests cover recent toolbar behavior, but physical webcam/PiP recording, final playback and A/V synchronization have not been formally accepted. Do not assume compatibility with a specific camera or driver.

## Audio capture & recording settings

Manage independent sources, capture quality and output settings.

### Audio Recorder

Choose Microphone Only, System Audio Only or Microphone + System Audio. System audio uses loopback from the selected output device. Windows audio-routing changes may invalidate that selection.

- **Audio format:** WAV or MP3. MP3 bitrate choices: 128, 192, 256 and 320 kbps.

- **Source controls:** Independent microphone/system-audio levels and mute. A microphone mute does not silence loopback.

- **Video quality:** Low, Standard, High, Ultra and Custom; Custom validates bitrate.

- **Frame rate:** 24, 30 and 60 FPS choices, subject to actual throughput.

- **Encoder:** Check available encoders before choosing hardware; explicit choices require successful capability checks.

- **Cursor / presets:** Cursor visibility/highlight/click animation; Apply, Save new, Rename and Delete recording presets.

### Verify the file, not just the meter

For an authorized short test, stop recording and wait for saving. Listen to the resulting file. For combined audio or video, use an observable event to assess timing. Moving meters alone do not establish audible sound or synchronization.

KNOW THE DIFFERENCE

Hardware acceleration is described for final export after capture; a lossless intermediate may require significant disk space. A preset stores configuration, not media or a backup.

## Shortcuts & background behavior

Quick actions are useful, but global shortcuts can work when the window is hidden.

- **Ctrl + Alt + R:** Start / Stop recording

- **Ctrl + Alt + P:** Pause / Resume

- **Ctrl + Alt + M:** Microphone mute

- **Ctrl + Alt + C:** Show / Hide CaptureFlow

- **Ctrl + S / Ctrl + O:** Save / Open project in the editor

- **Ctrl + Z / Ctrl + Y:** Undo / Redo

- **S / V / T:** Split / Select / Trim (editor focus)

- **Delete:** Remove selected content when available

### Manage shortcuts

Use Settings > Keyboard shortcuts to enable or adjust bindings, then choose Save shortcuts. Conflicts and duplicate assignments are reported; a saved setting alone does not prove successful global registration.

### System tray

The tray offers Open CaptureFlow, recording controls, Open Recordings and Exit. Settings > General >

Minimize and close to system tray controls whether closing hides the window when the tray is available.

BACKGROUND CAPTURE

Hiding the window does not necessarily stop recording. Check the recording state and use explicit Stop controls. Disable global shortcuts when accidental capture would be unsafe.

## Editor & timeline basics

Import source media, work on the timeline and regenerate the rendered preview.

Illustrative timeline structure (diagram, not an application screenshot)

Not a CaptureFlow screenshot

> **VIDEO**
> Clip A Clip B

> **AUDIO**
> Track mix Fades / gain

### Import and arrange

Open Video Editor, create a project and import media. Select a source in the media bin, choose Add to timeline, and use the clip selection to inspect its properties. Timeline Fit shows the project; zoom provides more precise control.

### Preview and transport

The monitor provides Jump to start, Previous frame, Play, Pause, Stop, Next frame and Jump to end. These act on rendered preview media. WPF seeking is approximate and codec-dependent, so frame buttons are not a guarantee of frame-exact visual inspection.

### Edit with confidence

Locked tracks prevent edits. Track mute affects the rendered mix, not just panel appearance. After committed changes, choose Update preview; an earlier preview can be stale. Stop playback and save before investigating persistent issues.

SOURCE PRESERVATION

Projects keep references to source files. Preserve originals, use a disposable project for destructive tests and export to a separate destination.

### Video Editor reference

![Genuine CaptureFlow Video Editor QA screenshot](images/video-editor.png)

Real M69.2 QA screenshot using synthetic color-bar media. No personal footage is shown. Historical layout, not final export playback or physical-device acceptance. Zoom in for timeline and Inspector labels.

## Trim, split, crop & transform

The essential tools for refining framing and timing.

- **Trim:** Select a clip and drag either edge to change the used source range. Original media remains unchanged.

- **Split:** Select a clip, position the playhead and press S with editor focus. Check both resulting clips.

Ripple delete / join Available under Edit when valid; ripple operations can move later content.

- **Crop:** Use Monitor > Crop selected source; Apply crop commits, Cancel abandons and Clear crop removes.

- **Transform:** Use Inspector > Transform & text for position, scale, rotation and opacity where offered.

- **Undo / redo:** Works on committed edits; it does not replace saving the project.

### Using the Inspector

Select a clip and confirm the Inspector shows the correct identity. Expand relevant controls and choose

Apply clip properties where shown. Changing selection or closing the panel does not automatically save a project.

### Review the result

Regenerate the preview after edits, check more than one timeline position and inspect downstream timing after ripple operations. Rotation can expose empty edges beyond the source frame.

SAFE EDITING

If an operation is rejected, use the validation message rather than forcing values. Source bounds and edit validity are enforced.

## Transitions, animation & mixing

Fine-tune movement, transitions and the final audio balance.

### Transitions

Inspector > Transitions supports fade/dissolve workflows. Apply a transition to a valid edge; Delete edge removes it. Dissolve requires compatible adjacent clips and sufficient source handles.

### Keyframes

Inspector > Animation provides Add at playhead, Move / update, Delete key and Reset animation. Keyframe time is clip-local, derived from the timeline playhead. Supported interpolation includes linear and hold where offered.

### Audio mix and silence analysis

Clip properties include gain, mute and fades. Track mix & silence offers track gain/mute and silence detection/removal. Use Apply clip properties or Apply track mix to commit. Inspect detected intervals before removal because timing may change.

For a safe test, use a generated tone. Render, listen, mute the intended track, regenerate and listen again.

Preview-monitor mute and exported-track mute are different operations.

COMPATIBILITY

Advanced edits can upgrade a project from format version 1 to 2; older builds may reject advanced projects. Human transition/keyframe acceptance remains pending.

## Projects, recovery & library

Understand what is saved, what is referenced and what can be recovered.

### Project files

A .cfedit file stores edit state and source references, not the media itself. Use Project > Save project and keep originals accessible. Moving source files can prevent rendering.

### Recovery and autosave

Settings > Recovery & Protection controls recording recovery and editor autosave. Supported autosave intervals are 15–300 seconds. Recovery metadata and snapshots use the configured location; recording intermediates remain in .captureflow-work beside the output.

When Recovery available appears, review session states, timestamps, folders and warnings. Recover /

Restore editor attempts recovery; Open Folder inspects files; Dismiss changes the workflow state. Recovery is best effort and is not a substitute for backups.

### Media library and storage

The Recordings page lists registered media with available open/play/edit actions. Settings > Storage >

Choose folder sets the shared recording destination. Library records are metadata, not copies of the media. A library refresh failure can occur after a valid export has already been saved.

PROTECT YOUR WORK

Do not erase unfamiliar work folders while jobs are active. Check the actual file path before renaming, deleting or cleaning recovery metadata.

## Export & verify

Produce MP4, MP3 or WAV output and confirm that it really plays.

- **MP4:** Video with optional audio.

- **MP3:** Compressed audio-only output.

- **WAV:** Audio-only output without MP3 encoding.

### Export workflow

1. Prepare. Save the project and render an updated preview. Choose the format and exposed settings.

2. Choose a safe destination. Use a fresh folder and unique filename; do not overwrite original media.

3. Wait for finalization. Status moves through Preparing, Exporting and Finalizing. Percentage is shown only when reliable.

4. Verify playback. Open the completed output and inspect video, audible audio, timing and intended edits.

### Understanding status

Export completed retains the full output path, byte size and Open File/Open Folder actions where available.

Export failed retains details; Retry creates a new output. Cancelled is not Completed, and recovery files may remain.

HUMAN PLAYBACK REQUIRED

The exporter probes/decodes output and checks for a nonempty file with a matching extension. These automated checks do not replace viewing or listening to the finished result. Formal export playback acceptance remains pending.

## Troubleshooting & privacy

Practical diagnosis without exposing sensitive recordings or weakening security.

- **Start is disabled:** Review mode, source selection, permissions, device readiness, busy state and validation message.

- **Stale editor preview:** Commit properties and choose Update preview; distinguish source audition from edited playback.

- **Export failure:** Check FFmpeg/FFprobe, codecs, source paths, writable destination and retained error details.

- **Lag or resource growth:** Stop preview; record app CPU, private memory, handles and threads plus overall system load.

- **Application Control block:** Retain the diagnostic and contact the administrator; do not disable security or rename binaries to evade policy.

### Privacy and permissions

Screen capture can reveal passwords and notifications. Microphones can record nearby conversations, loopback can contain calls and webcams can expose people or surroundings. Preview may initialize a device before recording. Obtain consent and select sources deliberately.

Recordings, projects, recovery metadata and diagnostics are stored locally in the reviewed implementation;

no application telemetry or cloud-upload implementation was found in that review. This is not an independent security certification. Paths, filenames and device identifiers may appear in diagnostics.

SHARE SAFELY

Use synthetic media for bug reports. Redact private paths, identifiers and credentials; never post personal recordings or security-sensitive exploit details in public GitHub Issues.

## Beta status & limitations

A transparent view of what is implemented and what still needs acceptance.

- **Automated regression:** 893 tests passed in the latest verified development run; clean build had zero warnings/errors.

- **Formal manual QA:** 20 PASS / 26 Not Run across 46 cases.

- **Physical capture:** Webcam/PiP recording, final playback and A/V synchronization still require documented acceptance.

- **Export playback:** Human visual/audible review and specified output evidence remain pending.

- **Performance:** Lag and WPF playback resource-retention questions remain open; previous high system-memory readings alone do not prove causation.

- **Deployment:** Signing/publisher decisions and clean-machine installer acceptance remain open.

### Additional limitations

Preview is rendered and codec-dependent rather than realtime or frame-exact. External media tools and retained intermediates require storage management. Additional themes and launch-at-sign-in are not implemented.

### Release policy

The proposed version is 0.2.0-beta.1, not an approved public installer. Future release notes should identify the actual installer, SHA-256 checksum, signer, tested Windows versions and remaining known issues.

BEFORE RELYING ON A RELEASE

Automated coverage does not prove camera compatibility, audible playback, synchronization or clean-machine installation. Do not use the outdated 0.1.0-preview installer as the repaired candidate.

## Technical reference & support

Architecture, third-party components and where to get help.

### Application architecture

- **Core:** Domain models and contracts.

- **Application:** Workflow coordination.

- **Infrastructure:** Capture pipelines, external media tools and storage.

- **Desktop:** WPF user interface and Windows lifecycle.

CaptureFlow uses .NET 10/WPF, Windows capture and camera APIs, NAudio/WASAPI, external

FFmpeg/FFprobe and SQLite. The editor renders a project snapshot to a preview file; final export is a separate operation. JSON stores preferences/recovery state and SQLite supports library metadata.

### Licensing

Original CaptureFlow code and documentation are MIT-licensed. Third-party dependencies, font assets and external FFmpeg builds retain their own terms. FFmpeg licensing varies by build options, including GPL components; the reviewed local external build is not automatically approved for redistribution.

### Help and updates

For non-sensitive problems, use GitHub Issues. Include app/build version, Windows version, recording mode, steps to reproduce, expected and actual behavior, and the exact redacted error. Check the project repository for approved future release information.

FINAL CHECKLIST

Before an important session: confirm sources and permissions, check free disk space, make a short authorized test, play/listen to it, and keep original media and project backups.

### Documentation and contribution links

- [Installation](INSTALLATION.md)
- [Known issues](KNOWN-ISSUES.md)
- [Architecture](ARCHITECTURE.md)
- [Contributing](CONTRIBUTING.md)
- [Privacy and permissions](PRIVACY.md)
- [MIT License](../LICENSE)
- [GitHub Issues](https://github.com/Janith2002/CaptureFlow/issues)
- [Project repository](https://github.com/Janith2002/CaptureFlow)
