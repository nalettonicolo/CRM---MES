<#
.SYNOPSIS
    Backup del database di Nicolò MES sul server del cliente (pianificato ogni notte dall'installazione).

.DESCRIPTION
    Legge C:\ProgramData\NicoloMES\server.json, esegue pg_dump in formato compresso, verifica che il file sia
    leggibile (pg_restore --list), copia il backup in una seconda posizione se configurata (Backup:CopyTo,
    ad esempio un NAS) e cancella i backup più vecchi di Backup:RetentionDays giorni. Scrive l'esito in
    logs\backup.log. Esce con codice 1 in caso di errore, così l'Utilità di pianificazione lo segnala.
#>
[CmdletBinding()]
param([string]$ConfigPath = (Join-Path $env:ProgramData 'NicoloMES\server.json'))

$ErrorActionPreference = 'Stop'
$logDir = Join-Path $env:ProgramData 'NicoloMES\logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logFile = Join-Path $logDir 'backup.log'

function Write-Log([string]$Text) {
    $line = "{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $Text
    Add-Content -Path $logFile -Value $line -Encoding UTF8
    Write-Host $line
}

function ConvertFrom-ConnectionString([string]$Text) {
    $parts = @{}
    foreach ($pair in $Text.Split(';')) {
        if ($pair -match '^\s*([^=]+?)\s*=\s*(.*)$') { $parts[$Matches[1].ToLowerInvariant().Replace(' ', '')] = $Matches[2].Trim() }
    }
    return $parts
}

function Find-PostgresBin {
    $fromPath = Get-Command pg_dump.exe -ErrorAction SilentlyContinue
    if ($fromPath) { return Split-Path -Parent $fromPath.Source }
    $candidates = Get-ChildItem -Path (Join-Path $env:ProgramFiles 'PostgreSQL') -Directory -ErrorAction SilentlyContinue |
        Sort-Object { [int]($_.Name -replace '\D', '0') } -Descending
    foreach ($candidate in $candidates) {
        $bin = Join-Path $candidate.FullName 'bin'
        if (Test-Path (Join-Path $bin 'pg_dump.exe')) { return $bin }
    }
    return $null
}

try {
    $config = Get-Content -Raw -Path $ConfigPath | ConvertFrom-Json
    $db = ConvertFrom-ConnectionString $config.ConnectionStrings.DefaultConnection
    $backupDir = if ($config.Backup -and $config.Backup.Path) { $config.Backup.Path } else { Join-Path $env:ProgramData 'NicoloMES\backup' }
    $retention = if ($config.Backup -and $config.Backup.RetentionDays) { [int]$config.Backup.RetentionDays } else { 30 }
    $copyTo = if ($config.Backup -and $config.Backup.CopyTo) { [string]$config.Backup.CopyTo } else { '' }

    $bin = Find-PostgresBin
    if (-not $bin) { throw 'pg_dump non trovato: installa gli strumenti client di PostgreSQL su questo server.' }

    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    $file = Join-Path $backupDir ("nicolomes-{0:yyyyMMdd-HHmmss}.dump" -f (Get-Date))

    $env:PGHOST = $db['host']
    $env:PGPORT = if ($db['port']) { $db['port'] } else { '5432' }
    $env:PGDATABASE = $db['database']
    $env:PGUSER = if ($db['username']) { $db['username'] } else { $db['userid'] }
    $env:PGPASSWORD = $db['password']
    if ($db['sslmode']) { $env:PGSSLMODE = $db['sslmode'].ToLowerInvariant() }

    & (Join-Path $bin 'pg_dump.exe') --format=custom --no-owner --file $file
    if ($LASTEXITCODE -ne 0) { throw "pg_dump non riuscito (codice $LASTEXITCODE)." }

    $list = & (Join-Path $bin 'pg_restore.exe') --list $file
    if ($LASTEXITCODE -ne 0 -or -not ($list | Select-String -Pattern 'TABLE DATA')) { throw 'Il file di backup non è leggibile o è vuoto.' }

    $size = [Math]::Round((Get-Item $file).Length / 1MB, 1)
    Write-Log "OK backup $file ($size MB)"

    if ($copyTo) {
        New-Item -ItemType Directory -Force -Path $copyTo | Out-Null
        Copy-Item -Path $file -Destination $copyTo -Force
        Write-Log "OK copia in $copyTo"
    }

    foreach ($folder in @($backupDir, $copyTo) | Where-Object { $_ }) {
        Get-ChildItem -Path $folder -Filter 'nicolomes-*.dump' -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-$retention) } |
            ForEach-Object { Remove-Item $_.FullName -Force; Write-Log "Eliminato backup scaduto $($_.Name)" }
    }
}
catch {
    Write-Log "ERRORE $($_.Exception.Message)"
    exit 1
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}
