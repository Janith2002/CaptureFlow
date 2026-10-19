# Milestone 83 — Licensing and redistribution clearance

16 October 2026 · CaptureFlow 1.0.0 · Windows 11 x64

## Final decision: BLOCKED

Public redistribution is **not cleared**. Substantial source, license and notice evidence is now verified, and the missing notice texts have been packaged. Remaining blockers are the Windows SDK projection grant/scope, demonstrated recipient/distributor compliance with Microsoft's conditional terms, and independent inspection of the compressed installer payload. These are not assertions that self-contained WPF is prohibited or that every Microsoft DLL needs bespoke permission.

The existing v1.0.0 GitHub draft was preserved unchanged: [owner-visible draft](https://github.com/Janith2002/CaptureFlow/releases/tag/untagged-fad777be21e89a401c1e), ID 414983908, draft=true, prerelease=false, assets empty. No upload, publication, tag creation, commit or push occurred. Physical tests, application regression execution and installation acceptance were not performed or promoted to PASS.

## Scope and package inspection

Read `V1-PUBLICATION-LICENSING-RECHECK.md` and `MILESTONE-82-OFFICIAL-V1-RELEASE.md`. Reverified the exact M82 packages and preserved their bytes/hashes. Inspected every portable DLL/EXE/native file by SHA-256, version resource, PE architecture/CLR header and byte-identical source matching; no application assembly was loaded or executed.

- **511 payload binaries**, including **22 native files**, traced to exact restored package bytes or original CaptureFlow/apphost build provenance. No unmapped binary remained.
- **54 production package/runtime archives** had successful `dotnet nuget verify --all` results. This verifies package signatures, not application behavior or license permission merely because NuGet hosts a package.
- All portable binary bytes matched the staged publish files.
- Both original and rebuilt installer compiler logs list every corresponding binary input. The installer wrapper's product metadata and unsigned status were inspected.
- **Important limit:** the compressed Inno Setup 6.7.3 installer was not independently unpacked. No compatible approved extractor was available; the current upstream innoextract README only claims support through 6.3.3. No random fork was downloaded or installer executed against the user's existing installation. Compiler-input correlation is not represented as a full extraction audit, and the generated uninstaller was not separately extracted. Complete that check with a suitable trusted extract-only tool or clean isolated install before claiming inspection of every embedded installer binary.

Version and package signatures alone do not establish permission. Matrix rows use explicit package license declarations plus retained upstream grant/NOTICE texts, immutable source commits and Microsoft's separate native-file mapping.

## Compliance matrices

- [Combined redistribution matrix](artifacts/qa/m83/redistribution-compliance-matrix.csv): **566 rows**, comprising 54 package/runtime groups, 511 payload binaries and one installer-engine/support row. These are different inventory levels, not 566 distinct binaries.
- [Per-binary matrix](artifacts/qa/m83/binary-compliance-matrix.csv): exact file version, package version/source path, license, permission, notices, official reference, status, SHA-256, PE kind/architecture and comparison with M82.
- [Per-package matrix](artifacts/qa/m83/package-compliance-matrix.csv): all 54 exact package/runtime versions and notice requirements.
- JSON counterparts retain structured details. `binary-versions.json`, `inventory-summary.json`, `final-inventory-verification.json` and `nuget-signature-verification.json` hold supporting evidence.

Per-binary result: **504 PASS / 7 UNRESOLVED**. PASS is scoped to that component's evidenced permission/notice mapping; it does not clear the whole release or the compressed installer. Per-package result: **51 PASS / 3 UNRESOLVED** (the two runtime packs have native exceptions, and the SDK projection package has mixed terms). The installer-engine row is UNRESOLVED pending independent inspection, despite an explicit vendor redistribution grant. None of these counts changes formal application QA.

## Exact SDK and Microsoft native components

| Included component | Exact version/source | License and permission | Status |
|---|---|---|---|
| Microsoft.Windows.SDK.NET.dll | File **10.0.19041.55**, product source `7e1ba99b2f0c1ea5311dcb3acfe20a75335efd6e`; `Microsoft.Windows.SDK.NET.Ref/10.0.19041.57/lib/net8.0/Microsoft.Windows.SDK.NET.dll` | Separate Windows SDK terms; specific projection grant/scope and recipient conditions not established | UNRESOLVED |
| WinRT.Runtime.dll | **2.2.0.48161**, source `8649ee3eeb2445ca2a36d80d878ef60b96a6c65d`; same SDK package, `lib/net8.0/WinRT.Runtime.dll` | Exact CsWinRT source commit supplies MIT; copyright and full license now retained | PASS |
| coreclr.dll | Runtime package **10.0.12**, file `10,0,1226,42308`, source `95017c711e6afc1085133d440e42b4bd78155701` | Microsoft's separate native-file mapping lists CoreCLR/.NET single-file runtime code under .NET Library terms; use the conditional terms rather than assuming package-level MIT is exhaustive | UNRESOLVED conditions |
| Microsoft.DiaSymReader.Native.amd64.dll | Exact file version and source match recorded in binary matrix; runtime 10.0.12 | .NET Library License | UNRESOLVED conditions |
| PresentationNative_cor3.dll | WindowsDesktop runtime **10.0.12**, exact file version/hash in matrix | .NET Library License | UNRESOLVED conditions |
| vcruntime140_cor3.dll | WindowsDesktop runtime **10.0.12**, exact file version/hash in matrix | .NET Library License; Microsoft identifies it as the corresponding VC runtime relicensed under these terms | UNRESOLVED conditions |
| wpfgfx_cor3.dll | WindowsDesktop runtime **10.0.12**, exact file version/hash in matrix | .NET Library License | UNRESOLVED conditions |
| D3DCompiler_47_cor3.dll | WindowsDesktop runtime **10.0.12**, exact file version/hash in matrix | Microsoft maps it to Windows SDK terms; conditions not demonstrated | UNRESOLVED conditions |

The package version `.57` must not be confused with the shipped projection DLL's `.55` file version. Only the two SDK-package runtime DLLs above are shipped from that targeting pack; its reference assemblies, build tools and Windows metadata are not added to the runtime payload.

### What is established about self-contained WPF

The [.NET Library License, section 3(a)](https://dotnet.microsoft.com/en-us/dotnet_library_license.htm) **expressly grants object-code redistribution as part of an application**, including distribution through application distributors. Its conditions include application use rather than standalone library distribution, recipient/distributor protective agreements, and indemnification with an express exception for claims based solely on unmodified Distributable Code. Its restrictions and data-related provisions also remain applicable; no general app-telemetry conclusion is inferred here.

Thus self-contained WPF is **conditionally permitted**, not inherently forbidden. The unresolved issue is demonstrating that the release satisfies the applicable conditions. Copying a license file does not by itself establish a recipient agreement. The current installer displays CaptureFlow's MIT LICENSE; no additional protective agreement process has been established for either installer or ZIP. We did not invent or impose a new end-user contract, accept indemnities for the owner, or restrict MIT rights on original CaptureFlow code.

The [Windows SDK license, section 2(a)](https://learn.microsoft.com/en-us/legal/windows-sdk/license) grants distribution of specified Distributable Code subject to its requirements, including significant application functionality, applicable unchanged-code, recipient/distributor, notice, platform and indemnification conditions. The [official REDIST list](https://learn.microsoft.com/en-us/legal/windows-sdk/redist) and package terms have not supplied conclusive projection-specific coverage established by this audit. Microsoft's [upstream answer](https://github.com/microsoft/WindowsAppSDK/discussions/4368) distinguishes that projection's licensing from the Windows App SDK's MIT license. Generic MIT availability elsewhere does not close this issue.

## Other dependencies and notices

The complete matrix covers actual shipped .NET/WPF runtime files, Microsoft.Extensions/Data.Sqlite packages, CommunityToolkit, System.Numerics.Tensors, NAudio, Serilog, SharpGen, SQLitePCLRaw, Vortice and their runtime/native contents. MIT and Apache-2.0 grants were checked against declared versions and preserved exact-source license texts/copyright metadata, not inferred merely from download availability. SQLitePCLRaw v2.1.12's complete Apache NOTICE and native SQLite public-domain attribution are retained. Inno Setup's [vendor license](https://jrsoftware.org/files/is/license.txt) expressly permits redistribution subject to retaining attribution and other conditions; its original license is included. Independent installer extraction remains a separate verification limit.

Created [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt), UTF-8, with component versions/copyrights and complete distinct upstream textual notices; byte-identical original HTML/RTF and repeated package notices remain under `Licenses/Distribution/`. Existing Markdown notices and original MIT LICENSE were preserved. Added:

- Exact-commit CsWinRT MIT license.
- Full .NET Library License HTML plus identified plain-text extraction.
- Immutable Microsoft native-license mapping at commit `6c25b9da384e8d8a16fd12c79da8a8aa6bdfc6a1`.
- Readable Windows SDK terms alongside the original RTF.
- Inno Setup vendor license.

`Licenses/DISTRIBUTION-PROVENANCE.json` now records **51 preserved license/notice files** and hashes. The desktop project copies the new TXT into distributions. No runtime functionality was removed or modified; the content-item change recompiled CaptureFlow.Desktop.dll, which matches the new publish build output. All third-party binaries remain byte-identical to M82. FFmpeg/FFprobe are not bundled; their redistribution is outside this package and is not claimed cleared.

## Rebuilt private artifacts

Original M82 packages remain unchanged. New packages were built in a new directory, with new hashes rather than silently altering recorded artifacts:

`artifacts/release/m74-public-docs/artifacts/installer/candidates/65103d004be84a4d91a169e6318b4d89/output/`

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| CaptureFlow-Setup-1.0.0-win-x64.exe | 59,912,692 | `b1e0af97b5075d80cba0e664fd1d420f94ddf266bf38a649f5b818f204079e1c` |
| CaptureFlow-Portable-1.0.0-win-x64.zip | 84,258,335 | `4740267efe5e6f7e4c72113e9e483dc83833f9a07e2a7e52cd7ec8596c79fe96` |

SHA256SUMS.txt is beside them. Publish and installer compilation succeeded; ZIP CRC, full extracted-file equality, embedded genuine icons, runtime files and all 51 notice-file provenance hashes passed. Both EXEs remain unsigned. Scoped Gitleaks scans found no leaks. No application test suite or human acceptance was run. Build and verification results are under `artifacts/qa/m83/`; local previous-file backups are under `root-backups/`.

## Exact remaining actions

1. **Resolve SDK projection scope:** obtain applicable authoritative Microsoft/package-owner confirmation or a qualified license determination for the shipped Microsoft.Windows.SDK.NET.dll from package 10.0.19041.57. Cite the actual grant and satisfy its conditions; do not seek a blanket waiver for MIT or already conditionally licensed runtime files.
2. **Establish Microsoft condition compliance:** have the owner/qualified reviewer approve how the .NET Library/SDK recipient and distributor requirements, notices, restrictions and applicable indemnification obligations are met. Implement the approved third-party-only terms flow for both installer and portable distribution while preserving CaptureFlow's original-code MIT rights. Rebuild and rehash if that changes packaging. Merely clicking approval of untested functionality does not clear third-party licensing.
3. **Finish installer content inspection:** use a trusted compatible Inno 6.7.3 extractor or a clean isolated installation to compare installed binaries and generated uninstall support against the matrix, checking vendor notices. Do not install over existing user files or disable execution controls. This check remains incomplete and must not be inferred from ZIP equality.
4. Reverify the final package/notice matrix and accepted source state, then separately satisfy or explicitly address the still-open release acceptance requirements. The draft stays unpublished; final risk confirmation required by the previous publication request has not been requested because licensing is not cleared.

### Alternatives if the current terms cannot be satisfied

No component has been conclusively determined nonredistributable; unresolved coverage is not proof of prohibition. A framework-dependent deployment could avoid bundling .NET/WPF native runtime files by requiring users to obtain Microsoft's supported Desktop Runtime directly. It would preserve application features but change the approved self-contained deployment/prerequisite experience and would **not** independently solve the SDK projection issue. A supported generated/embedded WinRT projection could be investigated for that issue, but requires its own metadata/license and functionality validation; it is not automatically cleared. Neither alternative was applied, and required capture functionality/DLLs were not removed to manufacture compliance.

## Evidence boundaries

No original recordings/projects, branding, README or existing user guide were changed. No security settings, physical devices or application session were touched. The owner's earlier physical-test deferral remains in effect. Historical 893 automated passes and formal QA 20 PASS / 26 Not Run were not inflated. The seven unresolved component rows and installer inspection limit prevent an honest **CLEARED FOR REDISTRIBUTION** decision today.
