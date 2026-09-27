# Build handoff

Updated 28 Sep 2026, mid-Milestone 5. Read `CLAUDE.md` first, then this page, then `docs/milestone-5.md`.

## Where things stand

- **Milestones 1 to 4 are built and pushed**: the sync engine (1), Google Drive and a second PC (2), detection and save discovery (3), sessions, launching and the background app (4). Each has its report in `docs/milestone-N.md` with results and known limits. The two-PC week waits until the whole app is built (the owner's choice).
- **Milestone 5, the UI, is under way.** Plan and scope: `docs/milestone-5.md`. Built so far:
  - Every screen is designed in the design system: Launcher home, Save manager, Settings, and since 28 Sep Game detail, Conflict, Plan (a tab of the save manager) and First run. Snapshot in `design/system/`; open `design/system/viewer.html`.
  - `src/GameSync.UI` (Avalonia 12.1.3, CommunityToolkit.Mvvm): the theme engine ported from the design system, tested against its own output (`ThemeTests`); the design system's components as styled controls; the Home and Library pages with their view models; the shell with the side rail.
  - Cover art and play time: `src/GameSync.Core/Art` (Steam's art, taken from the Steam client's own cache first, the store API asked once per new game), `Discovery/SteamActivity.cs` (last played and hours from Steam's `localconfig.vdf`), `src/GameSync.Host/Launcher.cs` and `LauncherData.cs` (what Home and the library show), `gamesync art [--refresh]`, `gamesync hide|unhide <game>`.
  - Onest and JetBrains Mono ship in `src/GameSync.UI/Assets/Fonts` with their OFL licences.
  - `tools/GameSync.Snapshots`: renders pages to PNG files with no display, in Dark, Light and Sakura.
- **Tests**: 311 pass, 4 skipped (the live Drive and real-saves runs, which need the owner's setup).

## What's next, in order

1. **The app itself**: `GameSync.Tray.exe` becomes the Avalonia app. Build `AppBuilder.Configure<GsApp>().UsePlatformDetect()` in `src/GameSync.Tray/Program.cs` (today it runs `Background.RunAsync` with no window); keep the no-window jobs (`daily`, `launch ... -- %command%`) working from the same exe.
   - A main window hosting `Views/Shell` with `ShellViewModel`; pages come from `Application.DataTemplates` in `GsApp.axaml`.
   - The tray icon with its states and counts (BG-07); use the app's status colours (the Gaps row suggests it).
   - One instance (BG-01): a second start brings the window forward, through a named pipe only this Windows user can open. The same pipe takes the command line's jobs when the app runs (BG-09).
   - The agent from Milestone 4 (`Host/Agent.cs`) runs inside, with an `IAgentOutput` that feeds the UI and notifications.
   - The theme from the person's saved choices (per PC, in state.db settings), applied with `ThemeService.Apply`; Match Windows and the Windows accent follow `Application.PlatformSettings` live (LOOK-01, LOOK-04, LOOK-07).
   - Art refreshes in the background after a scan and at start (`LauncherData.FetchArtAsync`).
2. **Make Home and the library work**: tabs switch views, a tile opens its game, Play runs the launch (`Cli.LaunchAsync`'s logic), Hide on a tile calls `LauncherData.SetHidden` and reloads, Choose games to sync opens the library's confirm flow.
3. **The other screens**, in the design system's layouts: first run (it turns on start at sign-in and the daily backup for the person, no admin: see Decisions), game detail, the save manager with Plan, Versions and Log, the conflict screen, settings, the share and import dialogs.
4. **Finishing the components**: a TextBox theme like the design's `gs-input`; the context menu in app colours; check every focus ring.
5. **Accessibility and speed** (A11Y-01 to A11Y-04, PERF-01 to PERF-03), then **the done-when check**: a self-contained zip set up by a friend with no help.

## How to work

```powershell
dotnet build GameSync.sln
dotnet test GameSync.sln
# Render pages to PNG files: the component galleries, or Home and the library from a data folder's games and art.
dotnet run --project tools/GameSync.Snapshots -- <output folder> gallery gallery2 gallery3
dotnet run --project tools/GameSync.Snapshots -- <output folder> --data <data folder> home library library-full library-hidden
```

- **Real data without touching the owner's**: make a scratch data folder with `gamesync --data <scratch> init --remote <scratch>\cloud --name DESKTOP`, `add-folder E:\Games`, `add-folder G:\`, `scan` (reads only), then `gamesync --data <scratch> art`. Render with `--data <scratch>`. Never sync from it: that copies real saves.
- **Look at renders** with the Read tool on the PNG files; compare with the design system's screens (`design/system/viewer.html`, or headless Edge screenshots of a preview page served locally).
- **The theme engine's expected values** come from the design system's own JavaScript: `node tests/GameSync.Core.Tests/Fixtures/make-theme-goldens.js` regenerates `theme-goldens.json` after a design-system theme change.
- **Changing the design system** (online, link in `CLAUDE.md`): list its files, read the ones to change, write them under one scratch folder at `project/<path>`, and publish with `root` set to that folder; the index `project/design-system.json` goes last, re-read just before, with its `lastChange` updated. Then copy the changed files into `design/system/` (and `viewer.html` embeds the screens: see how 28 Sep's change was scripted, adding entries after the SettingsScreen card).

## Gotchas found so far

- **Avalonia 12**: a control theme's nested styles can't reach into content (`^:selected c|GsIcon` throws at start). Put such rules in the global styles (`Styles/Type.axaml`) with a class, as `GsIcon.selectable` does.
- **Avalonia 12**: an `Image` sets its own height from its picture; banners use `Controls/GsCoverImage` (CSS `cover`, never sizes its parent) instead.
- **Resources across files**: cross-file references use `DynamicResource`; `StaticResource` only within one dictionary.
- **PowerShell** reads a curly apostrophe (’) as a quote mark, so a script that contains one fails to parse. Edit such text with the Edit tool.
- **The snapshot tool** needs `UseHeadlessDrawing = false` with Skia for real text and images; `Bitmap.Save(string)` shows an obsolete warning there, which is harmless.
- **Steam's store API** (`IStoreBrowseService/GetItems`): `type` 0 is a game, 6 software, 1 a demo whose `related_items.parent_appid` decides it. The Steam client's cache (`<Steam>\appcache\librarycache\<appid>\`, hashed subfolders too) has 300×450 covers, full-size heroes and logos.

## Decisions made on 28 Sep (don't re-ask)

- **Home and the game library are about games**: art, play time, and later achievements. Saves live in the save manager, folders in Settings.
- **Steam software** (Wallpaper Engine, Lossless Scaling, 3DMark Demo) goes under the library's Software tab.
- **Any game can be hidden** from the launcher, per PC, without changing its sync; a Hidden tab brings it back.
- **Art comes from the Steam client's cache first.** Steam's store is asked once per new game, by app ID only. Downloaded art is checked monthly, and missing art weekly.
- **Hours and last played** come from Steam's local record until GameSync has sessions of its own. The activity calendar uses GameSync's sessions only.
- **No admin for scheduling.** Start at sign-in uses the person's Run key, so Task Manager's Startup apps shows it. The daily backup and its catch-up are Task Scheduler tasks the person owns. First run turns both on by default, and Settings turns them off.
- **Windows 11 and Windows 10 22H2** are supported. Onest and JetBrains Mono ship inside the app.
- **Plan** is a tab of the save manager; Run N changes replaces Sync now there.

## Waiting on the owner

- Review the four new screen designs in the design system (Screens: Game detail, Conflict, Plan, First run).
- Copy these changes into the live requirements doc (link in `CLAUDE.md`):
  - ONB-04 reworded.
  - ACH-01 to ACH-04 added.
  - BG-02 reworded.
  - PKG-06 set to Windows 11 and 10 22H2.
  - The Gaps rows for the wireframes and for Windows 10 removed.
- From Milestone 2: Branding and Publish app in Google Cloud, and delete the two leftover "GameSync test test-…" folders in Drive.
- Still open in `docs/design.md` and the requirements' Gaps: the name, the installer, screenshots, friends' setups, the server, sharing between friends, what Share all includes, tray icon colours, the SteamGridDB key, performance targets, uninstall, diagnostics, more than one Google account, language.

## Rules and tests

- The rules in `CLAUDE.md` and R1 to R21 in design.md never break; each has an automated test.
- Name tests and issues after requirement IDs, for example `FOLD-04`. The owner records Pass, Fail or Blocked in the live requirements doc; `docs/requirements.md` is a copy.
- Commit or push only when the owner asks. Commits go to `main`, ending with the Co-Authored-By line.
