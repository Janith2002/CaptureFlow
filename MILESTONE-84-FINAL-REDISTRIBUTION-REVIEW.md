# Milestone 84 — Final redistribution review

16 October 2026 · CaptureFlow 1.0.0 · Windows 11 x64

## Final decision: BLOCKED

**Independent installer-content verification is now resolved. Two licensing questions remain unresolved:** the Windows SDK projection grant's exact scope, and compliance with Microsoft's recipient/distributor agreement conditions. No binaries were published, no application behavior was changed and no new package rebuild was necessary. The existing v1.0.0 draft remains unpublished and asset-free.

Read the full M83 report and compliance matrix and reviewed the consolidated THIRD-PARTY-NOTICES.txt and its component/license evidence. M83's historical evidence remains intact; [the updated M84 matrix](artifacts/qa/m84/redistribution-compliance-matrix.csv) records the newly completed installer-content verification.

## Exact SDK artifacts

Both files are byte-identical to the runtime files in **Microsoft.Windows.SDK.NET.Ref 10.0.19041.57**, under `lib/net8.0/`; no SDK reference assemblies, tools or Windows metadata were added to the application payload.

| File | Actual file/product source version | Applicable evidence | Result |
|---|---|---|---|
| Microsoft.Windows.SDK.NET.dll | **10.0.19041.55** / `7e1ba99b2f0c1ea5311dcb3acfe20a75335efd6e` | Exact package license URL resolves to Windows SDK terms; projection-specific distributable scope remains unestablished | UNRESOLVED |
| WinRT.Runtime.dll | **2.2.0.48161** / `8649ee3eeb2445ca2a36d80d878ef60b96a6c65d` | [MIT license at the actual CsWinRT source commit](https://raw.githubusercontent.com/microsoft/CsWinRT/8649ee3eeb2445ca2a36d80d878ef60b96a6c65d/LICENSE); full notice retained | PASS, license/notice scope |

The package version `.57` is distinct from projection file version `.55`. The extracted DLLs have valid Microsoft Authenticode signatures, but those signatures establish provenance/integrity rather than redistribution permission. NuGet availability, restoring and publishing the package are not treated as grants. File hashes and package-source matches are retained in M83's per-binary matrix and confirmed by independent extraction.

## Official terms and exact sections

| Official source | Sections | Release requirement / assessment |
|---|---|---|
| [Windows SDK license](https://learn.microsoft.com/en-us/legal/windows-sdk/license) | **1(a)** | Development/testing rights; do not substitute these for distribution rights. |
| Same | **2(a)(i)** and [REDIST list](https://learn.microsoft.com/en-us/legal/windows-sdk/redist) | Distribution grant refers to specified Distributable Code. This audit has not established explicit scope for the exact SDK projection DLL. |
| Same | **2(a)(ii)** | Application functionality; applicable unchanged-code handling; protective recipient/distributor agreements; copyright and indemnification requirements. Agreement compliance is not demonstrated by the current MIT-only setup screen or ZIP notice presence. |
| Same | **2(a)(iii)** | Preserve attribution, avoid misleading Microsoft branding, respect platform and excluded-license restrictions. Original CaptureFlow MIT rights do not relicense Microsoft binaries. |
| [.NET Library License](https://dotnet.microsoft.com/en-us/dotnet_library_license.htm) | **3(a)(i)** | Expressly permits object-code distribution with applications and through their distributors. Self-contained WPF is not inherently prohibited. |
| Same | **3(a)(ii)–(iii)** | Non-standalone use, protective recipient/distributor terms, indemnification with the stated unmodified-code exception, branding/source-license restrictions. |
| Same | **2; 4; 5; 6** | Preserve applicable third-party terms/notices; assess data provisions where applicable; respect scope/notice and export restrictions. No inference of app telemetry is made here. |
| [Microsoft Windows native-file mapping](https://raw.githubusercontent.com/dotnet/core/6c25b9da384e8d8a16fd12c79da8a8aa6bdfc6a1/license-information-windows.md) | Named-file lists | Distinguishes MIT runtime portions from the separate .NET Library and SDK portions. Package-level MIT is not exhaustive. |

The mapped .NET Library files present include coreclr.dll, Microsoft.DiaSymReader.Native.amd64.dll, PresentationNative_cor3.dll, vcruntime140_cor3.dll and wpfgfx_cor3.dll. D3DCompiler_47_cor3.dll is mapped to Windows SDK terms. Their exact runtime source is the **10.0.12** package set. Native-file conditional grants are distinguished from missing proof of satisfying the conditions; the report does not claim that these files are categorically nonredistributable.

### Remaining legal questions and actions

1. Establish the applicable grant/scope for Microsoft.Windows.SDK.NET.dll from the exact package, using authoritative Microsoft/package-owner evidence or a qualified licensing determination. A separate runtime's MIT grant does not establish this projection's grant.
2. Establish an approved, effective recipient/distributor terms model for both setup and ZIP. The owner/qualified reviewer must address the Microsoft conditions and applicable obligations while preserving MIT rights for CaptureFlow original material. Merely bundling text or accepting untested-product risk does not establish that these separate requirements are satisfied.

An actionable [clarification request](artifacts/qa/m84/MICROSOFT-CLARIFICATION-REQUEST.md) is prepared with the exact artifact/version/hash and section questions. It was **not sent** to anyone. No new agreement was invented or accepted on the owner's behalf. The grant remains UNRESOLVED rather than being inferred from conflicting/general guidance.

## Independent installer extraction — PASS

Input: the **M83 rebuilt installer**, not the older M82 or beta installer. Setup was never executed and no installation, registry change or application launch occurred.

The actual archive identifies itself as **Inno Setup Setup Data (6.7.0)**. A reviewed extraction reference was insufficient on its own: it skipped CRC verification, assumed LZMA1 payloads, used fixed/heuristic record handling and had an incompatible CALL/JMP decoder. It was not used to claim successful extraction.

Implemented a local extract-only verifier using Python standard-library decompression, checked against **Inno Setup is-6_7_3**, source commit `4adf37ed7f3fd2bd11c6836ba056e3de170fbabf`. Relevant source: Shared.Struct.pas, Shared.SetupEntFunc.pas, Compression.Base.pas, Compression.LZMADecompressor.pas, Setup.FileExtractor.pas and SetupLdr.dpr. The verifier validates bounds/path confinement, the loader table CRC, encryption-header CRC, compressed header block CRCs, exact record counts, LZMA2 chunk structure, the official 64 KiB CALL/JMP transform and each embedded SHA-256. The setup engine is recovered from its separate compressed stream and checked against its embedded CRC.

Results:

- **582 file-table records:** 581 normal-file entries and one generated-uninstaller control record.
- **581 embedded SHA-256 checks passed**; **316 compressed-header/engine sub-block CRC checks passed**. Loader/encryption-header checks also passed.
- **580 unique application files**, all recovered independently from installer bytes and compared with the portable ZIP. **Zero missing, extra or differing application files.**
- The duplicate PREVIEW-NOTICE.txt destination contains identical bytes; it comes from the recursive payload plus the explicit installer notice entry. It is not a second differing file.
- All **51 notice-provenance hashes** match. Required .NET/WPF runtime, e_sqlite3.dll, WinRT.Runtime.dll, SDK projection, root LICENSE, consolidated notices and portable instructions are present.
- All **511 application/native binaries** therefore match the accepted portable payload, including the recompiled original CaptureFlow.Desktop.dll from M83.

The extractor and comparison scripts are private audit tools, not application dependencies or release assets. Their hashes, pinned source references and downloaded reference review provenance are recorded under `artifacts/qa/m84/`. No unsupported native extractor binary or security-policy override was used.

### Installer-specific files and metadata

| Independently recovered support file | Bytes | SHA-256 | Explanation |
|---|---:|---|---|
| setup-engine.exe | 4,406,784 | `c539fe11002c74804ac1b6f447506605751b8f950d36c1d5b322d2f70c2a6376` | Inno setup engine and uninstaller template; embedded CRC verified. |
| resource-HELPER_EXE_AMD64.exe | 6,144 | `388a796580234efc95f3b1c70ad4cb44bfddc7ba0f9203bf4902b9929b136f95` | Extracted from engine RCDATA; byte-identical helper also exists in the original compiler Setup.e32. Official Setup.HelperEXEs.rc identifies this resource. |

These are expected installer support, not extra application code. The final extraction inventory has **582 physical files: 580 application files plus these two support files**. Engine resources are separately inventoried; embedded icons/control metadata are not miscounted as application files.

CaptureFlow.exe has file version **1.0.0.0**, product version **1.0.0+98952fee0e578c63d03053f91363bd9e426e53e1**, and publisher metadata Janith Akalanka. The engine product is 1.0.0; its internal file version **51.1054.0.0** and blank copyright placeholder match the [official Setup.version.rc](https://github.com/jrsoftware/issrc/blob/4adf37ed7f3fd2bd11c6836ba056e3de170fbabf/Projects/Res/Setup.version.rc). They are not unexplained application-version drift. The helper has no version resource. The application, setup engine/helper and outer setup are unsigned; no verified-publisher claim is made.

The installer generates installation-specific uninstall mode/messages/log data at installation time. The actual installed uninsNNN.exe was not generated or tested. Extraction verifies the embedded template/support and application payload, **not successful installation or uninstall behavior**. Clean-machine acceptance remains separate and NOT RUN.

## Evidence files

- [Extracted-file inventory](artifacts/qa/m84/extracted-file-inventory.csv), with every recovered path, byte count and SHA-256.
- [Application payload comparison](artifacts/qa/m84/payload-comparison.csv).
- `extraction-results.json`, `comparison-summary.json`, `engine-resource-inventory.json`, `extracted-executable-metadata.json`.
- `extract_verified.py`, `compare_payload.py`, source snapshots, `evidence-provenance.json`, and `independent-extraction/`.
- Updated compliance matrix resolves the installer-content row while retaining the unresolved Microsoft terms. M83 matrices and original reports remain historical evidence.

## Final unchanged package hashes

No rebuild was needed: extraction revealed no payload discrepancy, and no licensing/behavior change was applied. M83's notice-corrected files remain the current private artifacts at:

`artifacts/release/m74-public-docs/artifacts/installer/candidates/65103d004be84a4d91a169e6318b4d89/output/`

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| CaptureFlow-Setup-1.0.0-win-x64.exe | 59,912,692 | `b1e0af97b5075d80cba0e664fd1d420f94ddf266bf38a649f5b818f204079e1c` |
| CaptureFlow-Portable-1.0.0-win-x64.zip | 84,258,335 | `4740267efe5e6f7e4c72113e9e483dc83833f9a07e2a7e52cd7ec8596c79fe96` |

SHA256SUMS.txt matches. Original M82/beta artifacts were preserved and were not substituted.

## Remediation alternatives and release state

No component has been conclusively found prohibited; ambiguity is not treated as illegality. If the conditional runtime terms cannot be satisfied, framework-dependent deployment could omit bundled .NET/WPF runtime files and require users to obtain Microsoft's Desktop Runtime directly. That changes the approved self-contained prerequisite experience and does not independently resolve the projection. Supported generated/embedded projections require their own rights and functionality review. Neither alternative was applied; no required functionality or binary was removed to manufacture compliance.

The existing draft ID **414983908** remains draft=true, prerelease=false, assets empty. README, guide, source behavior, user data and security settings are unchanged. Physical tests remain deferred, historical 893 automated passes and formal QA 20 PASS / 26 Not Run remain unchanged. No new test or production-verification claim is made.

Because licensing remains unresolved, physical acceptance and final public-release approval were not represented as the next sufficient gate. Resolve the two legal questions first; any approved terms/packaging changes must be rebuilt and rehashed, then reverified. Only after licensing is supported should acceptance and the owner's final publication approval proceed.
