# Implemented features

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

## Video editing

For a first device-free session, open Video Editor and **Project > New**. Import a generated clip using **Import media**, select it in the bin and choose **Add to timeline**. Search imported media to find a source.

Select a timeline clip and confirm Inspector identity. Make a small change; use **Apply clip properties** where shown. Test Undo/Redo and save to a new disposable project path.

Choose **Update preview** to render the timeline. Playback uses a rendered file rather than realtime compositing. An edit invalidates the earlier preview. Regenerate before judging the result. **Monitor > Audition source audio** is not the edited mix.

Export to a separate folder and personally play/listen to the new file. A project describes edits and references source media; it is neither a rendered video nor a portable source bundle. Preserve original files at their expected paths.

## Known limitations

The proposed beta is not an approved general release. No new installer is published. Publisher identity/signing, licensing and clean-machine acceptance remain gates; old preview installation evidence does not certify the repaired candidate.

Formal QA is **20 PASS / 26 Not Run**. Pending: R01–R13; E05, E06, E11, E12, E14, E15; P03–P07; L02, L03. Six Editor PASS declarations lack required behavior detail and were preserved without formal upgrades.

Physical webcam/PiP recording/playback, A/V synchronization and complete export playback evidence remain pending. User-observed lag and restricted WPF playback resource retention remain open. Previously measured Windows memory load of 92–94% does not prove cause.

Latest 893 tests ran successfully under unchanged policy; historical Application Control restrictions still matter for other contexts. Preview is rendered/codec-dependent, not realtime/frame-exact. Additional themes and launch-at-sign-in are not implemented. External tools and retained intermediates require attention.

See the [complete guide](CaptureFlow-Complete-User-Guide.md).
