# CaptureFlow recovery

Clone the source on Windows with Git and the .NET 10 SDK installed:

```powershell
git clone https://github.com/Janith2002/CaptureFlow.git
cd CaptureFlow
dotnet restore CaptureFlow.slnx
dotnet build CaptureFlow.slnx -c Release
dotnet test CaptureFlow.slnx -c Release --no-build
```

To create a local application publish folder:

```powershell
dotnet publish src/CaptureFlow.Desktop/CaptureFlow.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false -o artifacts/recovery-publish
```

Installer creation additionally requires a verified Inno Setup compiler; see `installer/Build-Installer.ps1`. Configure external FFmpeg separately as described in the user documentation; it is not bundled.

Rebuilding restores functionality from source; it does not promise byte-identical historical packages. Preserve the original v1.0.0 packages and compare their SHA-256 values with `SHA256SUMS-1.0.0.txt`.

Binary redistribution remains blocked by the unresolved Microsoft Windows SDK projection scope and recipient/distributor terms recorded in Milestones 83 and 84. Creating local packages does not clear publication.

Private QA inventories, extraction tools, recordings, historical reports and build outputs are deliberately outside the public source backup. Retain a separately verified private backup of these materials. Links in the milestone reports to private `artifacts/qa` evidence require that backup; these files are not hosted in this repository.
