<#
.SYNOPSIS
    Chiamato dalla disinstallazione del server: ferma e rimuove servizio, regola del firewall e backup pianificato.

.DESCRIPTION
    NON cancella i dati: database PostgreSQL, configurazione, log e backup in C:\ProgramData\NicoloMES
    restano dove sono, così una reinstallazione riparte da dove si era. Per cancellarli davvero vanno
    rimossi a mano (database "nicolomes" e cartella C:\ProgramData\NicoloMES).
#>
$ErrorActionPreference = 'Continue'
$service = Get-Service -Name 'NicoloMES' -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') { Stop-Service -Name 'NicoloMES' -Force }
    & sc.exe delete NicoloMES | Out-Null
}

Get-NetFirewallRule -DisplayName 'Nicolò MES server' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Unregister-ScheduledTask -TaskName 'Nicolò MES - backup notturno' -Confirm:$false -ErrorAction SilentlyContinue
Write-Host 'Servizio, firewall e backup pianificato rimossi. I dati in C:\ProgramData\NicoloMES e il database restano.'
