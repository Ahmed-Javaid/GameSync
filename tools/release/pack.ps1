param(
    [string]$Client,
    [switch]$NoClient,
    [switch]$NoInstaller,
    [switch]$NoSign,
    [string]$Version,
    [string]$Out
)

# Makes GameSync's release for friends (PKG-01, PKG-02, PKG-03, CLOUD-12, R19): GameSync.Tray.exe, the app, and
# gamesync.exe, the command line, published self-contained for 64-bit Windows into one folder, so nothing needs .NET
# installed; GameSync's own Google client beside them as google-client.json, so a friend signs in to Drive with one
# click (from a git-ignored file, never the repository, and nothing here prints what's in it); then the zip of that
# folder, the per-user installer made from it with Inno Setup (tools\release\GameSync.iss), and the installer's
# signature with GameSync's release key (notes\release-key.pem), which the updater checks. No Windows code signing.
#   powershell -File tools\release\pack.ps1                      (the client signing in keeps in %LOCALAPPDATA%\GameSync, else notes\)
#   powershell -File tools\release\pack.ps1 -Client <file.json>  (the JSON Google Cloud gave you for its Desktop app client)
#   powershell -File tools\release\pack.ps1 -NoClient            (no Drive sign-in: the cloud is a folder every PC reaches)
#   -NoInstaller                                                 (the zip alone)
#   -NoSign                                                      (an installer to try, which no GameSync updates to)
#   -Version 0.5.1                                               (stamped as that version, for trying an update; a real
#                                                                 release changes VersionPrefix in Directory.Build.props)
# They go to artifacts\release (git-ignored): GameSync-<version>-<date>-win-x64.zip, GameSync-Setup-<version>.exe and
# GameSync-Setup-<version>.sig, the two a GitHub release carries for the updater.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $Out) { $Out = Join-Path $root 'artifacts\release' }

function Fail([string]$Message) { [Console]::Error.WriteLine($Message); exit 1 }

