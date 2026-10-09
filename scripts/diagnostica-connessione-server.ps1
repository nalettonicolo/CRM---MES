<#
.SINTESI
    Diagnostica perché il programma desktop non raggiunge un server che il browser raggiunge
    senza problemi. Controlla, in ordine, le cause più comuni su Windows quando "il browser
    funziona ma l'app .NET no": proxy WinHTTP (diverso da quello del browser), firewall,
    blocco antivirus/SmartScreen sul file, e infine una richiesta HTTPS fatta esattamente come
    la fa il programma (HttpClient di .NET), per vedere il vero errore.

.USO
    .\scripts\diagnostica-connessione-server.ps1
    .\scripts\diagnostica-connessione-server.ps1 -Url "https://crmmes-api.onrender.com/health"
    .\scripts\diagnostica-connessione-server.ps1 -EseguibileClient "C:\...\CrmMes.Desktop.exe"
#>

param(
    [string]$Url = "https://crmmes-api.onrender.com/health",
    [string]$EseguibileClient
)

$ErrorActionPreference = "Continue"
$uri = [Uri]$Url

function Titolo($t) { Write-Host "`n=== $t ===" -ForegroundColor Cyan }

Titolo "1. Risoluzione DNS di $($uri.Host)"
try {
    $dns = Resolve-DnsName $uri.Host -ErrorAction Stop
    $dns | Select-Object -First 3 Name, IPAddress | Format-Table -AutoSize
    Write-Host "OK" -ForegroundColor Green
} catch {
    Write-Host "FALLITA: $($_.Exception.Message)" -ForegroundColor Red
}

Titolo "2. Connessione TCP alla porta 443"
$tcp = Test-NetConnection -ComputerName $uri.Host -Port 443 -WarningAction SilentlyContinue
if ($tcp.TcpTestSucceeded) {
    Write-Host "OK (instradata tramite $($tcp.SourceAddress.IPAddress))" -ForegroundColor Green
} else {
    Write-Host "FALLITA: il PC non riesce proprio ad aprire una connessione alla porta 443 verso quell'host." -ForegroundColor Red
}

Titolo "3. Proxy di sistema: WinHTTP (usato dalle app .NET) vs WinINet (usato dal browser)"
Write-Host "WinHTTP (netsh):"
netsh winhttp show proxy
Write-Host "`nWinINet (registro, quello che legge il browser/Internet Explorer):"
try {
    $inet = Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings" -ErrorAction Stop
    "  ProxyEnable: $($inet.ProxyEnable)"
    if ($inet.ProxyServer) { "  ProxyServer: $($inet.ProxyServer)" }
} catch {
    "  Non leggibile."
}
Write-Host "`nSe WinHTTP indica un proxy diverso da WinINet (o uno dei due vuoto e l'altro no), è la causa più probabile:" -ForegroundColor Yellow
Write-Host "il browser e il programma .NET passano da strade diverse per uscire su internet." -ForegroundColor Yellow

Titolo "4. Firewall di Windows: regole che citano Nicolo MES o CrmMes"
$rules = Get-NetFirewallRule | Where-Object { $_.DisplayName -match "Nicol|CrmMes" }
if ($rules) {
    $rules | Select-Object DisplayName, Direction, Action, Enabled | Format-Table -AutoSize
} else {
    Write-Host "Nessuna regola dedicata trovata (normale se non è mai stata bloccata esplicitamente)." -ForegroundColor Gray
}

if ($EseguibileClient -and (Test-Path $EseguibileClient)) {
    Titolo "5. Il file del programma ha il blocco 'scaricato da internet' (Mark of the Web)?"
    $zone = Get-Item -Path $EseguibileClient -Stream Zone.Identifier -ErrorAction SilentlyContinue
    if ($zone) {
        Write-Host "SÌ: il file porta il blocco di Windows per i file scaricati. Può bastare da solo per far" -ForegroundColor Yellow
        Write-Host "intervenire SmartScreen/l'antivirus sulle sue connessioni di rete. Per toglierlo:" -ForegroundColor Yellow
        Write-Host "  Unblock-File -Path `"$EseguibileClient`"" -ForegroundColor Yellow
    } else {
        Write-Host "No, il file non ha quel blocco." -ForegroundColor Green
    }
}

Titolo "6. Richiesta HTTPS fatta come la fa il programma (HttpClient di .NET)"
Add-Type -AssemblyName System.Net.Http
try {
    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds(15)
    $resp = $client.GetAsync($Url).GetAwaiter().GetResult()
    $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Write-Host "OK: HTTP $([int]$resp.StatusCode) — $body" -ForegroundColor Green
} catch {
    Write-Host "FALLITA — questo è il vero errore che probabilmente vede anche il programma:" -ForegroundColor Red
    $ex = $_.Exception
    while ($ex) {
        Write-Host "  $($ex.GetType().Name): $($ex.Message)" -ForegroundColor Red
        $ex = $ex.InnerException
    }
}

Write-Host "`nFatto. Copia tutto questo output se devi chiedere aiuto." -ForegroundColor Cyan

