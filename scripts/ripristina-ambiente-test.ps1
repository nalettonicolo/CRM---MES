<#
.SINTESI
    Riporta il branch Neon di prova (default: ui-verification-temp) allo stato
    ATTUALE del branch di produzione (il suo genitore), poi applica le
    migrazioni di CrmMes.Api. Nessun rischio per la produzione: il branch di
    produzione non viene mai toccato da questo script, solo letto.

.USO
    $env:NEON_API_KEY = "la-tua-chiave"   # una volta per sessione di PowerShell
    .\scripts\ripristina-ambiente-test.ps1

.NOTE
    La chiave API si crea su https://console.neon.tech/app/settings/api-keys.
    Non va mai scritta in un file del repository: solo come variabile
    d'ambiente della tua sessione.
#>

param(
    [string]$ProjectId = "cool-field-94626300",
    [string]$BranchId = "br-jolly-unit-b1b8nnc5",   # ui-verification-temp
    [switch]$SkipMigrations
)

$ErrorActionPreference = "Stop"

if (-not $env:NEON_API_KEY) {
    Write-Error "Manca NEON_API_KEY. Impostala con: `$env:NEON_API_KEY = 'la-tua-chiave' (la crei su https://console.neon.tech/app/settings/api-keys)."
    exit 1
}

Write-Host "Riporto il branch $BranchId allo stato attuale della produzione..." -ForegroundColor Cyan
npx --yes neonctl branches reset $BranchId --project-id $ProjectId --parent
if ($LASTEXITCODE -ne 0) { Write-Error "Reset del branch fallito."; exit 1 }

if (-not $SkipMigrations) {
    Write-Host "Applico le migrazioni di CrmMes.Api sul branch appena resettato..." -ForegroundColor Cyan
    Push-Location (Join-Path $PSScriptRoot "..\CrmMes.Api")
    try {
        $env:ASPNETCORE_ENVIRONMENT = "Development"
        dotnet ef database update
        if ($LASTEXITCODE -ne 0) { Write-Error "Migrazioni fallite: controlla che ConnectionStrings:DefaultConnection (dotnet user-secrets) punti a questo branch." }
    }
    finally {
        Pop-Location
    }
}

Write-Host "Ambiente di prova pronto." -ForegroundColor Green
