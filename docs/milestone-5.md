# Milestone 5: the UI

Started 28 Sep 2026.

**Done when** a friend installs and syncs without help (design.md → Milestones).

## Scope

| Area | IDs |
| --- | --- |
| Launcher and library | PLAY-01 home, PLAY-09 playtime on tiles and the activity calendar, LIB-11 art in every slot, LIB-12 rescans at startup and when a store changes, ART-01 to ART-08 cover art |
| Game detail | BAK-14 versions across PCs, BAK-15 history size, BAK-16 Back up now, BAK-18 named saves, FOLD-01 a save place by hand, SYNC-09 per-game conflict choice |
| Sync screens | SYNC-10 the conflict screen, SYNC-14 Plan, SYNC-04 Swap |
| Save manager and sharing | MGR-01 to MGR-06, SHARE-01 to SHARE-13 |
| Settings | SET-01 to SET-04, FOLD-02 to FOLD-10, PC-01 and PC-02, CLOUD-05, CLOUD-08, CLOUD-09 |
| Appearance | LOOK-01 to LOOK-11 |
| First run | ONB-01 to ONB-05 |
| Tray and background | BG-01 one instance, the window comes forward, BG-07 tray icon, BG-09 the command line hands off to the app |
| Accessibility and speed | A11Y-01 to A11Y-04, PERF-01 to PERF-03 |

Not in this milestone: learn mode and its tracer (FIND-04, FIND-05, Milestone 6), the installer, updater and signing (PKG-01, PKG-03, R19: Milestone 6), `gamesync://` links (R12, registered by the installer), achievements (ACH-01 to ACH-04, after v1).

The screens are designed: the Launcher, Save manager and Settings since 27 Sep, and Game detail, Conflict, Plan and First run since 28 Sep, all in the design system (link in `CLAUDE.md`, snapshot in `design/system/`).

## How it's built

**One program for the tray and the windows.** `GameSync.Tray.exe` becomes an Avalonia app: the tray icon, the main window, the agent from Milestone 4 running inside it, and notifications. It still runs single jobs with no window (the daily backup, launches from Steam). The screens live in a library, `GameSync.UI`, so they can be tested and screenshotted without a display (Avalonia's headless platform). `gamesync.exe` stays the command line.

**Starting it for the person, never asking for admin.** First run turns on starting at sign-in and the daily backup, both on by default, and Settings turns them off. Neither needs admin: starting at sign-in is an entry in the person's own Run key, which Task Manager's Startup apps shows and can turn off, and the daily backup and its catch-up are Task Scheduler tasks the person owns (any signed-in user can add them, checked on 28 Sep). No command to type and no admin prompt.

**One instance, and the command line hands off** (BG-01, BG-09). The running app listens on a named pipe that only this Windows user can open. Starting the app again brings its window forward. `gamesync sync --all`, `plan`, `launch` and `restore` send their job over the pipe when the app is running, show its progress, and print the result; with the app closed they run as now, through the engine lock.

**Themes** (LOOK-01 to LOOK-11): the design system's theme engine, ported to C#, turns the four choices (mode, pure black, preset, swatches) into every colour. A test compares its output with the design system's own engine for every preset, mode and swatch, and checks every contrast pair (LOOK-09). Match Windows and the Windows accent follow Windows live. Choices are saved per PC.

**The look**: the design system's tokens become Avalonia resources, and each component (buttons, pill tabs, cards, status badges, the rail, tiles, the hero, console tables and logs, settings rows, folder fields) becomes a styled control with the same name. The icons come from the design system's own set. Onest and JetBrains Mono ship inside the app, with Segoe UI Variable and Cascadia Mono as fallbacks.

**What the screens show** comes from read models in `GameSync.Host` (the library, a game, the plan, a conflict, the save manager's table), so every number on screen is tested without the UI.

**Cover art** (ART-01 to ART-08): Steam's store API names each game's images; they're downloaded once into `art\`, checked (JPEG, PNG or WebP by content, size-capped) and shown offline. No art means a title cover. The privacy policy and README gain Steam's image servers.

**Home is the launcher; saves are their own tab** (the owner, 28 Sep). Home and the game library show games: the last-played one as the hero, your covers, play time, and later achievements. The save manager and the console hold the save data, and Settings holds folders. Two things keep it about games:

- **Software stays out.** Steam also installs software (Wallpaper Engine, Lossless Scaling) and software demos (3DMark Demo); Steam's store says which, and those go under the library's Software tab instead of Home.
- **Steam's own play record fills in.** For Steam games, last played and total hours come from Steam's record on this PC (each account's `localconfig.vdf`) until GameSync has sessions of its own, so Home is useful from the first run. The activity calendar is GameSync's own, since Steam keeps no daily record.

## Progress

- 28 Sep: the four remaining screens designed and published to the design system. `GameSync.UI` started: the theme engine (matches the design system's for every test case; every theme passes LOOK-09), the design system's components as Avalonia controls, and `tools/GameSync.Snapshots`, which renders pages to PNG files with no display.
- 28 Sep: cover art (`GameSync.Core/Art`, `gamesync art`), Steam's play record, and the Home and Library pages. On the owner's PC, read-only: 58 games, 52 with Steam's art and 6 with title covers; 3 Steam apps under Software.
- 28 Sep: art comes from the Steam client's cache on the PC first. On the owner's PC, a first run took 118 images from it and downloaded 41 for games the client hasn't cached, asking Steam's store once about 55 games; a second run contacted nothing. Games can be hidden from the launcher (`gamesync hide`, or right-click a tile; a Hidden tab brings them back). Onest and JetBrains Mono ship inside the app. Windows 10 22H2 is supported.

## Build order

1. The foundation: `GameSync.UI` with the theme engine and its tests, tokens as resources, the icon set, and the styled components.
2. The app shell: the tray icon and its states, the main window with the side rail, one instance and the pipe, the agent inside, the theme applied live.
3. Read models for the library, a game, the plan, a conflict and the save manager.
4. The screens, in order of use: first run, the launcher home and library, game detail, the save manager with Plan, Versions and Log, the conflict screen, settings, and the share and import windows.
5. Cover art and the activity calendar.
6. The command line handing off to the app.
7. Accessibility (keyboard, focus, screen-reader names, text size, animations off) and speed with 500 games.
8. The done-when check: a self-contained zip on a fresh Windows user, set up with no help.
