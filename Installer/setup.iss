; ═══════════════════════════════════════════════════════════════════════════════
;  ███████╗██████╗  ██████╗  ██████╗  ██████╗██╗   ██╗
;  ██╔════╝██╔══██╗██╔═══██╗██╔════╝ ██╔════╝╚██╗ ██╔╝
;  █████╗  ██████╔╝██║   ██║██║  ███╗██║  ███╗╚████╔╝ 
;  ██╔══╝  ██╔══██╗██║   ██║██║   ██║██║   ██║ ╚██╔╝  
;  ██║     ██║  ██║╚██████╔╝╚██████╔╝╚██████╔╝  ██║   
;  ╚═╝     ╚═╝  ╚═╝ ╚═════╝  ╚═════╝  ╚═════╝   ╚═╝   
;                                                      
;  RETRO INSTALLER - Bluetooth Battery Widget
;  Download Inno Setup from: https://jrsoftware.org/isdl.php
; ═══════════════════════════════════════════════════════════════════════════════

#define MyAppName "Froggy"
#define MyAppVersion "1.2.0"
#define MyAppPublisher "Froggy"
#define MyAppURL "https://github.com/froggy"
#define MyAppExeName "Froggy.exe"

[Setup]
; App identity
AppId={{8F4B8C3A-1234-5678-9ABC-DEF012345678}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Installation directories
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Output settings
OutputDir=Output
OutputBaseFilename=Froggy_Setup_{#MyAppVersion}
; SetupIconFile=..\Assets\logo.ico

; Compression
Compression=lzma2/ultra64
SolidCompression=yes

; ═══════════════════════════════════════════════════════════════════════════════
; RETRO VISUAL STYLE - Classic Windows installer look
; ═══════════════════════════════════════════════════════════════════════════════
WizardStyle=classic
WizardSizePercent=100

; Privileges - use admin for proper install
PrivilegesRequired=admin

; Uninstaller
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

; Silent install support
CloseApplications=yes
CloseApplicationsFilter=*.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
; Custom retro messages
WelcomeLabel1=Welcome to the [name] Setup Wizard
WelcomeLabel2=This wizard will install [name/ver] on your computer.%n%nIt is recommended that you close all other applications before continuing.%n%n>>> PRESS NEXT TO CONTINUE <<<
FinishedHeadingLabel=Installation Complete!
FinishedLabelNoIcons=Setup has finished installing [name] on your computer.%n%n>>> YOUR BLUETOOTH BATTERIES ARE NOW MONITORED <<<
FinishedLabel=Setup has finished installing [name] on your computer. The application may be launched by selecting the installed shortcuts.%n%n>>> GAME ON! <<<

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked
Name: "startupicon"; Description: "Start Froggy with Windows"; GroupDescription: "Startup options:"

[Files]
; Main application files from publish folder
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Assets
Source: "..\Assets\logo.jpg"; DestDir: "{app}\Assets"; Flags: ignoreversion
Source: "..\Assets\animation.mp4"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Add to Windows startup if selected
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupicon

[Run]
; Option to run app after install - only if not silent
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Froggy now!"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

