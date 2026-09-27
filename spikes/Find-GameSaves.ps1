<#
.SYNOPSIS
    Spike: find where a game keeps its saves without a save-location database.

.DESCRIPTION
    Default mode reads the game's install folder, identifies the engine, predicts where that engine
    stores saves, then searches the usual save folders for names taken from the game itself
    (folder name, exe product/company name, Unity company/product, Unreal project name).

    -Watch records every file written under the usual save folders and the install folder while the
    game runs (FileSystemWatcher, no admin needed). The session ends once no process started from
    the install folder has been alive for -ExitGraceSeconds and writes have settled; the report
    lists the folders the game wrote to. It never reads or touches the game's memory, and it skips
    games that ship an anti-cheat unless -Force is given.

.EXAMPLE
    .\Find-GameSaves.ps1 "E:\Games\ROUNDS"

.EXAMPLE
    .\Find-GameSaves.ps1 -ScanRoot "E:\Games", "G:\" -Exclude Downloads, Tools

.EXAMPLE
    .\Find-GameSaves.ps1 "E:\Games\ROUNDS" -Watch
#>
[CmdletBinding(DefaultParameterSetName = 'Game')]
param(
    [Parameter(ParameterSetName = 'Game', Mandatory = $true, Position = 0)]
    [string] $InstallDir,

    [Parameter(ParameterSetName = 'Game')]
    [switch] $Watch,

    # Run -Watch even when the game ships an anti-cheat.
    [Parameter(ParameterSetName = 'Game')]
    [switch] $Force,

    [Parameter(ParameterSetName = 'Scan', Mandatory = $true)]
    [string[]] $ScanRoot,

    [Parameter(ParameterSetName = 'Scan')]
    [string[]] $Exclude = @(),

    # Folders searched by name, and watched with -Watch. Defaults to the usual Windows save locations.
    [string[]] $SaveRoots,

    # Games often exit and relaunch themselves, so a short gap doesn't end the session.
    [int] $ExitGraceSeconds = 10,

    # Games keep writing briefly after the window closes.
    [int] $SettleSeconds = 5
)

if (-not $SaveRoots) {
    $SaveRoots = @(
        [Environment]::GetFolderPath('MyDocuments'),
        (Join-Path $env:USERPROFILE 'Saved Games'),
        $env:APPDATA,
        $env:LOCALAPPDATA,
        (Join-Path $env:USERPROFILE 'AppData\LocalLow'),
        (Join-Path $env:PUBLIC 'Documents'),
        $env:ProgramData
    )
}
$SaveRoots = @($SaveRoots | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique)

# Folders and files that change constantly but never hold saves.
$NoisePath = '\\(Temp|tmp|Cache|Caches|cache2|GPUCache|ShaderCache|DXCache|GLCache|D3DSCache|CrashDumps|Crashes|CrashReportClient|Logs?|INetCache|WebCache|Code Cache|htmlcache|Service Worker|IndexedDB|Local Storage|Session Storage|blob_storage|Packages|Microsoft|Google|Mozilla|BraveSoftware|NVIDIA|NVIDIA Corporation|AMD|discord|Spotify|JetBrains|Code|claude|npm-cache|pip)(\\|$)'
$NoiseFile = '\.(log|tmp|temp|dmp|mdmp|etl|pdb|lock|ldb|lnk)$'
$SaveLikeName = 'save|sav$|slot|profile|progress|player|world|character|\.plr$|\.wld$|\.dat$'
$ExeNoise = 'unins|setup|redist|crash|report|easyanticheat|battleye|beservice|dxsetup|prereq|helper|updater|webhelper|cefprocess'
# Files and folders that anti-cheat systems install next to the game.
$AntiCheatName = '^(EasyAntiCheat.*|start_protected_game\.exe|BattlEye|BEService.*|BEClient.*|GameGuard|EAAntiCheat.*|XIGNCODE.*)$'

function Get-Key([string] $Name) {
    # Letters and digits only, so "Slay the Spire 2" and "SlayTheSpire2" compare equal.
    return ($Name.ToLowerInvariant() -replace '[^a-z0-9]', '')
}

