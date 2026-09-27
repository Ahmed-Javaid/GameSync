<#
Restores a GameSync version from a downloaded copy of the GameSync folder, without GameSync.

  .\restore.ps1                                     lists the games
  .\restore.ps1 -Game terraria                      lists terraria's versions, newest first
  .\restore.ps1 -Game terraria -Version latest -To C:\Restored\Terraria
                                                    writes one version's files into an empty folder

It only reads this folder and writes into the folder you name. Every file is checked against its
hash and gets back the date the game gave it. Program files and unsafe paths are skipped.
#>
param(
    [string]$Game,
    [string]$Version,
    [string]$To
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$gamesFolder = Join-Path $PSScriptRoot 'games'
$blocked = @('.exe', '.dll', '.sys', '.scr', '.com', '.bat', '.cmd', '.ps1', '.psm1', '.vbs', '.vbe', '.js', '.jse',
    '.wsf', '.wsh', '.hta', '.msi', '.msp', '.lnk', '.url', '.reg', '.cpl', '.jar', '.pif', '.scf', '.ocx', '.drv',
    '.appx', '.msix', '.application', '.gadget', '.inf')
$reserved = @('CON', 'PRN', 'AUX', 'NUL', 'CONIN$', 'CONOUT$', 'COM0', 'COM1', 'COM2', 'COM3', 'COM4', 'COM5', 'COM6',
    'COM7', 'COM8', 'COM9', 'LPT0', 'LPT1', 'LPT2', 'LPT3', 'LPT4', 'LPT5', 'LPT6', 'LPT7', 'LPT8', 'LPT9')

function ConvertTo-Utc($value) {
    if ($value -is [DateTime]) { return $value.ToUniversalTime() }
    return [DateTime]::Parse([string]$value, [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]'AdjustToUniversal, AssumeUniversal')
}

function Get-GameFolders {
    if (-not (Test-Path -LiteralPath $gamesFolder)) {
        throw "There's no games folder next to this script. Run it from the top of the GameSync folder."
    }
    return @(Get-ChildItem -LiteralPath $gamesFolder -Directory)
}

function Get-GameId($folder) {
    return $folder.Name.Split(' ')[0]
}

function Find-GameFolder([string]$id) {
    $match = @(Get-GameFolders | Where-Object { (Get-GameId $_) -eq $id })
    if ($match.Count -eq 0) { throw "There's no game '$id' here. Run .\restore.ps1 to list the games." }
    return $match[0].FullName
}

function Get-Versions([string]$folder) {
    $thinned = @{}
    $thinnedFolder = Join-Path $folder 'thinned'
    if (Test-Path -LiteralPath $thinnedFolder) {
        foreach ($mark in Get-ChildItem -LiteralPath $thinnedFolder -File) { $thinned[$mark.Name] = $true }
    }
    $versionsFolder = Join-Path $folder 'versions'
    if (-not (Test-Path -LiteralPath $versionsFolder)) { return @() }
    $list = @()
    foreach ($file in Get-ChildItem -LiteralPath $versionsFolder -Filter '*.json' -File) {
        try { $v = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json } catch { continue }
        if ($v.id -ne $file.BaseName -or $thinned.ContainsKey($v.id)) { continue }
        $list += $v
    }
    return @($list | Sort-Object { ConvertTo-Utc $_.createdUtc } -Descending)
}

function Test-SafePath([string]$path) {
    if ([string]::IsNullOrEmpty($path) -or $path.Length -gt 1024 -or $path.Contains('\') -or $path.Contains(':')) { return $false }
    $parts = $path.Split('/')
    if ($parts.Count -lt 2) { return $false }
    foreach ($part in $parts) {
        if ($part -eq '' -or $part -eq '.' -or $part -eq '..') { return $false }
        if ($part.EndsWith('.') -or $part.EndsWith(' ')) { return $false }
        if ($part.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) { return $false }
        if ($reserved -contains $part.Split('.')[0].TrimEnd(' ').ToUpperInvariant()) { return $false }
    }
    return $true
}

function Find-Blob([string]$folder, [string]$hash) {
    if ($hash -cnotmatch '^[0-9a-f]{64}$') { return $null }
    $flat = Join-Path $folder "blobs\$hash.gz"
    $nested = Join-Path $folder ('blobs\' + $hash.Substring(0, 2) + "\$hash.gz")
    foreach ($candidate in @($flat, $nested)) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

function Expand-Blob([string]$blob, [string]$target, [string]$hash) {
    $source = [IO.File]::OpenRead($blob)
    try {
        $gzip = New-Object IO.Compression.GZipStream($source, [IO.Compression.CompressionMode]::Decompress)
        $output = [IO.File]::Open($target, [IO.FileMode]::CreateNew)
        try { $gzip.CopyTo($output) } finally { $output.Dispose(); $gzip.Dispose() }
    } finally {
        $source.Dispose()
    }
    $actual = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $hash) {
        Remove-Item -LiteralPath $target
        throw "A stored file is damaged (it doesn't match its hash): $target"
    }
}

function Format-Size([long]$bytes) {
    if ($bytes -lt 1KB) { return "$bytes B" }
    if ($bytes -lt 1MB) { return ('{0:0.#} KB' -f ($bytes / 1KB)) }
    if ($bytes -lt 1GB) { return ('{0:0.#} MB' -f ($bytes / 1MB)) }
    return ('{0:0.##} GB' -f ($bytes / 1GB))
}

if (-not $Game) {
    foreach ($folder in Get-GameFolders) {
        $id = Get-GameId $folder
        $count = @(Get-Versions $folder.FullName).Count
        '{0,-32} {1,5} versions  {2}' -f $id, $count, $folder.Name.Substring($id.Length).Trim()
    }
    return
}

$gameFolder = Find-GameFolder $Game
$versions = @(Get-Versions $gameFolder)

if (-not $Version) {
    foreach ($v in $versions) {
        $size = [long]0
        foreach ($f in @($v.files)) { $size += [long]$f.size }
        $note = @()
        if ($v.kind -ne 'Normal') { $note += ([string]$v.kind).ToLowerInvariant() }
        if ($v.label) { $note += $v.label }
        '{0,-44} {1:yyyy-MM-dd HH:mm}  {2,-12} {3,5} files {4,10}  {5}' -f $v.id, (ConvertTo-Utc $v.createdUtc).ToLocalTime(),
            $v.device.name, @($v.files).Count, (Format-Size $size), ($note -join ', ')
    }
    return
}

if (-not $To) { throw 'Say where to put the files: -To <an empty or new folder>.' }

if ($Version -eq 'latest') {
    $chosen = @($versions | Where-Object { $_.kind -eq 'Normal' }) | Select-Object -First 1
} else {
    $chosen = @($versions | Where-Object { $_.id -eq $Version }) | Select-Object -First 1
}
if (-not $chosen) { throw "There's no version '$Version' for $Game. Run .\restore.ps1 -Game $Game to list them." }

$target = [IO.Path]::GetFullPath($To)
if ((Test-Path -LiteralPath $target) -and @(Get-ChildItem -LiteralPath $target -Force).Count -gt 0) {
    throw "$target isn't empty. Name an empty or new folder."
}
New-Item -ItemType Directory -Force -Path $target | Out-Null

$written = 0
$missing = 0
$skipped = 0
foreach ($file in @($chosen.files)) {
    $path = [string]$file.path
    if (-not (Test-SafePath $path)) { Write-Warning "Skipped an unsafe path: $path"; $skipped++; continue }
    if ($blocked -contains [IO.Path]::GetExtension($path).ToLowerInvariant()) { Write-Warning "Skipped a program file: $path"; $skipped++; continue }
    $blob = Find-Blob $gameFolder ([string]$file.hash)
    if (-not $blob) { Write-Warning "Not in this copy of the folder: $path"; $missing++; continue }
    $destination = Join-Path $target $path.Replace('/', '\')
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Expand-Blob $blob $destination ([string]$file.hash)
    [IO.File]::SetLastWriteTimeUtc($destination, (ConvertTo-Utc $file.modifiedUtc))
    $written++
}

"Restored $written files of version $($chosen.id) into $target."
if ($missing -gt 0) { "$missing files weren't in this copy of the folder." }
if ($skipped -gt 0) { "$skipped files were skipped as unsafe." }
"Each folder there is one of the game's save folders. Close the game, then copy the files over its save folders, keeping a copy of what you replace."
