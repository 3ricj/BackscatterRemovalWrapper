; -----------------------------------------------------------------------------
;  BsxtBatch - Inno Setup script
;  Per-user install (no administrator rights / no UAC prompt):
;      %LOCALAPPDATA%\Programs\BsxtBatch
;  Creates a Start Menu entry, an optional Desktop shortcut, and a proper
;  Programs-and-Features (Apps & features) uninstall entry.
;
;  Build:
;      ISCC.exe /DMyAppVersion=1.0.0 installer\BsxtBatch.iss
;  or simply:
;      powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
;
;  Requires the self-contained publish output at ..\publish\BsxtBatch\
;  (see build-installer.ps1, or run the dotnet publish command in README.md).
; -----------------------------------------------------------------------------

#define MyAppName      "BsxtBatch"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "Backscatter Removal"
#define MyAppURL       "https://github.com/3ricj/BackscatterRemovalWrapper"
#define MyAppExeName   "BsxtBatch.App.exe"
#define PublishDir     "..\publish\BsxtBatch\"
#define OutputDir      "Output"

[Setup]
; NOTE: AppId uniquely identifies this application for upgrades/uninstall.
; Never change it between releases of the same app (the {{ escapes a literal brace).
AppId={{3D2A48FF-DC3E-4B9E-8111-AE056E8D63F0}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no

; Per-user, no elevation required.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline

OutputDir={#OutputDir}
OutputBaseFilename=BsxtBatch-Setup-{#MyAppVersion}
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern

; Custom icon for the setup executable itself (the app exe carries the same icon,
; so shortcuts and the uninstall entry pick it up automatically).
SetupIconFile=..\src\BsxtBatch.App\backscatter-fish.ico

; Close a running instance politely instead of failing the install/uninstall.
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The self-contained single-file app (no .NET runtime needed on the target machine).
Source: "{#PublishDir}{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}*.dll";           DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\README.md";                 DestDir: "{app}"; DestName: "README.txt"; Flags: ignoreversion isreadme

[Icons]
Name: "{group}\{#MyAppName}";           Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";     Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
