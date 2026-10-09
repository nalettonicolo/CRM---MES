<#
.SINTESI
    Disaster recovery di produzione da uno snapshot Neon. In DUE passi
    deliberatamente separati: questo script prepara il ripristino in
    ANTEPRIMA (una copia, non ancora collegata) e NON sostituisce mai la
    produzione da solo. Il passo finale (`snapshots finalize`) lo lanci tu,
    a mano, solo dopo aver verificato i dati — è irreversibile.

.USO
    $env:NEON_API_KEY = "la-tua-chiave"
    .\scripts\ripristina-produzione-da-snapshot.ps1 -ListaSnapshot
    .\scripts\ripristina-produzione-da-snapshot.ps1 -SnapshotId snap-xxxxx

.NOTE
    La chiave API si crea su https://console.neon.tech/app/settings/api-keys.
    Vedi anche DISASTER-RECOVERY.md per la procedura completa, inclusa la
    verifica riga per riga già testata.
#>

param(
    [string]$ProjectId = "cool-field-94626300",
    [string]$ProductionBranchId = "br-snowy-salad-b1h3nlx7",
    [string]$SnapshotId,
    [switch]$ListaSnapshot
)

$ErrorActionPreference = "Stop"

if (-not $env:NEON_API_KEY) {
    Write-Error "Manca NEON_API_KEY. Impostala con: `$env:NEON_API_KEY = 'la-tua-chiave' (la crei su https://console.neon.tech/app/settings/api-keys)."
    exit 1
}

if ($ListaSnapshot -or -not $SnapshotId) {
    Write-Host "Snapshot disponibili per il progetto:" -ForegroundColor Cyan
    npx --yes neonctl snapshots list --project-id $ProjectId
    if (-not $SnapshotId) {
        Write-Host "`nRilancia con -SnapshotId <id> per preparare il ripristino di quello scelto." -ForegroundColor Yellow
        exit 0
    }
}

Write-Host "Preparo il ripristino di $SnapshotId in ANTEPRIMA (la produzione non viene toccata ora)..." -ForegroundColor Cyan
npx --yes neonctl snapshots restore $SnapshotId --project-id $ProjectId --target-branch $ProductionBranchId --name "ripristino-anteprima-$(Get-Date -Format yyyyMMdd-HHmm)"
if ($LASTEXITCODE -ne 0) { Write-Error "Preparazione del ripristino fallita."; exit 1 }

Write-Host "`nControllo subito quale branch risulta 'production' ora (vedi nota in DISASTER-RECOVERY.md: un ripristino" -ForegroundColor Yellow
Write-Host "passato va sempre verificato subito dopo, non solo letto dall'output del comando sopra)." -ForegroundColor Yellow
npx --yes neonctl branches list --project-id $ProjectId

Write-Host "`nSe nell'elenco sopra il branch 'production' è ancora quello di prima (stesso id $ProductionBranchId," -ForegroundColor Yellow
Write-Host "stessi dati), l'anteprima è isolata e la produzione non è stata toccata. PRIMA DI CONTINUARE:" -ForegroundColor Yellow
Write-Host "  1. Collegati con un client Postgres al branch dell'anteprima e controlla i dati."
Write-Host "  2. Solo quando sei sicuro, esegui il comando che sostituisce la produzione (irreversibile):"
Write-Host "       npx neonctl snapshots finalize $ProductionBranchId --project-id $ProjectId"
Write-Host "  Finché non lanci quel comando, la produzione resta quella di adesso." -ForegroundColor Green
