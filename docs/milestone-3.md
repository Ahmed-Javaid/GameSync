# Milestone 3: detection and save discovery

Started and built 28 Sep 2026. The week on both PCs waits until the whole app is built (the owner's choice), so Milestone 2's last check happens then.

**Done when** GameSync finds everything Ludusavi finds on the owner's PC, plus 12 of the 16 loose games from the spike (design.md → Milestones). **Met** on 28 Sep 2026, in a read-only scan: see Results.

## Scope

| Area | IDs |
| --- | --- |
| Store detection | LIB-01 Steam, LIB-02 Epic, LIB-03 EA, LIB-05 loose folders, LIB-04 Xbox (Should, not built) |
| The library | LIB-06 matching, LIB-07 fixes that stick, LIB-08 not installed, LIB-09 anti-cheat, LIB-10 store cloud and online-only, LIB-12 rescans on demand |
| Finding saves | FIND-01 save list, FIND-02 engine rules, FIND-03 name search, FIND-06 confirm once, FIND-10 registry saves, FOLD-07, FOLD-08, FOLD-11, R8 |
| Moving from Ludusavi | ONB-04: custom games, the ignore list, and the backups |
| Two PCs | PC-04: a game installed later is offered its cloud saves |

Not in this milestone: learn mode (FIND-04, FIND-05: Milestone 6), "saves may have moved" and swap mode (FIND-07, FOLD-12: they need the process watcher, Milestone 4), cover art (Milestone 5), rescans at startup and when a store's records change (LIB-12's automatic half: the tray app, Milestone 5).

## Try it

Everything is on the command line, like Milestones 1 and 2. A scan only reads; nothing syncs until you confirm it.

```powershell
gamesync add-folder E:\Games            # game folders to scan (FOLD-07); Steam, Epic and EA are found on their own
gamesync add-folder G:\
gamesync scan                           # finds games and where each keeps its saves
gamesync import-ludusavi                # previews taking over Ludusavi's ignore list, custom games and backups
gamesync import-ludusavi --apply
gamesync show terraria                  # where a game's saves were found and why
gamesync confirm --all                  # everything found, except probably-online-only and name-search-only finds
gamesync import-ludusavi --apply        # again, for the backups of games confirmed since; ones already in are skipped
gamesync plan
gamesync sync
```

Also: `library [--ignored]`, `confirm <game> [--mine|--theirs]`, `ignore`/`unignore`, `rename <game> <title>`, `merge <game> <into>`, `folders`, `remove-folder`, `add-save-folder <folder> [--by-id]`/`remove-save-folder`, `savelist update [--file <manifest.yaml>]`, `set anti-cheat <game> yes|no|auto`. `gamesync help` lists them all.

## How it works

**Detection** reads each store's own records, never display names (`Discovery/Stores.cs`, `GameSync.Windows/StoreLocations.cs`):

| Store | Reads |
| --- | --- |
| Steam | the install path from the registry, `libraryfolders.vdf`, then each library's `appmanifest_*.acf`: app ID, name, install folder, build; the accounts in `loginusers.vdf` |
| Epic | the launcher's `Manifests\*.item` (path from the registry): name, app name, install folder, version; DLC, engines and half-installed apps left out |
| EA | `__Installer\installerdata.xml` in EA's games folder and in each install folder the registry names |
| Loose folders | the folders you add: each subfolder with a game program in it; downloads, tools, installers and launchers are left out |

Each install folder is one game; a store's record beats a loose folder, and store folders inside your game folders are left to their store.

**The save list** is the Ludusavi manifest: 51,661 titles, 21,737 with Windows paths. It downloads from GitHub when it's a week old (only if it changed, by ETag), or comes from a local Ludusavi install; offline, the last copy stays in use. Its placeholders become GameSync's portable folders: `<base>` is `<installDir>`, `<root>` is `<steamRoot>` for Steam, `<winAppData>` is `<roaming>` and so on; `<home>/AppData/LocalLow` becomes `<localLow>` so a moved folder still resolves, and `<storeUserId>` becomes a `*` so each PC's own account folder matches. A path names files or folders, and a folder means everything in it (`*/632360/remote/UserProfiles/**`).

**Finding saves**, per game, in the design's order; the first layer that finds files wins:

