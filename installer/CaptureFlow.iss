; Private pre-release package. Never bundle external FFmpeg or user data.
#ifndef PublishDir
  #define PublishDir "..\artifacts\installer\publish"
#endif
#ifndef PackageVersion
  #define PackageVersion "1.0.0"
#endif
#ifndef NumericVersion
  #define NumericVersion "1.0.0.0"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer\output"
#endif
#ifndef ProductId
  #define ProductId "CaptureFlow.C366019B-796E-4AF7-A026-CC39C7AA74CC"
#endif
#ifndef ProductName
  #define ProductName "CaptureFlow"
#endif

[Setup]
AppId={#ProductId}
AppName={#ProductName}
AppVersion={#PackageVersion}
AppVerName={#ProductName} {#PackageVersion}
VersionInfoVersion={#NumericVersion}
VersionInfoProductName=CaptureFlow
VersionInfoDescription=CaptureFlow Windows Setup
AppPublisher=Janith Akalanka
VersionInfoCompany=Janith Akalanka
DefaultDirName={localappdata}\Programs\{#ProductName}
DefaultGroupName={#ProductName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=CaptureFlow-Setup-{#PackageVersion}-win-x64
SetupIconFile=..\assets\branding\CaptureFlow-New-Icon.ico
UninstallDisplayIcon={app}\CaptureFlow.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupMutex=CaptureFlow.Setup.{#ProductId}
CloseApplications=no
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
DisableProgramGroupPage=yes
InfoBeforeFile=PREVIEW-NOTICE.txt
LicenseFile=..\LICENSE
DisableWelcomePage=no
DisableFinishedPage=no
Uninstallable=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "ffmpeg.exe,ffprobe.exe,*.pdb,*.mp4,*.mp3,*.wav,*.cfedit,*.db,*.db-wal,*.db-shm"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "PREVIEW-NOTICE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#ProductName}\{#ProductName}"; Filename: "{app}\CaptureFlow.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\{#ProductName}"; Filename: "{app}\CaptureFlow.exe"; WorkingDir: "{app}"; Tasks: desktopicon

; No automatic launch, forced app shutdown, user-data deletion, or external downloads.
; Inno's tracked-file uninstaller removes installed binaries/shortcuts only.
