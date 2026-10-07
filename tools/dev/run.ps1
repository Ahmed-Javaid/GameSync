param([switch]$NoBuild)

# Rebuilds GameSync from this repo and opens it, for trying a change in one step: quits the GameSync running from the build
# output (as its tray menu's Quit does: an open session is recorded first), builds the solution, and starts the new
# GameSync.Tray.exe with its window open. If the build fails, the errors stay on screen and the last good build starts
# again, so GameSync keeps backing up. tools\dev\shortcuts.ps1 puts it on the desktop and in the Start menu.
#   powershell -ExecutionPolicy Bypass -File tools\dev\run.ps1 [-NoBuild]
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$framework = 'net10.0-windows10.0.19041.0'
$tray = Join-Path $root "src\GameSync.Tray\bin\Debug\$framework\GameSync.Tray.exe"
$cli = Join-Path $root "src\GameSync.App\bin\Debug\$framework\gamesync.exe"
# The command line that asks the app to quit is built away from the build output, which the running app locks (building
# gamesync.exe builds GameSync.Tray.exe beside it).
$quitter = Join-Path $env:TEMP 'GameSync-dev\gamesync.exe'
$failed = $false

function Running { Get-Process -Name 'GameSync.Tray' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $tray } }

if (-not $NoBuild) {
    Write-Host 'Building the command line...'
    & dotnet build (Join-Path $root 'src\GameSync.App') -o (Split-Path $quitter) -nologo -v q
    if ($LASTEXITCODE -ne 0) { $failed = $true }
}

if (Running) {
    Write-Host 'Closing GameSync...'
    foreach ($exe in @($quitter, $cli)) {
        if (Test-Path $exe) {
            & $exe quit
            if ($LASTEXITCODE -eq 0) { break }
        }
    }
    $deadline = (Get-Date).AddSeconds(60)
    while ((Running) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
    if (Running) {
        Write-Host 'GameSync is still running, so it was not rebuilt. Quit it from its tray icon and run this again.' -ForegroundColor Yellow
        Read-Host 'Press Enter to close'
        exit 1
    }
}

if (-not $NoBuild -and -not $failed) {
    Write-Host 'Building GameSync...'
    & dotnet build (Join-Path $root 'GameSync.sln') -nologo -v q
    if ($LASTEXITCODE -ne 0) { $failed = $true }
}

if (-not (Test-Path $tray)) {
    Write-Host "There's no GameSync.Tray.exe to start yet: $tray" -ForegroundColor Yellow
    Read-Host 'Press Enter to close'
    exit 1
}

Write-Host 'Opening GameSync...'
Start-Process -FilePath $tray -WorkingDirectory (Split-Path $tray)
if ($failed) {
    Write-Host 'The build failed (above), so this is the last good build.' -ForegroundColor Yellow
    Read-Host 'Press Enter to close'
    exit 1
}

Start-Sleep -Seconds 2
