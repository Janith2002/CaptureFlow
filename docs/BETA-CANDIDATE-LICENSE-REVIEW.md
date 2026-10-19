# Beta candidate distribution notice review

Milestone 81, 16 October 2026. Scope: the private Windows x64 candidate based on source commit `98952fee0e578c63d03053f91363bd9e426e53e1` plus local packaging-only repairs.

The candidate dependency manifest contains 54 production package/runtime records. `Licenses/DISTRIBUTION-PACKAGES.json` maps their exact versions, package-declared copyright/license metadata and notice families. `Licenses/DISTRIBUTION-PROVENANCE.json` records the sources and SHA-256 values of 45 preserved license/notice files. No upstream implementation was modified or relicensed.

The .NET and WPF runtime packs are 10.0.12. Their supplied license texts and full .NET third-party notices are retained. Supplied package notices are also copied, including CommunityToolkit and System.Numerics.Tensors. Immutable source-commit licenses cover NAudio, Serilog, Serilog.Sinks.File, SharpGen, Vortice.Windows and Vortice.Mathematics. SQLitePCLRaw v2.1.12 resolves to `ca835d21508bff43121c65081035840ac5006c4c`; its Apache-2.0 license and complete NOTICE are preserved, including native SQLite attribution. Native SQLite's public-domain statement is preserved separately from its upstream site. Package metadata retains individual copyright holders rather than substituting CaptureFlow's copyright.

These MIT and Apache notices apply to their respective components. CaptureFlow's own root LICENSE is included without replacing third-party terms. FFmpeg and FFprobe are excluded from both packages; their binary redistribution is not approved. The original branding remains covered by the owner's previously supplied publication-rights confirmation.

## Specific unresolved public-distribution gate

`Microsoft.Windows.SDK.NET.Ref/10.0.19041.57` supplies the Windows runtime projection files and declares `https://aka.ms/WinSDKLicenseURL`, not MIT. The resolved Microsoft SDK terms are preserved as `Licenses/Distribution/Windows-SDK-terms.rtf`. The linked redistribution list was examined; this review has not established an explicit applicable redistribution grant for every runtime projection file or completed the associated end-user-term requirements. This record must not be marked cleared merely because other Microsoft components use MIT.

Before public binary distribution, obtain authoritative Microsoft redistribution evidence for the actual `Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll` supplied by that exact package, and satisfy its conditions (or establish a supported, licensed alternative). Do not remove required runtime files merely to make a checklist pass. Internal development/test candidates are retained privately; no broad binary compliance certification is claimed.

Authoritative references: [declared SDK terms](https://aka.ms/WinSDKLicenseURL), [SDK redistribution list](https://learn.microsoft.com/en-us/legal/windows-sdk/redist), [SQLite copyright](https://www.sqlite.org/copyright.html). Exact upstream license URLs and file hashes are in the provenance manifest.