function Find-First([string] $Dir, [string] $Filter, [int] $Depth) {
    Get-ChildItem -LiteralPath $Dir -Filter $Filter -Recurse -Depth $Depth -Force -ErrorAction SilentlyContinue |
        Select-Object -First 1
}

function Get-FolderSummary([string] $Path) {
    $files = @(Get-ChildItem -LiteralPath $Path -File -Recurse -Depth 5 -Force -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) { return $null }
    $newest = ($files | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
    $mb = ($files | Measure-Object Length -Sum).Sum / 1MB
    return '{0} files, {1:N1} MB, newest {2:yyyy-MM-dd}' -f $files.Count, $mb, $newest
}

function Get-MainExe([string] $Dir) {
    # The biggest exe is usually the game; Unreal's small root exe just starts <Project>-Win64-Shipping.exe.
    Get-ChildItem -LiteralPath $Dir -Filter *.exe -Recurse -Depth 3 -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notmatch $ExeNoise -and $_.FullName -notmatch '\\(_?CommonRedist|redist|DirectX|Support|Engine\\Binaries)\\' } |
        Sort-Object Length -Descending |
        Select-Object -First 1
}

function Get-Fingerprint([string] $Dir) {
    $fp = [pscustomobject]@{ Engine = 'unknown'; Detail = ''; Predicted = @(); Names = @(Split-Path $Dir -Leaf) }

    $exe = Get-MainExe $Dir
    if ($exe) {
        $fp.Names += $exe.BaseName, $exe.VersionInfo.ProductName, $exe.VersionInfo.CompanyName
    }

    $appInfo = Get-ChildItem -LiteralPath $Dir -Filter app.info -Recurse -Depth 2 -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -like '*_Data' } |
        Select-Object -First 1
    $shipping = Get-ChildItem -LiteralPath $Dir -Filter '*-Shipping.exe' -Recurse -Depth 4 -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(.+)-(Win64|WinGDK)-Shipping\.exe$' } |
        Select-Object -First 1
    $pck = Find-First $Dir '*.pck' 1

    if ($appInfo) {
        # Unity writes "<company>\n<product>" here and saves to LocalLow\<company>\<product> or PlayerPrefs.
        $company, $product = @(Get-Content -LiteralPath $appInfo.FullName -TotalCount 2 | ForEach-Object { $_.Trim() })
        # Unity swaps characters Windows forbids in folder names for '_' ("Stick Fight: The Game" -> "Stick Fight_ The Game").
        $invalid = '[' + [regex]::Escape(-join [IO.Path]::GetInvalidFileNameChars()) + ']'
        $fp.Engine = 'Unity'
        $fp.Detail = "$company / $product"
        $fp.Names += $company, $product
        $fp.Predicted += Join-Path $env:USERPROFILE ("AppData\LocalLow\{0}\{1}" -f ($company -replace $invalid, '_'), ($product -replace $invalid, '_'))
        $fp.Predicted += "HKCU:\Software\$company\$product"
    }
    elseif ($shipping) {
        $project = $shipping.Name -replace '-(Win64|WinGDK)-Shipping\.exe$', ''
        $projectDir = $shipping.Directory.Parent.Parent.FullName
        $fp.Engine = 'Unreal'
        $fp.Detail = "project $project"
        $fp.Names += $project
        $fp.Predicted += Join-Path $env:LOCALAPPDATA "$project\Saved\SaveGames"
        $fp.Predicted += Join-Path $projectDir 'Saved\SaveGames'
    }
    elseif ($pck) {
        $fp.Engine = 'Godot'
        $fp.Names += $pck.BaseName
        $fp.Predicted += Join-Path $env:APPDATA "Godot\app_userdata\$($pck.BaseName)"
    }
    elseif (Test-Path -LiteralPath (Join-Path $Dir 'data.win')) {
        $fp.Engine = 'GameMaker'
        if ($exe) { $fp.Predicted += Join-Path $env:LOCALAPPDATA $exe.BaseName }
    }
    elseif (Test-Path -LiteralPath (Join-Path $Dir 'renpy')) {
        $fp.Engine = "Ren'Py"
        $fp.Predicted += Join-Path $Dir 'game\saves'
    }
    elseif (Test-Path -LiteralPath (Join-Path $Dir 'www\js\rpg_core.js')) {
        $fp.Engine = 'RPG Maker MV'
        $fp.Predicted += Join-Path $Dir 'www\save'
    }
    elseif (Test-Path -LiteralPath (Join-Path $Dir 'js\rmmz_core.js')) {
        $fp.Engine = 'RPG Maker MZ'
        $fp.Predicted += Join-Path $Dir 'save'
    }
    elseif ((Find-First $Dir 'FNA.dll' 1) -or (Find-First $Dir 'Microsoft.Xna.Framework*.dll' 1)) {
        $fp.Engine = 'XNA/FNA'
    }
    elseif (Find-First $Dir 'lime.ndll' 1) {
        $fp.Engine = 'Haxe/Lime'
    }
    elseif (Find-First $Dir 'UnityPlayer.dll' 2) {
        $fp.Engine = 'Unity'
    }
    elseif (Test-Path -LiteralPath (Join-Path $Dir 'Engine\Binaries')) {
        $fp.Engine = 'Unreal'
    }

    $fp.Names = @($fp.Names | Where-Object { $_ })
    return $fp
}