1. The save list, matched by store ID, then install folder name, then title in letters and digits (LIB-06). Never a fuzzy match. Plus your ID-named folders (FOLD-08): a subfolder named by the game's Steam app ID.
2. Engine rules: Unity (`app.info`: LocalLow and its PlayerPrefs key), Unreal (`*-Win64-Shipping.exe`, both LocalAppData and the install folder), Godot, GameMaker, Ren'Py, RPG Maker MV and MZ.
3. Name search: folders named like the game (its folder, exe, product and company names) one level into Documents, My Games, Saved Games, AppData, ProgramData, Public Documents and your extra save folders, and save-named folders (`saves`, `savedata`, ...) inside the install folder.

Then, for games that **aren't installed**, the save list's paths that don't need an install folder: the design's "Saves found, game not installed" group. A folder no rule may reach (R5) is never proposed, whichever layer found it: the guard now also covers rclone, gcloud, GitHub CLI, password managers, Discord and Telegram sessions, Epic's sign-in and Steam's `config`. Paths are followed a folder at a time and each folder is read once, so a whole scan of the owner's PC, with the whole list, takes about 2 seconds.

**Registry saves** (FIND-10): a registry rule names a key under `HKEY_CURRENT_USER\Software`. Before each scan of the game, the key is exported with everything under it to a JSON file in GameSync's own folder (`%LOCALAPPDATA%\GameSync\registry\<game>`), stamped with the key's own last-change time, so a change made during play counts as made during play. That folder is one more root of the game, so versions, uploads, history and crash-safe restores treat the export like any save file. A restored export is checked before anything is written (it must name one of the game's own keys, never a startup key, R7), and written back after the files are in place; a restore cut off in between writes it back before the next scan. Values round-trip byte for byte, including Unity's 8-byte REG_DWORD floats. A key that vanishes leaves its last export: missing isn't deleted.

**The library** lives in `state.db`: each game with its store IDs, install folder, build, engine, anti-cheat, store-cloud and online-only flags, what the last scan found, and what you decided. Renames, ignores, merges and confirmed rules survive rescans (LIB-07). A game a scan doesn't find becomes Not installed, and syncing it says so rather than treating it as empty (LIB-08). Game IDs come from the save list's title (`black-myth-wukong`), and a game new on this PC takes the ID another PC already gave it when the titles match, so both PCs sync it as one game.

**Confirming** (FIND-06) pins what was found as the game's rules; a game its store's cloud syncs is backed up only (LIB-10). After that, a rescan's new places are suggestions (`show` lists them; `confirm` again adds them, keeping the rules already there). `confirm --all` leaves out games that look online-only and games only the name search found.

**Rules between PCs** (R8, PC-04): every version records the portable rules it was taken with. A PC that doesn't sync a game yet lists it as "Saves in the cloud" and, once it's installed, `confirm` takes up that PC's rules exactly, root keys and all, so the saves come down. When another PC's newest save used other rules, sync says so and this PC keeps its own until `confirm <game> --theirs`. A restore from a version taken with fewer rules leaves files outside them alone.

**Two games sharing a save** (FOLD-11): when two games' rules take the same file or registry key, both show the error and neither syncs or restores until one changes.

**Moving from Ludusavi** (ONB-04, `import-ludusavi`): its ignore list ignores those games here (a game confirmed in GameSync stays synced); its custom games are confirmed as games, their full paths made portable where they can be; and each game's latest backup comes into its history as a named save, "Ludusavi backup (2026-06-21)", kept aside and pinned, never current by itself, restorable by name. The backup's files are taken exactly as the game's rules would take them from the backup's copy of each folder. A game no longer on this PC gets its backup as history under the save list's rules for it, so a later scan uses the same root keys. Ludusavi's own files are never changed.

## Results

The owner records Pass, Fail or Blocked in the live requirements doc. Automated tests: 249 pass (4 skipped: the live Drive and real-saves runs, which need the owner's setup).

| ID | What was checked |
| --- | --- |
| LIB-01 | `DetectionTests.LIB_01_*`. This PC: 18 Steam games from `E:\Steam` and `G:\SteamLibrary`. |
| LIB-02 | `DetectionTests.LIB_02_*`. This PC: Grand Theft Auto V Enhanced. |
| LIB-03 | `DetectionTests.LIB_03_*` and the dedup test (a Steam game EA also lists is one game). This PC has no EA game installed: the registry still names Battlefield 1, but its folder is gone, and Need for Speed is found through Steam. |
| LIB-05 | `DetectionTests.LIB_05_*`. This PC: 17 games in `E:\Games` and `G:\`. |
| LIB-06 | Store ID, install folder and letters-and-digits titles only; there is no fuzzy matching, so nothing is matched without asking. |
| LIB-07, LIB-08 | `LibraryTests` (rename, ignore and confirm survive a restart; merge stays merged; uninstalled is Not installed with its rules kept); CLI run: remove a folder, rescan, sync says "isn't installed on this PC". |
| LIB-09 | This PC: Apex Legends, Helldivers 2, Rocket League and GTA V Enhanced flagged, the requirement's four. `set anti-cheat` sets it by hand. |
| LIB-10 | This PC: Cyberpunk 2077 is backed up only; Apex Legends is probably online-only, left out of `confirm --all`. |
| FIND-01 | This PC: Terraria and Sekiro found by the list. `DiscoveryTests.FIND_01_*` (Steam `userdata` folders, account folders, loose copies). |
| FIND-02 | `DetectionTests.FIND_02_*`, `DiscoveryTests.FIND_02_*`. On this PC the list knows Cuphead and Wukong, so it finds them first; Stick Fight's files come from the Unity rule. |
| FIND-03 | This PC: FNF, GuacameleeSTCE, and Bloodborne's emulator saves inside its install folder. |
| FIND-06, R8 | `LibraryTests`, `SharedRulesTests` (suggestions; rules recorded in versions; another PC's rules never taken up by themselves). |
| FIND-10, R7 | `RegistryTests`: PlayerPrefs round-trip with nothing else in the registry written; a change during play uploads; a tampered export aiming at the startup key is blocked with the registry untouched; the real registry round-trips every value kind in a test key of its own. This PC: Peak's and Stick Fight's keys found. |
| FOLD-07, FOLD-08 | CLI run; `DiscoveryTests.FOLD_08_*` (ID-named folder by Steam app ID; extra folder joins the name search). |
| FOLD-11 | `SharedRulesTests.FOLD_11_*`. |
| ONB-04 | `LudusaviTests`; preview on this PC: of 45 backups, 32 come in (28 synced games, the 3 custom games, Abyssus as history), Stick Fight's is registry only (step for Ludusavi's registry backups below), 12 ignored games stay with Ludusavi, Machinarium waits. |
| PC-04 | `SharedRulesTests.PC_04_*` (offered with its rules, offline too; taking them up brings the save down). |

**The done-when check**, 28 Sep 2026, read-only, in a scratch data folder with `E:\Games` and `G:\` added:

- Ludusavi's own preview (`ludusavi backup --preview`) finds 54 games: 25 it backs up and 29 on the owner's ignore list. GameSync finds 51 of them, including every one Ludusavi backs up. The other 3 (GeoGuessr, Spacewar, TOXIKK) are only Steam screenshots, which GameSync leaves off by default (FIND-09). Watch Dogs 2's saves come from the owner's Ludusavi custom game, which the import brings over.
- Loose games: `E:\Games` and `G:\` hold 17 game folders (the spike looked at 16 of them). Saves are found for 13: Child of Light, Cuphead, Dark Souls 3, FNF, GuacameleeSTCE, Stick Fight, Terraria, Black Myth Wukong, Bloodborne, Ghost of Tsushima, Ready or Not, Sekiro and Slay the Spire 2. ROUNDS needs learn mode, as the spike found; COD and OG show as installed with no saves found yet; Minecraft has no program in its folder (known limits).
- The whole scan takes about 2 seconds.

## Known limits

- **Xbox and Microsoft Store games** (LIB-04, Should) aren't detected as installed yet. Their saves still show as "saves found, game not installed" when the save list knows them (Forza Horizon 6 does).
- **Java games** such as Minecraft have no program in their folder, so the loose scan doesn't see them; add them by hand (`add-game`, or `add-save-folder`).
- **Ludusavi's registry backups** (`registry.yaml`) aren't imported; the files of each backup are. A game's registry keys back up from the moment it syncs.
- **The first scan after install needs the save list**: without a network or a local Ludusavi, it runs with engine rules and name search only, and says so.
- **Bloodborne-style kept folders**: the name search proposes the whole `savedata` folder, kept copies included. Point the rule at the live folder and bring the copies in as named saves (`import-saves`, Milestone 2).
- **`<storeUserId>` becomes `*`**: when the two PCs use different Steam accounts, each PC's own account folder syncs as a separate folder. PC-03's account check covers `<steamUser>` rules only.
- **Rescans** run when you ask (`scan`); rescanning at startup and when Steam, Epic or EA change their records comes with the tray app (Milestone 5).
