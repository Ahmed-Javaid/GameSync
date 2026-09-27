# Milestone 1: the sync engine

Built 27 Sep 2026. The sync engine works as a command-line tool, with a local folder standing in for Google Drive. Every Milestone 1 requirement has automated tests; they pass, including on copies of the owner's real saves.

## What's here

| Part | Where |
| --- | --- |
| Data model: ids, versions, file entries | `src/GameSync.Core/Model/` |
| Game definitions and their checks (R5) | `src/GameSync.Core/Games/` |
| Snapshot scanner: rules, default excludes, program-file check, missing drives | `src/GameSync.Core/Scanning/` |
| Safety guards: program files (R1), sensitive folders (R5), restore paths (R6), registry keys (R7), antivirus interface (R3) | `src/GameSync.Core/Safety/` |
| Storage interfaces and the folder backend | `src/GameSync.Core/Storage/` |
| This PC's state: base versions, statuses, sessions, job queue, activity log, hash cache (SQLite) | `src/GameSync.Core/State/` |
| Decision engine (pure), sync service, restore journal, crash points | `src/GameSync.Core/Sync/` |
| AMSI scanner (R3) | `src/GameSync.Windows/` |
| Command line (`gamesync.exe`) | `src/GameSync.App/` |
| Tests, named after requirement IDs | `tests/GameSync.Core.Tests/` |

## Try it

```powershell
dotnet build GameSync.sln
dotnet test GameSync.sln

# Also run every scenario on copies of real saves (the folder is only read; copies go to %TEMP%).
$env:GAMESYNC_TESTDATA = 'D:\Backups\Ludusavi'   # your Ludusavi backup folder: one subfolder per game
$env:GAMESYNC_TESTDATA_ALL = '1'   # every game; without it, a spread of games within 200 MB
dotnet test tests/GameSync.Core.Tests --filter "FullyQualifiedName~RealSaves" --logger "console;verbosity=detailed"
```

The command line keeps its data in `%LOCALAPPDATA%\GameSync` unless `--data <folder>` says otherwise. Use a test folder while trying it out:

```powershell
$gs = 'src\GameSync.App\bin\Debug\net10.0-windows10.0.19041.0\gamesync.exe'
& $gs --data D:\gs-test\desktop init --remote D:\gs-test\cloud --name DESKTOP
& $gs --data D:\gs-test\desktop add-game cuphead --title Cuphead --root saves=D:\gs-test\saves\Cuphead
& $gs --data D:\gs-test\desktop sync
& $gs help
```

Until the process watcher exists (Milestone 4), a change counts as made during play only if a session covers it: `gamesync session <game> <start> <end>`. Otherwise it's held for review (BAK-11), and `gamesync approve <game>` releases it.

## Real-saves run

On 27 Sep 2026, every game folder in the owner's Ludusavi backups with file saves passed the whole scenario: 44 games, 1,301 files, about 970 MB, in 5.1 minutes. The scenario is a first backup, a download on a new PC, play on one PC, a conflict and Swap, a crash between file contents and record, and a crash midway through swapping files in. Two folders were skipped: Battlefield 1 is empty, and Stick Fight keeps its save only in the registry.

## Requirement results

Record these in the live requirements doc.

