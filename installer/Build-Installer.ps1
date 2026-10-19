param(
    [Parameter(Mandatory=$true)][string]$Compiler,
    [string]$Version = '1.0.0',
    [string]$NumericVersion = '1.0.0.0',
    [switch]$PlanOnly
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(-[A-Za-z0-9.]+)?$' -or $NumericVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Invalid package version.' }
$root = Split-Path $PSScriptRoot -Parent
# Every approved invocation stages a new candidate. Historical preview artifacts are never replaced.
$candidate = Join-Path $root ('artifacts/installer/candidates/' + [Guid]::NewGuid().ToString('N'))
$publish = Join-Path $candidate 'publish'
$output = Join-Path $candidate 'output'
if (-not (Test-Path -LiteralPath $Compiler -PathType Leaf)) { throw 'Supply the verified Inno Setup ISCC.exe path.' }
if ($PlanOnly) {
    [pscustomobject]@{ Version=$Version; NumericVersion=$NumericVersion; PublishDirectory=$publish; OutputDirectory=$output; Compiler=$Compiler; InstallerScript=(Join-Path $PSScriptRoot 'CaptureFlow.iss'); BuildsInstaller=$false } | ConvertTo-Json
    return
}
& dotnet publish (Join-Path $root 'src/CaptureFlow.Desktop/CaptureFlow.Desktop.csproj') -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" "-p:FileVersion=$NumericVersion" "-p:AssemblyVersion=$NumericVersion" '-p:CaptureFlowPublisher=Janith Akalanka' -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($name in 'coreclr.dll','PresentationNative_cor3.dll','e_sqlite3.dll','WinRT.Runtime.dll','Microsoft.Windows.SDK.NET.dll','CaptureFlow.Desktop.exe','LICENSE','Licenses/DISTRIBUTION-PROVENANCE.json','THIRD-PARTY-NOTICES.md','Licenses/PROVENANCE.json') {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $name))) { throw "Publish dependency missing: $name" }
}
if (Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object { $_.Name -in @('ffmpeg.exe','ffprobe.exe') }) { throw 'Unverified external FFmpeg must not be bundled.' }
if (Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object { $_.Extension -in @('.mp4','.mp3','.wav','.cfedit','.db','.pdb','.pfx','.key') }) { throw 'User media or writable database found in publish; packaging stopped.' }
# Product-facing apphost name only; managed assembly and WPF pack URIs stay unchanged.
# This is not an execution-policy workaround. No candidate is automatically launched.
Move-Item -LiteralPath (Join-Path $publish 'CaptureFlow.Desktop.exe') -Destination (Join-Path $publish 'CaptureFlow.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PORTABLE-README.txt') -Destination $publish
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PREVIEW-NOTICE.txt') -Destination $publish
New-Item -ItemType Directory -Path $output | Out-Null
$zip = Join-Path $output "CaptureFlow-Portable-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
& $Compiler "/DPublishDir=$publish" "/DOutputDir=$output" "/DPackageVersion=$Version" "/DNumericVersion=$NumericVersion" (Join-Path $PSScriptRoot 'CaptureFlow.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$setup = Join-Path $output "CaptureFlow-Setup-$Version-win-x64.exe"
$hashes = @($setup,$zip) | ForEach-Object { $h=Get-FileHash -LiteralPath $_ -Algorithm SHA256; "$($h.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))" }
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), $hashes, [Text.UTF8Encoding]::new($false))
[pscustomobject]@{ CandidateDirectory=$candidate; PublishDirectory=$publish; OutputDirectory=$output; Installer=$setup; Portable=$zip; PublicReleaseApproved=$false } | ConvertTo-Json
