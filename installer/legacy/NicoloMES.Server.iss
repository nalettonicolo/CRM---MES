; Installer del SERVER di Nicolò MES (API, piattaforma web, pagina tecnici) per un server Windows del cliente.
; Compilato dalla pipeline di release a partire dalla cartella "publish-server" (API pubblicata self-contained:
; sul server non serve installare .NET).
;
; - Richiede i permessi di amministratore: crea un servizio di Windows, una regola del firewall e un'attività
;   pianificata di backup.
; - A fine copia esegue server\Configure-NicoloMESServer.ps1 in una finestra visibile: alla prima installazione
;   chiede PostgreSQL e i contatti di assistenza; negli aggiornamenti mantiene configurazione e dati, aggiorna
;   il database e riavvia il servizio.
; - Prima di copiare i file ferma il servizio (i file in uso non si possono sostituire).
; - La disinstallazione rimuove servizio, firewall e backup pianificato, ma NON i dati (database e
;   C:\ProgramData\NicoloMES).

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#define MyAppName "Nicolò MES server"

[Setup]
AppId={{5C1E7A92-3B4D-4F60-9A8E-2D7F1B6C0E43}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=Nicolò Naletto
VersionInfoVersion={#MyAppVersion}
DefaultDirName={commonpf64}\Nicolo MES Server
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\installer-output
OutputBaseFilename=NicoloMES-Server-Setup
SetupIconFile=..\CrmMes.Desktop\Assets\logo.ico
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Files]
Source: "..\publish-server\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\server\windows\*.ps1"; DestDir: "{app}\server"; Flags: ignoreversion
Source: "..\server\LEGGIMI-SERVER.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Configura Nicolò MES server"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -NoExit -File ""{app}\server\Configure-NicoloMESServer.ps1"""; WorkingDir: "{app}"
Name: "{group}\Backup adesso"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -NoExit -File ""{app}\server\Backup-NicoloMES.ps1"""; WorkingDir: "{app}"
Name: "{group}\Cartella dati e log"; Filename: "{commonappdata}\NicoloMES"
Name: "{group}\Istruzioni"; Filename: "{app}\LEGGIMI-SERVER.md"

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -NoExit -File ""{app}\server\Configure-NicoloMESServer.ps1"" -InstallDir ""{app}"""; \
  StatusMsg: "Configurazione del server..."; Flags: waituntilterminated

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\server\Remove-NicoloMESServer.ps1"""; \
  Flags: runhidden waituntilterminated; RunOnceId: "RemoveNicoloMESServer"

[Code]
// Stop the service before the files are replaced (an update while it runs would fail on locked files).
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop NicoloMES', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  // Give it time to release the files; a service not installed yet simply returns an error code.
  Sleep(3000);
end;