| ID | Result | Tests |
| --- | --- | --- |
| SYNC-01 | Pass | `DecisionEngineTests.SYNC_01_*` (4), `TwoPcTests.SYNC_01_*`, real saves |
| SYNC-02 | Pass | `TwoPcTests.SYNC_02_*` (a locked save file) |
| SYNC-03 | Pass | `DecisionEngineTests.SYNC_03_*`, `TwoPcTests.SYNC_03_*` (offline play on both PCs) |
| SYNC-04 | Pass | `DecisionEngineTests.SYNC_04_*` (2), `TwoPcTests.SYNC_04_*` with Swap, real saves. The Swap offer is text in the result until notifications arrive (Milestone 4). |
| SYNC-05 | Pass | `DecisionEngineTests.SYNC_05_*` (2), `TwoPcTests.SYNC_05_*` |
| SYNC-06 | Pass | `DecisionEngineTests.SYNC_06_*` |
| SYNC-07 | Pass | `DecisionEngineTests.SYNC_07_*`; sessions are passed in until Milestone 4 |
| SYNC-09 | Pass | `DecisionEngineTests.SYNC_09_*` (2); set with `add-game --policy` or `conflictPolicy` in `games.json` |
| SYNC-11 | Pass | `DecisionEngineTests.SYNC_11_*`; while it waits, this PC's side is also kept in history |
| SYNC-12 | Pass | `DecisionEngineTests.SYNC_12_*` (3), `TwoPcTests.SYNC_12_*` (a drive letter that doesn't exist) |
| SYNC-13 | Pass | `DecisionEngineTests.SYNC_13_*` (2), `TwoPcTests.SYNC_13_*` |
| SYNC-14 | Pass | `TwoPcTests.SYNC_14_*`, including a game that changed between plan and sync |
| BAK-02 | Pass | `StorageTests.BAK_02_*` (2) |
| BAK-03 | Pass | `TwoPcTests.BAK_03_*` (60 versions) |
| BAK-04 | Pass | `TwoPcTests.BAK_04_and_BAK_05_*` |
| BAK-05 | Pass | same test: exactly the preview is thinned |
| BAK-07 | Pass | `DecisionEngineTests.BAK_07_*`; the reinstall is marked by hand (`gamesync reinstalled`) until detection (Milestone 3) |
| BAK-08 | Pass | `TwoPcTests.BAK_08_*` (restore and undo), `CrashTests.BAK_08_*` (2), real saves |
| BAK-09 | Pass | `TwoPcTests.BAK_09_*`, real saves |
| BAK-11 | Pass | `DecisionEngineTests.BAK_11_*` (3), `TwoPcTests.BAK_11_*` |
| BAK-12 | Pass | `CrashTests.BAK_12_and_BAK_13_*`, real saves |
| BAK-13 | Pass | same test: the interrupted job resumes on the next start |
| BAK-16 | Pass | `TwoPcTests.BAK_16_*` |
| FIND-09 | Pass | `TwoPcTests.FIND_09_*` |
| FIND-11 | Pass | `SafetyTests.FIND_11_*` |
| R1 | Pass | `SafetyTests.R1_*` (4), `TwoPcTests.R1_and_R2_*` |
| R2 | Pass | `TwoPcTests.R1_and_R2_*`; the warning shows in results until the UI (Milestone 5) |
| R3 | Pass, with a stand-in | `TwoPcTests.R3_*` uses a stand-in scanner, so tests never trip Defender. The real AMSI scanner ran clean on every restore in a smoke test. The EICAR check is a manual test for the owner. |
| R5 | Pass | `SafetyTests.R5_*` (2) |
| R6 | Pass | `SafetyTests.R6_*` (14 paths and a junction), `TwoPcTests.R6_and_R8_*` |
| R7 | Pass (guard only) | `SafetyTests.R7_*`. Backing up and restoring registry saves themselves comes later. |
| R8 | Partly | Paths from the cloud get every check (`StorageTests.R8_*`, `TwoPcTests.R6_and_R8_*`). Confirming changed rules arrives with the save list and detection (Milestone 3). |

## Known limits, for later milestones

- **Speed with many small files**: every file is flushed to disk on its own, so a sync of Slay the Spire 2's 332 files takes about 6 seconds. Batching the flushes is an easy win before Milestone 2.
- **Not built yet**:
  - The local history cache (the backup folder), the cloud's `latest/` copy and `restore.ps1`: Milestone 2 with Drive.
  - The clock check (SYNC-08): Milestone 2.
  - Registry saves: later. Until then, Stick Fight has nothing to sync.
- **The hash cache trusts size and modified time.** A file changed without changing either is re-read only on upload. The upload checks the hash, so nothing wrong is ever stored.
- **AMSI scans files up to 256 MB.** Bigger files rely on Defender's own real-time scan as they're written.
