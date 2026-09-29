#define MyAppName "AudioFlow"
#define MyAppVersion "0.2.21"
#define MyAppPublisher "AudioFlow"
#define MyAppExeName "AudioFlow.exe"

[Setup]
AppId={{A9F47B75-E39B-475F-93D7-9B1CFF835D61}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\AudioFlow
DefaultGroupName=AudioFlow
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\outputs
OutputBaseFilename=AudioFlow-Setup-{#MyAppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\AudioFlow"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\AudioFlow"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch AudioFlow"; Flags: nowait postinstall skipifsilent
