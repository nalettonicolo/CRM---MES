param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v?\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'CrmMes.Desktop\CrmMes.Desktop.csproj'
$output = Join-Path $root 'artifacts\desktop-release'
$asset = Join-Path $root 'artifacts\CrmMes.Desktop-win-x64.zip'
$tag = if ($Version.StartsWith('v')) { $Version } else { "v$Version" }

if (Test-Path $output) { Remove-Item $output -Recurse -Force }
if (Test-Path $asset) { Remove-Item $asset -Force }

Set-Location $root
dotnet publish $project -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $asset -Force

if (Get-Command gh -ErrorAction SilentlyContinue) {
    gh release create $tag $asset --title "Nicolò - MES $tag" --generate-notes
    Write-Output "Release pubblicata: $tag"
}
else {
    Write-Output "Pacchetto creato: $asset"
    Write-Output "Pubblicare manualmente questo asset nella release GitHub $tag."
}
