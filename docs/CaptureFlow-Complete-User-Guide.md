# CAPTUREFLOW

Complete User Guide & Software Documentation

Proposed candidate: **0.2.0-beta.1** · Documentation edition: **2026-10-16**

No public installer is approved. Procedures describe implemented controls, not completion of every human acceptance case. No screenshots are included; diagrams are illustrative.

## Contents

1. [Introduction](#introduction)
2. [System requirements](#system-requirements)
3. [Installation and updates](#installation-and-updates)
4. [Main interface](#main-interface)
5. [Six recording modes](#six-recording-modes)
6. [Screen sources](#screen-sources)
7. [Webcam and PiP](#webcam-and-pip)
8. [Microphone and system audio](#microphone-and-system-audio)
9. [Recording settings](#recording-settings)
10. [Shortcuts and tray](#shortcuts-and-tray)
11. [Video editing](#video-editing)
12. [Timeline controls](#timeline-controls)
13. [Trimming splitting and cropping](#trimming-splitting-and-cropping)
14. [Inspector and transforms](#inspector-and-transforms)
15. [Transitions and keyframes](#transitions-and-keyframes)
16. [Audio editing and mixing](#audio-editing-and-mixing)
17. [Project saving and recovery](#project-saving-and-recovery)
18. [Media library](#media-library)
19. [MP4 MP3 and WAV exports](#mp4-mp3-and-wav-exports)
20. [Export progress and status](#export-progress-and-status)
21. [Troubleshooting](#troubleshooting)
22. [Privacy and permissions](#privacy-and-permissions)
23. [Known limitations](#known-limitations)
24. [Technical architecture](#technical-architecture)
25. [Third-party components](#third-party-components)
26. [FAQ](#faq)
27. [Updates and support](#updates-and-support)

## Introduction

CaptureFlow is a Windows desktop application for recording screen, webcam and audio sources, editing a timeline and exporting media. Begin with generated, non-sensitive clips when evaluating the editor. Read Privacy and permissions before enabling any physical source.

The latest development regression passed **893 tests**, with zero failures/skips/blocked tests. Its clean build reported zero warnings/errors. Formal human QA remains **20 PASS / 26 Not Run** across 46 cases. Automated success does not prove physical camera operation, audible playback or installation on another computer.

The proposed version is a beta candidate, not an available installer. The historical unsigned 0.1.0-preview setup predates current repairs. Do not use it as the repaired build or redistribute it as a current download.

For a device-free first session, import generated media, add a clip, make a small edit, update the rendered preview and save a disposable project. Only then export to a separate folder and personally verify playback. Keep original source media unchanged.

## System requirements

The intended package targets **Windows 11 x64**. The installer excludes ARM64 and 32-bit Windows. Its build floor is 22000, but that is not a tested support matrix. Use a currently supported Windows 11 release compatible with .NET 10. Clean-machine and OS-version acceptance are still pending.

The planned installer is self-contained .NET 10/WPF. A separate .NET SDK or Desktop Runtime should not be required once this is verified on a clean machine. A development checkout needs the .NET 10 SDK; that is not the public installation method.

FFmpeg and matching FFprobe are external dependencies. The documented paths require libx264, AAC and libmp3lame support. Hardware encoders additionally need compatible devices/drivers and a successful capability check.

No measured minimum RAM, CPU, GPU or storage specification has been accepted. Leave room for source media, preview renders, intermediates, exports and retained recovery files. Larger resolutions/FPS increase workload. A webcam, microphone or output device is needed only for applicable recording modes.

Reference: [Microsoft .NET Windows support](https://learn.microsoft.com/en-us/dotnet/core/install/windows).

## Installation and updates

There is **no approved public installer download**. When a candidate is approved, obtain it from this repository's release assets, compare its SHA-256 with the announced checksum and verify the expected publisher/signature. A hash identifies bytes; it does not certify trust. Do not disable Windows security to run setup.

The intended per-user location is `%LOCALAPPDATA%\Programs\CaptureFlow`. Setup requests lowest privileges, offers an optional desktop shortcut and does not automatically launch CaptureFlow or download external tools. Publisher/signing decisions remain open.

Before updating, save projects, stop active work and close the application yourself. Preserve backups. Setup avoids forced shutdown; current upgrade/uninstall behavior still requires clean-machine testing.

After launch, open **Settings > External tools**. Select the approved `ffmpeg.exe`, place matching `ffprobe.exe` in the same folder and choose **Validate and Save**. The absolute path is stored in local application data. Missing codecs/tool validation must be resolved before dependent operations. No automatic FFmpeg download is provided.

## Main interface

Recording Studio emphasizes the monitor, compact toolbar, **Sources**, **Inspector** and optional **Mixer**. Collapse Sources or Inspector for a larger preview. **Workspace > Reset layout** restores arrangement; it is not a request to remove media.

Video Editor provides the media bin, preview monitor, Inspector and timeline. **Project** holds New, Open project, Save project and Import media. **Edit** contains clip operations. **View** controls Media bin, Inspector and Reset layout. **Monitor** offers preview update, fullscreen, mute, source audition and cropping.

Read tooltips for compact controls. Unavailable commands can reflect selection, busy state or validation. Avoid repeated clicks while an operation is running.

Illustrative workflow, not a screenshot:

**Configure sources → authorized preview/recording → stop/finalize → library → edit → update preview → export → verify playback.**

For a device-free workflow, start at **Import media** and skip all physical preview/recording. Mode selection itself can initialize a camera, so do not use camera modes as a harmless visual check.

## Six recording modes

Video choices are **Screen Only**, **Webcam Only** and **Screen + Webcam**. Audio Recorder provides **Microphone Only**, **System Audio Only** and **Microphone + System Audio**.

Screen Only targets a screen, window or region. Webcam Only uses the selected camera. Screen + Webcam combines screen content and a camera overlay. Microphone Only captures an input; System Audio Only uses an output's loopback; the combined audio mode mixes both.

Before authorized capture, remove sensitive content, obtain consent, select sources and confirm output settings/destination. Camera-mode selection can initialize a physical device. A preview is device access even if no recording file is being written.

Use the available Start/Pause/Resume/Stop controls only when recording is intended. Wait for saving/finalization before closing the app or opening the output. Genuine source readiness determines availability. A clickable button alone does not prove capture works. Physical mode-by-mode playback and synchronization remain separate acceptance gates.

## Screen sources

Choose **Entire Screen**, **Application Window** or **Custom Region** and confirm the intended source. Recheck after changing displays or closing a target window. Inspect messages when a source disappears; do not assume the previous selection still represents the intended content.

**Fit** shows the complete preview; **1:1** exposes source pixels with scrolling. These viewing controls do not set output resolution. Start screen preview only with authorization and a safe desktop.

Video preferences select output resolution and frame rate. Aspect ratio is preserved and portrait sources retain orientation. A 4K output needs an appropriate source. Offered frame rates are 24, 30 and 60 FPS, but actual throughput depends on source/encoder load; overload may repeat or skip source frames.

Use a short, disposable recording before a critical session. Inspect the saved result rather than assuming exclusions or the preview provide a complete privacy guarantee. No physical recording was performed to produce this guide.

## Webcam and PiP

Select **Webcam Only** or **Screen + Webcam** only when camera access is intended. Check the selected device and reported format. Device resolution/FPS/pixel format must be supported; output scaling is separate from the capture format.

The main Studio toolbar exposes mode-aware preview actions without requiring Inspector. Camera mode needs valid camera readiness; PiP also needs a valid screen source. Configure camera/overlay controls and inspect framing in an authorized preview before recording.

If Start is unavailable, read validation, check device selection, permission, initialization and, for PiP, screen source. Close competing camera applications where appropriate. Review Windows permissions yourself; do not bypass readiness checks or weaken security to make Start clickable.

The toolbar repair and synthetic regressions are implemented. **Physical webcam/PiP recording, final playback and A/V synchronization are not explicitly accepted.** Report the mode, device, action and exact diagnostic if a problem returns. Synthetic tests cannot establish compatibility with a particular camera or driver.

## Microphone and system audio

In Audio Recorder, select the mode and required microphone/output device. System audio follows the selected output's loopback; changing Windows routing can make the old selection inappropriate.

Choose **WAV** or **MP3** before starting. MP3 choices are 128, 192, 256 and 320 kbps and require compatible FFmpeg. WAV avoids MP3 encoding but still requires valid capture devices.

Microphone and system-audio mute controls are independent. Microphone mute does not mute system audio or add a microphone to a mode that has none. Inspect levels to avoid clipping/feedback. Monitoring can expose private conversations; leave it off during configuration-only checks.

After an authorized short recording, Stop and await saving. Listen to the file to verify sources and level. For combined sources or video, use an observable event to evaluate synchronization. A moving meter does not prove the saved file contains audible sound or correct timing.

## Recording settings

Video settings include output resolution/FPS, quality preset and encoder. Quality choices are Low, Standard, High, Ultra and Custom. Custom exposes a validated bitrate and suggested values. Higher settings increase storage/processing and cannot restore detail absent from the source.

Use **Check available encoders** before a hardware choice. The UI explains that hardware accelerates final export after capture; a lossless intermediate requires extra storage. Explicit hardware selections require a successful check and do not silently fall back. Driver compatibility is not guaranteed by Auto mode.

Mouse options include showing/highlighting the cursor and click animation, with colors/radius/duration. They apply to screen workflows. Use **Save quality settings** where offered and read confirmation/validation.

Sources > Recording presets provides Apply, Save new, Rename and Delete preset. A preset stores configuration, not recorded media or a backup. Use a disposable preset during testing, and recheck current device selections/destination after applying one.

## Shortcuts and tray

Default global bindings:

- **Ctrl+Alt+R** — Start/Stop recording.
- **Ctrl+Alt+P** — Pause/Resume.
- **Ctrl+Alt+M** — microphone mute.
- **Ctrl+Alt+C** — Show/Hide CaptureFlow.

These may act without app focus. Disable them when accidental capture is unsafe. **Settings > Keyboard shortcuts** controls enablement and assignments; choose **Save shortcuts** to apply. Conflicts/duplicates are reported, not proof of successful registration.

The tray offers Open CaptureFlow, recording controls, Open Recordings and Exit. **Settings > General > Minimize and close to system tray** must be saved. If enabled and the tray is available, closing can hide the window. Hiding does not imply recording stopped; check state and use explicit controls.

Editor bindings include Ctrl+S save, Ctrl+O open, Ctrl+Z/Y undo/redo, Ctrl+C/X/V copy/cut/paste, Delete remove, S split, V select and T trim. They depend on focus and command availability; text inputs are handled separately.

## Video editing

For a first device-free session, open Video Editor and **Project > New**. Import a generated clip using **Import media**, select it in the bin and choose **Add to timeline**. Search imported media to find a source.

Select a timeline clip and confirm Inspector identity. Make a small change; use **Apply clip properties** where shown. Test Undo/Redo and save to a new disposable project path.

Choose **Update preview** to render the timeline. Playback uses a rendered file rather than realtime compositing. An edit invalidates the earlier preview. Regenerate before judging the result. **Monitor > Audition source audio** is not the edited mix.

Export to a separate folder and personally play/listen to the new file. A project describes edits and references source media; it is neither a rendered video nor a portable source bundle. Preserve original files at their expected paths.

## Timeline controls

Use **Fit** to view the project, then zoom for precise interaction. Scroll position, clip boundaries and scale should remain aligned. Select a clip before editing its properties.

The monitor provides Jump to start, Previous frame, Play, Pause, Stop, Next frame and Jump to end. These operate on rendered media. WPF seeking is approximate and codec-dependent; frame buttons do not certify frame-exact visual inspection.

Move clips with selection; use the trim interaction or edges within source bounds. Locked tracks prevent edits. Mute affects the rendered mix, not merely panel appearance. Update preview after committed changes.

If content appears stale, check render invalidation and whether source audition is active. Do not repeatedly launch full renders while seeking. Stop playback and save before investigating persistent issues; use a disposable project for destructive-operation testing.

## Trimming splitting and cropping

Select a clip and drag its left/right edge to change the used source range. The original media remains unchanged. Inspect timeline duration and playback, then Undo if needed.

Select a clip, place the playhead and use **S** with editor focus to split. Check both resulting clips/source ranges. **Edit** also exposes Cut, Copy, Paste, Ripple delete and Join clips where valid. Ripple operations can move following material; inspect downstream timing.

Use **Monitor > Crop selected source**, adjust the crop and select **Apply crop**. **Cancel crop** abandons the interaction; **Clear crop** removes the selected crop. Update preview to inspect committed framing.

If an operation is unavailable/rejected, read its diagnostic instead of forcing values. Source bounds and edit validity remain enforced. Save a new project before experimenting, retain originals and render a separate output only after reviewing the edit.

## Inspector and transforms

Inspector presents the selected clip's properties. Expand **Audio & fades** or **Transform & text** for detailed controls. Use **Apply clip properties** to commit where shown. **Reset transform** restores transform settings.

Selection and displayed identity must agree. Recheck fields after changing clips. Closing/reopening Inspector is not an explicit project save. Undo/Redo operates on committed edits; save separately.

Transforms alter framing/position/scale/rotation/opacity where offered. Cropping and text are separate concerns. Inspect rendered results rather than trusting numeric values alone; rotation can expose transparent/black areas outside the source.

Use a small change and Update preview, checking more than one time position. **View > Reset layout** restores panel arrangement without requesting timeline deletion. If values, selection and content disagree, stop and report a disposable reproduction.

## Transitions and keyframes

Inspector > **Transitions** includes supported fade/dissolve workflows. Select appropriate clips/settings and choose **Apply transition**; **Delete edge** removes the selected transition. Dissolve requires compatible adjacency and source handles. Invalid setups should explain why they cannot apply.

Inspector > **Animation** provides **Add at playhead**, **Move / update**, **Delete key** and **Reset animation**. Keys use clip-local time derived from the timeline playhead. Set two distinct keys and inspect values, markers and animation. Linear/hold interpolation is supported where offered.

Update preview after committing changes. Inspect boundaries/midpoints and Undo/Redo. Model evaluation at a requested time is not a promise of frame-exact WPF display.

Advanced edits can promote a project from version 1 to version 2. Older builds reject unsupported advanced projects instead of rendering them faithfully. Keep backups when sharing with older builds. Full human transition/keyframe behavior confirmation remains pending in the formal ledger.

## Audio editing and mixing

Clip settings include gain, mute and fades. **Track mix & silence** exposes track gain/mute and silence operations. **Apply clip properties** and **Apply track mix** commit their respective changes; a slider movement may not be the final operation.

Use a generated tone for a safe check. Update preview, hear the unmuted mix, mute the intended clip/track, regenerate as required and confirm silence. Restore the desired state and listen again. Preview-monitor mute is different from exported track mute.

**Detect silence** analyzes material. Inspect the result before **Remove detected silence**; removing intervals can change timing. Test with a saved disposable project and inspect Undo behavior.

Source audition is not proof of the edited mix. Finally listen to the exported MP4/MP3/WAV for intended audio, fades and timing. Waveforms and successful decoding do not prove audibility or perceptual synchronization.

## Project saving and recovery

Use **Project > Save project** for a `.cfedit` project. Open project restores edit state and references, not embedded media. Moving/changing sources can prevent rendering. Keep source files and backups.

**Settings > Recovery & Protection** controls recording recovery and editor autosave. Save protection preferences explicitly. Autosave interval is 15–300 seconds. Metadata/snapshots use the configured recovery location; recording intermediates remain beside output in `.captureflow-work`.

Review **Recovery available**, session states, timestamps, folders and warnings. **Recover / Restore editor** attempts recovery; **Open Folder** inspects files; **Dismiss** changes recovery workflow state. Recovery is best effort; missing/damaged data may be unrecoverable.

Inspect storage before confirmation-required **Clean terminal metadata**. This is not general media deletion. Finished journals can be archived, while retained media still consumes disk space. Never manually erase unfamiliar work folders during active jobs. Recovery does not replace backups or final playback checks.

## Media library

Use the **Recordings** page to browse registered media and available open/play/edit actions. Confirm the selected filename/location; similarly named exports can be distinct files. A library entry is metadata, not a media backup.

**Settings > Storage > Choose folder** sets the shared screen/audio destination and supports browsing existing MP4/WAV/MP3 files. **Open folder** opens it when present. Choosing a folder does not imply all old media has moved there.

If an item is missing, inspect its original path and any external move before touching database data. A library notification failure can occur after a validated export exists; use the retained output path to distinguish file success from library refresh failure.

Use disposable records for testing. Do not rename/delete existing user files to create a clean test. Formal resource checks L02/L03 concern preview lifecycle measurements, not removal of library entries or disclosure of personal record identities.

## MP4 MP3 and WAV exports

Open **Export**, select MP4 for video/optional audio or MP3/WAV for audio-only output, then review exposed format settings and destination. Audio-only output does not preserve a video stream.

Save the project and inspect an updated preview first. Use a fresh test folder and unique outputs; never approve replacement of existing media. The editor exporter allocates timestamp/GUID names and dedicated work directories.

Wait for finalization. The backend probes/decodes output before publication, while UI completion also requires a nonempty file with matching format extension. This is stronger than a process exit, but not a substitute for personal playback/listening.

Record the actual completion path, size and settings. Play the new MP4 and inspect edits; listen to intended audio in MP4/MP3/WAV. Formal P03–P07 remain pending until new outputs, metadata and human playback evidence satisfy their exact criteria. A general “works” report does not replace these observations.

## Export progress and status

Status remains visible through **Preparing**, **Exporting** and **Finalizing**, showing filename/path, format and elapsed time. A reliable duration/progress fraction enables percentage; otherwise the indicator is indeterminate. Partial progress does not mean the file is ready.

**Export completed** appears only after output checks. Retained completion includes full path, byte size and Open File/Open Folder actions when available. Shell-open failure can occur even if the file remains saved.

**Export failed** retains a summary and technical details. Correct the cause before **Retry**, which creates a new output. Common causes include invalid paths, missing sources, incompatible settings and external-tool errors. A brief Preparing state or process exit is not proof of success.

**Cancel** requests cancellation and may show Cancelling while it unwinds. Cancelled is separate from Completed; owned recovery files may remain and no completed output is confirmed. Dismiss applies to inactive status. Avoid repeated clicks; overlapping jobs are guarded.

## Troubleshooting

For startup failure, preserve the exact error and diagnostic reference. The read-only export-progress binding failure was corrected and has a real-WPF regression. An old executable can retain an old defect; check the actual build.

For disabled Start, check mode/source selection, readiness, active work and validation. Camera modes need appropriate camera state; PiP also needs a screen source. Do not bypass validation.

For export errors, retain status, format, actual path and details. Check approved FFmpeg/FFprobe, codecs, source presence and writable destination. For stale edits use Update preview; distinguish source audition from edited playback.

For lag, stop preview and record workload, app CPU/private memory/handles/threads and system memory. High system load alone does not establish causation. Do not terminate Windows-owned media threads. For an Application Control block, retain diagnostics and contact the maintainer/administrator; do not disable controls or rename binaries to evade policy.

Reference: [Smart App Control](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview).

## Privacy and permissions

Screen capture can expose passwords, notifications and windows. Microphones can include nearby conversations; loopback can contain calls; webcams can reveal people/surroundings. Preview may access a device before recording. Obtain consent and prepare a safe scene.

The reviewed implementation stores media, project references, preferences, recovery metadata and library data locally, and invokes external FFmpeg/FFprobe. Source review found no application telemetry/cloud-upload implementation; this is not an independent security certification or a guarantee about Windows, third-party tools or synced destinations.

Diagnostics/metadata may contain filenames, paths, device identifiers and exception details. Redact before sharing. Prefer a synthetic reproduction instead of uploading private recordings/projects.

Use Windows device permissions deliberately. Access denial is a valid condition, not a reason for bypass. This guide does not authorize capture or policy changes. Review behavior of external tools and any applications opening the resulting files separately.

## Known limitations

The proposed beta is not an approved general release. No new installer is published. Publisher identity/signing, licensing and clean-machine acceptance remain gates; old preview installation evidence does not certify the repaired candidate.

Formal QA is **20 PASS / 26 Not Run**. Pending: R01–R13; E05, E06, E11, E12, E14, E15; P03–P07; L02, L03. Six Editor PASS declarations lack required behavior detail and were preserved without formal upgrades.

Physical webcam/PiP recording/playback, A/V synchronization and complete export playback evidence remain pending. User-observed lag and restricted WPF playback resource retention remain open. Previously measured Windows memory load of 92–94% does not prove cause.

Latest 893 tests ran successfully under unchanged policy; historical Application Control restrictions still matter for other contexts. Preview is rendered/codec-dependent, not realtime/frame-exact. Additional themes and launch-at-sign-in are not implemented. External tools and retained intermediates require attention.

## Technical architecture

CaptureFlow uses .NET 10/WPF, view models and commands. Core holds domain models/contracts; Application coordinates workflows; Infrastructure provides recording, media tools and storage; Desktop owns Windows UI/lifecycle. This description does not grant application-source distribution rights.

Windows capture/camera and audio device/loopback paths feed existing recording pipelines. FFmpeg/FFprobe run externally for rendering, encoding, probing and decoding. The editor renders a project snapshot and WPF plays the resulting preview file.

Project documents retain media references; SQLite supports library data and JSON stores preferences/recovery state. Cancellation and ownership checks matter because media processing can outlive a single interaction. Final exports differ from temporary/intermediate files.

Automated tests use generated media/fakes and selected real-WPF checks without physical capture. They do not replace hardware/driver, listening, synchronization or deployment-policy verification on another machine.

## Third-party components

Dependencies include .NET/WPF, Windows SDK/WinRT components, CommunityToolkit, SQLite and media packages. The project maintains notices, package provenance and copied license texts. Reconcile the actual future payload to these inventories; an inventory alone is not compliance certification.

FFmpeg/FFprobe are not bundled by the current installer. FFmpeg licensing depends on build options, including optional GPL components. The locally reviewed external full build is not automatically approved for redistribution. Do not assume external invocation settles all legal questions.

Product publisher identity, application terms/license and redistribution decisions remain owner approvals. Documentation publication does not grant an open-source license or remove third-party rights.

See [third-party notice summary](THIRD-PARTY-NOTICES.md), [packaged notice inventory](../THIRD-PARTY-NOTICES.md) and [FFmpeg legal guidance](https://www.ffmpeg.org/legal.html). These distinguish component licensing from the application's unresolved license policy.

## FAQ

**Why is Start disabled?** Valid mode-specific sources/readiness are required; busy jobs can lock commands. Read the validation message.

**Why is my edit invisible?** Commit properties and Update preview. Source audition is not the edited mix.

**Where is my export?** Use its retained completion path/Open Folder. A library refresh problem is separate from saved output. Failed/cancelled is not completed.

**Can I send just the project?** It references media; keep sources. It is not a bundled video.

**Does hiding stop recording?** Do not assume so. Check state and use explicit controls; global shortcuts may work while hidden.

**Can I publish the existing setup?** No: it is outdated and unapproved. A new candidate needs separate authorization.

**Do 893 tests prove my camera works?** No. Physical device, listening, synchronization and deployment checks are separate.

## Updates and support

Use [GitHub Issues](https://github.com/Janith2002/CaptureFlow/issues) for non-sensitive reports. Include actual app/build version, Windows version, mode, steps, expected/actual behavior and exact error. Redact private paths/identifiers and never post credentials or personal recordings. No private vulnerability channel is approved; avoid sensitive exploit details in public issues.

Before critical use, select an approved candidate, check sources/permissions/destination, allow disk space and make a short authorized test. Play/listen before relying on a long recording. Save projects and preserve originals/backups.

Export acceptance needs generated inputs, unique new outputs, stream/format/duration validation and human visible/audible playback. Performance acceptance needs the same process, exactly three preview lifecycle cycles and cleanup/settle sampling, not unrelated snapshots.

Future release notes must identify the actual installer, SHA-256, signer, tested Windows versions and remaining issues. This guide is proposed-beta documentation, not release approval.
