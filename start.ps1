# Starts everything the app needs for local development and the demo:
# MariaDB, Ollama (optional) and the Tracker web app.
# Usage: powershell -ExecutionPolicy Bypass -File start.ps1 [-NoBrowser]
param(
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root = $PSScriptRoot
$appUrl = 'http://localhost:5011'
$dataDir = Join-Path $env:LOCALAPPDATA 'TrackerMariaDB\data'

function Test-Port([int]$port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $connect = $client.BeginConnect('127.0.0.1', $port, $null, $null)
        return $connect.AsyncWaitHandle.WaitOne(300) -and $client.Connected
    }
    catch { return $false }
    finally { $client.Close() }
}

function Wait-Port([int]$port, [int]$seconds) {
    for ($i = 0; $i -lt $seconds * 2; $i++) {
        if (Test-Port $port) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

# 1. MariaDB
if (Test-Port 3306) {
    Write-Host '✓ MariaDB вече работи (порт 3306)'
}
else {
    $mariadbd = Get-ChildItem 'C:\Program Files\MariaDB*\bin\mariadbd.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1

    if (-not $mariadbd) {
        Write-Host '✗ MariaDB не е инсталирана. Вижте „Пускане“ в README.md.' -ForegroundColor Red
        exit 1
    }

    if (-not (Test-Path (Join-Path $dataDir 'my.ini'))) {
        Write-Host "✗ Няма база в $dataDir. Вижте „Пускане“ в README.md." -ForegroundColor Red
        exit 1
    }

    Write-Host '… Стартирам MariaDB'
    Start-Process $mariadbd.FullName -WindowStyle Hidden -ArgumentList "--defaults-file=`"$dataDir\my.ini`""

    if (-not (Wait-Port 3306 30)) {
        Write-Host "✗ MariaDB не тръгна за 30 s. Логът е в $dataDir\*.err" -ForegroundColor Red
        exit 1
    }

    Write-Host '✓ MariaDB работи'
}

# 2. Ollama (optional: search works without it, only free-text queries need it)
if (Test-Port 11434) {
    Write-Host '✓ Ollama вече работи (порт 11434)'
}
else {
    $ollama = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'

    if (Test-Path $ollama) {
        Write-Host '… Стартирам Ollama'
        Start-Process $ollama -WindowStyle Hidden -ArgumentList 'serve'

        if (Wait-Port 11434 20) {
            Write-Host '✓ Ollama работи'
        }
        else {
            Write-Host '! Ollama не тръгна. Търсенето ще работи без ИИ.' -ForegroundColor Yellow
        }
    }
    else {
        Write-Host '! Ollama не е инсталиран. Търсенето ще работи без ИИ.' -ForegroundColor Yellow
    }
}

# 3. The app
if (Test-Port 5011) {
    Write-Host "✓ Приложението вече работи: $appUrl"
    if (-not $NoBrowser) { Start-Process $appUrl }
    exit 0
}

if (-not $NoBrowser) {
    # Open the browser once the app answers, without blocking `dotnet run`.
    Start-Job -ArgumentList $appUrl {
        param($url)
        for ($i = 0; $i -lt 120; $i++) {
            try { Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 2 | Out-Null; Start-Process $url; return }
            catch { Start-Sleep -Milliseconds 500 }
        }
    } | Out-Null
}

Write-Host "… Стартирам приложението на $appUrl (Ctrl+C за спиране)"
dotnet run --project (Join-Path $root 'Tracker') --launch-profile http
