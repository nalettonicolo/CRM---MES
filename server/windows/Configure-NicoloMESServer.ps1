<#
.SYNOPSIS
    Configura Nicolò MES su un server Windows dell'azienda cliente.

.DESCRIPTION
    Eseguito dall'installer del server (NicoloMES-Server-Setup.exe) al termine della copia dei file, e
    rieseguibile in qualsiasi momento dal collegamento "Configura Nicolò MES server".

    Alla prima esecuzione:
      1. trova PostgreSQL (o accetta la stringa di connessione di un database già esistente);
      2. crea l'utente e il database dedicati con una password generata (non viene mai mostrata);
      3. genera la chiave di firma degli accessi;
      4. scrive C:\ProgramData\NicoloMES\server.json leggibile solo da amministratori, SYSTEM e dal servizio;
      5. crea il servizio di Windows "NicoloMES" (account LocalService, riavvio automatico se si ferma);
      6. apre la porta nel firewall per la rete aziendale;
      7. aggiorna il database (migrazioni) e avvia il servizio;
      8. pianifica il backup notturno del database.

    Alle esecuzioni successive (aggiornamenti) mantiene configurazione e dati: ferma il servizio, aggiorna
    il database e lo riavvia.

    Nessuna password viene scritta a video, nei log o nella riga di comando dei processi figli.
