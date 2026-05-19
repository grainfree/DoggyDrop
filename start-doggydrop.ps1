$ErrorActionPreference = "Stop"

$projectRoot = "C:\Users\Jernej\Desktop\Faks\Diploma\Informatika DoggyDrop\app\DoggyDrop\DoggyDrop"
$projectFile = Join-Path $projectRoot "DoggyDrop.csproj"
$appUrl = "http://localhost:5177"
$healthUrl = "$appUrl/health"
$logOut = "C:\Users\Jernej\Desktop\Faks\Diploma\Informatika DoggyDrop\app\DoggyDrop\doggydrop-run.log"
$logErr = "C:\Users\Jernej\Desktop\Faks\Diploma\Informatika DoggyDrop\app\DoggyDrop\doggydrop-run.err.log"

Write-Host "DoggyDrop se zaganja ..." -ForegroundColor Green

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

if ($ready) {
    Write-Host "DoggyDrop je pripravljen. Odpiram browser ..." -ForegroundColor Green
    Start-Process $appUrl
    Write-Host ""
    Write-Host "DoggyDrop tece na $appUrl" -ForegroundColor Cyan
    Write-Host "Ce ga hoces ustaviti, zapri DoggyDrop / dotnet proces v Task Managerju." -ForegroundColor DarkGray
    exit 0
}

Write-Host ""
Write-Host "DoggyDrop se ni uspel zagnati." -ForegroundColor Red
if (Test-Path $logErr) {
    Write-Host ""
    Write-Host "Napaka iz loga:" -ForegroundColor Yellow
    Get-Content $logErr -Tail 40
}
elseif (Test-Path $logOut) {
    Write-Host ""
    Write-Host "Zadnje vrstice iz loga:" -ForegroundColor Yellow
    Get-Content $logOut -Tail 40
}
else {
    Write-Host "Ni log datoteke, preveri ali je dotnet namescen." -ForegroundColor Yellow
}

exit 1
