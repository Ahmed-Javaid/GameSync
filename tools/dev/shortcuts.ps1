# Puts two shortcuts on the desktop and in the Start menu, for the GameSync built from this repo: "GameSync" opens it (or
# brings its window forward when it's already running), and "Rebuild GameSync" runs tools\dev\run.ps1: quits it, builds
# this repo and opens the new one. Run it again after moving the repo.
#   powershell -ExecutionPolicy Bypass -File tools\dev\shortcuts.ps1
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$tray = Join-Path $root 'src\GameSync.Tray\bin\Debug\net10.0-windows10.0.19041.0\GameSync.Tray.exe'
$icon = Join-Path $root 'src\GameSync.Tray\Assets\gamesync.ico'
$shell = New-Object -ComObject WScript.Shell
foreach ($folder in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
    $open = $shell.CreateShortcut((Join-Path $folder 'GameSync.lnk'))
    $open.TargetPath = $tray
    $open.WorkingDirectory = Split-Path $tray
    $open.IconLocation = "$icon,0"
    $open.Description = 'Opens GameSync, built from ' + $root
    $open.Save()

    $rebuild = $shell.CreateShortcut((Join-Path $folder 'Rebuild GameSync.lnk'))
    $rebuild.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $rebuild.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $root 'tools\dev\run.ps1') + '"'
    $rebuild.WorkingDirectory = $root
    $rebuild.IconLocation = "$icon,0"
    $rebuild.Description = 'Quits GameSync, builds it from ' + $root + ' and opens the new one'
    $rebuild.Save()
    Write-Host "Shortcuts made in $folder"
}
