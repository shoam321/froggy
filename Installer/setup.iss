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
#define MyAppVersion "1.1.0"
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

; Privileges
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Uninstaller
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

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
; Option to run app after install
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Froggy now!"; Flags: nowait postinstall skipifsilent

[Code]
var
  RetroPage: TWizardPage;
  AsciiArt: TNewStaticText;
  FeatureList: TNewStaticText;
  StatusLbl: TNewStaticText;

procedure InitializeWizard();
begin
  // ═══════════════════════════════════════════════════════════════════════════
  // RETRO WELCOME PAGE
  // ═══════════════════════════════════════════════════════════════════════════
  RetroPage := CreateCustomPage(wpWelcome, 'F R O G G Y', 'Bluetooth Battery Monitor v1.0.0');
  
  // ASCII Art Title
  AsciiArt := TNewStaticText.Create(RetroPage);
  AsciiArt.Parent := RetroPage.Surface;
  AsciiArt.Caption := 
    '╔═══════════════════════════════════════╗' + #13#10 +
    '║                                       ║' + #13#10 +
    '║     ███████ ██████   ██████   ██████  ║' + #13#10 +
    '║     ██      ██   ██ ██    ██ ██       ║' + #13#10 +
    '║     █████   ██████  ██    ██ ██   ███ ║' + #13#10 +
    '║     ██      ██   ██ ██    ██ ██    ██ ║' + #13#10 +
    '║     ██      ██   ██  ██████   ██████  ║' + #13#10 +
    '║                                       ║' + #13#10 +
    '║       BLUETOOTH BATTERY WIDGET        ║' + #13#10 +
    '╚═══════════════════════════════════════╝';
  AsciiArt.Font.Name := 'Consolas';
  AsciiArt.Font.Size := 8;
  AsciiArt.Font.Color := clGreen;
  AsciiArt.Font.Style := [fsBold];
  AsciiArt.Left := 20;
  AsciiArt.Top := 5;
  AsciiArt.AutoSize := True;
  
  // Feature list with retro styling
  FeatureList := TNewStaticText.Create(RetroPage);
  FeatureList.Parent := RetroPage.Surface;
  FeatureList.Caption := 
    '┌─────────────────────────────────────────┐' + #13#10 +
    '│  > Real-time battery monitoring         │' + #13#10 +
    '│  > 4 themes: Retro/Pixel/Neon/Moss      │' + #13#10 +
    '│  > Network speed + ping display         │' + #13#10 +
    '│  > Battery drain rate tracking          │' + #13#10 +
    '│  > Screen edge snapping                 │' + #13#10 +
    '│  > Stays on top of other windows        │' + #13#10 +
    '└─────────────────────────────────────────┘';
  FeatureList.Font.Name := 'Consolas';
  FeatureList.Font.Size := 9;
  FeatureList.Font.Color := clAqua;
  FeatureList.Left := 20;
  FeatureList.Top := 145;
  FeatureList.AutoSize := True;
  
  // Made by credit
  StatusLbl := TNewStaticText.Create(RetroPage);
  StatusLbl.Parent := RetroPage.Surface;
  StatusLbl.Caption := 'Made by Shoam';
  StatusLbl.Font.Name := 'Consolas';
  StatusLbl.Font.Size := 10;
  StatusLbl.Font.Color := clYellow;
  StatusLbl.Font.Style := [fsBold];
  StatusLbl.Left := 20;
  StatusLbl.Top := 290;
  StatusLbl.AutoSize := True;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  // Update status messages based on page
  if CurPageID = wpSelectDir then
  begin
    WizardForm.DirEdit.Font.Name := 'Consolas';
  end;
  
  if CurPageID = wpInstalling then
  begin
    WizardForm.StatusLabel.Font.Name := 'Consolas';
    WizardForm.StatusLabel.Font.Color := clLime;
  end;
  
  if CurPageID = wpFinished then
  begin
    WizardForm.FinishedLabel.Font.Name := 'Consolas';
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
end;

