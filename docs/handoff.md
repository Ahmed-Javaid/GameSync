# Build handoff

Updated 28 Sep 2026, mid-Milestone 5, after the app shell. Read `CLAUDE.md` first, then this page, then `docs/milestone-5.md`.

## Where things stand

- **Milestones 1 to 4 are built and pushed**: the sync engine (1), Google Drive and a second PC (2), detection and save discovery (3), sessions, launching and the background app (4). Each has its report in `docs/milestone-N.md` with results and known limits. The two-PC week waits until the whole app is built (the owner's choice).
- **Milestone 5, the UI, is under way.** Plan and scope: `docs/milestone-5.md`. Built so far:
  - Every screen is designed in the design system: Launcher home, Save manager, Settings, Game detail, Conflict, Plan (a tab of the save manager) and First run. Snapshot in `design/system/`; open `design/system/viewer.html`.
  - `src/GameSync.UI` (Avalonia 12.1.3, CommunityToolkit.Mvvm): the theme engine ported from the design system and tested against its output (`ThemeTests`); the design system's components as styled controls; the Home, Library and Console pages; `Views/MainWindow` around the shell with the side rail; `Theming/Look.cs`, the person's look per PC in state.db; `Branding/MarkArt.cs`, which draws the tray icons and the app icon in code.
  - **The app shell (done 28 Sep)**: `GameSync.Tray.exe` is the app. `src/GameSync.Tray/Program.cs` routes a start: jobs with no window (`daily`, `launch ... -- %command%`, any command) as before; otherwise one app per Windows user and data folder (`AppPipe.TryClaim`, a named mutex), and a second start sends `show` over the pipe (`AppPipe`, current user only). `TrayApp.cs` holds it together: Windows' own tray icon (`GameSync.Windows/TrayIcon.cs`, Shell_NotifyIcon, a native menu that follows Windows' dark mode), the window (made on open, let go on close), the agent inside (`Host/AppAgent.cs`, with `AppOutput`'s events), the look following Windows live, art refreshed 30 seconds after start. Start at sign-in is the person's Run key (`GameSync.Windows/SignInStart.cs`, `gamesync schedule background on`).
  - **Home and the library work (28 Sep)**: the library's tabs switch its view in place (All games, Needs you, Software, Hidden, kept when the pages refresh); Home's tabs are the library's views, as the design system labels them, so My games and Needs you open the library on that view; Sync now, the card arrows and Continue playing work (`LauncherActions` in `ViewModels/LauncherViewModels.cs`, wired in `TrayApp`). Play goes through `Host/AppActions.PlayAsync`, which is `gamesync launch`: the check first for a game that syncs, and since 28 Sep a game found but not synced starts its store's way with nothing to check.
  - Cover art and play time: `src/GameSync.Core/Art`, `Discovery/SteamActivity.cs`, `src/GameSync.Host/Launcher.cs` and `LauncherData.cs`, `gamesync art [--refresh]`, `gamesync hide|unhide <game>`.
  - `tools/GameSync.Snapshots`: renders pages to PNG files with no display, in Dark, Light and Sakura; `icons` writes `gamesync.ico` and a sheet of every tray state.
- **Tests**: 331 pass, 4 skipped (the live Drive and real-saves runs, which need the owner's setup).

## What's next, in order

1. **Home and the library, the rest**: most of it works since 28 Sep (see Where things stand). Left, as their screens arrive: a tile opens its game (game detail), Needs you's buttons (Resolve, Review, Add a place: the conflict screen and game detail), "Choose games to sync" (first run's list of found games; today it opens the library), and the top bar's Google Drive and This PC buttons (settings).
2. **Glossy and Solid** (LOOK-17, LOOK-18, decided 28 Sep, Glossy the default; do it before the other screens so each is built in both modes): first the design system (tokens for the three glass strengths, the backdrop, the Surface setting, the screens in both modes), then the app: `Look` and `ThemeChoice` gain the mode, `ThemeService` swaps the surface brushes, a backdrop behind the pages in `MainWindow` (the hero art blurred once into a small bitmap, darkened as much as that picture needs), the fallbacks (Windows' transparency effects off, pure black, no art), and the contrast test over worst-case pictures. The values are in the mockups' boards (`design/mockups/`, each board's `renderVals` script: `Glossy` and `Solid`).
3. **The owner's library wishes (28 Sep, added to the list, not started)**: sorting the library by name, recently played and the like; favourite games, kept at the top (the design system's launcher already had a Favourites tab); a search; a list view like Steam's beside the cover grid; and every game opening its own page when clicked (game detail, in the next item).
4. **The other screens**, in the design system's layouts: first run (it turns on start at sign-in and the daily backup for the person, no admin: see Decisions; it replaces the "not set up yet" page; as on the mockups canvas since 28 Sep: no Ludusavi, and Add a game or folder, LIB-13, which the library's Add game button opens too), game detail, the save manager with Plan, Versions and Log, the conflict screen, settings (with the look, start at sign-in, Copy diagnostics), the share and import dialogs (sharing as decided on 28 Sep).
5. **The command line hands off to the app** (BG-09): `sync --all`, `plan`, `launch` and `restore` send their job over `AppPipe` when the app runs, and print its progress and result. The pipe already answers `show`, `status` and `quit`.
6. **Finishing the components**: a TextBox theme like the design's `gs-input`; the context menu in app colours; check every focus ring.
7. **Accessibility and speed** (A11Y-01 to A11Y-04, PERF-01 to PERF-03), then **the done-when check**: a self-contained zip set up by a friend with no help. That needs the owner's Google app published and release builds carrying its client (CLOUD-12): add the build step that puts the client in, from a git-ignored file, before the zip is made.

## How to work

```powershell
dotnet build GameSync.sln
dotnet test GameSync.sln
# The app on a scratch data folder, so nothing of the owner's is touched (see below for making one).
& src\GameSync.App\bin\Debug\net10.0-windows10.0.19041.0\GameSync.Tray.exe --data <scratch>
# Render pages to PNG files: the component galleries, or Home and the library from a data folder's games and art.
dotnet run --project tools/GameSync.Snapshots -- <output folder> gallery gallery2 gallery3
dotnet run --project tools/GameSync.Snapshots -- <output folder> --data <data folder> home library library-full library-hidden
# The app's icon file and a sheet of the tray icon in every state (copy gamesync.ico to src/GameSync.Tray/Assets; a test checks it's current).
dotnet run --project tools/GameSync.Snapshots -- <output folder> icons
```

- **Real data without touching the owner's**: make a scratch data folder with `gamesync --data <scratch> init --remote <scratch>\cloud --name DESKTOP`, `add-folder E:\Games`, `add-folder G:\`, `scan` (reads only), then `gamesync --data <scratch> art`. Render with `--data <scratch>`, or open the app with it. **Never confirm games in a scratch folder the app opens**: the app's agent syncs confirmed games by itself, and that copies real saves.
- **Talking to a running app**: the pipe is named `GameSync-` plus the first 20 hex digits of SHA-256 of `<user SID>|<DATA FOLDER, full path, upper case, no trailing \>`; send one line (`status`, `show`, `quit`) and read one line back. `AppPipe.SendAsync` does it from C#.
- **Look at the real window** by capturing it with `PrintWindow` from PowerShell (the window's title is "GameSync"), and at renders with the Read tool on the PNG files; compare with the design system's screens (`design/system/viewer.html`).
- **Drive the real window** through UI Automation from PowerShell (`UIAutomationClient`): find an element by its name, then `SelectionItemPattern.Select()` for a pill tab or `InvokePattern.Invoke()` for a button. It's also a quick screen-reader check: every name should read as a person would say it. Never invoke Play on the owner's PC; it starts the real game.
- **The theme engine's expected values** come from the design system's own JavaScript: `node tests/GameSync.Core.Tests/Fixtures/make-theme-goldens.js` regenerates `theme-goldens.json` after a design-system theme change.
- **Changing the design system** (online, link in `CLAUDE.md`): list its files, read the ones to change, write them under one scratch folder at `project/<path>`, and publish with `root` set to that folder; the index `project/design-system.json` goes last, re-read just before, with its `lastChange` updated. Then copy the changed files into `design/system/` (and `viewer.html` embeds the screens: see how 28 Sep's change was scripted, adding entries after the SettingsScreen card).
- **Measuring memory and CPU**: `dotnet-counters` and `dotnet-gcdump` are installed as global tools on the owner's PC; `dotnet-gcdump collect -p <pid>` also forces a full collection, which tells live memory from garbage.

## Gotchas found so far

- **Avalonia 12**: a control theme's nested styles can't reach into content (`^:selected c|GsIcon` throws at start). Put such rules in the global styles (`Styles/Type.axaml`) with a class, as `GsIcon.selectable` does.
- **Avalonia 12**: an `Image` sets its own height from its picture; banners use `Controls/GsCoverImage` (CSS `cover`, never sizes its parent) instead.
- **Avalonia 12** needs a text shaper: the app calls `UseHarfBuzz()` (package `Avalonia.HarfBuzz`), or it stops at start with "No text shaping system configured". The headless snapshot tool doesn't need it.
- **Avalonia 12 draws its own tray menus** with the app's theme, so GameSync doesn't use Avalonia's `TrayIcon`: `GameSync.Windows/TrayIcon.cs` calls Shell_NotifyIcon and shows Windows' own menu, which follows Windows (LOOK-11). Its hidden window is a top-level one on purpose: only those hear theme changes and the sign-out.
- **Rendering**: the app uses `Win32RenderingMode.Software`. With the GPU it waited in the tray at about 120 MB and 160 MB after the window closed; with Skia on the CPU, about 70 to 80 MB and 107 MB, looking the same. Revisit only if PERF-03 finds scrolling slow.
- **Memory in a quiet app**: .NET collects rarely when little is allocated, so garbage from a burst of work (the save list parsed for art, a sync) can sit for hours. `TrayApp.LetGoSoon` runs a full collection when the window closes and after background work, at most once a minute. A heap dump showed about 6 MB of live objects when idle.
- **Windows 11 hides a new tray icon** in the overflow (the ^ by the clock) until the person drags it out or turns it on in Settings → Personalization → Taskbar → Other system tray icons. First run should say so.
- **Screen-reader names of list items**: a `ListBoxItem` without a name reads its item's `ToString()`, which for a record is "NavItem { Id = … }". Records shown in lists override `ToString()` with what a person should hear (`NavItem`, `ThemeCard`, `SwatchCard`); give new ones the same.
- **Resources across files**: cross-file references use `DynamicResource`; `StaticResource` only within one dictionary.
- **PowerShell** reads a curly apostrophe (’) as a quote mark, so a script that contains one fails to parse. Edit such text with the Edit tool. Windows PowerShell 5.1 also has no `??`.
- **The snapshot tool** needs `UseHeadlessDrawing = false` with Skia for real text and images; `Bitmap.Save(string)` shows an obsolete warning there, which is harmless.
- **Steam's store API** (`IStoreBrowseService/GetItems`): `type` 0 is a game, 6 software, 1 a demo whose `related_items.parent_appid` decides it. The Steam client's cache (`<Steam>\appcache\librarycache\<appid>\`, hashed subfolders too) has 300×450 covers, full-size heroes and logos.

## Decisions (don't re-ask)

Made on 28 Sep, before the app shell:

- **Home and the game library are about games**: art, play time, and later achievements. Saves live in the save manager, folders in Settings.
- **Steam software** (Wallpaper Engine, Lossless Scaling, 3DMark Demo) goes under the library's Software tab.
- **Any game can be hidden** from the launcher, per PC, without changing its sync; a Hidden tab brings it back.
- **Art comes from the Steam client's cache first.** Steam's store is asked once per new game, by app ID only. Downloaded art is checked monthly, and missing art weekly.
- **Hours and last played** come from Steam's local record until GameSync has sessions of its own. The activity calendar uses GameSync's sessions only.
- **No admin for scheduling.** Start at sign-in uses the person's Run key, so Task Manager's Startup apps shows it. The daily backup and its catch-up are Task Scheduler tasks the person owns. First run turns both on by default, and Settings turns them off.
- **Windows 11 and Windows 10 22H2** are supported. Onest and JetBrains Mono ship inside the app.
- **Plan** is a tab of the save manager; Run N changes replaces Sync now there.

The owner's answers on 28 Sep, later:

- **Tray icon**: option C, the mark in the taskbar's own white or black with a badge in the status colours (drawn in the chat, built in `MarkArt`); not the whole mark in colour, nor design.md's old green and blue.
- **Friends' sign-in**: release builds carry the owner's Google client, never the public repository (CLOUD-12). The owner publishes the Google app.
- **Sharing**: mostly current saves, and quick. Share all packs each game's current save; any version can be shared, even one only in the cloud, and only that version's files download, since the version list is already small records (SHARE-04, SHARE-05). Multiplayer world files can be shared too; anti-cheat and online-only games stay locked out (R16).
- **Accepted as proposed**: the speed targets (PERF-01 to PERF-03), Copy diagnostics in Settings (SET-05), your own backup folder with full history on the PC (FOLD-02, FOLD-06, BAK-17), one Google account per PC (CLOUD-13), English only (PKG-07).
- **Glossy and Solid** (after comparing three looks on the mockups canvas): two surface modes, no middle one. Glossy shows a blurred copy of the game's art through the app: full glass on game detail, conflict and the save manager; Home a step more solid than full glass; first run and settings only a soft glow at the top over nearly solid cards. Solid is today's plain look. Settings → Appearance → Surface picks one, per PC, and a fresh install starts in Glossy (the owner's choice) (LOOK-17, LOOK-18; design.md → UI). The design session first numbered them LOOK-15 and LOOK-16, which the Later rows for tinting pages from cover art and a theme in first run already had; IDs are never reused, so they're LOOK-17 and LOOK-18.
- **First run without Ludusavi**: the Ludusavi import (ONB-04) is command line only; almost nobody migrates that way. Choose games takes the full width, ends with "Something missing? Add a game or folder", and has the anti-cheat note (ONB-05) beside Back and Continue. The store-cloud group is "Synced by their store", and the status everywhere reads "Synced by Steam" (or Epic, Xbox) instead of "Backup only", which confused the owner. The design system's First run still has the old Ludusavi side column; update it with the Glossy work.
- **Your own games and folders** (LIB-13): Add a game or folder (first run, and the library's Add game) takes a name, a folder and optionally the program that uses it; it syncs when that program closes, or once the folder has been quiet for 5 minutes, for a game server's world. Program files are never copied (R1). The engine already has hand-added entries (Ludusavi's custom games, `games.json`); new are the dialog and the quiet-folder session in the agent. The dialog is on the mockups canvas.
- **Made while building** (the owner can overrule): the window closes to the tray and Quit is in the tray menu; the tray menu is Open GameSync, Sync now, Quit GameSync; a click on the icon opens the window; the app draws on the CPU (see Gotchas); the app icon is the cyan mark on a dark rounded tile.

## Waiting on the owner

Done on 28 Sep: the Google app is published (In production, External; its 100-user cap counts only sensitive scopes, and `drive.file` isn't one), the screen designs were reviewed and redone on the mockups canvas, and Glossy is the default.

- Sign in to Drive once more (`gamesync signin`): the saved sign-in was made while the Google app was in Testing, so it still runs out 7 days after it was made. Then CLOUD-02's 8-day check can pass.
- Delete the two leftover "GameSync test …" folders in Drive (search "GameSync test" at drive.google.com), left by the live Drive test runs. Not `tests\GameSync.Core.Tests`, which is the test suite.
- Still open in `docs/design.md` and the requirements' Gaps: the name, the installer, screenshots, friends' setups beyond Windows, the server, the SteamGridDB key, how a shared zip reaches Import saves, uninstall, and reading achievements from Steam's files.

## Rules and tests

- The rules in `CLAUDE.md` and R1 to R21 in design.md never break; each has an automated test.
- Name tests and issues after requirement IDs, for example `FOLD-04`. The owner records Pass, Fail or Blocked in the live requirements doc; `docs/requirements.md` is a copy.
- Commit or push only when the owner asks. Commits go to `main`, ending with the Co-Authored-By line.
