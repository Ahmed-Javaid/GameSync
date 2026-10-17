param([switch]$Clean, [switch]$NoBuild)

# Tries GameSync's first run without touching your own GameSync: builds a test copy into artifacts\first-run\app and starts
# it on a new, empty data folder, so first run starts from its first step. Its scan only reads your games folders; it
# changes nothing that starts with Windows (first run's switches act only on GameSync's usual data folder); and it shows
# as a second GameSync icon by the clock, beside your own, which keeps running. For where saves are kept, pick Only on
# this PC, never your Google Drive: this is a test copy. Each run starts a fresh one; -Clean quits every test copy and
# deletes their folders. tools\dev\shortcuts.ps1 puts it on the desktop and in the Start menu.
#   powershell -ExecutionPolicy Bypass -File tools\dev\try-first-run.ps1 [-Clean] [-NoBuild]
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$base = Join-Path $root 'artifacts\first-run'
$app = Join-Path $base 'app'
$tray = Join-Path $app 'GameSync.Tray.exe'
$cli = Join-Path $app 'gamesync.exe'

function TestCopies { if (Test-Path $base) { Get-ChildItem $base -Directory -Filter 'data-*' } }

if ($Clean) {
    foreach ($folder in @(TestCopies)) {
        if (Test-Path $cli) { & $cli --data $folder.FullName quit 2>$null | Out-Null }
    }

    Start-Sleep -Seconds 2
    foreach ($folder in @(TestCopies)) {
        try {
            Remove-Item -LiteralPath $folder.FullName -Recurse -Force
            Write-Host "Deleted $($folder.FullName)"
        }
        catch {
            Write-Host "$($folder.Name) is still in use: quit that test copy from its tray icon and run this again." -ForegroundColor Yellow
        }
    }

    Write-Host 'Test copies cleaned up. Your own GameSync and its saves were not touched.'
    Start-Sleep -Seconds 3
    exit 0
}

if (-not $NoBuild) {
    Write-Host 'Building a test copy of GameSync...'
    & dotnet build (Join-Path $root 'src\GameSync.App') -o $app -nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'The build failed (above).' -ForegroundColor Yellow
        Read-Host 'Press Enter to close'
        exit 1
    }
}

if (-not (Test-Path $tray)) {
    Write-Host "There's no test copy to start yet: $tray" -ForegroundColor Yellow
    Read-Host 'Press Enter to close'
    exit 1
}

# A new, empty data folder, with the save list your own GameSync already downloaded, so the scan needn't fetch it.
$data = Join-Path $base ('data-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $data | Out-Null
$own = Join-Path $env:LOCALAPPDATA 'GameSync'
foreach ($file in 'savelist.json.gz', 'savelist.etag') {
    $from = Join-Path $own $file
    if (Test-Path $from) { Copy-Item -LiteralPath $from -Destination $data }
}

Write-Host ''
Write-Host 'Opening first run in a test copy of GameSync.' -ForegroundColor Cyan
Write-Host "  Its data folder: $data"
Write-Host '  Where saves are kept: pick Only on this PC, never your Google Drive.'
Write-Host '  It changes nothing that starts with Windows, and your own GameSync keeps running.'
Write-Host '  When you are done, quit it from its tray icon (the second GameSync icon by the clock),'
Write-Host '  and run this with -Clean to delete the test folders.'
Start-Process -FilePath $tray -ArgumentList @('--data', ('"' + $data + '"')) -WorkingDirectory $app
Start-Sleep -Seconds 6
