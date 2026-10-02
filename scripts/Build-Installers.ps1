param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$publish = Join-Path $root 'publish'
$publishServer = Join-Path $root 'publish-server'
$outDir = Join-Path $root 'installer-output'

foreach ($path in @($publish, $publishServer, $outDir)) {
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

Write-Host "Publish client → publish/"
dotnet publish (Join-Path $root 'CrmMes.Desktop\CrmMes.Desktop.csproj') `
    -c $Configuration -r win-x64 --self-contained false `
    -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "Publish client fallito." }

Write-Host "Publish server → publish-server/"
dotnet publish (Join-Path $root 'CrmMes.Api\CrmMes.Api.csproj') `
    -c $Configuration -r win-x64 --self-contained true `
    -p:Version=$Version -o $publishServer
if ($LASTEXITCODE -ne 0) { throw "Publish server fallito." }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "ISCC.exe non trovato. Installa Inno Setup 6+ (hai già il setup in Download: innosetup-7.1.0-x64.exe)."
}

Write-Host "Compilo suite NicoloMES.iss ($Version)"
& $iscc "/DMyAppVersion=$Version" (Join-Path $root 'installer\NicoloMES.iss')
if ($LASTEXITCODE -ne 0) { throw "Compilazione suite fallita." }

Write-Host "Compilo server-only (legacy, ancora utile)"
& $iscc "/DMyAppVersion=$Version" (Join-Path $root 'installer\NicoloMES-Server.iss')
if ($LASTEXITCODE -ne 0) { throw "Compilazione server fallita." }

Get-ChildItem $outDir | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
Write-Host "Fatto. Installer in $outDir"
