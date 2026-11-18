# Build script for Windows (PowerShell).  Usage:
#   .\build.ps1              -> run tests, then publish a self-contained win-x64 build to .\publish
#   .\build.ps1 -SkipTests   -> publish only
#   .\build.ps1 -Sln         -> also create SmartWater.sln (to open the whole project in Visual Studio)
param([switch]$SkipTests, [switch]$Sln)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Fail($msg) { Write-Host ""; Write-Host "ERROR: $msg" -ForegroundColor Red; exit 1 }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "The .NET 8 SDK is not installed. Opening the download page..." -ForegroundColor Yellow
    Start-Process "https://dotnet.microsoft.com/download/dotnet/8.0"
    Fail "Install '.NET SDK 8.0.x' (x64), then run this script again."
}
$ver = (dotnet --version)
Write-Host "Using .NET SDK $ver"
if ([int]($ver.Split('.')[0]) -lt 8) { Fail "SDK 8 or newer is required (found $ver)." }

if ($Sln) {
    if (-not (Test-Path "SmartWater.sln")) { dotnet new sln -n SmartWater | Out-Null }
    $projects = Get-ChildItem -Recurse -Filter *.csproj | ForEach-Object { $_.FullName }
    dotnet sln SmartWater.sln add $projects | Out-Null
    Write-Host "Created SmartWater.sln"
}

if (-not $SkipTests) {
    Write-Host "`n[1/2] Running tests (about 1-2 minutes; some tests run in real time)..." -ForegroundColor Cyan
    dotnet test tests/SmartWater.Tests/SmartWater.Tests.csproj -c Release --logger "trx;LogFileName=results.trx"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "`nSome tests FAILED. Results file: tests\SmartWater.Tests\TestResults\results.trx" -ForegroundColor Yellow
        $a = Read-Host "Continue and build the app anyway? (y/N)"
        if ($a -ne 'y' -and $a -ne 'Y') { exit 1 }
    }
}

Write-Host "`n[2/2] Building the app (self-contained, win-x64)..." -ForegroundColor Cyan
if (Test-Path publish) { Remove-Item publish -Recurse -Force }
dotnet publish src/SmartWater.App/SmartWater.App.csproj -c Release -r win-x64 --self-contained true -o publish
if ($LASTEXITCODE -ne 0) { Fail "Build failed. Copy the first 'error' lines above and send them." }

$exe = Join-Path $PSScriptRoot "publish\SmartWaterMonitor.exe"
if (-not (Test-Path $exe)) { Fail "Build finished but SmartWaterMonitor.exe was not found." }
Write-Host "`nDONE: $exe" -ForegroundColor Green
Write-Host "Copy the whole 'publish' folder to the target PC and run SmartWaterMonitor.exe"
