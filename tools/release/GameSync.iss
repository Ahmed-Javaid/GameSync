; GameSync's installer (PKG-01, PKG-05, R12; design.md -> Packaging). It installs GameSync for the person alone, in
; %LOCALAPPDATA%\Programs\GameSync, with no admin prompt at install, update or uninstall (the owner, 7 Oct 2026: "yes,
; per-user install and tracer after v1"), and isn't code-signed (R19: the updater checks GameSync's own release
; signature instead). tools\release\pack.ps1 builds it:
;   ISCC.exe /DAppVersion=1.0.1 /DSourceDir=<the published folder> /DOutputDir=<folder> tools\release\GameSync.iss
; The updater runs it with /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH=window|background and,
; for a data folder other than the usual one, /DATA=<folder>: GameSync is asked to quit first and opened again after.

#ifndef AppVersion
  #error Give the version: /DAppVersion=1.0.1
#endif
#ifndef SourceDir
  #error Give the published folder: /DSourceDir=<folder>
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
AppId={{6C4E9B2D-3F1A-4B7E-9D5C-2A8F0E1B7C63}
AppName=GameSync
AppVersion={#AppVersion}
AppVerName=GameSync {#AppVersion}
AppPublisher=GameSync
AppPublisherURL=https://github.com/Ahmed-Javaid/GameSync
AppSupportURL=https://github.com/Ahmed-Javaid/GameSync/issues
AppUpdatesURL=https://github.com/Ahmed-Javaid/GameSync/releases
AppComments=Keeps your game saves backed up and in sync across your PCs.
DefaultDirName={userpf}\GameSync
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=yes
PrivilegesRequired=lowest
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 22H2 and Windows 11 (PKG-06).
MinVersion=10.0.19045
OutputDir={#OutputDir}
OutputBaseFilename=GameSync-Setup-{#AppVersion}
SetupIconFile=..\..\src\GameSync.Tray\Assets\gamesync.ico
UninstallDisplayIcon={app}\GameSync.Tray.exe
UninstallDisplayName=GameSync
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=yes
ChangesAssociations=yes
VersionInfoVersion={#AppVersion}
VersionInfoProductName=GameSync
VersionInfoDescription=GameSync setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Put GameSync on the desktop"; Flags: unchecked

[Files]
; The zip's README says how to unzip it; the installer's own pages say the rest.
Source: "{#SourceDir}\*"; Excludes: "README.txt"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\GameSync"; Filename: "{app}\GameSync.Tray.exe"; Comment: "Keeps your game saves backed up and in sync"
Name: "{userdesktop}\GameSync"; Filename: "{app}\GameSync.Tray.exe"; Tasks: desktopicon

[Registry]
; R12: gamesync://play/<game> and gamesync://open/<game>, for this person alone; GameSync starts only a game it knows.
Root: HKCU; Subkey: "Software\Classes\gamesync"; ValueType: string; ValueName: ""; ValueData: "URL:GameSync link"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\gamesync"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\gamesync\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\GameSync.Tray.exe"",0"
Root: HKCU; Subkey: "Software\Classes\gamesync\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\GameSync.Tray.exe"" --link ""%1"""

[Run]
Filename: "{app}\GameSync.Tray.exe"; Description: "Open GameSync"; Flags: nowait postinstall skipifsilent
; After an update: GameSync again, on the data folder it ran on, with its window if it had it open.
Filename: "{app}\GameSync.Tray.exe"; Parameters: "{code:RelaunchParameters}"; Flags: nowait; Check: Relaunching

[UninstallRun]
; PKG-05: GameSync quits, and what starts this copy goes (the Run key's GameSync, the daily backup's tasks). The data
; folder, the backups and the cloud copy stay.
Filename: "{app}\gamesync.exe"; Parameters: "uninstalling"; Flags: runhidden waituntilterminated; RunOnceId: "GameSyncCleanUp"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  EnvironmentKey = 'Environment';

// The data folder the updater named, quoted, after --data; empty for the usual one.
function DataArguments(): String;
var
  Data: String;
begin
  Data := ExpandConstant('{param:DATA|}');
  if Data = '' then
    Result := ''
  else
    Result := '--data "' + Data + '"';
end;

function Relaunching(): Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|}') <> '';
end;

function RelaunchParameters(Param: String): String;
begin
  Result := DataArguments();
  if ExpandConstant('{param:RELAUNCH|}') = 'background' then
    Result := Trim(Result + ' --background');
end;

// GameSync records any open session and quits before its files are replaced: gamesync quit waits until it has.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\gamesync.exe')) then
    Exec(ExpandConstant('{app}\gamesync.exe'), Trim(DataArguments() + ' quit'), '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

// gamesync in the person's terminal: the folder on their own PATH, taken away again on uninstall.
function WithoutFolder(Paths, Folder: String): String;
var
  Rest, Item: String;
  At: Integer;
begin
  Result := '';
  Rest := Paths;
  while Rest <> '' do
  begin
    At := Pos(';', Rest);
    if At = 0 then
    begin
      Item := Rest;
      Rest := '';
    end
    else
    begin
      Item := Copy(Rest, 1, At - 1);
      Rest := Copy(Rest, At + 1, Length(Rest));
    end;
    if (Item <> '') and (CompareText(RemoveBackslashUnlessRoot(Item), RemoveBackslashUnlessRoot(Folder)) <> 0) then
    begin
      if Result <> '' then
        Result := Result + ';';
      Result := Result + Item;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Paths: String;
begin
  if CurStep = ssPostInstall then
  begin
    if not RegQueryStringValue(HKCU, EnvironmentKey, 'Path', Paths) then
      Paths := '';
    Paths := WithoutFolder(Paths, ExpandConstant('{app}'));
    if Paths <> '' then
      Paths := Paths + ';';
    RegWriteExpandStringValue(HKCU, EnvironmentKey, 'Path', Paths + ExpandConstant('{app}'));
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Paths: String;
begin
  if (CurUninstallStep = usPostUninstall) and RegQueryStringValue(HKCU, EnvironmentKey, 'Path', Paths) then
    RegWriteExpandStringValue(HKCU, EnvironmentKey, 'Path', WithoutFolder(Paths, ExpandConstant('{app}')));
end;