#>
[CmdletBinding()]
param(
    # The installer copies these scripts to <cartella del programma>\server.
    [string]$InstallDir = (Split-Path -Parent $PSScriptRoot),
    [int]$Port = 5092,
    [switch]$Unattended
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$ServiceName = 'NicoloMES'
$DataDir = Join-Path $env:ProgramData 'NicoloMES'
$ConfigPath = Join-Path $DataDir 'server.json'
$LogsDir = Join-Path $DataDir 'logs'
$BackupDir = Join-Path $DataDir 'backup'
$ApiExe = Join-Path $InstallDir 'CrmMes.Api.exe'
$FirewallRule = 'Nicolò MES server'
$BackupTask = 'Nicolò MES - backup notturno'

function Write-Step([string]$Text) { Write-Host ''; Write-Host "==> $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text) { Write-Host "    $Text" -ForegroundColor Green }
function Write-Warn([string]$Text) { Write-Host "    $Text" -ForegroundColor Yellow }

function Test-Administrator {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function New-RandomSecret([int]$Bytes = 32) {
    $buffer = New-Object byte[] $Bytes
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
    return [Convert]::ToBase64String($buffer)
}

function New-DatabasePassword {
    # Letters and digits only: no quoting problems in connection strings or SQL.
    $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $buffer = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
    return -join ($buffer | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
}

function ConvertFrom-SecureToPlain([Security.SecureString]$Secure) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function Find-PostgresBin {
    $fromPath = Get-Command psql.exe -ErrorAction SilentlyContinue
    if ($fromPath) { return Split-Path -Parent $fromPath.Source }
    $candidates = Get-ChildItem -Path (Join-Path $env:ProgramFiles 'PostgreSQL') -Directory -ErrorAction SilentlyContinue |
        Sort-Object { [int]($_.Name -replace '\D', '0') } -Descending
    foreach ($candidate in $candidates) {
        $bin = Join-Path $candidate.FullName 'bin'
        if (Test-Path (Join-Path $bin 'psql.exe')) { return $bin }
    }
    return $null
}

function Invoke-Psql([string]$Bin, [string]$Password, [string[]]$Arguments) {
    # The password travels in the child's environment (PGPASSWORD), never on its command line.
    $previous = $env:PGPASSWORD
    $env:PGPASSWORD = $Password
    try {
        $output = & (Join-Path $Bin 'psql.exe') @Arguments 2>&1
        if ($LASTEXITCODE -ne 0) { throw "psql: $($output -join ' ')" }
        return $output
    }
    finally { $env:PGPASSWORD = $previous }
}

function Protect-DataDirectory {
    # Administrators and SYSTEM full control; the service account reads the configuration and writes logs.
    New-Item -ItemType Directory -Force -Path $DataDir, $LogsDir, $BackupDir | Out-Null
    & icacls $DataDir /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' '*S-1-5-19:(OI)(CI)RX' | Out-Null
    & icacls $LogsDir /grant:r '*S-1-5-19:(OI)(CI)M' | Out-Null
}

function Read-Answer([string]$Question, [string]$Default = '') {
    if ($Unattended) { return $Default }
    $suffix = if ($Default) { " [$Default]" } else { '' }
    $answer = Read-Host "$Question$suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
    return $answer.Trim()
}

function New-ServerConfiguration {
    Write-Step 'Database PostgreSQL'
    $bin = Find-PostgresBin
    $connectionString = $null

    if ($bin) {
        Write-Ok "PostgreSQL trovato in $bin"
        $mode = Read-Answer 'Creo un database nuovo su questo server (N) oppure usi un database esistente (E)?' 'N'
    }
    else {
        Write-Warn 'PostgreSQL non è installato su questo server.'
        Write-Warn 'Installa PostgreSQL 16 o 17 da https://www.postgresql.org/download/windows/ e riesegui questa configurazione,'
        Write-Warn 'oppure indica un database PostgreSQL esistente su un altro server.'
        $mode = Read-Answer 'Usi un database esistente (E) o esci per installare PostgreSQL (X)?' 'X'
        if ($mode -notmatch '^[Ee]') { throw 'Configurazione interrotta: installa PostgreSQL e riesegui "Configura Nicolò MES server".' }
    }

    if ($mode -match '^[Ee]') {
        $secure = Read-Host 'Stringa di connessione (Host=...;Database=...;Username=...;Password=...)' -AsSecureString
        $connectionString = ConvertFrom-SecureToPlain $secure
        if ($connectionString -notmatch 'Host=' -or $connectionString -notmatch 'Database=') {
            throw 'Stringa di connessione non valida: servono almeno Host e Database.'
        }
    }
    else {
        $superPassword = ConvertFrom-SecureToPlain (Read-Host "Password dell'utente postgres (quella scelta installando PostgreSQL)" -AsSecureString)
        $appPassword = New-DatabasePassword
        $exists = Invoke-Psql $bin $superPassword @('-h', 'localhost', '-U', 'postgres', '-tAc', "SELECT 1 FROM pg_roles WHERE rolname='nicolomes'")
        if (($exists -join '').Trim() -eq '1') {
            Invoke-Psql $bin $superPassword @('-h', 'localhost', '-U', 'postgres', '-c', "ALTER ROLE nicolomes WITH LOGIN PASSWORD '$appPassword'") | Out-Null
        }
        else {
            Invoke-Psql $bin $superPassword @('-h', 'localhost', '-U', 'postgres', '-c', "CREATE ROLE nicolomes LOGIN PASSWORD '$appPassword'") | Out-Null
        }

        $dbExists = Invoke-Psql $bin $superPassword @('-h', 'localhost', '-U', 'postgres', '-tAc', "SELECT 1 FROM pg_database WHERE datname='nicolomes'")
        if (($dbExists -join '').Trim() -ne '1') {
            Invoke-Psql $bin $superPassword @('-h', 'localhost', '-U', 'postgres', '-c', 'CREATE DATABASE nicolomes OWNER nicolomes ENCODING ''UTF8''') | Out-Null
        }

        $superPassword = $null
        $connectionString = "Host=localhost;Port=5432;Database=nicolomes;Username=nicolomes;Password=$appPassword"
        Write-Ok 'Utente e database "nicolomes" pronti (password generata e salvata solo nella configurazione protetta).'
    }

    Write-Step 'Assistenza'
    $supportName = Read-Answer 'Nome di chi presta assistenza' ''
    $supportPhone = Read-Answer 'Telefono assistenza' ''
    $supportEmail = Read-Answer 'Email assistenza' ''
    $rustDeskServer = Read-Answer 'Server RustDesk per la teleassistenza (vuoto = server pubblico RustDesk)' ''
    $rustDeskKey = if ($rustDeskServer) { Read-Answer 'Chiave pubblica del server RustDesk' '' } else { '' }

    $config = [ordered]@{
        ConnectionStrings = [ordered]@{ DefaultConnection = $connectionString }
        Jwt = [ordered]@{ Key = (New-RandomSecret 48) }
        Kestrel = [ordered]@{ Endpoints = [ordered]@{ Http = [ordered]@{ Url = "http://0.0.0.0:$Port" } } }
        Logs = [ordered]@{ Path = $LogsDir }
        Backup = [ordered]@{ Path = $BackupDir; RetentionDays = 30; CopyTo = '' }
        Support = [ordered]@{
            Name = $supportName; Phone = $supportPhone; Email = $supportEmail
            RustDeskIdServer = $rustDeskServer; RustDeskKey = $rustDeskKey
        }
    }

    Protect-DataDirectory
    $json = $config | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText($ConfigPath, $json, (New-Object Text.UTF8Encoding($false)))
    & icacls $ConfigPath /inheritance:r /grant:r '*S-1-5-32-544:F' '*S-1-5-18:F' '*S-1-5-19:R' | Out-Null
    $connectionString = $null
    Write-Ok "Configurazione salvata in $ConfigPath (accesso ristretto)."
}

function Install-Service {
    Write-Step 'Servizio di Windows'
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $service) {
        & sc.exe create $ServiceName binPath= "`"$ApiExe`"" start= delayed-auto obj= 'NT AUTHORITY\LocalService' DisplayName= 'Nicolò MES server' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Creazione del servizio non riuscita.' }
        & sc.exe description $ServiceName 'Gestionale Nicolò MES: API, piattaforma web e pagina tecnici.' | Out-Null
        Write-Ok 'Servizio "NicoloMES" creato (account LocalService, avvio automatico).'
    }
    else {
        & sc.exe config $ServiceName binPath= "`"$ApiExe`"" | Out-Null
        Write-Ok 'Servizio "NicoloMES" già presente: aggiornato il percorso del programma.'
    }

    # Restart after a crash: 5 s, 5 s, then 30 s; counter reset after a day.
    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
}

function Open-Firewall {
    Write-Step 'Firewall'
    Get-NetFirewallRule -DisplayName $FirewallRule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -DisplayName $FirewallRule -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow -Profile Domain, Private | Out-Null
    Write-Ok "Porta $Port aperta per la rete aziendale (profili Dominio e Privato, non Pubblico)."
}

function Update-Database {
    Write-Step 'Aggiornamento del database'
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
    }

    $output = & $ApiExe --migrate 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Aggiornamento del database non riuscito: $($output | Select-Object -Last 5 | Out-String)" }
    Write-Ok (($output | Where-Object { $_ -match 'Database aggiornato' }) -join ' ')
}

function Start-AndVerify {
    Write-Step 'Avvio'
    Start-Service -Name $ServiceName
    $healthy = $false
    for ($i = 0; $i -lt 30 -and -not $healthy; $i++) {
        Start-Sleep -Seconds 2
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:$Port/health" -UseBasicParsing -TimeoutSec 5
            $healthy = $response.StatusCode -eq 200
        }
        catch { }
    }

    if (-not $healthy) { throw "Il servizio non risponde su http://localhost:$Port/health. Guarda i log in $LogsDir." }
    Write-Ok 'Servizio attivo e database raggiungibile.'
}

function Register-Backup {
    Write-Step 'Backup notturno'
    $script = Join-Path $InstallDir 'server\Backup-NicoloMES.ps1'
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$script`""
    $trigger = New-ScheduledTaskTrigger -Daily -At '02:00'
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::FromHours(2))
    Register-ScheduledTask -TaskName $BackupTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    Write-Ok "Backup ogni notte alle 02:00 in $BackupDir, conservati 30 giorni (modificabile in server.json)."
}

# ---------------------------------------------------------------------------------------------------------

if (-not (Test-Administrator)) { throw 'Esegui come amministratore (tasto destro, "Esegui come amministratore").' }
if (-not (Test-Path $ApiExe)) { throw "Programma non trovato in $ApiExe." }

Write-Host 'Nicolò MES - configurazione del server' -ForegroundColor White
$firstRun = -not (Test-Path $ConfigPath)
if ($firstRun) {
    New-ServerConfiguration
}
else {
    Write-Ok "Configurazione esistente mantenuta: $ConfigPath"
    Protect-DataDirectory
}

Install-Service
Open-Firewall
Update-Database
Start-AndVerify
Register-Backup

$hostName = [Net.Dns]::GetHostName()
Write-Host ''
Write-Host 'Fatto.' -ForegroundColor Green
Write-Host "  Indirizzo da dare ai PC:          http://$($hostName):$Port/"
Write-Host "  Piattaforma web:                  http://$($hostName):$Port/app/"
Write-Host "  Pagina tecnici (telefono):        http://$($hostName):$Port/tecnici/"
Write-Host "  Log:                              $LogsDir"
Write-Host "  Backup:                           $BackupDir"
if ($firstRun) {
    Write-Host ''
    Write-Host '  Primo accesso: dal programma desktop o dalla piattaforma web crea l''amministratore.' -ForegroundColor Yellow
    Write-Host '  In rete aziendale la connessione è http; per accessi da fuori usa una VPN o un certificato (vedi LEGGIMI).' -ForegroundColor Yellow
}
