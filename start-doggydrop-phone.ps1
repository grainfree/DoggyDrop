$ErrorActionPreference = "Stop"

$projectRoot = "C:\Users\Jernej\Desktop\Faks\Diploma\Informatika DoggyDrop\app\DoggyDrop\DoggyDrop"
$appPort = 5177
$appUrl = "http://0.0.0.0:$appPort"
$healthUrl = "http://localhost:$appPort/health"
$logOut = "C:\Users\Jernej\Desktop\Faks\Diploma\Informatika DoggyDrop\app\DoggyDrop\doggydrop-phone-run.log"
$logErr = "C:\Users\Jernej\Desktop\Faks\Diploma\Informatika DoggyDrop\app\DoggyDrop\doggydrop-phone-run.err.log"

Write-Host "DoggyDrop phone mode se zaganja ..." -ForegroundColor Green

$existing = Get-Process DoggyDrop, dotnet -ErrorAction SilentlyContinue | Where-Object {
    ($_.Path -like "*Informatika DoggyDrop*") -or $_.ProcessName -eq "DoggyDrop"
}

if ($existing) {
    Write-Host "Zapiram obstojece DoggyDrop procese ..." -ForegroundColor Yellow
    $existing | Stop-Process -Force
    Start-Sleep -Seconds 2
}

if (Test-Path $logOut) { Remove-Item $logOut -Force }
if (Test-Path $logErr) { Remove-Item $logErr -Force }

$process = Start-Process -FilePath "dotnet" `
    -WorkingDirectory $projectRoot `
    -ArgumentList @("run", "--project", "DoggyDrop.csproj", "--urls", $appUrl) `
    -RedirectStandardOutput $logOut `
    -RedirectStandardError $logErr `
    -PassThru

Write-Host "Cakam, da se app pripravi ..." -ForegroundColor Cyan

$ready = $false
for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Seconds 1

    if ($process.HasExited) {
        break
    }

    try {
        $response = Invoke-WebRequest $healthUrl -UseBasicParsing -TimeoutSec 2
        if ($response.StatusCode -eq 200) {
            $ready = $true
            break
        }
    }
    catch {
    }
}

if (-not $ready) {
    try {
        $fallbackResponse = Invoke-WebRequest $healthUrl -UseBasicParsing -TimeoutSec 5
        if ($fallbackResponse.StatusCode -eq 200) {
            $ready = $true
        }
    }
    catch {
    }
}

if (-not $ready) {
    Write-Host ""
    Write-Host "DoggyDrop se ni uspel zagnati." -ForegroundColor Red
    if (Test-Path $logErr) {
        Write-Host ""
        Write-Host "Napaka iz loga:" -ForegroundColor Yellow
        Get-Content $logErr -Tail 40
    }
    exit 1
}

$localIps = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object {
        $_.IPAddress -notlike "127.*" -and
        $_.IPAddress -notlike "169.254.*" -and
        $_.PrefixOrigin -ne "WellKnown"
    } |
    Select-Object -ExpandProperty IPAddress -Unique

Write-Host ""
Write-Host "DoggyDrop tece." -ForegroundColor Green
Write-Host "Na racunalniku odpri: http://localhost:$appPort" -ForegroundColor Cyan

if ($localIps) {
    Write-Host ""
    Write-Host "Na telefonu odpri enega od teh naslovov:" -ForegroundColor Cyan
    foreach ($ip in $localIps) {
        Write-Host "http://$ip`:$appPort" -ForegroundColor Yellow
    }
}
else {
    Write-Host "Lokalnega IP-ja nisem uspel samodejno najti. Pozeni ipconfig in uporabi IPv4 naslov." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Telefon in racunalnik morata biti na istem Wi-Fi." -ForegroundColor DarkGray
Write-Host "Ce telefon ne odpre strani, preveri Windows Firewall." -ForegroundColor DarkGray

Start-Process "http://localhost:$appPort"
Write-Host ""
Write-Host "Pritisni Enter za zaprtje tega okna." -ForegroundColor DarkGray
[void][System.Console]::ReadLine()
