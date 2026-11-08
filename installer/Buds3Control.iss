#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{B07DD286-2FB8-4B13-ADEF-E1BF4F5214EB}
AppName=Buds 3 Control
AppVersion={#AppVersion}
AppPublisher=thousandecibels
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Buds 3 Control
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0.22000
AppMutex=Local\Buds3Control
RestartApplications=no
UninstallDisplayName=Buds 3 Control
UninstallDisplayIcon={app}\Buds3Control.exe
SetupIconFile=..\assets\Buds3Control.ico
OutputDir=..\dist\release
OutputBaseFilename=Buds3Control-Setup
WizardStyle=modern dynamic
Compression=lzma2
SolidCompression=yes

[Files]
Source: "..\dist\release\Buds3Control.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Buds 3 Control"; Filename: "{app}\Buds3Control.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\Buds3Control.exe"; Description: "Open Buds 3 Control"; Flags: nowait postinstall skipifsilent
