; =============================================================================
; Nicolò MES — installer unificato (suite)
; -----------------------------------------------------------------------------
; Un solo wizard: Programma Windows, Server aziendale, oppure entrambi.
; - Aggiornamento automatico del client (/UPDATE=1): installa solo il programma,
;   senza elevazione e senza toccare il server (UpdateService.cs).
; - Output: NicoloMES-Setup.exe (stesso nome atteso dall'auto-update).
;
; Gli script storici restano in installer/legacy/ (Client e Server separati).
; Snapshot di build vecchie: installer/archive/.
; =============================================================================

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#define MyAppName "Nicolò MES"
#define MyAppExe "CrmMes.Desktop.exe"
#define DefaultServer "https://crmmes-api.onrender.com/"

[Setup]
AppId={{8F3B2C1A-5D4E-4B7A-9C2F-1E6D7A8B9C0D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=Nicolò Naletto
VersionInfoVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\Nicolo MES
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
; Client senza admin; se scegli il server il wizard chiede elevazione (OverridesAllowed).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\installer-output
OutputBaseFilename=NicoloMES-Setup
SetupIconFile=..\CrmMes.Desktop\Assets\logo.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no
MinVersion=10.0

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Types]
Name: "client"; Description: "Solo programma (PC ufficio / reparto)"
Name: "server"; Description: "Solo server (PC o Windows Server dell'azienda)"
Name: "full"; Description: "Programma e server sullo stesso PC"
Name: "custom"; Description: "Scelta personalizzata"; Flags: iscustom

[Components]
Name: "client"; Description: "Programma Nicolò MES"; Types: client full custom; Flags: disablenouninstallwarning
Name: "server"; Description: "Server Nicolò MES (API, web, tecnici)"; Types: server full custom; Flags: disablenouninstallwarning

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Components: client

[Files]
; --- Client (framework-dependent, come prima) ---
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: client
; --- Server (self-contained) ---
Source: "..\publish-server\*"; DestDir: "{commonpf64}\Nicolo MES Server"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: server
Source: "..\server\windows\*.ps1"; DestDir: "{commonpf64}\Nicolo MES Server\server"; Flags: ignoreversion; Components: server
Source: "..\server\LEGGIMI-SERVER.md"; DestDir: "{commonpf64}\Nicolo MES Server"; Flags: ignoreversion; Components: server

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Components: client
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon; Components: client
Name: "{group}\Configura Nicolò MES server"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -NoExit -File ""{commonpf64}\Nicolo MES Server\server\Configure-NicoloMESServer.ps1"""; WorkingDir: "{commonpf64}\Nicolo MES Server"; Components: server
Name: "{group}\Backup adesso"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -NoExit -File ""{commonpf64}\Nicolo MES Server\server\Backup-NicoloMES.ps1"""; WorkingDir: "{commonpf64}\Nicolo MES Server"; Components: server
Name: "{group}\Cartella dati e log del server"; Filename: "{commonappdata}\NicoloMES"; Components: server
Name: "{group}\Istruzioni server"; Filename: "{commonpf64}\Nicolo MES Server\LEGGIMI-SERVER.md"; Components: server

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent; Components: client; Check: not IsUpdate
Filename: "{app}\{#MyAppExe}"; Flags: nowait; Components: client; Check: IsUpdate
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -NoExit -File ""{commonpf64}\Nicolo MES Server\server\Configure-NicoloMESServer.ps1"" -InstallDir ""{commonpf64}\Nicolo MES Server"""; \
  StatusMsg: "Configurazione del server..."; Flags: waituntilterminated; Components: server; Check: not IsUpdate

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{commonpf64}\Nicolo MES Server\server\Remove-NicoloMESServer.ps1"""; \
  Flags: runhidden waituntilterminated; RunOnceId: "RemoveNicoloMESServer"; Components: server

[Code]
var
  ServerChoicePage: TInputOptionWizardPage;
  ServerPage: TInputQueryWizardPage;
  WantedServer: Boolean;

function IsUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

function SettingsPath: String;
begin
  Result := ExpandConstant('{localappdata}\CrmMes\settings.json');
end;

function HasDesktopRuntime8: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec);
  if Result then
    FindClose(FindRec);
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  WantedServer := False;

  // Auto-update: only the client, never touch the server.
  if IsUpdate then
  begin
    // Force client-only by leaving types as default; WizardSilent selects components from Types.
    exit;
  end;

  if not HasDesktopRuntime8 then
  begin
    // Runtime needed only if client will be installed; we still warn early.
    if MsgBox('Nicolò MES richiede Microsoft .NET 8 Desktop Runtime (x64) per il programma Windows.' + #13#10#13#10 +
              'Se installi solo il server puoi continuare; se installi il programma, scarica il Runtime.' + #13#10#13#10 +
              'Apro la pagina ufficiale di Microsoft?',
              mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;

procedure InitializeWizard;
begin
  if IsUpdate then
    exit;

  ServerChoicePage := CreateInputOptionPage(wpSelectComponents,
    'Server del programma', 'A quale server si collega il programma?',
    'Vale solo se installi il programma Windows. Se non lo sai ancora, collegalo in seguito.',
    True, False);
  ServerChoicePage.Add('Server dell''azienda (installato nella vostra rete)');
  ServerChoicePage.Add('Servizio in cloud');
  ServerChoicePage.Add('Collegherò il server in seguito');
  ServerChoicePage.SelectedValueIndex := 2;

  ServerPage := CreateInputQueryPage(ServerChoicePage.ID,
    'Server', 'Indirizzo del server dell''azienda',
    'Te lo indica chi ha installato il server, ad esempio http://SERVER-MES:5092/.');
  ServerPage.Add('Indirizzo:', False);
  ServerPage.Values[0] := 'http://';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  if IsUpdate then
  begin
    Result := True;
    exit;
  end;

  if (ServerChoicePage = nil) or (ServerPage = nil) then
  begin
    Result := False;
    exit;
  end;

  // No client component → skip program-server pages.
  if not WizardIsComponentSelected('client') then
  begin
    Result := (PageID = ServerChoicePage.ID) or (PageID = ServerPage.ID);
    exit;
  end;

  if FileExists(SettingsPath) then
    Result := (PageID = ServerChoicePage.ID) or (PageID = ServerPage.ID)
  else
    Result := (PageID = ServerPage.ID) and (ServerChoicePage.SelectedValueIndex <> 0);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Url: String;
  ErrorCode: Integer;
begin
  Result := True;

  // After component selection: if server is selected and we are not elevated, relaunch elevated.
  if (CurPageID = wpSelectComponents) and WizardIsComponentSelected('server') and not IsAdminInstallMode then
  begin
    if MsgBox('Per installare il server servono i permessi di amministratore. Riavvio l''installer con elevazione?',
              mbConfirmation, MB_YESNO) <> IDYES then
    begin
      Result := False;
      exit;
    end;
    if not ShellExec('runas', ExpandConstant('{srcexe}'),
         ExpandConstant('/TYPE=full /DIR="{app}"'),
         '', SW_SHOWNORMAL, ewNoWait, ErrorCode) then
      MsgBox('Impossibile richiedere i permessi di amministratore.', mbError, MB_OK);
    Result := False;
    exit;
  end;

  if (ServerPage <> nil) and (CurPageID = ServerPage.ID) then
  begin
    Url := Lowercase(Trim(ServerPage.Values[0]));
    if ((Pos('https://', Url) <> 1) and (Pos('http://', Url) <> 1)) or (Url = 'http://') or (Url = 'https://') then
    begin
      MsgBox('Inserisci l''indirizzo completo del server, che inizia con http:// o https://.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  NeedsRestart := False;
  if WizardIsComponentSelected('server') then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop NicoloMES', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(3000);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Url: String;
begin
  if IsUpdate or (ServerChoicePage = nil) then
    exit;

  if (CurStep = ssPostInstall) and WizardIsComponentSelected('client')
     and not FileExists(SettingsPath) and (ServerChoicePage.SelectedValueIndex <> 2) then
  begin
    if ServerChoicePage.SelectedValueIndex = 1 then
      Url := '{#DefaultServer}'
    else
      Url := Trim(ServerPage.Values[0]);
    StringChangeEx(Url, '\', '\\', True);
    StringChangeEx(Url, '"', '\"', True);
    ForceDirectories(ExtractFileDir(SettingsPath));
    SaveStringToFile(SettingsPath, '{ "ApiBaseUrl": "' + Url + '" }', False);
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoTypeInfo + NewLine + NewLine + MemoComponentsInfo;
  if MemoDirInfo <> '' then
    Result := Result + NewLine + NewLine + MemoDirInfo;
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;
end;
