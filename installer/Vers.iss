#define AppName "Vers"
#define AppVersion "1.5.0"
[Setup]
AppId={{LuneVers-Vers}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Rizwan
AppComments=Desktop lyrics, word by word
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
OutputDir=..\out
OutputBaseFilename=Vers-Setup
SetupIconFile=..\Vers\app.ico
UninstallDisplayIcon={app}\Vers.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
[Files]
Source: "..\publish\Vers\Vers.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Shared\Fonts\LICENSE.txt"; DestDir: "{app}"; DestName: "Inter-LICENSE.txt"; Flags: ignoreversion
[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Vers.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Vers.exe"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start {#AppName} when Windows starts"
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Vers"; ValueData: """{app}\Vers.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue
[Run]
Filename: "{app}\Vers.exe"; Parameters: "--background"; Flags: nowait; Check: WizardSilent
Filename: "{app}\Vers.exe"; Description: "Open {#AppName}"; Flags: nowait postinstall skipifsilent
[UninstallRun]
Filename: "taskkill"; Parameters: "/im Vers.exe /f"; Flags: runhidden; RunOnceId: "StopVers"
