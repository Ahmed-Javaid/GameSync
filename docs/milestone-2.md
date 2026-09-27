# Milestone 2: Google Drive and a second PC

Started 27 Sep 2026. Drive stays unencrypted (decided 27 Sep 2026).

**Done when** the desktop and the laptop sync for a week with no false conflicts (design.md → Milestones).

**Status (28 Sep 2026):** built and tested against an in-memory Drive and a fake Google sign-in, and then live:
- The owner's OAuth client works.
- The whole two-PC scenario passed on the real Drive in 193 seconds, crash recovery included.

Still to do:

1. Publish the app (step 6 below), so sign-ins stop expiring after 7 days.
2. The week on both PCs. It waits until the whole app is built, the owner decided on 28 Sep 2026, so it tests the finished app on both devices at once.

## What's here

| Part | Where |
| --- | --- |
| Local-first sync: the backup folder, its outbox, pull and push, limits, moving it | `src/GameSync.Core/Storage/LocalHistory.cs`, `LocalFirstStore.cs` |
| The cloud bundle (`ICloud`) and the folder backend, now with devices, `latest/` and the restore kit | `src/GameSync.Core/Storage/` |
| `HOW-TO-RESTORE.txt` and `restore.ps1` (Windows PowerShell 5.1, no GameSync needed) | `src/GameSync.Core/Storage/RestoreKit/` |
| Portable folders and account IDs (FIND-08, PC-03) | `src/GameSync.Core/Games/RootResolver.cs`, `src/GameSync.Windows/KnownFolders.cs` |
| Clock check, offline decisions | `src/GameSync.Core/Sync/DecisionEngine.cs` |
| Where the backup folder may go (FOLD-04) | `src/GameSync.Core/Safety/BackupFolderGuard.cs` |
| Google Drive backend: layout, merges, errors and backoff | `src/GameSync.Storage.Drive/` |
| Google sign-in: PKCE, 127.0.0.1, DPAPI, revoke | `src/GameSync.Storage.Drive/GoogleAuth.cs`, `LoopbackRedirect.cs`, `src/GameSync.Windows/DpapiProtector.cs` |
| Named saves, added 27 Sep 2026 (BAK-18, BAK-19): Save as, restore by name, rename, forget, import kept folders | `src/GameSync.Core/Sync/SyncService.NamedSaves.cs`, `NamedSaveFinder.cs` |
| Commands: `init --drive`, `signin`, `signout`, `account`, `devices`, `rename-device`, `set …`, `save`, `saves`, `import-saves` | `src/GameSync.App/Program.cs` |
| Tests: an in-memory Drive, a fake Google, faults, two PCs, named saves | `tests/GameSync.Core.Tests/` (`CloudTests`, `DriveTests`, `GoogleAuthTests`, `LocalHistoryTests`, `NamedSaveTests`, `FakeDrive`) |

## Named saves

Named saves are pinned versions under a name you choose. They're never thinned, they sync to every PC, and they stay in this PC's backup folder while they fit in the size limit. The launcher's buttons come with the UI (Milestone 5); until then:

```powershell
& $gs save bloodborne "Before Lady Maria"          # the save as it is now, under that name
& $gs saves bloodborne                             # every named save, newest first
& $gs restore bloodborne "Before Lady Maria"       # brings it back; your current files are kept first
& $gs rename-save bloodborne "Before Lady Maria" "Before Maria, NG+"
& $gs forget-save bloodborne "Before Maria, NG+"   # removes the name; the save stays in history
```

Your Bloodborne saves on shadPS4: point the game at the live folder, not the folder around it, then import the folders you kept:

```powershell
& $gs add-game bloodborne --title Bloodborne --root "saves=<installDir>/Shadlix/user/savedata/1/CUSA00207/SPRJ0005"
& $gs set install-dir bloodborne "G:\Bloodborne GOTY"
& $gs import-saves bloodborne "G:\Bloodborne GOTY\Shadlix\user\savedata\1\CUSA00207"            # preview
& $gs import-saves bloodborne "G:\Bloodborne GOTY\Shadlix\user\savedata\1\CUSA00207" --apply
```

Tried on a copy of that folder:
- 38 kept saves were found, and 36 imported in 4 seconds. "Ieosefka" matched "After ROM at chapel" and `Before Crow.zip` matched its folder, so those two kept one name each.
- The two `.rar` files were skipped with a note; the unpacked folders next to them were imported.
- 1,022 MB of kept folders took 74 MB in the cloud.
- Restoring "Before Orphan" matched that folder file for file.

## How it works

**Local first.** Every sync writes to the backup folder on this PC (`%LOCALAPPDATA%\GameSync\history` unless moved), then uploads from there:

