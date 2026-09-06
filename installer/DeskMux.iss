#ifndef MyAppVersion
  #define MyAppVersion "0.1.0.0"
#endif
#ifndef MyAppDisplayVersion
  #define MyAppDisplayVersion "0.1.0-alpha.1"
#endif

[Setup]
AppId={{8B4B6254-925D-47AD-9BA7-63C13EA5AF2C}
AppName=DeskMux
AppVersion={#MyAppDisplayVersion}
AppVerName=DeskMux {#MyAppDisplayVersion}
AppPublisher=Kurtian
AppPublisherURL=https://deskmux.kurtian.dev
AppSupportURL=https://github.com/kurtianbernaldez/DeskMux/issues
AppUpdatesURL=https://github.com/kurtianbernaldez/DeskMux/releases
DefaultDirName={localappdata}\Programs\DeskMux
DefaultGroupName=DeskMux
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\release
OutputBaseFilename=DeskMux-Setup-x64
SetupIconFile=..\src\DeskMux.App\Assets\DeskMux.ico
UninstallDisplayIcon={app}\DeskMux.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany=Kurtian
VersionInfoDescription=DeskMux installer
VersionInfoProductName=DeskMux

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Start DeskMux when I sign in"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\artifacts\DeskMux-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\DeskMux"; Filename: "{app}\DeskMux.exe"
Name: "{userdesktop}\DeskMux"; Filename: "{app}\DeskMux.exe"; Tasks: desktopicon
Name: "{userstartup}\DeskMux"; Filename: "{app}\DeskMux.exe"; Tasks: startup

[Run]
Filename: "{app}\DeskMux.exe"; Description: "Launch DeskMux"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\DeskMux.exe')) then
  begin
    Exec(ExpandConstant('{app}\DeskMux.exe'), '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(800);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    if FileExists(ExpandConstant('{app}\DeskMux.exe')) then
    begin
      Exec(ExpandConstant('{app}\DeskMux.exe'), '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Sleep(800);
    end;
  end;
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    if MsgBox('Delete your DeskMux sessions, settings, and logs?', mbConfirmation, MB_YESNO) = IDYES then
      DelTree(ExpandConstant('{localappdata}\DeskMux'), True, True, True);
end;
