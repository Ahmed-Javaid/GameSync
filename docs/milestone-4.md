# Milestone 4: sessions and launching

Started and built 28 Sep 2026. The week on both PCs waits until the whole app is built (the owner's choice).

**Done when** you play, quit, and the laptop has it without a single click (design.md → Milestones). **Met in simulation** on 28 Sep 2026: a stand-in game played on DESKTOP with the agent watching, and LAPTOP's agent brought the save down with no click (see Results). The real check, on both PCs through Google Drive, is part of the two-PC week.

## Scope

| Area | IDs |
| --- | --- |
| Sessions | PLAY-04 timing, PLAY-05 games started anywhere, PLAY-06 "I'm done playing", PLAY-09 playtime and last played |
| Launching | PLAY-02 launch routes, PLAY-03 pre-launch check, PLAY-07 now-playing marker, PLAY-08 `launch` for shortcuts and Steam |
| While playing | BAK-10 no download or restore while the game runs, BG-08 no hashing or uploads during play, BAK-06 the save before a game update, FIND-07 "saves may have moved" |
| Background work | BG-01 starts with Windows, one instance, BG-02 and SET-02 the daily backup and catching up, BG-03 the daily run, SET-03 its last-run line, BG-04 retries, BG-05 and BG-06 notifications held during fullscreen, BG-09 the command line alongside it |

Not in this milestone: the launcher's home screen and the tray icon (PLAY-01, BG-07: the UI, Milestone 5), long-session snapshots (PLAY-10, Should: they'd hash during play, against BG-08, so they wait for an opt-in), swap mode (FOLD-12, Should).

## Try it

Publish once into a folder of its own. Windows' tasks and Steam then point at a copy that a rebuild neither locks nor replaces:

```powershell
dotnet publish src\GameSync.App -c Release -o "$env:LOCALAPPDATA\Programs\GameSync"
$gs = "$env:LOCALAPPDATA\Programs\GameSync\gamesync.exe"
& $gs agent                    # the background app in this window: start a game from anywhere, quit, and watch it sync
& $gs launch terraria          # the check first, then the game through Steam; with the agent off, this waits and syncs after
& $gs done terraria            # when a launcher keeps the session open after you quit
& $gs games                    # now with playtime and last played
& $gs schedule                 # what Windows runs for GameSync, and the last daily run
& $gs schedule daily 20:00     # adds the daily backup, and its catch-up after sign-in
& $gs schedule background on   # starts GameSync.Tray.exe, the background app, when you sign in
```

Run `schedule` from the published copy: the tasks point at the `GameSync.Tray.exe` next to the `gamesync.exe` that adds them. `schedule daily off` and `schedule background off` remove them.

For Steam, put this in a game's Properties → Launch options, so a launch from Steam gets the check with no window (the full path; Steam doesn't expand `%LOCALAPPDATA%`):

```
"C:\Users\<you>\AppData\Local\Programs\GameSync\GameSync.Tray.exe" launch terraria -- %command%
```

## How it works

**Two programs, one engine.** `gamesync.exe` is the command line, and `GameSync.Tray.exe` is the background app, with no window; the tray icon and the UI join it in Milestone 5. A Windows program either opens with no window or prints to a terminal, not both, so there are two. Both are thin: the verbs, the agent, the daily run and the tasks are in `src/GameSync.Host`, over the engine in `GameSync.Core`. One engine touches saves at a time: both take `engine.lock` in the data folder, a file Windows lets go of if its program dies, and a command waits while the agent finishes a sync, and says so. The agent also holds `agent.lock`, so it runs once per user.

**The agent** (`Host/Agent.cs`) looks every 2 seconds:

- A system-wide process snapshot gives names and IDs without opening any process. A process named like one of a game's programs is opened once, with query-limited access, for its full path and start time. The programs come from the game's install folder, leaving out launchers, crash reporters, updaters and anti-cheat services. A process belongs to the game only when it runs from that folder (`Windows/ProcessWatcher.cs`, `Core/Sessions/GamePrograms.cs`).
- A session (`Core/Sessions/SessionTracker.cs`) starts at the first process's start time minus 2 seconds. It ends once none of the game's processes has run for 10 seconds and its save folders have been quiet for 5, which file-system notifications tell without reading anything. A launcher handing over to the game within those 10 seconds stays one session. `done <game>` ends a stuck session, and the processes running then are ignored until they exit.
- At a session's start, the game shows Playing and the now-playing marker goes up. At its end, the session is recorded, so its changes count as made during play. Once nothing plays, the game syncs, the marker comes down, and it's checked for saves that may have moved (FIND-07).
- While any game plays, the agent only watches (BG-08).
- Between sessions, every game syncs every 15 minutes and when the agent starts, so what another PC played comes down with no click. What waits to upload tries again after 1 minute, doubling up to an hour (BG-04). Each synced game's build is read every minute, and a new build pins the current save first (BAK-06).
- A session the agent had open when it was stopped (a crash, a power cut) is recorded up to the last save written, and synced when the agent starts again. Commands don't count a session left open that way as playing.

**Launching** (`Host/Cli.Play.cs`):

- The check comes first (PLAY-03). A newer cloud save comes down when that's safe, and the check says when another PC is playing, a conflict waits, or changes are held. It waits up to 2 minutes for a sync in the background, then lets the game start without the check.
- Then the store's own route (PLAY-02): `steam://rungameid/<id>`, Epic's `com.epicgames.launcher://apps/<id>?action=launch&silent=true`, or a loose game's main program from its own folder; never as admin.
- From Steam's launch options (PLAY-08), `GameSync.Tray.exe launch <game> -- %command%` runs Steam's command itself, with no window. The game starts even when GameSync can't check first; what needs you arrives as a notification.
- With the agent off, `launch` waits for the game to close and syncs it then.