# The client: the one given, or the copy signing in keeps, or the one Google Cloud gave the owner, kept in notes\.
if (-not $NoClient) {
    if (-not $Client) {
        $found = @(Join-Path $env:LOCALAPPDATA 'GameSync\google-client.json') +
            @(Get-ChildItem -Path (Join-Path $root 'notes') -Filter 'client_secret_*.json' -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
        $Client = $found | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    }

    if (-not $Client -or -not (Test-Path -LiteralPath $Client)) {
        Fail 'No Google client found. Give the JSON Google Cloud gave you for its Desktop app client with -Client <file>, or use -NoClient for a build that syncs through a folder instead of Drive.'
    }

    # Checked as GameSync reads it (GoogleClient.Parse): a Desktop app client with an ID and a secret.
    try { $json = [IO.File]::ReadAllText($Client) | ConvertFrom-Json } catch { $json = $null }
    if (-not $json -or -not $json.installed -or -not $json.installed.client_id -or -not $json.installed.client_secret) {
        Fail "$Client isn't a Google client for a Desktop app: GameSync needs the JSON of an OAuth client whose type is Desktop app."
    }
}

# The version: Directory.Build.props' VersionPrefix, and today's date for this build, in its name and its product version
# only: a version with the date in it would rewrite every project's packages.lock.json each day.
[xml]$props = [IO.File]::ReadAllText((Join-Path $root 'Directory.Build.props'))
$prefix = if ($Version) { $Version } else { @($props.Project.PropertyGroup | ForEach-Object { $_.VersionPrefix } | Where-Object { $_ })[0] }
if (-not $prefix) { Fail 'Directory.Build.props has no VersionPrefix.' }
if ($prefix -notmatch '^\d+\.\d+\.\d+$') { Fail "A version looks like 1.0.1, not $prefix." }
$suffix = Get-Date -Format 'yyyyMMdd'
$name = "GameSync-$prefix-$suffix-win-x64"
$folder = Join-Path $Out $name
if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
New-Item -ItemType Directory -Force -Path $folder | Out-Null

# Both programs into one folder: they share the runtime, each with its own .deps.json and .runtimeconfig.json.
foreach ($project in 'src\GameSync.Tray', 'src\GameSync.App') {
    "Publishing $project ..."
    & dotnet publish (Join-Path $root $project) -c Release -r win-x64 --self-contained true -o $folder `
        "-p:VersionPrefix=$prefix" "-p:InformationalVersion=$prefix-$suffix" -p:DebugType=none -p:DebugSymbols=false --nologo -v q
    if ($LASTEXITCODE -ne 0) { Fail "Publishing $project failed; see above." }
}

# Native libraries' debug symbols come with their packages (SkiaSharp's alone is 84 MB); nobody running GameSync needs them.
Get-ChildItem -LiteralPath $folder -Filter '*.pdb' -Recurse | Remove-Item -Force

foreach ($program in 'GameSync.Tray.exe', 'gamesync.exe') {
    if (-not (Test-Path -LiteralPath (Join-Path $folder $program))) { Fail "$program isn't in $folder after publishing." }
}

if (-not $NoClient) {
    Copy-Item -LiteralPath $Client -Destination (Join-Path $folder 'google-client.json')
}

$cloud = if ($NoClient) { 'pick a folder every PC can reach, such as a NAS or a synced folder' } else { 'sign in to Google Drive, or pick a folder every PC can reach' }
[IO.File]::WriteAllLines((Join-Path $folder 'README.txt'), [string[]]@(
    "GameSync $prefix ($suffix)",
    '',
    '1. Unzip this folder somewhere it can stay, such as C:\Users\<you>\Apps. GameSync starts from here when you sign in.',
    '2. Open GameSync.Tray.exe. GameSync is not code-signed, so Windows may say it protected your PC: choose More info, then Run anyway.',
    "3. First run finds your games and their saves. Choose the games to keep, then $cloud.",
    '',
    'Your saves stay in their folders; GameSync keeps copies of every version. Settings turns off starting with Windows.',
    'gamesync.exe is the command line: gamesync help lists what it does.'
))

$zip = Join-Path $Out "$name.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($folder, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
$carries = if ($NoClient) { 'no Google client, so Drive sign-in is off' } else { "GameSync's Google client" }
'{0}: {1:N0} MB, {2} files, with {3}.' -f $zip, ((Get-Item -LiteralPath $zip).Length / 1MB), @(Get-ChildItem -LiteralPath $folder -Recurse -File).Count, $carries

if ($NoInstaller) { exit 0 }

# PKG-01: the per-user installer, from the same folder.
$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $iscc) { Fail 'Inno Setup 6 builds the installer: winget install --id JRSoftware.InnoSetup --scope user. Or use -NoInstaller for the zip alone.' }

"Building the installer ..."
& $iscc /Q "/DAppVersion=$prefix" "/DSourceDir=$folder" "/DOutputDir=$Out" (Join-Path $root 'tools\release\GameSync.iss')
if ($LASTEXITCODE -ne 0) { Fail 'Inno Setup failed; see above.' }
$installer = Join-Path $Out "GameSync-Setup-$prefix.exe"

# R19: the installer's signature with GameSync's release key, which every GameSync's updater checks before it installs.
if ($NoSign) {
    '{0}: {1:N0} MB, not signed (-NoSign), so no GameSync updates to it.' -f $installer, ((Get-Item -LiteralPath $installer).Length / 1MB)
    exit 0
}

& dotnet run --project (Join-Path $root 'tools\GameSync.Release') -- sign $installer --version $prefix
if ($LASTEXITCODE -ne 0) { Fail 'Signing the installer failed; see above.' }
'{0}: {1:N0} MB, and {2} beside it. A GitHub release v{3} carries both.' -f $installer, ((Get-Item -LiteralPath $installer).Length / 1MB), "GameSync-Setup-$prefix.sig", $prefix