function Find-ByName([string[]] $Names, [string[]] $Roots) {
    # Looks one level deep too, to catch <root>\<company>\<product> and Documents\My Games\<game>.
    $keys = @($Names | ForEach-Object { Get-Key $_ } | Where-Object { $_.Length -ge 4 } | Select-Object -Unique)
    if ($keys.Count -eq 0) { return }
    foreach ($root in $Roots) {
        Get-ChildItem -LiteralPath $root -Directory -Depth 1 -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch $NoisePath -and $keys -contains (Get-Key $_.Name) } |
            Select-Object -ExpandProperty FullName
    }
}

function Get-SaveCandidates([string] $Dir, [switch] $ShowMisses) {
    $fp = Get-Fingerprint $Dir
    $hits = @()
    foreach ($p in $fp.Predicted) {
        $summary = $null
        if ($p -like 'HKCU:*') {
            if (Test-Path -LiteralPath $p) { $summary = 'registry key exists (Unity PlayerPrefs)' }
        }
        elseif (Test-Path -LiteralPath $p) {
            $summary = Get-FolderSummary $p
        }
        if ($summary) { $hits += [pscustomobject]@{ How = 'engine'; Path = $p; Summary = $summary } }
        elseif ($ShowMisses) { $hits += [pscustomobject]@{ How = 'engine'; Path = $p; Summary = 'predicted, not present' } }
    }
    foreach ($p in Find-ByName $fp.Names $SaveRoots) {
        if (@($hits | Where-Object { $_.Path -eq $p }).Count -gt 0) { continue }
        $summary = Get-FolderSummary $p
        if ($summary) { $hits += [pscustomobject]@{ How = 'name'; Path = $p; Summary = $summary } }
    }

    $detail = if ($fp.Detail) { ": $($fp.Detail)" } else { '' }
    $found = @($hits | Where-Object { $_.Summary -ne 'predicted, not present' })
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('')
    $lines.Add(('== {0}  [{1}{2}]' -f (Split-Path $Dir -Leaf), $fp.Engine, $detail))
    foreach ($h in $hits) { $lines.Add(('   {0,-6}  {1}  ->  {2}' -f $h.How, $h.Path, $h.Summary)) }
    if ($found.Count -eq 0) { $lines.Add('   not found -> needs -Watch (learn mode) or a manual path') }

    $verdict = 'none'
    if (@($found | Where-Object { $_.How -eq 'engine' }).Count -gt 0) { $verdict = 'engine' }
    elseif ($found.Count -gt 0) { $verdict = 'name' }
    return [pscustomobject]@{ Verdict = $verdict; Lines = $lines }
}

