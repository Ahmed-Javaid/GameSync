param([Parameter(Mandatory = $true)][string]$Scratch, [Parameter(Mandatory = $true)][string]$GameSync)

# Makes a real conflict for the stand-in game Lantern Keep, whose saves live in a scratch folder: a second scratch PC
# (LAPTOP) on the same scratch cloud plays and uploads, then DESKTOP (the scratch data folder, <Scratch>\data, set up as
# docs/handoff.md says, with Lantern Keep set to Always ask in its games.json) plays and changes the same save, so the
# conflict waits for the person. -GameSync is a gamesync.exe built into a scratch folder. Nothing outside -Scratch is
# touched.
$s = $Scratch
$gs = $GameSync
$desktop = "$s\data"
$laptop = "$s\data-laptop"
$laptopSaves = "$s\fake-saves-laptop\Lantern Keep\Saves"
$desktopSaves = "$s\fake-saves\Lantern Keep\Saves"

# DESKTOP: settle anything held earlier.
& $gs --data $desktop approve lantern-keep
& $gs --data $desktop sync lantern-keep

# LAPTOP: a new scratch PC on the same cloud, with its own copy of the game's saves.
if (-not (Test-Path "$laptop\games.json")) {
    & $gs --data $laptop init --remote "$desktop\cloud" --name LAPTOP
    New-Item -ItemType Directory -Force $laptopSaves | Out-Null
    & $gs --data $laptop add-game lantern-keep --title "Lantern Keep" --root "saves=$laptopSaves"
}
& $gs --data $laptop sync lantern-keep

# LAPTOP plays 47 minutes and saves; it uploads.
$now = Get-Date
& $gs --data $laptop session lantern-keep $now.AddMinutes(-60).ToString("yyyy-MM-dd HH:mm") $now.AddMinutes(-12).ToString("yyyy-MM-dd HH:mm")
Set-Content "$laptopSaves\slot1.sav" "laptop: the lighthouse, chapter 3" -Encoding ascii
(Get-Item "$laptopSaves\slot1.sav").LastWriteTime = $now.AddMinutes(-13)
& $gs --data $laptop sync lantern-keep

# DESKTOP plays longer and later, before hearing of it, and changes two files.
& $gs --data $desktop session lantern-keep $now.AddMinutes(-110).ToString("yyyy-MM-dd HH:mm") $now.AddMinutes(1).ToString("yyyy-MM-dd HH:mm")
Set-Content "$desktopSaves\slot1.sav" "desktop: the lighthouse and the warden, chapter 4" -Encoding ascii
Set-Content "$desktopSaves\slot2.sav" "desktop: a second run" -Encoding ascii
(Get-Item "$desktopSaves\slot1.sav").LastWriteTime = $now.AddMinutes(-2)
(Get-Item "$desktopSaves\slot2.sav").LastWriteTime = $now.AddMinutes(-3)
& $gs --data $desktop sync lantern-keep
& $gs --data $desktop games
