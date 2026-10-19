# Third-party dependency notices (release preparation)

This inventory records license declarations verified from restored production NuGet metadata. It is not a declaration of distribution compliance. Preserve upstream copyright notices and license texts before distributing a release.

| Package | Version | Declared license |
|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Data.Sqlite | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Data.Sqlite.Core | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Configuration | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Configuration.Binder | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Configuration.CommandLine | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Configuration.FileExtensions | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Configuration.Json | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Configuration.UserSecrets | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.DependencyInjection | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Diagnostics | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Diagnostics.Abstractions | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.FileProviders.Abstractions | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.FileProviders.Physical | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.FileSystemGlobbing | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Hosting | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Logging | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Logging.Abstractions | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Logging.Configuration | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Logging.Console | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Logging.Debug | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Logging.EventLog | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Logging.EventSource | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Options | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Options.ConfigurationExtensions | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| Microsoft.Extensions.Primitives | 10.0.12 | [MIT](https://licenses.nuget.org/MIT) |
| NAudio | 3.1.0 | [MIT](https://licenses.nuget.org/MIT) |
| NAudio.Asio | 3.1.0 | [MIT](https://licenses.nuget.org/MIT) |
| NAudio.Core | 3.1.0 | [MIT](https://licenses.nuget.org/MIT) |
| NAudio.Dmo | 3.1.0 | [MIT](https://licenses.nuget.org/MIT) |
| NAudio.Midi | 3.1.0 | [MIT](https://licenses.nuget.org/MIT) |
| NAudio.Wasapi | 3.1.0 | [MIT](https://licenses.nuget.org/MIT) |
| NAudio.WinForms | 3.1.0 | [MIT](https://licenses.nuget.org/MIT) |
| NAudio.WinMM | 3.1.0 | [MIT](https://licenses.nuget.org/MIT) |
| Serilog | 4.4.0 | [Apache-2.0](https://licenses.nuget.org/Apache-2.0) |
| Serilog.Sinks.File | 7.0.0 | [Apache-2.0](https://licenses.nuget.org/Apache-2.0) |
| SharpGen.Runtime | 2.4.2-beta | [MIT](https://licenses.nuget.org/MIT) |
| SharpGen.Runtime.COM | 2.4.2-beta | [MIT](https://licenses.nuget.org/MIT) |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.12 | [Apache-2.0](https://licenses.nuget.org/Apache-2.0) |
| SQLitePCLRaw.core | 2.1.12 | [Apache-2.0](https://licenses.nuget.org/Apache-2.0) |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.12 | [Apache-2.0](https://licenses.nuget.org/Apache-2.0) |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.12 | [Apache-2.0](https://licenses.nuget.org/Apache-2.0) |
| System.Numerics.Tensors | 9.0.0 | [MIT](https://licenses.nuget.org/MIT) |
| Vortice.Direct3D11 | 3.8.3 | [MIT](https://licenses.nuget.org/MIT) |
| Vortice.DirectX | 3.8.3 | [MIT](https://licenses.nuget.org/MIT) |
| Vortice.DXGI | 3.8.3 | [MIT](https://licenses.nuget.org/MIT) |
| Vortice.Mathematics | 2.1.0 | [MIT](https://licenses.nuget.org/MIT) |

Milestone 63 adds `Licenses/`: unmodified .NET 10.0.12 runtime license and full third-party notices, Windows Desktop 10.0.12 license, CommunityToolkit 8.4.2 license/notices and System.Numerics.Tensors 9.0.0 license. `PROVENANCE.json` records source package paths and SHA-256; `PACKAGE-NOTICES.json` preserves each production package's declared license, copyright, authors and repository commit. These files are copied to build and publish outputs. This inventory excludes test-only packages. Packages that supply only a license expression still need source-level notice review; completeness is not claimed.

FFmpeg and ffprobe are external tools and are NOT bundled. Local verification used Gyan 9.0.2-full_build-www.gyan.dev, with GPL and version3 enabled. Its corresponding source, third-party library notices and redistribution obligations have not been fully verified. No redistribution approval is claimed.

Sources: https://ffmpeg.org/legal.html and https://www.gyan.dev/ffmpeg/builds/. Executable hashes and configure output: artifacts/qa/release-ffmpeg-audit.json and release-ffmpeg-buildconf.txt.

## Milestone 81 private candidate notice supplement

Licenses/Distribution contains unmodified exact-version runtime/package notices and immutable upstream license texts for NAudio, Serilog, SharpGen, Vortice and SQLitePCLRaw. DISTRIBUTION-PACKAGES.json maps all 54 runtime/package records from the candidate dependency manifest; DISTRIBUTION-PROVENANCE.json records source URLs/package paths and hashes. SQLitePCLRaw NOTICE and native SQLite public-domain terms are retained. CaptureFlow original material remains under LICENSE; third-party terms are not replaced.

Windows SDK .NET Ref 10.0.19041.57 declares separate SDK license terms, not a generic MIT license. Those terms are included; public redistribution scope and end-user terms for its runtime projection files remain a specific acceptance blocker. These packages are PRIVATE internal test candidates. No blanket binary-distribution clearance is claimed. FFmpeg/FFprobe remain external and excluded.
