#define AppName "Lune"
#define AppVersion "1.5.0"
[Setup]
AppId={{LuneVers-Lune}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Rizwan
AppComments=Screen-edge light that follows your music
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
OutputDir=..\out
OutputBaseFilename=Lune-Setup
SetupIconFile=..\Lune\app.ico
UninstallDisplayIcon={app}\Lune.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
[Files]
Source: "..\publish\Lune\Lune.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Shared\Fonts\LICENSE.txt"; DestDir: "{app}"; DestName: "Inter-LICENSE.txt"; Flags: ignoreversion
[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Lune.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Lune.exe"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "startup"; Description: "Start {#AppName} when Windows starts"
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Lune"; ValueData: """{app}\Lune.exe"" --background"; Tasks: startup; Flags: uninsdeletevalue
[Run]
Filename: "{app}\Lune.exe"; Parameters: "--background"; Flags: nowait; Check: WizardSilent
Filename: "{app}\Lune.exe"; Description: "Open {#AppName}"; Flags: nowait postinstall skipifsilent
[UninstallRun]
Filename: "taskkill"; Parameters: "/im Lune.exe /f"; Flags: runhidden; RunOnceId: "StopLune"
