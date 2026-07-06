; FreeVoice Studio installer — per-user, no admin. Ships the native app + Python backend.
; Python 3.12 + `pip install -r requirements.txt` are the user's one-time prerequisite.
[Setup]
AppName=FreeVoice Studio
AppVersion=1.0.0
AppPublisher=Baron
AppPublisherURL=https://github.com/The-Berin/FreeVoice
DefaultDirName={autopf}\FreeVoice Studio
DefaultGroupName=FreeVoice Studio
UninstallDisplayIcon={app}\FreeVoice Studio.exe
OutputDir=..\dist
OutputBaseFilename=FreeVoice-Studio-Setup-1.0.0
SetupIconFile=..\src\FreeVoiceStudio\Assets\app.ico
Compression=lzma2/max
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
CloseApplications=yes
PrivilegesRequired=lowest
WizardStyle=modern
DisableProgramGroupPage=yes

[Files]
Source: "..\publish\FreeVoice Studio.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs
Source: "..\server.py"; DestDir: "{app}\backend"; Flags: ignoreversion
Source: "..\core.py"; DestDir: "{app}\backend"; Flags: ignoreversion
Source: "..\freevoice.py"; DestDir: "{app}\backend"; Flags: ignoreversion
Source: "..\requirements.txt"; DestDir: "{app}\backend"; Flags: ignoreversion

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"
Name: startmenu; Description: "Pin to Start Menu"; Flags: unchecked

[Icons]
Name: "{group}\FreeVoice Studio"; Filename: "{app}\FreeVoice Studio.exe"
Name: "{autodesktop}\FreeVoice Studio"; Filename: "{app}\FreeVoice Studio.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\FreeVoice Studio.exe"; Description: "Launch FreeVoice Studio"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill"; Parameters: "/f /im ""FreeVoice Studio.exe"""; Flags: runhidden; RunOnceId: "KillFreeVoice"
