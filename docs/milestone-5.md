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
| Appearance | LOOK-01 to LOOK-11, LOOK-17 and LOOK-18 Glossy and Solid |
| First run | ONB-01 to ONB-05 |
| Tray and background | BG-01 one instance, the window comes forward, BG-07 tray icon, BG-09 the command line hands off to the app |
| Accessibility and speed | A11Y-01 to A11Y-04, PERF-01 to PERF-03 |

Not in this milestone: learn mode and its tracer (FIND-04, FIND-05, Milestone 6), the installer, updater and signing (PKG-01, PKG-03, R19: Milestone 6), `gamesync://` links (R12, registered by the installer), achievements (ACH-01 to ACH-04, after v1).

The screens are designed: the Launcher, Save manager and Settings since 27 Sep, and Game detail, Conflict, Plan and First run since 28 Sep, all in the design system (link in `CLAUDE.md`, snapshot in `design/system/`), each in Glossy and Solid since version 13.

## How it's built

**One program for the tray and the windows.** `GameSync.Tray.exe` becomes an Avalonia app: the tray icon, the main window, the agent from Milestone 4 running inside it, and notifications. It still runs single jobs with no window (the daily backup, launches from Steam). The screens live in a library, `GameSync.UI`, so they can be tested and screenshotted without a display (Avalonia's headless platform). `gamesync.exe` stays the command line.

**Starting it for the person, never asking for admin.** First run turns on starting at sign-in and the daily backup, both on by default, and Settings turns them off. Neither needs admin: starting at sign-in is an entry in the person's own Run key, which Task Manager's Startup apps shows and can turn off, and the daily backup and its catch-up are Task Scheduler tasks the person owns (any signed-in user can add them, checked on 28 Sep). No command to type and no admin prompt.

**One instance, and the command line hands off** (BG-01, BG-09). The running app holds a named mutex for this Windows user and data folder, and listens on a named pipe that only this Windows user can open. Starting the app again sends `show` over it and the window comes forward; an installer can send `quit`, and `status` gives the tray icon's state and hover text. `gamesync sync --all`, `plan`, `launch` and `restore` will send their job over the pipe when the app is running, show its progress, and print the result; with the app closed they run as now, through the engine lock.

**The window comes and goes; the app stays.** Closing the window leaves GameSync in the tray with its agent watching, and lets go of the window and its pictures, so it waits small (PERF-01). The tray icon opens a new window; Quit is in its menu. The app draws with Skia on the CPU rather than the GPU: it looks the same, and it waits in the tray at about 70 MB rather than 120.

**Themes** (LOOK-01 to LOOK-11): the design system's theme engine, ported to C#, turns the four choices (mode, pure black, preset, swatches) into every colour. A test compares its output with the design system's own engine for every preset, mode and swatch, and checks every contrast pair (LOOK-09). Match Windows and the Windows accent follow Windows live. Choices are saved per PC.

**Glossy and Solid** (LOOK-17, LOOK-18; the owner, 28 Sep; a fresh install starts in Glossy): the theme engine gains a fifth choice, the surface. Glossy swaps the surface colours for see-through ones in three strengths (full glass, Home's, and the soft glow of first run and settings, as drawn on the mockups canvas), laid over the theme's for the window's frame and page only, and adds a backdrop behind them: the last-played game's hero art, blurred once into a 160 × 100 picture off the UI thread, and darkened row by row as much as that picture needs for every text colour to keep 4.5:1. Each page says its strength, and the frame follows it. Solid is the look from before, unchanged: its edges are clear. Both are tested like the themes: the engine against the design system's for every choice, and the backdrop's contrast over worst-case pictures for every dark theme and strength. Glossy goes Solid in light mode, with pure black, with Windows' transparency effects off, and with no art.

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
- 28 Sep, later: the owner settled the open questions (see handoff.md → Decisions) and **the app shell is built**. `GameSync.Tray.exe` is now the app: the window with the rail, Home, the library, a live Console page and placeholders for the save manager and settings; Windows' own tray icon in the owner's chosen style (the mark in the taskbar's ink, a badge in the status colours) with its menu; the agent running inside; one copy per Windows user, a second start bringing the window forward over a pipe; the look from the saved choice, following Windows' mode and accent live; art refreshed 30 seconds after start. Starting at sign-in moved from Task Scheduler to the person's Run key (`gamesync schedule background on`). Checked on the owner's PC with a scratch data folder: tray only, 84 MB at start and about 70 to 80 MB idle; with the window open, 223 MB; closed again, about 107 MB; idle CPU 0.05% of the machine; a second start exits in about a second with the window forward.

- 28 Sep, last: Home and the library work. The library's tabs switch its view in place; Home's tabs are the library's views (the design system labels them "Library view"), so My games and Needs you open the library there; Sync now, the card arrows and Continue playing work, and Play launches like `gamesync launch`, which now also starts games found but not synced. Checked in the real window through UI Automation on the scratch folder (Play left alone: it starts the real game). A screen-reader bug found on the way: pill tabs read as "NavItem { Id = … }"; list items now read their labels.

- 28 Sep, night: **Glossy and Solid** (LOOK-17, LOOK-18). First the design system (version 13): the engine's `glass()` with its three strengths and six more tokens the boards needed (the art ring, the rail dot's ring, the dialog edge, wells, fields), edges and a lit top on cards, the boards' new look for things on cover art in both surfaces, the Surface choice on the Settings screen, which every screen follows, and First run without Ludusavi as on the canvas. Then the app, from the same values: the surface per PC (`gamesync set surface glossy|solid`, which an open app follows at once), the backdrop, the fallbacks, and the store-cloud status reading "Synced by Steam" instead of "Backup only" (LIB-10). Checked on the scratch folder: the window opens in Glossy over Risk of Rain 2's art; the console switches to full glass; `set surface solid` repaints the open window and `glossy` brings it back; idle CPU 0.07% of the machine; with the window open about 3 MB more than Solid (208 against 205 MB private), and nothing once it closes. The contrast test runs every dark theme and strength over 12 worst-case pictures and checks every pixel of the result; a bright picture's backdrop takes about 6 ms to make. Not yet checked: turning off Windows' transparency effects on a real PC (the code reads the setting and follows Windows' change message).

- 29 Sep: from the owner's review at full screen: the pills drawn as ellipses (a regression from Glossy's edges) and the flickering ring on covers are fixed; the window has no separate title bar any more (the page and Glossy's art run to the top, with GameSync's own caption buttons, which keep Windows 11's snap layouts); Home's banner grows with the window and Jump back in shows as many covers as fit; Home says "No games yet" before any scan instead of an empty banner. The owner's own data folder was scanned for the first time (19 installed games, 52 in the library; nothing confirmed).
- 29 Sep, later: Home fills any window. The first scaling grew the banner with the window's width only, so a taller screen (1920 × 1200 against 1920 × 1080) left gaps again; now the cards take what their content needs at the width and the banner the rest, the side cards keep a readable width, and Jump back in shows more covers on wider screens. Checked at eight window sizes from 1024 × 640 to 3440 × 1440. The page header sits 16px closer to the top.

## How to try the app

```powershell
$gs = "src\GameSync.App\bin\Debug\net10.0-windows10.0.19041.0"
& "$gs\GameSync.Tray.exe"                      # the app, with its window; again, and the window comes forward
& "$gs\GameSync.Tray.exe" --background         # as at sign-in: the tray icon only
& "$gs\gamesync.exe" schedule background on    # start it at sign-in, from your Run key (Task Manager lists it)
```

It uses `%LOCALAPPDATA%\GameSync` like the command line, so on the owner's PC it syncs the confirmed games and signs in to Drive as `gamesync` does. `gamesync set surface solid` (or `glossy`) switches the look, and an open window repaints. With `--data <folder>` it uses another data folder. Windows 11 puts a new tray icon in the hidden overflow (the ^ by the clock); drag it onto the taskbar, or turn it on under Settings → Personalization → Taskbar → Other system tray icons.

## Build order

1. The foundation: `GameSync.UI` with the theme engine and its tests, tokens as resources, the icon set, and the styled components. **Done.**
2. The app shell: the tray icon and its states, the main window with the side rail, one instance and the pipe, the agent inside, the theme applied live. **Done 28 Sep.**
3. Read models for the library, a game, the plan, a conflict and the save manager.
4. The screens, in order of use: first run, the launcher home and library (**working since 28 Sep**, except what waits on the screens below), game detail, the save manager with Plan, Versions and Log, the conflict screen, settings, and the share and import windows.
5. Cover art and the activity calendar.
6. The command line handing off to the app.
7. Accessibility (keyboard, focus, screen-reader names, text size, animations off) and speed with 500 games.
8. The done-when check: a self-contained zip on a fresh Windows user, set up with no help.
