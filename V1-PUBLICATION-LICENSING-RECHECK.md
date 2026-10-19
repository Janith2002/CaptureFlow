# v1.0.0 publication licensing recheck

16 October 2026. **Licensing clearance remains incomplete. Release kept unpublished.**

## GitHub result

[Owner-visible v1.0.0 release draft](https://github.com/Janith2002/CaptureFlow/releases/tag/untagged-fad777be21e89a401c1e)

Release ID 414983908; intended tag v1.0.0; draft=true; prerelease=false; assets empty. The existing beta draft was preserved. No public stable release, binary upload, public download link, commit or push was performed. Final publication confirmation was not requested because the licensing prerequisite has not passed.

Professional notes are saved at `artifacts/qa/v1-publication-review/RELEASE-NOTES.md` and in the draft. They disclose unsigned packages, incomplete physical/playback/installation acceptance, blocked current regression, historical 893-test baseline and formal QA 20 PASS / 26 Not Run. The draft's target is only the published base; it must be updated to a committed, accepted source state before eventual publication because the packages include local M81/M82 changes.

## Files reverified without alteration

Directory: `artifacts/release/m74-public-docs/artifacts/installer/candidates/3e3b07f3f2874545bc67c4ec8b17e512/output/`.

| File | Bytes | SHA-256 |
|---|---:|---|
| CaptureFlow-Setup-1.0.0-win-x64.exe | 59,873,561 | `f96b961e6a5b3c2f631f0e332e58b95232cc68959b02ce18057e4ade43225d79` |
| CaptureFlow-Portable-1.0.0-win-x64.zip | 84,183,401 | `ffad2d6d394fa10dcd0e94cc12c13e0b2259791c5126d5e373c91bc1f5db0c81` |

Both hashes and sizes match Milestone 82. SHA256SUMS.txt contains those values. ZIP CRC passes; every archived file matches the staged publish payload; all recorded license-provenance hashes match. Package construction checks are preserved; they do not prove installation/startup or legal completeness. Both EXEs remain unsigned. Old beta packages were not used or uploaded.

## Exact unresolved licensing work

1. **SDK projection coverage:** establish the applicable public redistribution grant for Microsoft.Windows.SDK.NET.dll from Microsoft.Windows.SDK.NET.Ref 10.0.19041.57 and document the terms applicable to the accompanying WinRT.Runtime.dll. The package declares the Windows SDK license; neither CaptureFlow's MIT license nor the Windows App SDK's MIT license settles that projection's rights. Obtain authoritative Microsoft/package-owner clarification or a qualified license determination against the exact files. Do not infer a prohibition solely from uncertainty.
2. **Recipient/distributor conditions:** determine and implement how the applicable SDK protection/agreement requirements are satisfied for both installer and portable ZIP distribution. Current setup displays only CaptureFlow's MIT LICENSE; bundled SDK text alone does not establish that all acceptance obligations have been met. Confirm any relevant notice, platform, unchanged-binary and distributor requirements.
3. **Additional native WPF terms:** Microsoft's .NET-on-Windows guidance lists PresentationNative_cor3.dll, vcruntime140_cor3.dll and wpfgfx_cor3.dll under the separate .NET Library License, and D3DCompiler_47_cor3.dll under Windows SDK terms. These files are in the candidate. The copied runtime LICENSE files are generic MIT; no separate .NET Library License was located in the candidate notices. Match the relevant terms to the shipped 10.0.12 runtime, include any required license/notice text and document compliance. Existing 54-record package inventory/45 collected notices is useful evidence, not blanket clearance for nested native components.
4. **Freeze compliant artifacts:** if any payload or installer change is necessary, rebuild and hash new artifacts, updating acceptance/provenance. Do not silently modify the existing Milestone 82 files while claiming their hashes still apply. After clearance, attach only the approved matching artifacts and checksum file to the draft, update its source commit and prepare verified README links. Then request the owner's final explicit acceptance of the remaining accurately disclosed testing risks before public stable publication.

No contact with Microsoft or other third parties was sent on the owner's behalf. No security settings were changed, signing performed, or physical devices accessed. README remains unchanged while no public downloads exist.

## Primary references reviewed

- [Windows SDK license, especially distribution requirements](https://learn.microsoft.com/en-us/legal/windows-sdk/license)
- [Windows SDK redistribution list](https://learn.microsoft.com/en-us/legal/windows-sdk/redist)
- [Microsoft upstream explanation of SDK projection licensing](https://github.com/microsoft/WindowsAppSDK/discussions/4368)
- [.NET Windows native-file license mapping](https://github.com/dotnet/core/blob/main/license-information-windows.md)
- [.NET Library License](https://dotnet.microsoft.com/en-us/dotnet_library_license.htm)

Reference snapshots and hashes, draft request and response evidence are under `artifacts/qa/v1-publication-review/`. This is an evidence-based unresolved clearance record, not a legal conclusion that the software cannot be redistributed.
