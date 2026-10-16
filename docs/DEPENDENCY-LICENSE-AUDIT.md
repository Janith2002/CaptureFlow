# Dependency and asset license audit — MIT source publication

Milestone 78: the owner approved MIT for CaptureFlow's original material and authorized the audited source publication and narrowly scoped personal email metadata remediation. The final review retains the 65-package restored inventory; third-party licenses are not replaced by MIT.

## Scope and permission profiles

Inventory derived from restored project.assets.json and package nuspecs, including production, test and local QA tool dependencies. 65 unique packages; 51 used by production projects. Exact package names/versions below are restored evidence, not a new restore or lockfile guarantee. No package archive, package DLL, third-party implementation source, runtime, FFmpeg executable, media fixture or installer compiler is proposed for the source repository. Original code merely references the listed dependencies. No original source attribution marker identified copied third-party implementation, which is not proof of independent authorship; the owner separately confirmed publication rights.

**M profile (MIT):** third-party source and binary redistribution permitted with applicable copyright/permission notices; retain warranty disclaimer. No general source-disclosure requirement. MIT for original application code appears compatible with these declared reference dependencies, subject to retaining their own notices; no blanket clearance is claimed.

**A profile (Apache-2.0):** third-party source/binary redistribution permitted subject to license, applicable NOTICE/attribution, retained notices and marking modifications. No general requirement to disclose the original application's source; patent/trademark terms remain relevant. MIT for original code does not replace Apache-2.0 terms on dependencies; retain their own licenses/NOTICE obligations in any applicable distribution. Actual bundled notices must be verified before binary distribution. See [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0).

