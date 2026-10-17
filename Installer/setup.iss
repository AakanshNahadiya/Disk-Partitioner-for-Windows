; Inno Setup 6 Script for Disk Partitioner for Windows
; Accessible Tools Suite - http://github.com/AakanshNahadiya
#define MyAppName "Disk Partitioner for Windows"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Accessible Tools"
#define MyAppURL "https://github.com/AakanshNahadiya"
#define MyAppExeName "DiskPartitioner.exe"

[Setup]
; Unique GUID for Disk Partitioner for Windows
AppId={{8B84F074-686E-4547-8B52-327A79D20EF1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\Disk Partitioner for Windows
UsePreviousAppDir=no
DirExistsWarning=no
DefaultGroupName=Accessible Tools\{#MyAppName}
DisableProgramGroupPage=yes
DisableReadyPage=yes
OutputDir=Output
OutputBaseFilename=DiskPartitioner_Setup_v1.0.0
SetupIconFile=..\Resources\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
UninstallDisplayNameMark=%1
UninstallDisplayNameMarks=%1

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{commondesktop}\{#MyAppName}.lnk"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Check: ShouldCreateDesktopIcon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
Type: files; Name: "{commondesktop}\{#MyAppName}.lnk"
Type: filesandordirs; Name: "{app}"

[Code]
var
  DesktopIconCheck: TNewCheckBox;

function ShouldCreateDesktopIcon(): Boolean;
begin
  Result := (DesktopIconCheck <> nil) and DesktopIconCheck.Checked;
end;

procedure InitializeWizard();
begin
  // Embed desktop shortcut option directly on the destination directory selection page
  DesktopIconCheck := TNewCheckBox.Create(WizardForm);
  DesktopIconCheck.Parent := WizardForm.SelectDirPage;
  DesktopIconCheck.Left := WizardForm.DirEdit.Left;
  DesktopIconCheck.Top := WizardForm.DiskSpaceLabel.Top + WizardForm.DiskSpaceLabel.Height + ScaleY(12);
  DesktopIconCheck.Width := WizardForm.DirEdit.Width;
  DesktopIconCheck.Height := ScaleY(24);
  DesktopIconCheck.Caption := 'Create a &desktop shortcut';
  DesktopIconCheck.Checked := True;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  // On the single options page, present the button directly as "Install"
  if CurPageID = wpSelectDir then
  begin
    WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);
  end;
end;

function KillRunningApp(): Boolean;
var
  ResultCode: Integer;
  i: Integer;
begin
  Result := True;
  
  // Only attempt termination if an active instance is currently running
  if CheckForMutexes('Global\DiskPartitioner_SingleInstance_Mutex,DiskPartitioner_SingleInstance_Mutex') then
  begin
    // Terminate running instance directly (installer runs elevated as Administrator)
    Exec('taskkill.exe', '/F /T /IM DiskPartitioner.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Poll for up to 500ms until mutex is completely released
    for i := 1 to 10 do
    begin
      if not CheckForMutexes('Global\DiskPartitioner_SingleInstance_Mutex,DiskPartitioner_SingleInstance_Mutex') then
        Break;
      Sleep(50);
    end;
    Sleep(100);
  end;
end;

function InitializeSetup(): Boolean;
begin
  // Terminate any running instance immediately on setup launch
  KillRunningApp();
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // Terminate any running instance when user clicks "Install" button right before extracting files
  KillRunningApp();
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    // Double-ensure app is terminated immediately prior to file operations
    KillRunningApp();
  end;
end;

function InitializeUninstall(): Boolean;
begin
  // Terminate running instance immediately when uninstaller launches
  KillRunningApp();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    // Double-ensure app is terminated before file deletion step
    KillRunningApp();
  end;
end;
