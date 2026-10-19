# Installation and requirements

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

See the [complete guide](CaptureFlow-Complete-User-Guide.md).


## Build from source

Clone the complete source repository to build the application. Use Windows with the .NET 10 SDK and compatible external FFmpeg/FFprobe for media operations.

```powershell
git clone https://github.com/Janith2002/CaptureFlow.git
cd CaptureFlow
dotnet restore CaptureFlow.slnx
dotnet build CaptureFlow.slnx
dotnet test CaptureFlow.slnx --no-build
dotnet run --project src/CaptureFlow.Desktop/CaptureFlow.Desktop.csproj
```

Do not run a build against locked output files; save and close a running development instance yourself if necessary. No physical preview/recording is required to launch or run deterministic tests. See [architecture](ARCHITECTURE.md) and [license status](SOURCE-CODE-POLICY.md).

## Version 1.0.0 validation builds

The requested stable-release target is now 1.0.0. Local validation artifacts are named CaptureFlow-Setup-1.0.0-win-x64.exe and CaptureFlow-Portable-1.0.0-win-x64.zip. They are unsigned and private; the stable release is not approved or published. Required regression, licensing, physical playback and clean-machine acceptance remain incomplete. No download links are advertised until those gates pass. The published guide remains unchanged; a local 1.0.0 validation edition is prepared separately.