function Watch-Session([string] $Dir) {
    $gameDir = (Resolve-Path -LiteralPath $Dir).Path.TrimEnd('\')

    # The real app skips learn mode for games that ship an anti-cheat, so the spike asks for -Force.
    $antiCheat = Get-ChildItem -LiteralPath $gameDir -Recurse -Depth 3 -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match $AntiCheatName } |
        Select-Object -First 1
    if ($antiCheat -and -not $Force) {
        Write-Warning "This game ships an anti-cheat ($($antiCheat.Name)). Learn mode is skipped for these games; add -Force to run it anyway."
        return
    }

    $gameExeNames = @(Get-ChildItem -LiteralPath $gameDir -Filter *.exe -Recurse -Force -ErrorAction SilentlyContinue |
        ForEach-Object { $_.BaseName } | Select-Object -Unique)
    if ($gameExeNames.Count -eq 0) {
        Write-Warning "No .exe found under $gameDir."
        return
    }

    if (-not ('WriteRecorder' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public sealed class WriteRecorder : IDisposable
{
    private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
    private long lastEventTicks = DateTime.MinValue.Ticks;
    private int overflows;

    public readonly ConcurrentDictionary<string, DateTime> Changed =
        new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

    public WriteRecorder(string[] roots)
    {
        foreach (string root in roots)
        {
            var watcher = new FileSystemWatcher(root);
            watcher.IncludeSubdirectories = true;
            watcher.InternalBufferSize = 64 * 1024;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
            watcher.Created += (s, e) => Record(e.FullPath);
            watcher.Changed += (s, e) => Record(e.FullPath);
            watcher.Renamed += (s, e) => Record(e.FullPath);
            watcher.Error += (s, e) => Interlocked.Increment(ref overflows);
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }
    }

    public DateTime LastEvent { get { return new DateTime(Interlocked.Read(ref lastEventTicks)); } }
    public int Overflows { get { return overflows; } }

    private void Record(string path)
    {
        DateTime now = DateTime.Now;
        Changed[path] = now;
        Interlocked.Exchange(ref lastEventTicks, now.Ticks);
    }

    public void Dispose()
    {
        foreach (var watcher in watchers) watcher.Dispose();
    }
}

public static class GameProcessProbe
{
    // PROCESS_QUERY_LIMITED_INFORMATION: the least access Windows offers, and what Task Manager uses.
    // It can't read or change the game's memory.
    private const uint QueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    // Returns the exe path and start time, or null when the process can't be queried.
    public static Tuple<string, DateTime> Query(int processId)
    {
        IntPtr handle = OpenProcess(QueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var name = new StringBuilder(1024);
            int size = name.Capacity;
            if (!QueryFullProcessImageName(handle, 0, name, ref size)) return null;
            long creation, exit, kernel, user;
            DateTime started = GetProcessTimes(handle, out creation, out exit, out kernel, out user)
                ? DateTime.FromFileTime(creation)
                : DateTime.Now;
            return Tuple.Create(name.ToString(), started);
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
"@
    }

    $roots = @($SaveRoots) + $gameDir
    $steamPath = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if ($steamPath -and (Test-Path -LiteralPath (Join-Path $steamPath 'userdata'))) {
        $roots += (Resolve-Path -LiteralPath (Join-Path $steamPath 'userdata')).Path
    }
    $roots = @($roots | Select-Object -Unique)

    $recorder = [WriteRecorder]::new([string[]] $roots)
    try {
        Write-Host "Watching $($roots.Count) folders. Start the game now, from anywhere. Ctrl+C aborts."
        $prefix = $gameDir + '\'
        $started = $null
        $lastSeen = $null
        $confirmed = @{}   # PID -> start time, for processes running from the install folder
        $rejected = @{}    # PIDs with a matching name that run from somewhere else
        while ($true) {
            # Get-Process -Name reads a system-wide snapshot and opens no process. Only processes named like one
            # of the game's exes get opened, once each, with the lowest query right and never memory access.
            $running = @(Get-Process -Name $gameExeNames -ErrorAction SilentlyContinue)
            foreach ($p in $running) {
                if ($confirmed.ContainsKey($p.Id) -or $rejected.ContainsKey($p.Id)) { continue }
                $info = [GameProcessProbe]::Query($p.Id)
                if ($info -and $info.Item1.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { $confirmed[$p.Id] = $info.Item2 }
                else { $rejected[$p.Id] = $true }
            }
            $alive = @($running | Where-Object { $confirmed.ContainsKey($_.Id) })
            $now = Get-Date
            if ($alive.Count -gt 0) {
                if (-not $started) {
                    # Polling notices the game up to a second late, and games write on startup, so use the real start time.
                    $earliest = $alive | ForEach-Object { $confirmed[$_.Id] } | Sort-Object | Select-Object -First 1
                    $started = $earliest.AddSeconds(-2)
                    Write-Host ('Game started: {0}' -f (($alive | ForEach-Object { "$($_.Name) ($($_.Id))" }) -join ', '))
                }
                $lastSeen = $now
            }
            elseif ($started -and ($now - $lastSeen).TotalSeconds -ge $ExitGraceSeconds) {
                break
            }
            Start-Sleep -Seconds 1
        }
        Write-Host 'Game exited. Waiting for writes to settle...'
        while (((Get-Date) - $recorder.LastEvent).TotalSeconds -lt $SettleSeconds) { Start-Sleep -Milliseconds 500 }
    }
    finally {
        $recorder.Dispose()
    }

    $files = foreach ($entry in $recorder.Changed.GetEnumerator()) {
        if ($entry.Value -lt $started) { continue }
        if ($entry.Key -match $NoisePath -or $entry.Key -match $NoiseFile) { continue }
        if (-not (Test-Path -LiteralPath $entry.Key -PathType Leaf)) { continue }
        Get-Item -LiteralPath $entry.Key -Force
    }
    $files = @($files)

    Write-Output ''
    Write-Output ('Session {0:HH:mm:ss} - {1:HH:mm:ss}: {2} files written outside known noise.' -f $started, $lastSeen, $files.Count)
    if ($recorder.Overflows -gt 0) { Write-Output "Warning: the watcher overflowed $($recorder.Overflows) time(s); the list may be incomplete." }
    foreach ($group in ($files | Group-Object DirectoryName | Sort-Object Count -Descending | Select-Object -First 15)) {
        $mb = ($group.Group | Measure-Object Length -Sum).Sum / 1MB
        $saveLike = @($group.Group | Where-Object { $_.Name -match $SaveLikeName }).Count
        $tags = @()
        if ($saveLike -gt 0) { $tags += "$saveLike save-like name(s)" }
        if ($group.Name -match '\\760\\remote\\') { $tags += 'Steam screenshots' }
        if ($group.Name.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or $group.Name -eq $gameDir) { $tags += 'install folder' }
        $examples = ($group.Group | Select-Object -First 3 -ExpandProperty Name) -join ', '
        Write-Output ('{0,4} files {1,8:N2} MB  {2}' -f $group.Count, $mb, $group.Name)
        Write-Output ('                      e.g. {0}{1}' -f $examples, $(if ($tags) { "   [$($tags -join '; ')]" } else { '' }))
    }
}

if ($PSCmdlet.ParameterSetName -eq 'Scan') {
    $dirs = foreach ($root in $ScanRoot) {
        Get-ChildItem -LiteralPath $root -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object { $Exclude -notcontains $_.Name -and -not ($_.Attributes -band [IO.FileAttributes]::System) }
    }
    $verdicts = @()
    foreach ($dir in $dirs) {
        if (-not (Find-First $dir.FullName '*.exe' 4)) { continue }
        $result = Get-SaveCandidates $dir.FullName
        $result.Lines
        $verdicts += $result.Verdict
    }
    Write-Output ''
    Write-Output ('Summary: {0} games | found by engine rule: {1} | found by name only: {2} | not found: {3}' -f
        $verdicts.Count,
        @($verdicts | Where-Object { $_ -eq 'engine' }).Count,
        @($verdicts | Where-Object { $_ -eq 'name' }).Count,
        @($verdicts | Where-Object { $_ -eq 'none' }).Count)
}
elseif ($Watch) {
    Watch-Session $InstallDir
}
else {
    (Get-SaveCandidates $InstallDir -ShowMisses).Lines
}
