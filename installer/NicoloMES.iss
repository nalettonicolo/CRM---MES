; Installer di Nicolò MES (client Windows), compilato con Inno Setup 6 dalla pipeline di release
; (.github/workflows/release.yml) a partire dalla cartella "publish" prodotta da dotnet publish.
;
; Scelte:
; - Installazione PER UTENTE in %LOCALAPPDATA%\Programs: nessun permesso di amministratore di Windows e,
;   soprattutto, gli aggiornamenti automatici possono sostituire i file senza elevazione (in Programmi
;   Windows li bloccava in silenzio).
; - Gli aggiornamenti usano questo stesso installer (UpdateService.cs lo avvia con /UPDATE=1):
;   CloseApplications chiude il programma e attende che sia davvero chiuso, poi lo riapre a fine
;   installazione. Sostituisce il vecchio script con attesa fissa di 2 secondi.
;
; - Pagina "Server" solo alla prima installazione, tre scelte: server dell'azienda (indirizzo in rete),
;   servizio in cloud, oppure "collegherò il server in seguito" (nessun file scritto: al primo avvio il
;   programma chiede l'indirizzo). Scrive %LOCALAPPDATA%\CrmMes\settings.json (lo stesso file di
;   ClientSettings.cs) solo se non esiste già; gli aggiornamenti non lo toccano mai.
; - Il client è framework-dependent: se manca .NET 8 Desktop Runtime lo dice e apre la pagina ufficiale.

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
PrivilegesRequired=lowest
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

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
; Prima installazione: casella "Avvia Nicolò MES" nella pagina finale.
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
; Aggiornamento dal programma (/UPDATE=1): lo riapre da solo, anche in modalità silenziosa.
Filename: "{app}\{#MyAppExe}"; Flags: nowait; Check: IsUpdate

[Code]
var
  ServerChoicePage: TInputOptionWizardPage;
  ServerPage: TInputQueryWizardPage;

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
  if not HasDesktopRuntime8 then
  begin
    if MsgBox('Nicolò MES richiede Microsoft .NET 8 Desktop Runtime (x64), che non risulta installato su questo PC.' + #13#10#13#10 +
              'Apro la pagina di download ufficiale di Microsoft? Installa ".NET Desktop Runtime 8" per Windows x64, poi riavvia questa installazione.',
              mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Result := False;
  end;
end;

procedure InitializeWizard;
begin
  ServerChoicePage := CreateInputOptionPage(wpSelectTasks,
    'Server', 'A quale server si collega Nicolò MES?',
    'Scegli dove si trova il gestionale. Se non lo sai ancora, collegalo in seguito: al primo avvio il programma ' +
    'chiederà l''indirizzo, e l''amministratore può sempre cambiarlo da "Impostazioni server".',
    True, False);
  ServerChoicePage.Add('Server dell''azienda (installato nella vostra rete)');
  ServerChoicePage.Add('Servizio in cloud');
  ServerChoicePage.Add('Collegherò il server in seguito');
  ServerChoicePage.SelectedValueIndex := 2;

  ServerPage := CreateInputQueryPage(ServerChoicePage.ID,
    'Server', 'Indirizzo del server dell''azienda',
    'Te lo indica chi ha installato il server, ad esempio http://SERVER-MES:5092/ oppure https://mes.azienda.it/.');
  ServerPage.Add('Indirizzo:', False);
  ServerPage.Values[0] := 'http://';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  // Already configured on this PC (a previous install, or an update): never ask again.
  if FileExists(SettingsPath) then
    Result := (PageID = ServerChoicePage.ID) or (PageID = ServerPage.ID)
  else
    // The address page only for the company's own server.
    Result := (PageID = ServerPage.ID) and (ServerChoicePage.SelectedValueIndex <> 0);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Url: String;
begin
  Result := True;
  if CurPageID = ServerPage.ID then
  begin
    Url := Lowercase(Trim(ServerPage.Values[0]));
    if ((Pos('https://', Url) <> 1) and (Pos('http://', Url) <> 1)) or (Url = 'http://') or (Url = 'https://') then
    begin
      MsgBox('Inserisci l''indirizzo completo del server, che inizia con http:// o https://.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Url: String;
begin
  // "Collegherò il server in seguito": no file, the program asks at its first start.
  if (CurStep = ssPostInstall) and not FileExists(SettingsPath) and (ServerChoicePage.SelectedValueIndex <> 2) then
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