- New versions and pins go into the backup folder plus an outbox. Uploading sends each version's files first and its record last, so BAK-12 still holds in the cloud.
- Before planning, GameSync copies the records it hasn't seen from the cloud, so decisions read local files and only new records cost a download.
- Offline, a sync still snapshots this PC and queues the upload (PC-05). Downloads and conflicts wait for the cloud, with this PC's changes kept meanwhile, and the game shows Upload pending or Newer in cloud.
- The backup folder keeps every record, the files of the last 10 versions per game within 2 GB, and always everything not uploaded yet, the current version and this PC's base (BAK-17). Older files come back from the cloud when a restore needs them.
- If the cloud loses versions this PC has (its folder was deleted, or it's a new account), they go back in the outbox and upload again.
- A cloud folder that can't be reached is Offline, never empty.

**Google Drive:**

- It follows design.md → Storage backends, with blobs flat in each game's `blobs` folder.
- Folders are found by app properties, so the GameSync folder can be renamed or moved.
- When two PCs create the same folder at once, the older one wins and the other's contents move into it.
- Uploads are checked against Drive's MD5.
- Errors are typed and retried with backoff where waiting helps. Rate limits are tried up to 6 times, Google's own errors 4 times, and a dropped connection twice.

**Sign-in:** the browser, PKCE, and a redirect to `127.0.0.1` on a port Windows picks, open only while it waits. GameSync asks for `drive.file` alone and refuses a wider grant. The refresh token is encrypted with DPAPI for your Windows user, and sign-out revokes it at Google.

## Try it now, with a folder as the cloud

```powershell
$gs = 'src\GameSync.App\bin\Debug\net10.0-windows10.0.19041.0\gamesync.exe'
& $gs --data D:\gs-test\desktop init --remote D:\gs-test\cloud --name DESKTOP
& $gs --data D:\gs-test\desktop add-game wukong --title "Black Myth: Wukong" --root "saves=<installDir>/b1/Saved/SaveGames"
& $gs --data D:\gs-test\desktop set install-dir wukong D:\gs-test\games\Wukong
& $gs --data D:\gs-test\desktop sync
& $gs --data D:\gs-test\desktop devices
& $gs --data D:\gs-test\desktop set history-folder D:\gs-test\backups
```

Use test folders: placeholders like `<documents>` resolve to your real folders.

## Your part: a Google Cloud project

GameSync needs its own OAuth client to reach Drive. It takes about five minutes, once:

1. At console.cloud.google.com, create a project named GameSync.
2. In APIs & Services → Library, enable the **Google Drive API**.
3. In Google Auth Platform (formerly "OAuth consent screen"), choose Get started: app name GameSync, your email, audience **External**.
4. Under Data Access, add the scope `.../auth/drive.file` only.
5. Under Branding, fill in what Google needs before an app can be published:
   - **Application home page:** `https://github.com/Ahmed-Javaid/GameSync`
   - **Application privacy policy link:** `https://github.com/Ahmed-Javaid/GameSync/blob/main/PRIVACY.md`
   - **Authorized domains:** `github.com`
   - Leave the logo and the terms of service empty: a logo makes Google require verification.
6. Under Audience, **Publish app** so it's In production; sign-ins made while it's in Testing expire after 7 days. `drive.file` isn't a sensitive scope, so Google doesn't need to verify the app.
7. Under Clients, create a client of type **Desktop app**, then download its JSON.
8. Save the JSON in `notes\` (git-ignored). Never commit it.

Then, on the desktop:

```powershell
& $gs init --drive --name DESKTOP
& $gs signin --client notes\<the downloaded file>.json
& $gs account
```

And the live test, which works in its own throwaway folder in your Drive and never touches the GameSync folder:

```powershell
$env:GAMESYNC_DRIVE_DATA = "$env:LOCALAPPDATA\GameSync"
dotnet test tests/GameSync.Core.Tests --filter "FullyQualifiedName~LiveDrive"
```

## The week on both PCs

Until the process watcher (Milestone 4), GameSync doesn't know when you play, so a change counts as made during play only when a session covers it. The simplest routine:

- After playing, run `gamesync approve <game>`. It syncs your changes as made during play.
- Before playing on the other PC, run `gamesync sync`.
- Each day, glance at `gamesync games` for anything that isn't Synced.

A false conflict is any "Conflict: needs you" that you didn't cause by playing on both PCs. Note each one with `gamesync log <game>`.

## Requirement results so far

Record these in the live requirements doc once the live checks pass.

On copies of your real saves, every decision and crash scenario passes through the new local-first sync: all 44 games over the cloud folder (7.6 minutes), and a spread of 7 over the in-memory Drive, Slay the Spire 2's 332 files and Cyberpunk's 131 MB among them.

| ID | Result | Tests |
| --- | --- | --- |
| CLOUD-01 | Pass | `DriveTests.CLOUD_01_*`; live, Google granted `drive.file` alone (`LiveDriveTests.R9_*`) |
| CLOUD-02 | Pass, except the 8-day check | `GoogleAuthTests.CLOUD_02_*`; live sign-in worked on 28 Sep. Being still signed in 8 days later needs the app published. |
| CLOUD-03 | Pass | `LocalHistoryTests.CLOUD_03_*` (2), `DriveTests.CLOUD_03_*`; `restore.ps1` restores a version in Windows PowerShell |
| CLOUD-04 | Pass | `CloudTests.CLOUD_04_*` (4), `DriveTests.CLOUD_04_*` (8), `GoogleAuthTests.CLOUD_04_*` |
| CLOUD-05 | Pass | `CloudTests.CLOUD_05_*` |
| CLOUD-06 | Pass | `CloudTests.CLOUD_06_*`, `DriveTests.CLOUD_06_*` |
| CLOUD-07 | Pass (fake Google); live check left | `GoogleAuthTests.CLOUD_07_*` |
| CLOUD-08 | Pass (command line) | `gamesync account` shows the account, storage and a link to the folder |
| CLOUD-09 | Partly | Progress lines for big uploads; a restart skips what's stored (`LocalHistoryTests.BAK_12_*`). Importing your Ludusavi backups to Drive is the live check. |
| PC-01, PC-02 | Pass | `CloudTests.PC_01_and_PC_02_*`, command-line smoke test |
| PC-03 | Pass | `CloudTests.PC_03_*` (2); account IDs are set by hand until detection (Milestone 3) |
| PC-05 | Pass | `TwoPcTests.SYNC_03_and_PC_05_*`, `CloudTests.PC_05_*` (4), `DecisionEngineTests.PC_05_*` (4) |
| FIND-08 | Pass | `CloudTests.FIND_08_*` (3); install folders are set by hand until detection |
| SYNC-08 | Pass | `DecisionEngineTests.SYNC_08_*` (3), `CloudTests.SYNC_08_*` |
| BAK-17 | Pass | `LocalHistoryTests.BAK_17_*` (2) |
| FOLD-02 | Pass | `gamesync set history-folder`, smoke test |
| FOLD-03 | Pass | `LocalHistoryTests.FOLD_03_*` (3) |
| FOLD-04 | Pass | `LocalHistoryTests.FOLD_04_*` |
| FOLD-05 | Pass | `LocalHistoryTests.FOLD_05_*` |
| FOLD-06 | Pass | `LocalHistoryTests.FOLD_06_*` |
| R4 | Pass | as CLOUD-06 |
| R9 | Pass | `GoogleAuthTests.R9_*`, and live `LiveDriveTests.R9_*` |
| R10 | Pass | `GoogleAuthTests.R10_*` |
| R11 | Pass | `GoogleAuthTests.CLOUD_02_*`, `GoogleAuthTests.R11_*` |
| BAK-18 | Pass (command line) | `NamedSaveTests.BAK_18_*` (5), `LocalHistoryTests.BAK_17_named_saves_*`; the launcher's Save as… comes with the UI |
| BAK-19 | Pass | `NamedSaveTests.BAK_19_*` (2); a copy of your Bloodborne folder imported 36 named saves in 4 seconds, with 1,022 MB of kept folders taking 74 MB in the cloud, and "Before Orphan" restored file for file |

## Known limits

- **Drive uploads each file with two requests** (the client library's resumable upload), so a first upload of about 1,300 files takes several minutes. Later uploads send only what changed.
- **A sync with nothing new still asks Drive about every game**: about four calls each, so roughly half a minute for 46 games. Following Drive's change feed instead is a later speed-up.
- **Other apps can drop hidden files into GameSync's folders.** In both live runs, something put a file GameSync can't see into each new top-level folder, which keeps Drive from letting GameSync trash it. It isn't Google Drive for desktop on this PC. Syncing never trashes a folder, and merging duplicate folders now renames and unmarks the extra copy instead of needing the trash. The live test's two leftover "GameSync test" folders have to be deleted by hand at drive.google.com.
- **Each sync writes new files twice on this PC's side**: into the backup folder, then compressed again for the cloud. The all-games run on real saves took 7.6 minutes, up from 5.1.
- **By hand until detection:** install folders and account IDs (`gamesync set install-dir`, `set account`), offering a cloud save once a game is installed (PC-04), and confirming changed save rules (R8). Milestone 3 does these for detected games: see `docs/milestone-3.md`. `set install-dir` and `set account` remain for games added by hand.
