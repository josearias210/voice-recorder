; Inno Setup script para Voice Recorder
; Se compila con: ISCC.exe /DAppVersion=x.y.z installer\VoiceRecorder.iss
; Requiere que el publish esté en ..\publish (scripts\build.ps1)

#ifndef AppVersion
#define AppVersion "1.0.0"
#endif

[Setup]
AppId={{3960D3E3-FBD7-4EF5-957E-7288F8A816F5}
AppName=Voice Recorder
AppVersion={#AppVersion}
AppPublisher=josearias210
AppPublisherURL=https://github.com/josearias210/voice-recorder
AppSupportURL=https://github.com/josearias210/voice-recorder/issues
DefaultDirName={autopf}\Voice Recorder
DefaultGroupName=Voice Recorder
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=output
OutputBaseFilename=VoiceRecorder-Setup-{#AppVersion}
SetupIconFile=..\src\VoiceRecorder.App\Assets\app.ico
UninstallDisplayIcon={app}\VoiceRecorder.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
MinVersion=10.0

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Voice Recorder"; Filename: "{app}\VoiceRecorder.exe"
Name: "{group}\Desinstalar Voice Recorder"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Voice Recorder"; Filename: "{app}\VoiceRecorder.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\VoiceRecorder.exe"; Description: "{cm:LaunchProgram,Voice Recorder}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; No borramos %LOCALAPPDATA%\VoiceRecorder: contiene historial y modelos del usuario.
Type: filesandordirs; Name: "{app}"

