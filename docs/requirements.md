# GameSync requirements

> Snapshot of the live [GameSync requirements](https://claude.ai/code/artifact/44d3a52e-5ad7-4d90-9eba-2818c484f613) doc, 27 Sep 2026. Record Pass, Fail or Blocked in the live doc; when rows change there, copy them here. Tests and issues refer to requirements by ID.

## How to use this doc

There are 189 requirements in ten areas, each with an ID, a priority and a test: run the test and set Result as you go. Anything you expected that has no row here is a gap, so add it to the last section.

- **ID**: area plus number, for example `FOLD-04`. IDs are never reused, so test notes and GitHub issues can point at them.
- **Priority**: **Must** means v1 doesn't ship without it. **Should** means v1 unless it slips. **Later** means designed for now, built after v1.
- **Result**: Pass, Fail or Blocked, picked from the dropdown; blank means not tested yet. When a row fails, comment on it with what you saw.
- **Test**: the shortest check that proves it. Use two PCs (DESKTOP and LAPTOP), a Google account, and copies of real saves, never the originals.
- **Test kit**: one game per route. Terraria (Documents, over 300 MB), Cuphead (Unity, with registry PlayerPrefs), Black Myth: Wukong (saves inside the install folder), Slay the Spire 2 (Roaming), ROUNDS (no known saves, for learn mode), Apex Legends (ships an anti-cheat).
- **Sources**: `docs/design.md` as of 27 Sep 2026, the [GameSync design system](https://claude.ai/code/artifact/6a117b31-dc9a-4cd7-be46-037c91f1f074), and safety rules R1 to R21. Rows marked *new* come from decisions made after `design.md`.

## Library, launcher and play sessions

GameSync builds its library from each store's records plus the game folders you add, launches games the official way, and times every session.

| ID | Requirement | Priority | How to test | Result |
| --- | --- | --- | --- | --- |
| LIB-01 | Detects Steam games in every Steam library, from `libraryfolders.vdf` and `appmanifest_*.acf`. | Must | Games in both `E:\Steam` and `G:\SteamLibrary` appear with the right titles. |  |
| LIB-02 | Detects Epic games from the launcher's `*.item` manifests. | Must | Every installed Epic game appears, on C: and E:. |  |
| LIB-03 | Detects EA app games from `__Installer\installerdata.xml`. | Should | An EA game in `C:\Program Files\EA Games` appears. |  |
| LIB-04 | Detects Xbox and Microsoft Store games and marks them Backup only. | Should | An Xbox game appears with the Backup only badge. |  |
| LIB-05 | Scans the loose game folders you add and matches games by folder and exe names. | Must | Add `E:\Games`: the loose games from the spike appear. |  |
| LIB-06 | Matches by store ID, then install folder, then letters-and-digits names; a fuzzy title match is only a suggestion you confirm. | Must | "Slay the Spire 2" matches `SlayTheSpire2`; a fuzzy match asks first. |  |
| LIB-07 | Merges, splits, renames and hand-added games survive rescans and restarts. | Must | Merge "Spacewar" into its real game, rescan, restart: still merged. |  |
| LIB-08 | A game that disappears becomes Not installed, never deleted, and its saves stay in the cloud. | Must | Uninstall a synced game: it shows Not available and every version remains. |  |
| LIB-09 | Flags games that ship an anti-cheat, from the install folder, PCGamingWiki, or by hand. | Must | Apex Legends, Helldivers 2, Rocket League and GTA V Enhanced are flagged. |  |
| LIB-10 | Marks store-cloud games Backup only and leaves probably-online-only games unticked. | Must | Cyberpunk 2077 shows Backup only; Apex Legends starts unticked. |  |
| LIB-11 | Every game shows art in every slot, or a title cover; the Cover art rows below say where the art comes from. | Should | Scroll the whole library: no tile is empty. |  |
| LIB-12 | Rescans at startup, when a store's records change, and on demand. | Must | Install a Steam game with GameSync open: it appears without a restart. |  |
| PLAY-01 | *New.* Launcher home shows the last-played game as the hero with its save status, plus Needs you, Jump back in and Activity. | Must | Play Sekiro and quit: the hero shows Sekiro with "Save synced" and the time. |  |
| PLAY-02 | Play starts store games through the store's own link and loose games from their main exe in their own folder, never as admin. | Must | Launch a Steam, an Epic and a loose game; the loose game's process isn't elevated. |  |
| PLAY-03 | Before launching, GameSync pulls a newer cloud save, and warns if the game is open on another PC. | Must | Play on LAPTOP and quit, then press Play on DESKTOP: the laptop's progress loads. |  |
| PLAY-04 | A session starts at the process start time minus 2 s and ends once no game process has run for 10 s and save files have been quiet for 5 s. | Must | Fake-game harness: a save written in the first second lands in the session. |  |
| PLAY-05 | Games started outside GameSync (Steam, a shortcut) are noticed and tracked. | Must | Start a game from Steam: it shows Playing within a few seconds. |  |
| PLAY-06 | "I'm done playing" ends a stuck session. | Must | Leave a launcher running after quitting; the button ends the session and the sync runs. |  |
| PLAY-07 | The now-playing marker is set at launch and cleared after the exit upload; after 12 hours with no upload, other PCs read "DESKTOP never synced back". | Must | Cut DESKTOP's network mid-session and move LAPTOP's clock 12 hours on. |  |
| PLAY-08 | Shortcuts and Steam launch options can run `gamesync launch <game>` and get the pre-launch check. | Should | Add the launch option in Steam: a newer cloud save loads first. |  |
| PLAY-09 | Playtime and last played are recorded per game and shown on tiles and the activity calendar. | Should | Play for 20 minutes: the tile and today's calendar cell update. |  |
| PLAY-10 | Optional local snapshots every 30 minutes in long sessions, never uploaded before the session ends. | Should | A 65-minute session leaves 2 local snapshots and 1 upload. |  |

### Cover art

*New.* Steam's store API gives official art for any Steam app ID with no key, and that covers most of the owner's library, Epic and loose copies included. `design/real-art-preview.html` shows the owner's games filled this way.

| ID | Requirement | Priority | How to test | Result |
| --- | --- | --- | --- | --- |
| ART-01 | Every game with a Steam app ID gets Steam's official art, including Epic and loose copies whose Steam ID comes from the save list. No key needed. | Must | An Epic or loose copy of a game that is also on Steam shows Steam's cover. |  |
| ART-02 | Image file names come from Steam's store API (`IStoreBrowseService/GetItems` with `include_assets`), so games whose art sits under hashed paths still get it. | Must | Slay the Spire 2 and Helldivers 2, whose art uses hashed paths, show their covers. |  |
| ART-03 | Each slot takes the right image: tiles the 600×900 library capsule, the hero the library hero with the game's logo on top, rows the 300×450 capsule. | Must | Sekiro's hero shows its logo over the hero art, not text. |  |
| ART-04 | Games with no Steam art use SteamGridDB, only if the person has added their own free SteamGridDB API key in Settings. | Should | Add a key: Friday Night Funkin' gets a community cover. Remove it: back to its title cover. |  |
| ART-05 | No art at all gives a title cover, the game's name on a themed tile, never an empty box. | Must | A loose game with no Steam ID and no key shows its title cover. |  |
| ART-06 | Per game, the person can pick another cover (Steam's, a SteamGridDB option or their own image file), and the choice survives rescans. | Should | Pick your own image for ROUNDS and rescan: it stays. |  |
| ART-07 | Art is cached on the PC, shown offline, refreshed when Steam's image timestamp changes, and never synced to the cloud or put in a shared zip. | Must | Go offline and restart: covers still show. Open a shared zip: no images. |  |
| ART-08 | Downloaded images are untrusted: only JPEG, PNG or WebP under a size limit, checked by content, and only ever decoded for display. | Must | A fake image that is really a program is refused and the game shows its title cover. |  |

## Finding saves and choosing folders

Saves are found in four layers and confirmed once, and you can always choose folders yourself: a game's save location, where backups live on this PC, and extra folders to scan.

| ID | Requirement | Priority | How to test | Result |
| --- | --- | --- | --- | --- |
| FIND-01 | Looks up save paths in the PCGamingWiki list (the Ludusavi manifest) first. | Must | Terraria and Sekiro are found by the list. |  |
| FIND-02 | Engine rules find Unity, Unreal, Godot, GameMaker, Ren'Py and RPG Maker saves. | Must | Cuphead and Stick Fight (Unity) and Black Myth: Wukong (Unreal) are found by engine rules. |  |
| FIND-03 | Name search covers Documents, Saved Games, AppData, ProgramData, the install folder, the registry and Steam's `userdata`. | Must | The 9 name-search games from the spike are found. |  |
| FIND-04 | Learn mode watches one play session to find saves nothing else found. | Must | ROUNDS' saves are proposed after one session. |  |
| FIND-05 | Learn mode can use the admin file tracer, one session at a time, only if you allow it in Settings. | Should | Setting off: learn mode never asks for admin. Setting on: it asks once per session. |  |
| FIND-06 | Every found location is a proposal with its evidence (layer, files, size, newest date) that you confirm once; confirmed rules are pinned. | Must | Update the save list: a confirmed rule shows a suggestion instead of changing. |  |
| FIND-07 | Shows "saves may have moved" when a pinned folder goes quiet while a new one appears during play. | Must | Point a test game at a new save folder: the status appears after the session. |  |
| FIND-08 | Save rules use portable roots (`<documents>`, `<roaming>`, `<installDir>`, `<steamUser>` and so on) that each PC resolves for itself. | Must | One Wukong version restores to the right folder on both PCs, whatever drive the game is on. |  |
| FIND-09 | `save` files sync, `config` files are backed up per PC unless you opt in, and `screenshots` are off by default. | Must | Change graphics settings on LAPTOP: DESKTOP's settings stay as they were. |  |
| FIND-10 | Registry saves are exported per key to JSON and restored only under the game's own key. | Must | Cuphead's PlayerPrefs round-trip; nothing else under HKCU changes. |  |
| FIND-11 | Logs, crash dumps, shader caches and web caches are excluded by default. | Must | A game's `logs` folder is in no version. |  |
| FOLD-01 | *New.* You can set any game's save location by hand: browse to a folder or file, pick its category, and see the resolved path before saving. | Must | On a Saves not found game: Add path, pick the folder, and the next sync backs it up. |  |
| FOLD-02 | *New.* You choose where GameSync keeps backups on this PC; the default is `%LOCALAPPDATA%\GameSync\history`. | Must | Change it to `E:\Saves and Backups\Games\GameSync`: new versions land there. |  |
| FOLD-03 | *New.* Moving the backup folder copies everything, checks every hash, and only then removes the old copy; if anything fails, the old folder stays in use. | Must | Move it to a USB drive and pull the drive out mid-copy: GameSync keeps the old folder and says why. |  |
| FOLD-04 | *New.* The backup folder can't be inside a game's install folder, a cloud-synced folder (OneDrive, Dropbox, Google Drive for desktop), Program Files or Windows. | Must | Try each one: each is refused with a one-line reason. |  |
| FOLD-05 | A missing backup drive is never read as empty: backups pause with "Drive E: not connected" and resume when it's back. | Must | Unplug the drive holding the folder: nothing is deleted and Settings says so. |  |
| FOLD-06 | *New.* You choose how much history stays on this PC: the last 10 versions per game within 2 GB by default, up to keeping everything. | Should | Choose "keep everything": after 12 versions, all 12 are on disk. |  |
| FOLD-07 | Add and remove game folders to scan, such as `E:\Games` and `G:\`. | Must | Remove `G:\`: its games become Not installed and their saves stay. |  |
| FOLD-08 | Add extra save folders and folders named by game ID; they live in this PC's settings, never in the public repo. | Must | Add an ID-named folder: its subfolders map to games by Steam app ID. |  |
| FOLD-09 | *New.* Choose where shared zips go: Downloads by default, or ask every time. | Should | Pick "ask every time": Create zip opens a Save As window. |  |
| FOLD-10 | *New.* Every folder field shows the full path with Change and Open in Explorer, plus free space where it matters. | Must | Settings, Storage and folders: the backup folder shows all three. |  |
| FOLD-11 | Two rules claiming the same file are an error shown on both games. | Must | Give two games overlapping rules: both show the error. |  |
| FOLD-12 | Swap mode keeps separate saves for games that write the same file names to one place. | Should | Two such games launched through GameSync keep their own saves. |  |

## Backups, versions and restoring

Every change becomes a version that is kept forever, and any version restores in one step without losing the files it replaces.

| ID | Requirement | Priority | How to test | Result |
| --- | --- | --- | --- | --- |
| BAK-01 | A new version is made at the end of a session when any save file's hash changed, and only then. | Must | Play, save and quit: one new version. Play without saving: none. |  |
| BAK-02 | Versions are immutable, record parent, device, time and session, and store each unique file once by hash. | Must | Two versions sharing an unchanged Terraria world store it once. |  |
| BAK-03 | Every version is kept forever; nothing is deleted automatically. | Must | After 60 versions, all 60 are listed. |  |
| BAK-04 | Pinned versions (before-update saves, conflict sides, ones you pin) are never thinned. | Must | Thin a game's history: every pinned version is still there. |  |
| BAK-05 | Thinning is manual and per game, with a preview of what will go. | Should | Thin a game: exactly what the preview listed is gone. |  |
| BAK-06 | When a game's build changes, the current save is pinned as "before update to build N" before the new build runs. | Must | Fake a Steam `buildid` change: the pinned version exists before the game starts. |  |
| BAK-07 | A reinstall that writes a new save over an existing slot follows the first-sync rule: your save stays current and the new one is kept. | Must | Reinstall Terraria and start a new world: your old world stays current. |  |
| BAK-08 | Restore archives the current files first, restores into a temporary folder, checks hashes, then swaps by rename. | Must | Restore an old version and undo it; kill GameSync during a swap: no half-written files. |  |
| BAK-09 | Restored files keep their original modified times. | Must | Compare modified times at backup and after restore. |  |
| BAK-10 | No download or restore while any of the game's processes run. | Must | Restore with the game open: it waits and says why. |  |
| BAK-11 | Changes made while the game wasn't running are Held for review: backed up, but not made current or pulled by other PCs until you approve. | Must | Edit a save with the game closed: it shows Held for review, and LAPTOP gets it only after you approve. |  |
| BAK-12 | File contents upload first and the version record last, so a crash never leaves half a version. | Must | Kill GameSync between the two steps: after a restart the job resumes and no broken version shows. |  |
| BAK-13 | The job queue is durable: unfinished jobs resume after a crash or reboot. | Must | Reboot mid-upload: the upload finishes after sign-in. |  |
| BAK-14 | Game detail lists every version across PCs (date, PC, a note such as Current or Pinned) with Restore and Export. | Must | Sekiro lists versions from both PCs with the right notes. |  |
| BAK-15 | Each game shows how much space its history uses. | Should | The size on game detail matches the game's files in the cloud folder. |  |
| BAK-16 | Back up now works on any game, Backup-only games included. | Must | Press it on Cyberpunk 2077: a new version appears. |  |
| BAK-17 | This PC keeps the last 10 versions per game within 2 GB by default (changeable, FOLD-06); the cloud keeps everything. | Must | After 12 versions, 10 are on this PC and all 12 are in the cloud. |  |
| BAK-18 | *New.* Named saves: Save as… keeps the save as it is now under a name you give, like "Before Lady Maria", pinned and on every PC. The launcher and game detail list them by date and restore one in a step, keeping your current files first. Names can be changed or removed; the save stays in history. | Must | Save "Before Lady Maria", beat the boss, restore it: the save is back, and the save from after the fight is in history. LAPTOP lists the name too. |  |
| BAK-19 | *New.* Save folders you kept by hand next to a game's live save (a copy of the live folder, the files themselves, or a .zip) import as named saves, named after each folder. The folders are never changed, and identical copies are stored once. | Must | Import Bloodborne's CUSA00207 folder: every "Before …" and "After …" folder becomes a named save, and restoring "Before Orphan" matches that folder file for file. |  |

## Sync between PCs and conflicts

Each game syncs on its own with a three-way compare; the newest save wins by default, and four fixed cases always wait for you.

| ID | Requirement | Priority | How to test | Result |
| --- | --- | --- | --- | --- |
| SYNC-01 | Each game compares this PC and the cloud against the version both last agreed on, then does nothing, uploads, downloads (archiving first) or raises a conflict. | Must | Run each of the four cases from the decision table once, plus the unit tests. |  |
| SYNC-02 | One game failing never blocks the rest. | Must | Lock one game's save file: every other game still syncs. |  |
| SYNC-03 | Two uploads from the same parent (a fork) are a conflict for that game only. | Must | Take both PCs offline, play the same game on each, reconnect: one conflict, nothing else affected. |  |
| SYNC-04 | By default the newest save wins, judged by the save files' own modified times; the other side is pinned and a notification offers Swap. | Must | LAPTOP's newer save becomes current, DESKTOP's is pinned, and Swap reverses it. |  |
| SYNC-05 | Never automatic on a game's first sync on a PC: the cloud wins and the local files are archived. | Must | New PC with an older local Terraria save: the cloud's stays current and the local one is in history. |  |
| SYNC-06 | Never automatic when the newer side lost more than half its size or files, or is empty. | Must | Delete half of a save's files and sync: "Conflict: needs you". |  |
| SYNC-07 | Never automatic when the newer side changed while the game wasn't running. | Must | Edit a save with the game closed while the other PC uploads: "Conflict: needs you". |  |
| SYNC-08 | Never automatic when this PC's clock is more than 2 minutes off Google's, and that PC shows a warning. | Must | Set the clock 5 minutes ahead: newest-wins turns off with a warning. |  |
| SYNC-09 | Per game, you can choose "always ask" or "this PC always wins". | Must | Set Sekiro to always ask: its next conflict waits for you. |  |
| SYNC-10 | The conflict screen shows both sides (PC, save time, session length and how it started, size, changed files), a suggested choice with its reason, and Keep this PC's, Keep the cloud's, Compare files and Decide later. | Must | Open a waiting conflict: every item is there. |  |
| SYNC-11 | Until you decide, the game neither uploads nor downloads, and each PC keeps playing its own copy. | Must | Play on both PCs while undecided: nothing is overwritten. |  |
| SYNC-12 | Missing is not deleted: a vanished folder, unplugged drive or uninstalled game never becomes an empty version. | Must | Unplug G: during a sync: the game shows "Drive G: not connected". |  |
| SYNC-13 | Deleting some files in a session syncs; losing every file never does. | Must | Rotating autosaves sync; an emptied save folder does not. |  |
| SYNC-14 | Plan shows what the next sync would do for each game and why, without acting, and Sync now then does exactly that. | Must | Compare Plan with the log after Sync now. |  |
| PC-01 | Each PC is a named device (a random ID plus the name you pick), listed in the cloud with its app version and last-seen time. | Must | Settings, Devices lists DESKTOP and LAPTOP with last seen. |  |
| PC-02 | You can rename a device. | Must | Rename LAPTOP: DESKTOP shows the new name after its next sync. |  |
| PC-03 | Account IDs (`<steamUser>`, `<epicUser>`) resolve per PC, and games that embed the ID warn when they differ. | Must | Sign into another Steam account on LAPTOP: Sekiro warns. |  |
| PC-04 | A game not installed on this PC keeps its saves in the cloud, and a restore is offered once the game is detected. | Must | Install Terraria on LAPTOP: GameSync offers the cloud save. |  |
| PC-05 | Offline, sessions snapshot locally and uploads queue; back online, the normal rules apply. | Must | Play offline on LAPTOP and reconnect: the upload runs, or a conflict appears if DESKTOP changed it too. |  |

## Cloud storage and accounts

v1 keeps everything in your own Google Drive, in one GameSync folder that the app can see and nothing else.

| ID | Requirement | Priority | How to test | Result |
| --- | --- | --- | --- | --- |
| CLOUD-01 | Google Drive with the `drive.file` scope only: GameSync sees only the files it created. | Must | Google's consent screen asks only for GameSync's own files; your other Drive files stay invisible to it. |  |
| CLOUD-02 | Sign-in happens in the browser (desktop OAuth client, PKCE, a 127.0.0.1 redirect), and the Google project is In production, so sign-ins don't expire after 7 days. | Must | Sign in, then check you're still signed in 8 days later. |  |
| CLOUD-03 | The cloud copy is readable without the app: a `GameSync` folder with `HOW-TO-RESTORE.txt`, `restore.ps1`, and per game `latest`, `versions` and `blobs`. | Must | Browse Drive and restore one save by hand with the script. |  |
| CLOUD-04 | Quota full, sign-in expired, rate limited and offline are told apart, retried with backoff where that helps, and shown on the game. | Must | Fill the Drive: the game shows Upload pending, "Google Drive is full". |  |
| CLOUD-05 | GameSync warns when Drive passes 80% full. | Must | Fake 81% usage: the warning shows once. |  |
| CLOUD-06 | Files Google flags as malware are never downloaded, and the game shows Blocked. | Must | A fake backend returns the flag: nothing is downloaded. |  |
| CLOUD-07 | Sign out revokes the token at Google. | Must | After signing out, the old token is rejected. |  |
| CLOUD-08 | Settings shows the signed-in account and opens the GameSync folder in Drive. | Should | Open folder in Drive lands in the right folder. |  |
| CLOUD-09 | The first big upload shows progress and survives restarts; later uploads send only changed files. | Must | Import the Ludusavi backups: progress shows, and the next sync uploads only changes. |  |
| CLOUD-10 | Encrypted mode, as a later opt-in: a recovery code, AES-256-GCM on every file and record, HMAC file IDs, and no readable `latest` copy. Shared saves are decrypted on this PC before packing. Drive stays unencrypted until then (decided 27 Sep 2026). | Later | Turn it on: Drive shows no readable save files, and LAPTOP works after entering the code. |  |
| CLOUD-11 | S3-style buckets (R2, B2) and the GameSync server plug in behind the same two interfaces. | Later | The same sync tests pass against each backend. |  |

## Save manager and sharing saves

The save manager is the dense, console-style view of every game's saves, and sharing packs picked saves, or everything, into one zip a friend can import.

| ID | Requirement | Priority | How to test | Result |
| --- | --- | --- | --- | --- |
| MGR-01 | *New.* The save manager lists every game in a console-style table: status, save path, versions, size and last backup. | Must | Every ticked game appears, with values that match its game detail. |  |
| MGR-02 | *New.* Rows can be ticked, with select all, and the top bar shows "Share N selected", enabled only when something is ticked. | Must | Tick 2 rows: the button reads Share 2 selected. Untick both: it's disabled. |  |
| MGR-03 | *New.* A stats strip shows games, save folder size, versions, last backup and how many games need you. | Must | The numbers match the table and the log. |  |
| MGR-04 | *New.* A live log shows time, tag and one plain line per event, with errors highlighted. | Must | Sync now writes lines as it runs; a failed upload shows as an error line. |  |
| MGR-05 | *New.* The table sorts and filters by status, name, size and last backup. | Should | Filter to Needs you: only those games remain. |  |
| MGR-06 | *New.* Each row's status has its action one click away (Resolve, Review, Retry, Add path). | Should | Every status in the table offers its button from the Statuses table in `design.md`. |  |
| SHARE-01 | *New.* Share selected opens the share window with those games ticked; ticking a game takes its latest version. | Must | Tick 2 rows and press Share: both are ticked, with 1 version each. |  |
| SHARE-02 | *New.* In the share window you can open a game and pick exact versions, older and pinned ones included. | Must | Add Sekiro's "before update" version: the counts and the size update. |  |
| SHARE-03 | *New.* The footer shows games, versions, the zip name and total size as the selection changes. | Must | The size shown is within 5% of the finished zip. |  |
| SHARE-04 | *New.* Share all packs the whole backup folder into one zip and shows its size before starting. | Must | The zip holds every game in the backup folder. |  |
| SHARE-05 | *New.* Share all can be limited to the latest save of each game. | Should | Tick "latest only": the zip is smaller and holds one version per game. |  |
| SHARE-06 | *New.* Only save data goes in: every file passes the same program-file check as backups (R1). | Must | Plant `a.dll` in a save folder: it's left out and the game shows a warning. |  |
| SHARE-07 | *New.* Games with an anti-cheat or flagged online-only can't be shared (R16), and the window says why. | Must | Apex Legends is greyed out with the reason; Share all leaves it out and says so. |  |
| SHARE-08 | *New.* The zip holds a manifest (games, IDs, versions, portable paths, hashes, GameSync version) and a README with manual restore steps. | Must | Open the zip: both are there, and the README steps work without GameSync. |  |
| SHARE-09 | *New.* Packing shows progress and can be cancelled; when done, the window shows the path with Copy path and Show in folder. | Must | Cancel halfway: no partial zip is left behind. |  |
| SHARE-10 | *New.* Import saves reads a shared zip, matches games by ID, and adds each save as a pinned version labelled "Imported" with the date, never as current. | Must | Import on LAPTOP: nothing changes until you restore one of the imported versions. |  |
| SHARE-11 | *New.* Imported files get every restore check: approved folders only (R6), the game's own registry key only (R7), an AMSI scan (R3), no program files (R1). | Must | A zip with `..\` paths or a renamed `.exe` is rejected, with the reason. |  |
| SHARE-12 | *New.* Import warns when a save embeds another person's account ID and may not load, as FromSoftware saves do. | Should | Import a friend's Sekiro save: the warning shows before anything is added. |  |
| SHARE-13 | *New.* An imported save for a game that isn't installed waits until the game is detected, like PC-04. | Should | Import Terraria on a PC without it: the save waits and is offered after install. |  |

## Appearance and customization

*New.* v1 ships preset themes, each a primary and a secondary colour, in dark, light or pure black; fully custom colours come later on the same theme engine. The picker works like Mihon's: a live mini preview per theme.

| ID | Requirement | Priority | How to test | Result |
| --- | --- | --- | --- | --- |
| LOOK-01 | Mode: Dark (the default), Light, or Match Windows. | Must | Try each. With Match Windows on, switch Windows to light: GameSync follows without a restart. |  |
| LOOK-02 | Pure black backgrounds for OLED screens, in dark mode. | Must | Turn it on: page and rail backgrounds are #000000 and cards stay visible. |  |
| LOOK-03 | Six preset themes, each a primary colour, a secondary colour and a faint surface tint: Arcade (the default), Moss, Tidal, Sakura, Citrus and Mono. | Must | Apply each: launcher, save manager, share window and settings all repaint in its colours. |  |
| LOOK-04 | A Windows accent theme takes its primary from the Windows accent colour and follows it when it changes. | Must | Change the Windows accent: GameSync's primary follows within a few seconds. |  |
| LOOK-05 | The theme picker shows each theme as a small preview of GameSync in the current mode, with the chosen one ticked. | Must | In light mode, the Sakura card shows light surfaces with pink. |  |
| LOOK-06 | Primary and secondary can each be swapped for another colour from a curated set of swatches, every one checked in every mode. | Should | Pick Tidal, then a pink primary: the secondary stays aqua and the preview shows the mix. |  |
| LOOK-07 | Changes apply live, with no restart, and are saved per PC. | Must | Pick Moss and restart: still Moss. LAPTOP keeps its own choice. |  |
| LOOK-08 | Status colours never change with the theme, and statuses always show an icon and a word. | Must | In every theme, Conflict is amber with its warning icon and the word Conflict. |  |
| LOOK-09 | Every theme in every mode meets contrast: 4.5:1 for text, 3:1 for large text, icons, focus rings and control borders. An automated check covers them all. | Must | The contrast test passes; spot-check one theme with a contrast tool. |  |
| LOOK-10 | Reset to default returns to Arcade, Dark, pure black off. | Should | Change everything, then reset. |  |
| LOOK-11 | The theme covers every GameSync window; Windows notifications and the tray menu follow Windows. | Must | Walk every screen in Sakura, Light: no dark panels left over. |  |
| LOOK-12 | Custom colours: any primary or secondary by hex code or colour picker. A colour that fails contrast is adjusted, and the new value is shown. | Later | Enter #202020 as the primary in dark mode: it's lightened until it passes, and the note shows the new value. |  |
| LOOK-13 | A custom primary close to a status colour gets a note that statuses keep their icon and word. | Later | Enter #F2B544: the note appears. |  |
| LOOK-14 | Export and import a theme as a small file, to share with friends. | Later | Export Sakura with a custom primary and import it on LAPTOP: identical. |  |
| LOOK-15 | Optionally tint a game's pages from its cover art. | Later | With it on, Cuphead's detail page takes its accent from the cover. |  |
| LOOK-16 | Pick a theme during first-run setup. | Later | Fresh install: the picker appears before the scan, and the choice sticks. |  |

## Onboarding, settings, background work and notifications

First run takes four steps; after that the tray app works quietly and speaks up only when something needs you.

| ID | Requirement | Priority | How to test | Result |
| --- | --- | --- | --- | --- |
| ONB-01 | First run has four steps: scan this PC, choose games, connect cloud, daily backup time. | Must | A fresh install walks through all four and ends on the launcher. |  |
| ONB-02 | Found games are grouped: Sync, Back up only, Probably online-only (unticked), Saves found but game not installed, and No saves found yet (watched on first play). | Must | The owner's PC shows each group with the games from the onboarding mockup. |  |
| ONB-03 | Each game shows its save path and how it was found (save list, engine rule, name search). | Must | Wukong shows "engine rule". |  |
| ONB-04 | The Ludusavi import brings custom games, the ignore list, and backups as each game's first version. | Should | The 46 Ludusavi backups become first versions; the 32 ignored games stay ignored. |  |
| ONB-05 | Anti-cheat games are explained up front: no learn mode, and they launch through their own launcher. | Must | The note names the flagged games. |  |
| SET-01 | *New.* Settings has sections for Appearance, Storage and folders, Backup and sync, Cloud, Devices, Notifications and Safety. | Must | Every section opens, by mouse and by keyboard. |  |
| SET-02 | You set the daily backup time; a missed run catches up about 10 minutes after the next sign-in. | Must | Set 20:00 with the PC off at 20:00: the backup runs about 10 minutes after sign-in. |  |
| SET-03 | Settings shows the last daily run: time, games checked, uploads. | Must | The line matches the log. |  |
| SET-04 | Safety settings: allow learn mode to ask for admin, and later, encrypt everything in the cloud. | Must | Each setting survives a restart. |  |
| BG-01 | The tray app starts with Windows for your user and runs as a single instance. | Must | Sign in: one tray icon. Start the exe again: the open window comes forward. |  |
| BG-02 | A Task Scheduler entry runs `gamesync sync --all` with no window, or hands the job to the tray app when it's running. | Must | The task exists and its last run shows in the log. |  |
| BG-03 | The daily run skips running games, backs up and uploads changed games, checks for game updates, refreshes the save list weekly, and logs one line per game. | Must | Read the log after a daily run. |  |
| BG-04 | Failed jobs retry after 1 minute, doubling up to 1 hour, and survive restarts. | Must | Go offline: retries back off. Reboot: the job resumes. |  |
| BG-05 | Windows notifications only for what needs you (a conflict, an expired sign-in, saves not found, a blocked file), plus an optional daily summary. | Must | A healthy sync shows no notification. |  |
| BG-06 | Notifications are held while a fullscreen game runs and shown after it closes. | Must | Cause a conflict during a fullscreen game: the notification appears after you quit. |  |
| BG-07 | The tray icon shows synced, working, needs you or offline, and hovering shows the counts. | Must | Trigger each state and hover. |  |
| BG-08 | No hashing or uploads during play; only the watcher, and learn mode when you start it, run. | Must | Resource Monitor during a session: no GameSync disk activity beyond the watcher. |  |
| BG-09 | The command-line verbs `sync --all`, `plan`, `launch <game>` and `restore <game> <version>` work with the app closed and hand off to it when it's open. | Must | Run each both ways. |  |

## Safety and security rules

Each rule from `design.md` keeps its ID and gets an automated test; the check below is the one you can also run by hand.

| ID | Requirement | Priority | How to test | Result |
| --- | --- | --- | --- | --- |
| R1 | Program files are never backed up, restored or shared, caught by extension and by the Windows program header. | Must | Put `a.dll` and a program renamed `a.sav` in a save folder: neither is in the version. |  |
| R2 | A program file found in a save folder raises a warning on that game. | Must | Same setup: the game shows the warning. |  |
| R3 | Restored and imported files are scanned through AMSI before they're moved into place; a detection blocks the restore. | Must | Restore a version holding the EICAR test string: blocked. |  |
| R4 | Files Google flags as malware are never downloaded. | Must | A fake backend returns the flag: nothing is downloaded. |  |
| R5 | No rule can include browser profiles, credential stores, `.ssh`, `.aws`, crypto wallets, Windows credential folders or GameSync's own token store. | Must | Try adding `%USERPROFILE%\.ssh` as a save path: refused. |  |
| R6 | Every restored path resolves inside the game's approved folders: no `..`, absolute paths, alternate data streams, reserved names or links that point elsewhere. | Must | Crafted version records with each case: all refused. |  |
| R7 | Registry restores touch only the game's own approved key, never `Run`, `RunOnce` or anything outside it. | Must | A version that writes a `Run` value: refused. |  |
| R8 | Rules and paths from the cloud or the save list get the same checks, and a new or changed rule needs your confirmation. | Must | Change a rule in the cloud: the other PC asks before using it. |  |
| R9 | Google access uses the `drive.file` scope only. | Must | Inspect the token's scopes. |  |
| R10 | Tokens are encrypted with DPAPI for your Windows user, and Sign out revokes them at Google. | Must | Copy the token file to another Windows user: it can't be read. |  |
| R11 | Nothing listens on the network except the sign-in redirect on 127.0.0.1, for the seconds sign-in takes. | Must | `netstat -ano` during and after sign-in. |  |
| R12 | Launch commands never leave the PC, and `gamesync://` links launch only games GameSync already knows. | Must | Open a `gamesync://` link for an unknown game: nothing runs. |  |
| R13 | Game processes are opened with query-limited access at most: no memory access, injection, suspension, hooks, debugging or overlays. | Must | Audit GameSync's handles to a running game: query-limited only. |  |
| R14 | No kernel driver; learn mode uses its own uniquely named ETW session, never the NT Kernel Logger. | Must | `logman query -ets` during learn mode lists only GameSync's session. |  |
| R15 | Learn mode is off for games with an anti-cheat, and those games launch only through their official route. | Must | Learn mode is unavailable on Apex Legends, and Play goes through its store's launcher. |  |
| R16 | Saves are never edited, and never moved between accounts for online games, including by sharing. | Must | Hashes match before and after a sync; SHARE-07 passes. |  |
| R17 | The main app never runs as admin. | Must | Task Manager shows GameSync as not elevated. |  |
| R18 | The tracer runs only during learn mode, lives under Program Files, is signature-checked before it starts, listens only on a pipe limited to your user, and accepts no command that writes. | Must | Connect to the pipe as another user: refused. |  |
| R19 | Releases are code-signed through SignPath, and the updater checks the signature before installing. | Must | Tamper with an installer: the updater refuses it. |  |
| R20 | GitHub uses a passkey or 2FA, a protected main branch, pinned dependencies and CI with least-privilege tokens and pinned actions. | Must | Review the repository settings and workflow files. |  |
| R21 | Server: ownership checked on every request, a storage limit per person, a spending alert, short-lived upload links scoped to one folder, no client deletes, encryption on. | Later | The server's test suite. |  |

## Accessibility, performance, packaging and updates

GameSync must be usable without colour or a mouse, stay light while games run, and install for friends with one prompt. The performance numbers are proposed targets; nothing in `design.md` sets them yet.

| ID | Requirement | Priority | How to test | Result |
| --- | --- | --- | --- | --- |
| A11Y-01 | Every status has an icon and a word, never colour alone; status dots on the side rail carry a text label too. | Must | A greyscale screenshot still reads; Narrator reads the rail dots. |  |
| A11Y-02 | Everything works from the keyboard, with a visible focus ring. | Must | Share two saves and change the theme using only the keyboard. |  |
| A11Y-03 | Screen readers get names for icon buttons, tables, progress bars and dialogs. | Should | Narrator reads "Share 2 selected", the dialog title and the packing percentage. |  |
| A11Y-04 | GameSync follows Windows text size and the Animation effects setting. | Should | At 150% text size nothing is clipped; with animations off, nothing moves. |  |
| PERF-01 | Proposed: the tray app idles under 150 MB of memory and near 0% CPU with 50 games. | Should | Task Manager after an hour idle. |  |
| PERF-02 | Proposed: the main window opens from the tray in under 2 seconds. | Should | Time it five times. |  |
| PERF-03 | Proposed: the save manager stays smooth with 500 games and a 10,000-line log. | Should | Scroll both with a generated library. |  |
| PKG-01 | One per-machine installer (Inno Setup) under Program Files, with one admin prompt. | Must | Install on a clean Windows 11 VM: one prompt. |  |
| PKG-02 | A self-contained .NET publish, so friends don't need .NET installed. | Must | Run it on a VM without .NET. |  |
| PKG-03 | The updater checks GitHub Releases, downloads the signed installer and verifies it before running it. | Must | Publish a test release: it installs. A tampered one is refused (R19). |  |
| PKG-04 | A portable zip runs without installing, with the admin tracer turned off. | Should | Run it from a USB folder: learn mode works without the tracer. |  |
| PKG-05 | *New.* Uninstalling removes the app, its startup entry and the scheduled task, but never the backup folder or the cloud copy. | Must | Uninstall: the backups and the Drive files remain. |  |
| PKG-06 | Runs on Windows 11. Windows 10 support is undecided. | Must | The full test pass on Windows 11. |  |

## Gaps

Sixteen things are not designed or decided yet; each row names the requirements it affects.

| Gap | Affects | Suggested next step |
| --- | --- | --- |
| The Import saves window is designed, but not how a zip reaches it (file picker, drag and drop, opening the zip from Explorer). | SHARE-10 to SHARE-13 | File picker and drag and drop in v1; opening a zip from Explorer later. |
| Game detail, Conflict, Plan and Onboarding are still wireframes in the old look. | BAK-14, SYNC-10, SYNC-14, ONB-01 to ONB-05 | Rebuild them from the design system's components. |
| `design.md` gives the tray icon green, blue, amber and grey, but in the app Synced is cyan and status colours never change with the theme. | BG-07, LOOK-08 | Use the app's status colours for the tray icon too. |
| Choosing your own backup folder and keeping full history on this PC are new; `design.md` treats local history as a 10-version, 2 GB cache. | FOLD-02, FOLD-06, BAK-17 | Confirm both. |
| Share all packs the backup folder, which only holds what this PC keeps; older versions that are only in the cloud are left out. | SHARE-04, SHARE-05 | Decide: the folder as it is, or download the missing versions first. |
| Sharing moves single-player saves between people's accounts; R16 only forbids that for online games. | SHARE-07, SHARE-12, R16 | Confirm single-player sharing is fine and add it to R16's wording. |
| No performance targets exist. | PERF-01 to PERF-03 | Accept or change the proposed numbers. |
| Windows 10 support is undecided. | PKG-06 | Decide before Milestone 5. |
| Uninstall behaviour isn't in `design.md`. | PKG-05 | Add it. |
| Friends have no way to send logs with a bug report. | none yet | Add Copy diagnostics in Settings: logs and version lists, never tokens. |
| More than one Google account, or switching accounts, isn't covered. | CLOUD-02, CLOUD-07 | One account per PC for v1. |
| Screenshots: ignore, back up or sync is still open. | FIND-09 | Decide; they stay off meanwhile. |
| The "GameSync" name isn't checked on GitHub yet. | none | Check before the repo goes public. |
| English only is assumed. | none | Confirm. |
| SteamGridDB needs an API key, and a key built into an open-source app would be public. | ART-04, ART-06 | Each person pastes their own free key in Settings, optional; later the GameSync server can fetch art for everyone. |