**L profile (legacy URL):** xunit.abstractions/2.0.3 provides a licenseUrl rather than a modern expression. Its declared URL resolves to Apache-2.0 wording with MIT exceptions for named imported xUnit code. Retrieved reference SHA-256: E9DCF94CC4864BA47AEAAEC879CFABAF3FEADB813C77AF480C3AD14C6DE0ED79. Upstream abstractions tag 2.0.3 resolves to 489a2b3949a9ae8587201a22a8ecfc1d791fa1f3. No package binaries/source are republished here. Treat binary notice provenance as pending rather than substituting a license from another version. [Declared reference](https://raw.githubusercontent.com/xunit/xunit/master/license.txt).

The profiles specify source permission, binary permission, required notices, source-disclosure obligations and MIT source-scope compatibility for each corresponding row. A package's metadata declaration is not a complete nested-native or binary compliance audit. Binary redistribution remains unapproved; no package is relicensed by this document.

## Exact package inventory

Origin links identify the exact NuGet version. License evidence is the restored package nuspec's license/licenseUrl plus supplied notice files; local hashes and repository commits are in the nonpublic audit inventory. P=production dependency, T=test/tool only.

| Component | Version | Scope | Origin | Declared license / profile | Supplied license/notice files |
|---|---|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | P | [NuGet](https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2) | MIT / M | License.md; ThirdPartyNotices.txt |
| Microsoft.Data.Sqlite | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.12) | MIT / M | No standalone file located; review upstream notices before bundling |
| Microsoft.Data.Sqlite.Core | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Data.Sqlite.Core/10.0.12) | MIT / M | No standalone file located; review upstream notices before bundling |
| Microsoft.Extensions.Configuration | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Abstractions/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Configuration.Binder | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Binder/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Configuration.CommandLine | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.CommandLine/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.EnvironmentVariables/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Configuration.FileExtensions | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.FileExtensions/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Configuration.Json | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.Json/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Configuration.UserSecrets | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Configuration.UserSecrets/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.DependencyInjection | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Diagnostics | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Diagnostics.Abstractions | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.Abstractions/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.FileProviders.Abstractions | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.FileProviders.Abstractions/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.FileProviders.Physical | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.FileProviders.Physical/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.FileSystemGlobbing | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.FileSystemGlobbing/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Hosting | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Hosting/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Hosting.Abstractions/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Logging | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Logging/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Logging.Abstractions | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Logging.Configuration | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Configuration/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Logging.Console | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Console/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Logging.Debug | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Debug/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Logging.EventLog | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Logging.EventLog/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Logging.EventSource | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Logging.EventSource/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Options | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Options/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Options.ConfigurationExtensions | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Options.ConfigurationExtensions/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| Microsoft.Extensions.Primitives | 10.0.12 | P | [NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Primitives/10.0.12) | MIT / M | THIRD-PARTY-NOTICES.TXT |
| NAudio | 3.1.0 | P | [NuGet](https://www.nuget.org/packages/NAudio/3.1.0) | MIT / M | No standalone file located; review upstream notices before bundling |
| NAudio.Asio | 3.1.0 | P | [NuGet](https://www.nuget.org/packages/NAudio.Asio/3.1.0) | MIT / M | No standalone file located; review upstream notices before bundling |
| NAudio.Core | 3.1.0 | P | [NuGet](https://www.nuget.org/packages/NAudio.Core/3.1.0) | MIT / M | No standalone file located; review upstream notices before bundling |
| NAudio.Dmo | 3.1.0 | P | [NuGet](https://www.nuget.org/packages/NAudio.Dmo/3.1.0) | MIT / M | No standalone file located; review upstream notices before bundling |
| NAudio.Midi | 3.1.0 | P | [NuGet](https://www.nuget.org/packages/NAudio.Midi/3.1.0) | MIT / M | No standalone file located; review upstream notices before bundling |
| NAudio.Wasapi | 3.1.0 | P | [NuGet](https://www.nuget.org/packages/NAudio.Wasapi/3.1.0) | MIT / M | No standalone file located; review upstream notices before bundling |
| NAudio.WinForms | 3.1.0 | P | [NuGet](https://www.nuget.org/packages/NAudio.WinForms/3.1.0) | MIT / M | No standalone file located; review upstream notices before bundling |
| NAudio.WinMM | 3.1.0 | P | [NuGet](https://www.nuget.org/packages/NAudio.WinMM/3.1.0) | MIT / M | No standalone file located; review upstream notices before bundling |
| Serilog | 4.4.0 | P | [NuGet](https://www.nuget.org/packages/Serilog/4.4.0) | Apache-2.0 / A | No standalone file located; review upstream notices before bundling |
| Serilog.Sinks.File | 7.0.0 | P | [NuGet](https://www.nuget.org/packages/Serilog.Sinks.File/7.0.0) | Apache-2.0 / A | No standalone file located; review upstream notices before bundling |
| SharpGen.Runtime | 2.4.2-beta | P | [NuGet](https://www.nuget.org/packages/SharpGen.Runtime/2.4.2-beta) | MIT / M | No standalone file located; review upstream notices before bundling |
| SharpGen.Runtime.COM | 2.4.2-beta | P | [NuGet](https://www.nuget.org/packages/SharpGen.Runtime.COM/2.4.2-beta) | MIT / M | No standalone file located; review upstream notices before bundling |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.12 | P | [NuGet](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/2.1.12) | Apache-2.0 / A | No standalone file located; review upstream notices before bundling |
| SQLitePCLRaw.core | 2.1.12 | P | [NuGet](https://www.nuget.org/packages/SQLitePCLRaw.core/2.1.12) | Apache-2.0 / A | No standalone file located; review upstream notices before bundling |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.12 | P | [NuGet](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3/2.1.12) | Apache-2.0 / A | No standalone file located; review upstream notices before bundling |
| SQLitePCLRaw.provider.e_sqlite3 | 2.1.12 | P | [NuGet](https://www.nuget.org/packages/SQLitePCLRaw.provider.e_sqlite3/2.1.12) | Apache-2.0 / A | No standalone file located; review upstream notices before bundling |
| System.Numerics.Tensors | 9.0.0 | P | [NuGet](https://www.nuget.org/packages/System.Numerics.Tensors/9.0.0) | MIT / M | LICENSE.TXT; THIRD-PARTY-NOTICES.TXT |
| Vortice.Direct3D11 | 3.8.3 | P | [NuGet](https://www.nuget.org/packages/Vortice.Direct3D11/3.8.3) | MIT / M | No standalone file located; review upstream notices before bundling |
| Vortice.DirectX | 3.8.3 | P | [NuGet](https://www.nuget.org/packages/Vortice.DirectX/3.8.3) | MIT / M | No standalone file located; review upstream notices before bundling |
| Vortice.DXGI | 3.8.3 | P | [NuGet](https://www.nuget.org/packages/Vortice.DXGI/3.8.3) | MIT / M | No standalone file located; review upstream notices before bundling |
| Vortice.Mathematics | 2.1.0 | P | [NuGet](https://www.nuget.org/packages/Vortice.Mathematics/2.1.0) | MIT / M | No standalone file located; review upstream notices before bundling |
| coverlet.collector | 6.0.4 | T | [NuGet](https://www.nuget.org/packages/coverlet.collector/6.0.4) | MIT / M | No standalone file located; review upstream notices before bundling |
| Microsoft.CodeCoverage | 17.14.1 | T | [NuGet](https://www.nuget.org/packages/Microsoft.CodeCoverage/17.14.1) | MIT / M | ThirdPartyNotices.txt; build/netstandard2.0/ThirdPartyNotices.txt |
| Microsoft.NET.Test.Sdk | 17.14.1 | T | [NuGet](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.14.1) | MIT / M | No standalone file located; review upstream notices before bundling |
| Microsoft.TestPlatform.ObjectModel | 17.14.1 | T | [NuGet](https://www.nuget.org/packages/Microsoft.TestPlatform.ObjectModel/17.14.1) | MIT / M | No standalone file located; review upstream notices before bundling |
| Microsoft.TestPlatform.TestHost | 17.14.1 | T | [NuGet](https://www.nuget.org/packages/Microsoft.TestPlatform.TestHost/17.14.1) | MIT / M | ThirdPartyNotices.txt |
| Newtonsoft.Json | 13.0.3 | T | [NuGet](https://www.nuget.org/packages/Newtonsoft.Json/13.0.3) | MIT / M | LICENSE.md |
| xunit | 2.9.3 | T | [NuGet](https://www.nuget.org/packages/xunit/2.9.3) | Apache-2.0 / A | No standalone file located; review upstream notices before bundling |
| xunit.abstractions | 2.0.3 | T | [NuGet](https://www.nuget.org/packages/xunit.abstractions/2.0.3) | Legacy URL / L | No standalone file located; review upstream notices before bundling |
| xunit.analyzers | 1.18.0 | T | [NuGet](https://www.nuget.org/packages/xunit.analyzers/1.18.0) | Apache-2.0 / A | No standalone file located; review upstream notices before bundling |
| xunit.assert | 2.9.3 | T | [NuGet](https://www.nuget.org/packages/xunit.assert/2.9.3) | Apache-2.0 / A | No standalone file located; review upstream notices before bundling |
| xunit.core | 2.9.3 | T | [NuGet](https://www.nuget.org/packages/xunit.core/2.9.3) | Apache-2.0 / A | No standalone file located; review upstream notices before bundling |
| xunit.extensibility.core | 2.9.3 | T | [NuGet](https://www.nuget.org/packages/xunit.extensibility.core/2.9.3) | Apache-2.0 / A | No standalone file located; review upstream notices before bundling |
| xunit.extensibility.execution | 2.9.3 | T | [NuGet](https://www.nuget.org/packages/xunit.extensibility.execution/2.9.3) | Apache-2.0 / A | No standalone file located; review upstream notices before bundling |
| xunit.runner.visualstudio | 3.1.4 | T | [NuGet](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.4) | Apache-2.0 / A | No standalone file located; review upstream notices before bundling |

## Non-NuGet components and assets

| Component/version | Origin and evidence | Source / binary permissions and notices | MIT source-scope compatibility / status |
|---|---|---|---|
| .NET runtime + Windows Desktop/WPF 10.0.12 | Restored Microsoft runtime packs; existing Licenses/PROVENANCE.json and copied upstream license/notices | Microsoft component license texts, including MIT and included third-party exceptions, govern any copied source/binary. Retain exact notices; do not assume all nested components share one license. | No runtime files in source scope; actual self-contained payload/notices must be reconciled before binary distribution. |
| Windows SDK/WinRT + COM/DirectX interop | Restored package metadata above and SDK references in project files | Upstream package/platform terms apply. Reference declarations do not redistribute Windows. Check exact candidate runtime closure. | Reference-only scope has no observed copyleft conflict; binary closure pending. |
| FFmpeg + FFprobe 9.0.2-full_build-www.gyan.dev | Local recorded tool hashes/build configuration; provider gyan.dev; FFmpeg upstream legal guidance | External processes, not included. --enable-gpl and --enable-version3; includes libx264, libx265 and many other libraries. GPL/version3-enabled full build, not LGPL-only. Corresponding source, build and all component notices required for any proposed redistribution. | No FFmpeg code/library/binary in source payload. PUBLICATION BLOCKER for bundling tools until obligations are resolved. Public original-source visibility is not satisfaction of tool obligations. |
| Native SQLite within SQLitePCLRaw.lib.e_sqlite3 2.1.12 | Package metadata Apache-2.0; upstream SQLite copyright policy | Wrapper/package license and native SQLite rights are separate. Upstream SQLite core is public domain; assess precise bundled build/extensions before binary approval. | No native binary in source scope. Binary provenance/notices pending; do not call the whole SQLitePCLRaw package public domain. |
| NAudio and NAudio.Asio 3.1.0 | Exact restored nuspecs declare MIT; upstream repository commit recorded | M profile applies to declared packages; separately check any native ASIO SDK/driver redistribution or copied SDK material. No such SDK/driver binary is included. | No observed incompatibility for references; do not infer SDK rights from NAudio's MIT expression. |
| Serilog 4.4.0 + File sink 7.0.0 | Exact restored nuspecs, Apache-2.0 | A profile; applicable upstream notices must accompany redistributed copies. | Reference-only original source need not be relicensed. |
| Current logo/icon and legacy logo/icon, unversioned | Four local asset hashes in candidate manifest; owner explicitly confirms ownership/publication rights | Owner confirms original asset rights and approves MIT publication; third-party and embedded font terms are separate. Do not invent a registered trademark. | Original assets covered by root MIT unless separately identified; no trademark rights implied. |
| Segoe UI regular/bold/italic, Windows-installed versions | Existing PDF renderer references system fonts; inspected OS/2 fsType=8 for all three | Editable document embedding permitted under flags and applicable Microsoft terms; no standalone TTF/OTF is included. Document embedding is not an application font redistribution grant. | PDF scope reviewed; no raw Windows fonts may be added to repository/package. |
| Images/media fixtures | Source tests generate fixtures; no MP4/MP3/WAV or personal projects in source allowlist; two reviewed real UI screenshots use idle workspace/synthetic media | No external media redistribution assumed. Four owner-approved original image/icon assets and two reviewed synthetic/idle QA screenshot copies only. | No recording content proposed. Do not add later assets without provenance. |
| Inno Setup 6.7.3 | Existing local official compiler/package evidence and license.txt | Inno's own license governs use/modified compiler redistribution; notices/origin restrictions apply. No compiler is included; only original setup scripts. | No copyleft source-model conflict identified for own scripts. Review build-tool entitlement/terms for the precise installer workflow; no installer authorized. |
| PDF build libraries (local only) | ReportLab 5.0.1, pypdf 6.20.1, PyMuPDF 1.28.2, Pillow 12.3.0 downloaded for M74 | Not application dependencies and not copied into the source allowlist; PyMuPDF has copyleft/commercial licensing considerations. No module source/binary or generator linking to it is proposed in the repository. Generated document is not a redistribution of the Python libraries. | Keep local tooling excluded; assess its licenses separately before redistributing tooling or a linked service. |

References: [FFmpeg legal guidance](https://www.ffmpeg.org/legal.html), [SQLite copyright](https://sqlite.org/copyright.html), [Microsoft font embedding](https://learn.microsoft.com/en-us/typography/fonts/font-faq), [Inno license](https://jrsoftware.org/files/is/license.txt).

## Milestone 78 compatibility conclusion

MIT is approved and applied to original CaptureFlow material. The reviewed source scope contains package references, not dependency DLLs, native libraries, runtime packs, vendor implementation source, media fixtures or FFmpeg tools. The 64 modern license declarations are MIT/Apache-2.0; the legacy test package points to upstream Apache-2.0 wording with imported-code exceptions. No declared NuGet copyleft component was identified. No source-scope license conflict was found in this reviewed payload. This is a bounded technical audit, not a legal compliance certificate for other distributions.

Third-party licenses/notices, native component provenance and patent/trademark conditions remain distinct. Required existing notice/license texts are preserved byte-for-byte. Do not relabel Apache dependencies or embedded Windows fonts as MIT.

The external GPL/version3-enabled FFmpeg build is not bundled or linked as an application library here. Corresponding-source/build/notices and all library obligations remain binary-redistribution blockers if bundling is proposed; publishing original CaptureFlow source does not discharge those requirements. Keep external tools excluded.

The owner confirmed original code and current/legacy branding publication rights. Two real M69.2 screenshots were reviewed, use idle/synthetic content and are labeled historical. Standalone Windows font files and local PDF build libraries/tooling are excluded. Editable document font embedding remains subject to Microsoft terms, not root MIT.

Source publication authorization and the scoped email-history approval supersede earlier holds. Installer/signing/public binary release, physical capture, final playback/synchronization/performance and clean-machine gates remain separate.

References: [MIT](https://opensource.org/license/mit), [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0), [FFmpeg legal guidance](https://www.ffmpeg.org/legal.html).