**The now-playing marker** (PLAY-07) goes up at a session's start and comes down after its sync. This PC notes each marker it put up, so one it couldn't take down (offline at the exit) comes down at a later sync.

**The daily backup** (BG-02, BG-03, SET-02, SET-03; `Host/Daily.cs`, `Host/Schedule.cs`). `schedule daily 20:00` adds two Task Scheduler tasks through `schtasks`, from a task definition:

- the daily run at 20:00;
- a catch-up 10 minutes after sign-in, which runs only when the PC was off at 20:00 (`daily --if-missed`).

Both run `GameSync.Tray.exe daily` as you, without admin rights, at below-normal priority. They're stopped after 2 hours and never wake the PC. While the agent runs, it takes the daily run over.

The run skips running games, keeps the saves of games that updated, and syncs the rest. It refreshes the save list when it's a week old, logs one line per game, and records the run for `schedule`.

**Notifications** (BG-05, BG-06) only come for what needs you: a conflict, saves missing, a blocked file, saves that may have moved, or an expired sign-in (one for all games).

- They're held while a fullscreen game or a presentation runs (`SHQueryUserNotificationState`), and shown after it closes.
- Each shows once while its problem lasts, and more than three at once become one.
- The first one registers GameSync's name for Windows notifications, under `HKCU\Software\Classes\AppUserModelId\GameSync`.
- The background app writes a log a day in the data folder's `logs`.

## Results

The owner records Pass, Fail or Blocked in the live requirements doc. Automated tests: 287 pass (4 skipped: the live Drive and real-saves runs, which need the owner's setup).

| ID | What was checked |
| --- | --- |
| PLAY-04 | `SessionTests.PLAY_04_*` (2 s before, 10 s after once saves are quiet for 5 s; a launcher handing over; a game already running). With the stand-in game in `AgentTests` and `BackgroundTests.PLAY_08_*`, the save written while playing is in the session. |
| PLAY-05 | `WatcherTests` (found from its install folder, the same name elsewhere not; launchers, crash reporters, updaters and anti-cheat left out). |
| PLAY-06 | `SessionTests.PLAY_06_*`. |
| PLAY-03, PLAY-07 | `PlayTests` (a newer save comes down; a conflict is left alone and said; another PC playing until its save is back; "never synced back" after 12 hours; a marker that couldn't come down offline comes down later). |
| PLAY-08 | `BackgroundTests.PLAY_08_*`: Steam's command runs even with GameSync not set up; with the agent off, launch waits for the game, syncs it, and takes the marker down. |
| PLAY-09 | Command line, scratch folder: two recorded sessions show as "2 h 30 min played, last on 2026-09-28" in `games`. |
| BAK-10, BG-08 | `PlayTests.BAK_10_*`, `PlayTests.BG_08_*`, `BackgroundTests.BAK_10_*`. |
| BAK-06 | `PlayTests.BAK_06_*`. |
| BG-01, BG-09 | `AgentTests` (the engine one at a time; a second agent stays out). |
| BG-02, SET-02 | `BackgroundTests` (the tasks' definitions: triggers, as you without admin, below-normal priority, never waking the PC; when a daily run counts as missed). No task was added to this PC. |
| BG-05, BG-06 | `BackgroundTests` (held while busy and shown once after; a problem that comes back shows again; many become one; a healthy sync notifies nobody; an expired sign-in is one notification). No notification was shown on this PC. |

**The done-when check**, 28 Sep 2026, `AgentTests.Play_quit_and_the_laptop_has_it_without_a_single_click`. DESKTOP and LAPTOP are two data folders sharing a cloud folder:

- The stand-in game, installed as a game with its own folder, runs on DESKTOP for 3 seconds and writes its save at 1 second.
- DESKTOP's agent notices it, records the session once it has closed, uploads the save as a session version, and takes the marker down.
- LAPTOP's agent, on its first look, brings the save down. Nothing was clicked or typed on either.

**To try on the real PCs** (with the two-PC week, or before):

- PLAY-02: Steam, Epic and loose launches; a loose game's process isn't elevated.
- PLAY-05: start a game from Steam, and it shows Playing within a few seconds (`gamesync games`).
- BG-01: after `schedule background on`, sign out and in: one background app.
- BG-06: cause a conflict during a fullscreen game, and the notification appears after you quit.
- SET-02: set a daily time with the PC off then, and the run happens about 10 minutes after sign-in.

## Known limits

- **No tray icon or window yet** (Milestone 5). Notifications and the log are how the background app talks; `gamesync games` shows the rest.
- **A warning before a launch doesn't stop it.** With no window to ask in, it's shown in the terminal or as a notification, and the game starts. Milestone 5 asks first.
- **The command line waits for the agent rather than handing it the work** (BG-09). The daily run and "done" are handed over. `sync`, `restore` and the rest run in the command's own process, one engine at a time.
- **Store links hand the launch to the store.** With the agent off, if the game doesn't show up running within 3 minutes, `launch` says so and stops waiting.
- **Builds are read for Steam, Epic and loose games**; EA games have no build check yet.
- **"Saves may have moved"** is checked for games from the library (found by a scan), not for games added by hand to games.json.
- **The optional daily summary** (BG-05) isn't built.
- **Epic launches** haven't been tried with a real Epic game yet.
- **Building while the background app runs from the build folder fails**, since its files are in use. Run it from a published copy, as in Try it. The build folder is now `src\GameSync.App\bin\Debug\net10.0-windows10.0.19041.0`, for Windows' notifications.
