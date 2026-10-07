// Renders pages of GameSync's UI to PNG files, in the themes asked for, with Avalonia's headless platform and Skia:
//   dotnet run --project tools/GameSync.Snapshots -- <output folder> [--data <data folder>] [--game <id>] [--size 1920x1128] [--hover x,y] [--click x,y] [--bench N] [--many N] [--tab N] [--text-scale 1.5] [page ...]
// Pages: gallery, gallery2, gallery3, busy (KAN-80's busy buttons and job progress), zenith (the Zenith medal at every
// size, earned and not, the badge alone and the monument, earned and not), and with --data, home, home-playing (the hero game running),
// home-second (the banner moved on to the next game played, KAN-125), library, library-installed,
// library-marks (every game given a made-up status: the marks of games that are fine, KAN-48, and the badges),
// saves-needs (the save manager's Needs you tab, KAN-46), library-installed (Installed only ticked, KAN-47),
// saves-by-name and saves-by-status (the Saves tab's tables sorted by a heading, each on its own: Every game by name; Syncing
// by status and Every game by status reversed, KAN-49),
// library-local, library-search and library-game (a game's page, the
// one --game names or the hero game), saves and game-saves (the save manager, and that game's saves in it), conflict and
// conflict-settled (a made-up conflict of that game, waiting and settled), plan (the save manager's Plan tab, made up),
// add-place and import-kept (those dialogs over
// its saves, made up), and
// properties-general, -art, -launch, -files, -saves and -sync (its Properties) from that data folder's games and art;
// first run over that data folder's library, as if it weren't set up (its stores' folders made up): setup-scan (the scan
// under way), setup, setup-choose, setup-cloud, setup-cloud-folder, setup-daily and setup-daily-time; home-nocloud and
// connect-cloud (Home after Skip for now, and its Connect the cloud dialog); add-game, add-game-program and setup-add-game
// (Add a game or folder over the library and over first run, made up); settings, settings-storage, settings-backup,
// settings-cloud, settings-devices, settings-notifications, settings-achievements and settings-safety (Settings from that data folder's own
// settings, SET-01), settings-cloud-drive (signed in to a made-up Drive) and settings-backup-apply (a default changed,
// offering to apply it); settings-updates, -latest, -downloading and -refused (Updates as GameSync 1.0.0 installed, with a
// made-up 1.0.1, PKG-03); and the
// Glossy window (LOOK-17) over the last-played game's art: glossy-home, glossy-library, glossy-game, glossy-saves,
// glossy-game-saves, glossy-conflict, glossy-properties, glossy-console, glossy-settings, glossy-setup, glossy-setup-choose.
// Themes: every page in Arcade dark, Arcade light and Sakura dark; Glossy in light mode is light glass (design system version 35).
// "icons" writes gamesync.ico (copy it to src/GameSync.Tray/Assets) and a sheet of the tray icon in every state.
// Timings (KAN-124): "backdrop-timing" with --art <folder> says how long each picture's backdrop takes in each mode and
// strength; "look-timing" with --data how long a change between Dark and Light takes in Glossy for the window's own work.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;
using GameSync.Snapshots;
using GameSync.UI;
using GameSync.UI.Theming;
using GameSync.UI.ViewModels;
using GameSync.UI.Views;

var list = args.ToList();
var dataDir = TakeOption(list, "--data");
// The game whose page library-game and glossy-game show; the hero game by default.
var gameId = TakeOption(list, "--game");
// A window size for the app's pages instead of the design's 1280 Ã— 800, such as a maximized window's.
var size = TakeOption(list, "--size")?.Split('x') is [var w, var h] ? (Width: int.Parse(w), Height: int.Parse(h)) : (Width: 1280, Height: 800);
// The mouse pointer resting at x,y (window coordinates), to see hover states.
var hover = TakeOption(list, "--hover")?.Split(',') is [var hx, var hy] ? new Point(double.Parse(hx), double.Parse(hy)) : (Point?)null;

// A click at x,y (window coordinates) before the render, to see what it opens, such as a flyout.
var click = TakeOption(list, "--click")?.Split(',') is [var cx, var cy] ? new Point(double.Parse(cx), double.Parse(cy)) : (Point?)null;
// KAN-58: --bench N scrolls the page's biggest scroller N times, rendering a whole frame each time, and prints how long
// the frames took (Skia on the CPU, as the app draws), in place of saving a picture.
var bench = TakeOption(list, "--bench") is { } benchFrames ? int.Parse(benchFrames) : 0;
// PERF-03: --many N makes the data folder's games up to N (copies of them, every other one syncing, each with a backup
// made up) and the save manager's log 10,000 lines, for --bench to time a big library: its first frame and its scrolling.
var many = TakeOption(list, "--many") is { } manyGames ? int.Parse(manyGames) : 0;
// A11Y-04: --text-scale 1.5 renders with Windows' text size at 150%, to see nothing is clipped.
var textScale = TakeOption(list, "--text-scale") is { } scaleText ? double.Parse(scaleText, System.Globalization.CultureInfo.InvariantCulture) : 1;
// --revisit with --bench: after the first frame, goes to Home and back to the same page, as the app keeps its pages, and
// times coming back (the library stays made, PERF-03).
var revisit = list.Remove("--revisit");
// A11Y-02: --tab N presses Tab N times, from the page's first save manager row when it has one, printing what a screen
// reader would say has focus each time, as a keyboard user goes through it; then saves a sheet of every stop, the focused
// control cut out with its focus ring and named, to check each ring at a glance (design system: 2px primary, offset 2px).
var tabs = TakeOption(list, "--tab") is { } tabPresses ? int.Parse(tabPresses) : 0;
// --keys "Tab*3,Down*2,Space" presses those keys instead (Avalonia's key names, each optionally *N times), the same way.
var keys = (TakeOption(list, "--keys") is { } keyList ? keyList : tabs > 0 ? $"Tab*{tabs}" : "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .SelectMany(k => k.Split('*') is [var key, var times] ? Enumerable.Repeat(key, int.Parse(times)) : [k])
    .Select(Enum.Parse<Avalonia.Input.Key>)
    .ToList();
// "backdrops" with --art <folder>: each picture's Glossy backdrop before and after its colour is tamed, with its chroma.
var artFolder = TakeOption(list, "--art");
var caps = (TakeOption(list, "--caps") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
    .Select(c => c.Split(':'))
    .Select(c => (Knee: double.Parse(c[0], System.Globalization.CultureInfo.InvariantCulture), Limit: double.Parse(c[1], System.Globalization.CultureInfo.InvariantCulture)))
    .ToList();
Avalonia.Media.IImage? glowNext = null;
var output = Path.GetFullPath(list.Count > 0 ? list[0] : "snapshots");
var wanted = list.Skip(1).ToHashSet(StringComparer.OrdinalIgnoreCase);
Directory.CreateDirectory(output);
if (wanted.Remove("icons"))
{
    foreach (var file in Icons.Write(output))
    {
        Console.WriteLine(file);
    }

    if (wanted.Count == 0)
    {
        return;
    }
}

// Each render is one still frame: what moves as a page opens (the Achievements page's trophies, the Zenith medal's sheen) is
// drawn where it comes to rest.
GameSync.UI.Controls.Motion.Still = true;
AppBuilder.Configure<GsApp>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();
GameSync.UI.Theming.TextScale.Apply(Application.Current!.Resources, textScale);

// KAN-124: how long a backdrop takes to make, for each picture in --art, in each mode and strength.
if (wanted.Remove("backdrop-timing") && artFolder is not null)
{
    foreach (var file in Directory.EnumerateFiles(artFolder).Order(StringComparer.OrdinalIgnoreCase))
    {
        foreach (var mode in new[] { ThemeMode.Dark, ThemeMode.Light })
        {
            var choice = new ThemeChoice(Mode: mode);
            var theme = ThemeEngine.Build(choice);
            foreach (var strength in Enum.GetValues<GlassStrength>())
            {
                if (ThemeEngine.Glass(choice, strength) is not { } glass || Backdrop.Pixels(file) is null)
                {
                    continue;
                }

                var watch = System.Diagnostics.Stopwatch.StartNew();
                var art = Backdrop.Pixels(file)!.Value;
                var read = watch.Elapsed.TotalMilliseconds;
                var rgb = Backdrop.Tame(Backdrop.Soften(art.Pixels, art.Width, art.Height));
                var soft = watch.Elapsed.TotalMilliseconds;
                var alphas = Backdrop.ScrimAlphas(rgb, glass, theme);
                var scrim = watch.Elapsed.TotalMilliseconds;
                Backdrop.Bake(rgb, glass, alphas);
                Console.WriteLine($"{Path.GetFileName(file)} {mode} {strength}: read {read:F0} ms, soften {soft - read:F0} ms, scrim {scrim - soft:F0} ms, in all {watch.Elapsed.TotalMilliseconds:F0} ms");
            }
        }
    }

    if (wanted.Count == 0)
    {
        return;
    }
}

if (wanted.Remove("backdrops") && artFolder is not null)
{
    BackdropSheet(artFolder);
    if (wanted.Count == 0)
    {
        return;
    }
}

var glossy = new Dictionary<string, Func<ThemeChoice, Window>>(StringComparer.OrdinalIgnoreCase);
var pages = new Dictionary<string, (Func<Control> Make, int Width, int Height)>(StringComparer.OrdinalIgnoreCase)
{
    ["gallery"] = (() => new Gallery(), 1280, 1100),
    ["gallery2"] = (() => new Gallery2(), 1280, 1180),
    ["gallery3"] = (() => new Gallery3(), 1280, 800),
    ["busy"] = (() => new BusyGallery(), 1280, 800),
    ["zenith"] = (ZenithSheet, 1280, 820),
};

if (dataDir is not null)
{
    var now = DateTime.Now;
    var (games, home) = LauncherData.Read(dataDir, now);
    if (many > games.Count && games.Count > 0)
    {
        var real = games;
        games = Enumerable.Range(0, many).Select(i => i < real.Count ? real[i] : real[i % real.Count] with
        {
            Id = GameId.Parse($"{real[i % real.Count].Id.Value}-{i / real.Count + 1}"),
            Title = $"{real[i % real.Count].Title} {i / real.Count + 1}",
            Syncs = i % 2 == 0,
            Status = i % 2 == 0 ? GameStatus.Synced : null,
            RunningSinceUtc = null,
        }).ToList();
    }

    var rail = ShellViewModel.DefaultRail(games.FirstOrDefault(g => g.IsRunning)?.Title, games.Count(g => g.NeedsYou));

    // ACH-02: how far each game's achievements are, from Steam's own files on this PC, for the ring on its cover.
    var progress = GameSync.Host.Achievements.ForAll(dataDir, games).ToDictionary(v => v.Game, v => (v.Unlocked, v.Total));
    // The library as it opens, on its Hidden view, with a search typed, and with a game's page open beside the list: the
    // one --game names, or the hero game, its places, saves and history read from the data folder as the app reads them.
    var shown = gameId is not null ? games.FirstOrDefault(g => g.Id.Value == gameId) : home.Hero ?? games.FirstOrDefault();

    // KAN-80: every game's upload or download, as the app keeps them; game-saves-busy makes one up.
    var transfers = new TransferBoard { Cloud = "your Google Drive" };

    // Actions that do nothing, so buttons look as the app's do; pages read their content here, not through them.
    var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { })
    {
        TransferOf = transfers.For,
        SetFavourite = (_, _) => { },
        OpenGame = _ => { },
        OpenAchievements = (_, _) => { },
        OpenFolder = _ => { },
        SyncGame = _ => Task.CompletedTask,
        BackUpNow = _ => Task.CompletedTask,
        OpenNamedSave = _ => { },
        KeepNamed = (_, _) => Task.FromResult<string?>(null),
        Restore = (_, _, _) => Task.FromResult<string?>(null),
        OpenSaves = _ => { },
        OpenProperties = (_, _) => { },
        OpenLink = _ => { },
        Approve = _ => { },
        OpenConflict = _ => { },
        Resolve = (_, _, _) => { },
        Swap = _ => { },
        Retry = _ => { },
        RenameSave = (_, _, _) => { },
        ForgetSave = (_, _) => { },
        OpenAddPlace = _ => { },
        AddPlace = (_, _, _) => { },
        OpenImportKept = _ => { },
        SaveProperties = (_, _) => { },
        CloseDialog = () => { },
        SetInstalledOnly = _ => { },
        ScanFolder = (folder, _) => Task.FromResult(new FolderScan(folder, [], 0)),
        Locate = (_, _) => { },
        OpenAddGame = () => { },
    };

    // First run's and Connect the cloud's two ways, answering without signing in or writing: a folder picked becomes
    // GameSync's folder in it.
    var setupCloud = new CloudActions(true, _ => Task.FromResult<string?>(null), picked => Path.Combine(picked, "GameSync"));

    // Read off the UI thread, as the app does: waiting on it here would hold the thread its awaits come back to.
    T Off<T>(Func<Task<T>> read) => Task.Run(read).GetAwaiter().GetResult();

    LibraryViewModel Library(string tab = "all", string search = "", bool open = false, bool installedOnly = false)
    {
        var library = LibraryViewModel.From(games, now, tab, actions, installedOnly: installedOnly, progress: progress);
        library.Search = search;
        if (open && shown is not null)
        {
            library.Open(shown.Id);
            if (Off(() => GameDetails.ReadAsync(dataDir, shown.Id, CancellationToken.None, shown.SteamAppId)) is { } detail)
            {
                library.Page?.Show(detail, now);
            }
        }

        return library;
    }

    // The save manager: every game's saves, or the saves of the game --game names (the hero game by default).
    SaveManagerViewModel Saves(bool open = false)
    {
        var saves = new SaveManagerViewModel(actions);
        saves.Update(games);
        saves.Show(many > 0
            ? games.Select((g, i) => new GameSaveSummary(g.Id, $@"C:\Users\Player\Saved Games\{g.Title}", 3 + i % 40, 1_000_000L * (1 + i % 300),
                now.AddHours(-i).ToUniversalTime(), i % 3 == 0 ? "LAPTOP" : "DESKTOP")).ToList()
            : Off(() => SaveOverview.ReadAsync(dataDir, CancellationToken.None)), now);
        saves.ShowSpace(SaveOverview.Space(dataDir), now);
        if (open && shown is not null)
        {
            saves.Open(shown.Id);
            if (Off(() => GameDetails.ReadAsync(dataDir, shown.Id, CancellationToken.None, shown.SteamAppId)) is { } detail)
            {
                saves.Game?.Show(detail, now);
            }
        }

        return saves;
    }

    // The save manager's Plan tab (SYNC-14) as the design draws it: three changes, three games that won't run and six in
    // sync. Made up, like the conflict.
    SaveManagerViewModel PlanTab()
    {
        var saves = Saves();
        GameId Id(string id) => GameId.Parse(id);
        saves.Plan.Show(new SyncPlanView(
            [
                new PlannedChange(Id("ready-or-not"), "Ready or Not", PlanStep.Upload, "Changed here on 22 Sep. The upload waited because this PC was offline.", 14_155_776, "a"),
                new PlannedChange(Id("slay-the-spire-2"), "Slay the Spire 2", PlanStep.Download, "LAPTOP uploaded a newer save on 26 Sep and this PC's is unchanged. This PC's copy goes into history first.", 913_408, "b"),
                new PlannedChange(Id("cyberpunk-2077"), "Cyberpunk 2077", PlanStep.BackUp, "Steam Cloud syncs this game, so GameSync only adds this save to its history.", 16_777_216, "c"),
            ],
            [
                new PlannedWait(Id("sekiro"), Id("sekiro"), "Sekiro: Shadows Die Twice", GameSync.Core.State.GameStatus.Conflict, "Changed on DESKTOP and LAPTOP since the last sync. Nothing moves until you choose.", "Resolve"),
                new PlannedWait(Id("ghost"), Id("ghost"), "Ghost of Tsushima", GameSync.Core.State.GameStatus.Playing, "Being played now. It syncs a few seconds after it closes.", null),
                new PlannedWait(Id("rounds"), Id("rounds"), "ROUNDS", GameSync.Core.State.GameStatus.NoSaves, "No save folder found yet. Add a place where it keeps its saves.", "Add a place"),
            ],
            ["Terraria", "Hades", "Hollow Knight", "Core Keeper", "Valheim", "Risk of Rain 2"],
            now.ToUniversalTime()));
        saves.Tab = "plan";
        return saves;
    }

    // The save manager's Versions tab (MGR-08) as the design draws it: the newest versions from two PCs, one for each
    // reason a version is kept, and 204 older ones behind Show N older; kept: Named and kept only; empty: a game that
    // hasn't synced since it was added. Made up, like the plan.
    SaveManagerViewModel VersionsTab(string state)
    {
        var saves = Saves();
        saves.Tab = "versions";
        DateTime At(int daysAgo, int hour, int minute) => now.Date.AddDays(-daysAgo).AddHours(hour).AddMinutes(minute).ToUniversalTime();
        SavedVersion V(string game, string title, DateTime at, string pc, string why, KeptFor kind, int files, long bytes, bool named = false, bool kept = false) =>
            new(GameId.Parse(game), title, VersionId.New(at, pc), at, at, pc, why, kind, named, kept, files, bytes);
        var versions = new List<SavedVersion>
        {
            V("sekiro", "Sekiro: Shadows Die Twice", At(0, 21, 6), "DESKTOP", "After play · 3 h 10 min", KeptFor.Play, 2, 2_202_009),
            V("sekiro", "Sekiro: Shadows Die Twice", At(0, 21, 4), "LAPTOP", "LAPTOP's save, replaced by DESKTOP's in a conflict", KeptFor.Kept, 2, 2_199_552, kept: true),
            V("terraria", "Terraria", At(0, 20, 0), "DESKTOP", "Daily backup", KeptFor.Daily, 3, 341_835_776),
            V("slay-the-spire-2", "Slay the Spire 2", At(0, 19, 30), "LAPTOP", "After play · 47 min", KeptFor.Play, 1, 913_408),
            V("black-myth-wukong", "Black Myth: Wukong", At(1, 23, 31), "DESKTOP", "Named: Before Erlang Shen", KeptFor.Kept, 3, 522_240, named: true, kept: true),
            V("black-myth-wukong", "Black Myth: Wukong", At(1, 22, 47), "DESKTOP", "Changed outside play, held for review", KeptFor.Held, 3, 519_168),
            V("minecraft-server-world", "Minecraft server world", At(1, 21, 10), "DESKTOP", "After 5 quiet minutes", KeptFor.Quiet, 142, 40_265_318),
            V("hollow-knight", "Hollow Knight", At(2, 18, 5), "LAPTOP", "Kept before update to build 1.5.78 (28 Sep)", KeptFor.Kept, 6, 1_153_434, kept: true),
            V("dark-souls-3", "Dark Souls III", At(2, 12, 0), "DESKTOP", "Kept before a restore", KeptFor.Kept, 1, 6_815_744, kept: true),
            V("cuphead", "Cuphead", At(4, 20, 0), "LAPTOP", "First backup", KeptFor.First, 4, 48_128),
        };
        var played = new[]
        {
            ("sekiro", "Sekiro: Shadows Die Twice", 2_199_552L), ("terraria", "Terraria", 341_000_000L), ("slay-the-spire-2", "Slay the Spire 2", 905_216L),
            ("hollow-knight", "Hollow Knight", 1_150_000L), ("cuphead", "Cuphead", 48_128L), ("dark-souls-3", "Dark Souls III", 6_815_744L),
        };
        for (var i = 0; i < 204; i++)
        {
            var (id, title, bytes) = played[i % played.Length];
            versions.Add(V(id, title, At(5 + i / 16, 22 - i % 16, 10), i % 3 == 0 ? "LAPTOP" : "DESKTOP",
                $"After play · {Launcher.DurationText(TimeSpan.FromMinutes(20 + i % 7 * 13))}", KeptFor.Play, 2, bytes));
        }

        var games = versions.Select(v => new HistoryGame(v.Game, v.Title)).DistinctBy(g => g.Id).Append(new HistoryGame(GameId.Parse("hades"), "Hades"))
            .OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        saves.Versions.Show(new VersionsView(versions, games, "DESKTOP", ["DESKTOP", "LAPTOP"], 10, HasCloud: true), now);
        if (state == "kept")
        {
            saves.Versions.KeptOnly = true;
        }
        else if (state == "empty")
        {
            saves.Versions.Game = "hades";
        }

        return saves;
    }

    // The save manager's Log tab (MGR-09): lines as GameSync writes them, newest first and dated, with the daily
    // backup's for the days before; needs: Only what needs you; search: "dark souls" typed. Made up, like the plan.
    SaveManagerViewModel SavesLogTab(string state)
    {
        var saves = Saves();
        saves.Tab = "log";
        DateTime At(int daysAgo, int hour, int minute, int second) =>
            now.Date.AddDays(-daysAgo).AddHours(hour).AddMinutes(minute).AddSeconds(second).ToUniversalTime();
        LogEntry L(DateTime at, string game, string title, string level, string tag, string message) => new(at, GameId.Parse(game), title, level, tag, message);
        const string Sekiro = "Sekiro: Shadows Die Twice", Wukong = "Black Myth: Wukong", Souls = "Dark Souls III";
        var entries = new List<LogEntry>
        {
            L(At(0, 21, 6, 13), "sekiro", Sekiro, "warn", EventTags.Conflict,
                "Changed on DESKTOP and on LAPTOP; the newest save wins. Kept DESKTOP's save. LAPTOP's is pinned in history; Swap switches to it."),
            L(At(0, 21, 6, 2), "sekiro", Sekiro, "info", EventTags.Session, "Played 3 h 10 min."),
            L(At(0, 20, 0, 11), "black-myth-wukong", Wukong, "warn", EventTags.Held, "Changed while the game wasn't running: backed up and held for review."),
            L(At(0, 20, 0, 9), "terraria", "Terraria", "info", EventTags.Backup, "Backing up this PC's saves (the store's cloud syncs this game)."),
            L(At(0, 20, 0, 4), "cuphead", "Cuphead", "info", EventTags.Daily, "Daily backup: In sync."),
            L(At(0, 19, 31, 2), "slay-the-spire-2", "Slay the Spire 2", "info", EventTags.Download, "Newer save from LAPTOP."),
            L(At(0, 18, 40, 55), "dark-souls-3", Souls, "error", EventTags.InUse, "Can't read DS30000.sl2: another program has it open. Other games carry on."),
            L(At(1, 23, 31, 40), "black-myth-wukong", Wukong, "info", EventTags.Named, "Saved as 'Before Erlang Shen'."),
            L(At(1, 22, 47, 3), "black-myth-wukong", Wukong, "info", EventTags.Session, "Played 2 h 4 min."),
            L(At(2, 12, 0, 31), "dark-souls-3", Souls, "info", EventTags.Restore, "Restored the save from 25 Sep 21:40; the files it replaced stay in its history."),
        };
        var quiet = new[]
        {
            ("cuphead", "Cuphead"), ("hollow-knight", "Hollow Knight"), ("terraria", "Terraria"), ("dark-souls-3", Souls), ("sekiro", Sekiro),
            ("slay-the-spire-2", "Slay the Spire 2"),
        };
        for (var day = 3; day < 12; day++)
        {
            foreach (var (id, title) in quiet)
            {
                entries.Add(L(At(day, 20, 0, 4), id, title, "info", EventTags.Daily, "Daily backup: In sync."));
            }
        }

        // PERF-03: a 10,000-line log, the daily backup's lines over the months before.
        for (var i = 0; many > 0 && entries.Count < 10_000; i++)
        {
            var (id, title) = quiet[i % quiet.Length];
            entries.Add(L(At(12 + i / quiet.Length, 20, 0, 4), id, title, "info", EventTags.Daily, "Daily backup: In sync."));
        }

        saves.LogTab.Show(entries, now);
        if (state == "needs")
        {
            saves.LogTab.Showing = "needs";
        }
        else if (state == "search")
        {
            saves.LogTab.Search = "dark souls";
        }

        return saves;
    }

    // A game's conflict as the design draws it (SYNC-10): this PC's newer, longer play against LAPTOP's, with Compare
    // files open; or the same conflict settled by newest wins, with Swap (SYNC-04). Made up, in the game --game names.
    SaveManagerViewModel Conflict(bool settled)
    {
        var saves = Saves();
        if (shown is null)
        {
            return saves;
        }

        DateTime At(int hour, int minute) => now.Date.AddHours(hour).AddMinutes(minute).ToUniversalTime();
        FileEntry File(string name, int hour, int minute) => new($"saves/{name}", 11_953_766, At(hour, minute), BlobId.Parse(new string('a', 64)));
        var desktop = new ConflictSide
        {
            Pc = "DESKTOP", IsThisPc = true, Version = VersionId.Parse("2026-09-29T20-04-00Z_DESKTOP_d1"), SavedUtc = At(21, 4),
            Session = new SessionInfo(At(19, 12), At(21, 4)), Sessions = 2, Played = TimeSpan.FromMinutes(190), Bytes = 11_953_766, Files = 2,
            Changed = ["S0000.sl2", "S0000.sl2.bak"], Newest = true, Suggested = true,
        };
        var laptop = new ConflictSide
        {
            Pc = "LAPTOP", Version = VersionId.Parse("2026-09-29T18-30-00Z_LAPTOP_l1"), SavedUtc = At(19, 30),
            Session = new SessionInfo(At(18, 43), At(19, 30)), Sessions = 1, Played = TimeSpan.FromMinutes(47), Bytes = 11_953_766, Files = 2,
            Changed = ["S0000.sl2", "S0000.sl2.bak"],
        };
        var detail = new ConflictDetail
        {
            Id = shown.Id,
            Title = shown.Title,
            Waiting = !settled,
            Sides = [desktop, laptop],
            Reason = "Changed on DESKTOP and on LAPTOP; this game is set to always ask.",
            LastSyncUtc = At(12, 0),
            Files =
            [
                new ConflictFile("saves/S0000.sl2", File("S0000.sl2", 21, 4), File("S0000.sl2", 19, 30), "Changed on both PCs"),
                new ConflictFile("saves/S0000.sl2.bak", File("S0000.sl2.bak", 20, 58), File("S0000.sl2.bak", 19, 29), "Changed on both PCs"),
            ],
            KeptPc = settled ? "DESKTOP" : null,
            PinnedPc = settled ? "LAPTOP" : null,
        };
        saves.OpenConflict(shown.Id);
        saves.Conflict?.Show(detail, now);
        if (saves.Conflict is { } conflict)
        {
            conflict.Comparing = !settled;
        }

        return saves;
    }

    // A game's Properties over its page, on the section given.
    PropertiesViewModel? Properties(string section)
    {
        if (shown is null)
        {
            return null;
        }

        var properties = new PropertiesViewModel(shown.Id, shown.Title, actions, section);
        if (Off(() => GameSettings.ReadAsync(dataDir, shown.Id, shown.SteamAppId, CancellationToken.None)) is { } read)
        {
            properties.Show(read);
        }

        return properties;
    }

    // Home's Achievements card, from Steam's own files on this PC for the data folder's games (design system version 33); each
    // game the banner can show brings its own (KAN-125).
    HomeViewModel HomeOf() => HomeViewModel.From(home, games, now,
        achievements: dataDir is null ? default : GameSync.Host.Achievements.ForHome(dataDir, home.Hero, games),
        achievementsOf: dataDir is null ? null : game => GameSync.Host.Achievements.ForHome(dataDir, game, games), progress: progress);

    // The Achievements page (design system version 33): every game's achievements from Steam's own files on this PC, as
    // kept in the data folder (icons and rarity fetched by the app or gamesync achievements --fetch).
    TrophyRoomViewModel Trophies()
    {
        var room = new TrophyRoomViewModel(actions);
        room.Update(games);
        room.Show(GameSync.Host.Achievements.ForAll(dataDir, games), now);
        return room;
    }

    // Every achievement of the game --game names, beside the library's list, on the tab given.
    LibraryViewModel GameAchievements(string tab)
    {
        var library = Library(open: true);
        if (shown is not null)
        {
            library.ShowAchievements(shown.Id, known: GameSync.Host.Achievements.For(dataDir, shown));
            if (library.Achievements is { } achievements)
            {
                achievements.Tab = tab;
            }
        }

        return library;
    }

    Dictionary<string, Func<object?>> Makers() => new()
    {
        ["home"] = HomeOf,
        ["home-second"] = () =>
        {
            // KAN-125: the banner moved on to the next game played.
            var second = HomeOf();
            second.NextHeroCommand.Execute(null);
            return second;
        },
        ["home-last-month"] = () =>
        {
            // KAN-66: the Activity card's arrow, a month back.
            var page = HomeOf();
            page.EarlierMonthCommand.Execute(null);
            return page;
        },
        ["playing"] = Playing,
        ["library"] = () => Library(),
        ["marks"] = Marked,
        ["installed"] = () => Library(installedOnly: true),
        ["saves-by-name"] = () =>
        {
            var saves = Saves();
            saves.AllSort.SortCommand.Execute("Name");
            return saves;
        },
        ["saves-by-status"] = () =>
        {
            var saves = Saves();
            saves.SyncingSort.SortCommand.Execute("Status");
            saves.AllSort.SortCommand.Execute("Status");
            saves.AllSort.SortCommand.Execute("Status");
            return saves;
        },
        ["saves-selected"] = () =>
        {
            // SHARE-01: a game ticked in Syncing, for Share selected.
            var saves = Saves();
            if (saves.SyncingRows.FirstOrDefault() is { } first)
            {
                saves.SelectCommand.Execute(first.Id);
            }

            return saves;
        },
        ["saves-needs"] = () =>
        {
            var saves = Saves();
            saves.ShowTab(SaveManagerViewModel.NeedsTab);
            return saves;
        },
        ["local"] = () => Library("local"),
        ["hidden"] = () => Library("hidden"),
        ["searched"] = () => Library(search: "re"),
        ["game"] = () => Library(open: true),
        ["achievements"] = Trophies,
        ["achievements-game"] = () =>
        {
            var room = Trophies();
            room.OpenGame(shown?.Id);
            return room;
        },
        ["game-achievements"] = () => GameAchievements(AchievementsViewModel.AllTab),
        ["game-achievements-locked"] = () => GameAchievements(AchievementsViewModel.LockedTab),
        ["game-achievements-hidden"] = () => GameAchievements(AchievementsViewModel.HiddenTab),
        ["saves"] = () => Saves(),
        ["game-saves"] = () => Saves(open: true),
        ["game-saves-playing"] = () =>
        {
            // KAN-91: the shown game running now (made up): Back up now works, and Restore's ask says what to expect.
            var saves = Saves(open: true);
            if (shown is not null)
            {
                saves.Game?.Update(shown with { RunningSinceUtc = now.ToUniversalTime().AddMinutes(-42) });
            }

            return saves;
        },
        ["game-saves-busy"] = () =>
        {
            // KAN-80: its upload under way, beside a named save being restored (made up).
            if (shown is not null)
            {
                var t0 = now.ToUniversalTime();
                var id = shown.Id;
                transfers.Apply(new TransferUpdate(new GameSync.Core.Storage.TransferProgress(id, 0, 1108, 0, 118_489_088), TransferState.Running), t0.AddSeconds(-7));
                transfers.Apply(new TransferUpdate(new GameSync.Core.Storage.TransferProgress(id, 412, 1108, 44_145_049, 118_489_088), TransferState.Running), t0);
            }

            var saves = Saves(open: true);
            if (saves.Game is { } game)
            {
                if (game.NamedSaves.FirstOrDefault() is { } named)
                {
                    named.IsRestoring = true;
                    (game.Restoring, game.RestoreNote) = (true, $"Bringing back “{named.Name}”");
                }
                else
                {
                    (game.Restoring, game.RestoreNote) = (true, "Bringing back the save from 23 Sep 21:02");
                }
            }

            return saves;
        },
        // FIND-04: the shown game as if nothing were found for it, learn mode waiting, watching, or with what it found (made up).
        ["game-saves-learn"] = () => LearnSaves(GameSync.Host.LearnState.Waiting),
        ["game-saves-learning"] = () => LearnSaves(GameSync.Host.LearnState.Watching),
        ["game-saves-learned"] = () => LearnSaves(GameSync.Host.LearnState.Found),
        ["game-saves-kept"] = () =>
        {
            // KAN-61: the shown game as if not syncing yet, its live save beside copies kept by hand (made up).
            var saves = Saves(open: true);
            if (saves.Game is { } game && Off(() => GameDetails.ReadAsync(dataDir, game.Id, CancellationToken.None)) is { } detail)
            {
                var kept = KeptMadeUp();
                game.Show(detail with
                {
                    Syncs = false,
                    Kept = kept,
                    FoundBy = "Found by name search at the last scan, 29 Sep.",
                    Places = [new GamePlace(@"<installDir>\Shadlix\user\savedata\1\CUSA00207\SPRJ0005", kept.LiveFolder, null, "34 files · 33.6 MB · newest 17 Sep 02:14")],
                    Versions = [],
                    NamedSaves = [],
                    FoundFiles = kept.LiveFiles,
                    FoundBytes = kept.LiveBytes,
                }, now);
            }

            return saves;
        },
        ["conflict"] = () => Conflict(settled: false),
        ["conflict-settled"] = () => Conflict(settled: true),
        ["plan"] = PlanTab,
        ["versions"] = () => VersionsTab(""),
        ["versions-kept"] = () => VersionsTab("kept"),
        ["versions-empty"] = () => VersionsTab("empty"),
        ["saves-log"] = () => SavesLogTab(""),
        ["saves-log-needs"] = () => SavesLogTab("needs"),
        ["saves-log-search"] = () => SavesLogTab("search"),
        ["home-nocloud"] = () => HomeViewModel.From(home, games, now, actions,
            new HomeStatus("No cloud yet", GameSync.Core.Storage.NoCloud.Message, Environment.MachineName, []) { NoCloud = true }),
        ["setup-scan"] = () => Setup("scanning"),
        ["setup"] = () => Setup("scan"),
        ["setup-choose"] = () => Setup("choose"),
        ["setup-cloud"] = () => Setup("cloud"),
        ["setup-cloud-folder"] = () => Setup("cloud-folder"),
        ["setup-daily"] = () => Setup("daily"),
        ["setup-daily-time"] = () => Setup("daily-time"),
        ["settings"] = () => SettingsPage(SettingsViewModel.AppearanceId),
        ["settings-storage"] = () => SettingsPage(SettingsViewModel.StorageId),
        ["settings-backup"] = () => SettingsPage(SettingsViewModel.BackupId),
        ["settings-backup-apply"] = () => SettingsPage(SettingsViewModel.BackupId, apply: true),
        ["settings-cloud"] = () => SettingsPage(SettingsViewModel.CloudId),
        ["settings-cloud-drive"] = () => SettingsPage(SettingsViewModel.CloudId, drive: true),
        ["settings-devices"] = () => SettingsPage(SettingsViewModel.DevicesId),
        ["settings-notifications"] = () => SettingsPage(SettingsViewModel.NotificationsId),
        ["settings-safety"] = () => SettingsPage(SettingsViewModel.SafetyId),
        ["settings-achievements"] = () => SettingsPage(SettingsViewModel.AchievementsId),
        ["settings-updates"] = () => SettingsPage(SettingsViewModel.UpdatesId, updates: "ready"),
        ["settings-updates-latest"] = () => SettingsPage(SettingsViewModel.UpdatesId, updates: "latest"),
        ["settings-updates-downloading"] = () => SettingsPage(SettingsViewModel.UpdatesId, updates: "downloading"),
        ["settings-updates-refused"] = () => SettingsPage(SettingsViewModel.UpdatesId, updates: "refused"),
    };

    // FIND-04: what learn mode found for the shown game (made up), for its saves page and the finds dialog.
    GameSync.Core.Discovery.LearnFinds LearnMadeUp()
    {
        var t = now.ToUniversalTime();
        var title = shown?.Title ?? "ROUNDS";
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low";
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return new GameSync.Core.Discovery.LearnFinds(t.AddMinutes(-48), t.AddMinutes(-2),
        [
            new(Path.Combine(local, "Landfall Games", title), $"<localLow>/Landfall Games/{title}", false, 3, 49_152, t.AddMinutes(-3), ["Player.sav", "Unlocks.sav", "Settings.json"],
                [GameSync.Core.Discovery.LearnPlaces.SaveLikeTag]),
            new(Path.Combine(documents, "My Games", title, "Replays"), $"<documents>/My Games/{title}/Replays", false, 12, 2_306_867, t.AddMinutes(-6), ["replay-0412.rpl", "replay-0413.rpl"], []),
            new(@"E:\Games\" + title + @"\settings.cfg", "<installDir>/settings.cfg", true, 1, 812, t.AddMinutes(-2), ["settings.cfg"], [GameSync.Core.Discovery.LearnPlaces.OwnFolderTag]),
        ]);
    }

    SaveManagerViewModel LearnSaves(GameSync.Host.LearnState learn)
    {
        var saves = Saves(open: true);
        if (saves.Game is { } game && Off(() => GameDetails.ReadAsync(dataDir, game.Id, CancellationToken.None)) is { } detail)
        {
            game.Show(detail with
            {
                Syncs = false,
                Places = [],
                FoundBy = null,
                Versions = [],
                NamedSaves = [],
                Kept = null,
                Learn = new GameSync.Host.LearnView(learn, learn == GameSync.Host.LearnState.Watching ? now.ToUniversalTime().AddMinutes(-21) : null,
                    learn == GameSync.Host.LearnState.Found ? LearnMadeUp() : null),
            }, now);
        }

        return saves;
    }

    // Settings (SET-01) from this data folder's own settings, on a section; the actions only answer, so nothing is
    // written. With drive, the cloud is a made-up Google Drive, signed in and 81% full (CLOUD-05); with apply, the
    // settings files default was just changed, so What to back up offers to apply it to games that differ (SET-06).
    SettingsViewModel SettingsPage(string section, bool drive = false, bool apply = false, string? updates = null)
    {
        var view = SettingsData.Read(dataDir);
        if (drive)
        {
            view = view with { Cloud = SettingsData.Drive, SignedIn = true, CloudFolder = null };
        }

        if (apply)
        {
            view = view with { FilesDiffer = Math.Max(1, view.Games), Defaults = view.Defaults with { SettingsFiles = GameSync.Core.Games.GameDefaults.SyncBetween } };
        }

        var figures = Task.Run(() => SettingsData.FiguresAsync(dataDir, CancellationToken.None)).GetAwaiter().GetResult();
        CloudDetails? details = drive
            ? new CloudDetails("https://drive.google.com/drive/folders/example", "sam.rivera@gmail.com", 13_300_000_000, 15L * 1024 * 1024 * 1024)
            : view.CloudFolder is { } folder ? new CloudDetails(folder, null, null, 212L * 1024 * 1024 * 1024) : null;
        var settings = new SettingsViewModel(new SettingsActions
        {
            Read = _ => Task.FromResult<SettingsView?>(view),
            Figures = _ => Task.FromResult(figures),
            Cloud = _ => Task.FromResult(details),
            Diagnostics = _ => Task.FromResult(""),
            AchievementGames = _ => Task.FromResult(GameSync.Host.Achievements.SettingsRows(dataDir, games)),
        }, new Look(), section);
        settings.Apply(view);
        settings.Storage.LoadFigures();
        settings.Cloud.LoadDetails();
        settings.Achievements.LoadGames();
        if (apply)
        {
            // Touched: a change made on the page, which the page then reads back.
            settings.Backup.SettingsFiles = GameSync.Core.Games.GameDefaults.ThisPc;
        }

        // PKG-03 (design system version 53): Updates as GameSync 1.0.0 installed, with 1.0.1 made up: ready, already the
        // latest, coming down, or refused by GameSync's own check.
        if (updates is not null)
        {
            var utc = now.ToUniversalTime();
            var next = new Version(1, 0, 1);
            var made = new UpdatesView(new Version(1, 0, 0), Installed: true, Daily: true, utc.AddHours(-2),
                updates == "ready"
                    ? new ReadyUpdate(next, "", "", "https://github.com/Ahmed-Javaid/GameSync/releases/tag/v1.0.1",
                        ["Restoring a named save says which file a running game still holds.", "Achievements of copies Steam doesn't run count from their own record.", "Home's Zenith is a dot, orange to red."])
                    : null,
                updates == "refused" ? new UpdateRefusal(next, "it isn't the installer its signature describes, so it may have been changed on the way", utc) : null,
                null);
            settings.Updates.Show(made, now);
            if (updates == "downloading")
            {
                settings.Updates.ShowDownloading(next, 24L * 1048576, 58L * 1048576, "1.2 MB/s, about 30 s left");
            }
        }

        return settings;
    }

    // First run (ONB-01 to ONB-05) over this data folder's library, as if GameSync weren't set up yet: the scan while it
    // runs, then each step; the cloud with a folder chosen, and the daily time being changed. The stores' folders are
    // made up; the actions only answer, so nothing is written.
    FirstRunViewModel Setup(string state)
    {
        var groups = FirstRun.Groups(dataDir);
        int Installed(GameSync.Core.Discovery.StoreKind store) => games.Count(g => g.Installed && !g.IsSoftware && g.Store == store);
        string Where(GameSync.Core.Discovery.StoreKind store, string where) => Installed(store) == 0 ? "Nothing installed right now" : where;
        var stores = new[]
        {
            new StoreSummary("Steam", Installed(GameSync.Core.Discovery.StoreKind.Steam), Where(GameSync.Core.Discovery.StoreKind.Steam, @"E:\Steam and G:\SteamLibrary")),
            new StoreSummary("Epic Games", Installed(GameSync.Core.Discovery.StoreKind.Epic), Where(GameSync.Core.Discovery.StoreKind.Epic, @"C:\Program Files\Epic Games")),
            new StoreSummary("EA app", Installed(GameSync.Core.Discovery.StoreKind.Ea), Where(GameSync.Core.Discovery.StoreKind.Ea, @"E:\EA Games")),
        };
        var scan = new SetupScan(stores, [new GameFolderSummary(@"G:\", Installed(GameSync.Core.Discovery.StoreKind.Loose), false)],
            games.Count(g => !g.IsSoftware), groups.Where(g => g.Kind != SetupGroupKind.NoSaves).Sum(g => g.Games.Count), TimeSpan.FromSeconds(41));

        // While it scans, the stores have been read and each game's saves are being looked for; it never finishes here.
        Task<SetupScan> Scan(IProgress<ScanProgress> progress, CancellationToken ct)
        {
            if (state != "scanning")
            {
                return Task.FromResult(scan);
            }

            progress.Report(new ScanProgress("Getting the list of where games keep their saves", 0, 0) { Folders = [@"G:\"] });
            progress.Report(new ScanProgress("Looking for each game's saves", 18, 46) { Stores = stores });
            return new TaskCompletionSource<SetupScan>().Task;
        }

        var setup = new FirstRunViewModel(new SetupActions(
            Scan,
            _ => Task.FromResult(groups),
            (_, _) => Task.FromResult<string?>(null),
            setupCloud,
            (_, _) => Task.FromResult(new SetupResult(0, 0, [])))
        {
            AddGame = () => { },
        });
        setup.Start();
        var steps = state switch { "choose" => 1, "cloud" or "cloud-folder" => 2, "daily" or "daily-time" => 3, _ => 0 };
        for (var i = 0; i < steps; i++)
        {
            setup.NextCommand.Execute(null);
        }

        if (state == "cloud-folder")
        {
            setup.Cloud.ChooseFolder(@"D:\");
        }
        else if (state == "daily-time")
        {
            setup.EditTimeCommand.Execute(null);
        }

        return setup;
    }

    // The library with every game given a made-up status in turn, to show the fine games' marks and the badges (KAN-45, KAN-48).
    LibraryViewModel Marked()
    {
        GameStatus?[] turns = [GameStatus.Synced, GameStatus.BackupOnly, GameStatus.Synced, null, GameStatus.UploadPending, GameStatus.Conflict, GameStatus.BackupOnly];
        var marked = games.Select((g, i) => turns[i % turns.Length] is { } status
            ? g with { Status = status, Syncs = true, Store = status == GameStatus.BackupOnly && i % 2 == 0 ? null : g.Store }
            : g with { Status = null, Syncs = false }).ToList();
        return LibraryViewModel.From(marked, now, "all", actions);
    }

    // Home with a game running (PLAY-12): the game --game names, or the hero game, playing for the last 35 minutes.
    HomeViewModel Playing()
    {
        var running = games.Select(g => g.Id == shown?.Id ? g with { RunningSinceUtc = now.AddMinutes(-35).ToUniversalTime() } : g).ToList();
        using var state = new GameSync.Core.State.StateStore(dataDir);
        return HomeViewModel.From(Launcher.Home(running, state, now), running, now);
    }

    // Add a place over a game's saves, with a folder picked (made up, in the game --game names): as the design draws it.
    // New named save (BAK-18, KAN-77) over the shown game's saves, from what this data folder knows of it: just opened,
    // with a name its named saves have already, or as for a game not syncing yet. The actions only answer.
    // KAN-61: Bloodborne's live save beside its copies kept by hand, as the owner's folder holds them (made up, as drawn).
    GameKept KeptMadeUp() => new("<installDir>/Shadlix/user/savedata", "1/CUSA00207/SPRJ0005", @"G:\Bloodborne GOTY\Shadlix\user\savedata\1\CUSA00207\SPRJ0005",
        @"G:\Bloodborne GOTY\Shadlix\user\savedata\1\CUSA00207", 18, 1_036_000_000, 34, 35_232_153, now.ToUniversalTime().AddDays(-14), ["Before Orphan", "After maria"]);

    KeptCopiesViewModel KeptCopiesDialogFor(bool backup, bool done, string? busy = null)
    {
        const long MB = 1_048_576;
        var at = now.ToUniversalTime().AddDays(-37);
        var names = new[] { "Witch ded", "After Abandoned Workshop Before Amelia", "Before Iosefka clinic and FF boss", "Before Iosefka clinic and FF boss / anoter",
            "After ROM at chapel", "Before Crow", "Before Crow (zip)", "After crow", "SPRJ0005 - Copy", "BEFORE church dog lady", "Mensis Fog", "Near Bloody Bride",
            "befo ludwig", "After Failure", "Befo living failure", "after ludwig", "After maria", "Before Orphan" };
        var items = names.Select((name, i) => new GameSync.Core.Sync.ImportItem(name, name, i < 2 ? 15 : 34, (i < 2 ? 14 : 33) * MB + i * 1000, at.AddDays(i * 1.3),
            name == "Before Crow (zip)" ? "'Before Crow'" : name == "Befo living failure" ? "'After Failure'" : null)).ToList();
        var dialog = new KeptCopiesViewModel(new KeptCopiesStart(shown?.Id ?? GameId.Parse("bloodborne"), "Bloodborne GOTY", backup), actions);
        dialog.Show(new KeptLook(KeptMadeUp(), items, [], 1_071_872_663), now);
        if (done)
        {
            dialog.DoneText = "Bloodborne GOTY syncs its live save now.";
            dialog.DoneNote = "16 named saves are under Named saves, on every PC. Restore brings one back, keeping the save there now first.";
            dialog.UploadNote = "It uploads to the cloud now, beside whatever you do next: the game's saves show how far it is.";
            dialog.Stage = "done";
        }
        else if (busy == "looking")
        {
            // KAN-80: as it opens, each copy is read (made up: 7 of the 18 so far).
            dialog.Stage = "looking";
            (dialog.LookValue, dialog.LookDetail) = (7 * 100.0 / 18, "7 of 18 folders · 212 MB of 1022.2 MB read");
        }
        else if (busy == "keeping")
        {
            // KAN-80: the copies coming in (made up: 6 of the 16 kept).
            dialog.Stage = "keeping";
            (dialog.KeepValue, dialog.KeepDetail, dialog.KeepSpeed, dialog.KeepLeft) = (20 + 80 * 143.0 / 453.2, "6 of 16 copies · 143 of 453.2 MB", "48 MB/s", "about 7 s left");
        }

        return dialog;
    }

    NamedSaveViewModel NamedSave(string state)
    {
        var detail = shown is null ? null : Off(() => GameDetails.ReadAsync(dataDir, shown.Id, CancellationToken.None, shown.SteamAppId));
        var names = (detail?.NamedSaves.Select(n => n.Name) ?? []).DefaultIfEmpty("Before the Warden").ToList();
        var start = NamedSaveStart.For(shown?.Id ?? GameId.Parse("game"), shown?.Title ?? "Game", detail?.Places.Select(p => (p.Folder, p.Tag, p.Evidence)) ?? [],
            names, state == "new" ? GameSavesViewModel.KeepNoteFor(34, 35_232_153) : null);
        return new NamedSaveViewModel(start, actions) { Name = state == "taken" ? names[0] : "" };
    }

    AddPlaceViewModel AddPlace()
    {
        var add = new AddPlaceViewModel(shown?.Id ?? GameId.Parse("game"), shown?.Title ?? "Game", actions);
        add.Place = new NewPlaceLook
        {
            Path = @"C:\Users\You\AppData\LocalLow\Team Cherry\Hollow Knight",
            Folder = @"C:\Users\You\AppData\LocalLow\Team Cherry\Hollow Knight",
            Portable = "<localLow>/Team Cherry/Hollow Knight",
            Files = 6,
            Bytes = 1_153_434,
            NewestUtc = now.Date.AddHours(21).AddMinutes(4).ToUniversalTime(),
        };
        return add;
    }

    // Add a game or folder (LIB-13) with a server's world picked, as the design draws it, or an old game with its program;
    // over the library, or over first run. Made up: the actions only answer.
    AddOwnViewModel AddGame(bool setup, bool program)
    {
        var folder = program ? @"C:\Users\You\Saved Games\Diablo II" : @"D:\Servers\Minecraft\world";
        var look = new NewPlaceLook
        {
            Path = folder,
            Folder = folder,
            Portable = folder,
            Files = program ? 12 : 142,
            Bytes = program ? 2_202_009 : 40_265_318,
            NewestUtc = now.Date.AddHours(20).AddMinutes(55).ToUniversalTime(),
        };
        var add = new AddOwnViewModel(new OwnActions(
            (_, _) => Task.FromResult(look),
            (path, _) => Task.FromResult(Path.GetDirectoryName(path)!),
            (game, _) => Task.FromResult(new OwnGameAdded(GameId.Parse("game"), game.Name, "")),
            () => { }), setup);
        add.LookAt(folder);
        add.Name = program ? "Diablo II" : "Minecraft server world";
        if (program)
        {
            add.ChooseProgram(@"D:\Games\Diablo II\Game.exe");
        }

        return add;
    }

    // Import kept saves with a folder read (made up): each copy by name, one the same as another.
    ImportKeptViewModel ImportKept()
    {
        var import = new ImportKeptViewModel(shown?.Id ?? GameId.Parse("game"), shown?.Title ?? "Game", actions) { Folder = @"D:\Saves\Bloodborne\CUSA00207" };
        DateTime At(int day, int hour, int minute) => new DateTime(now.Year, 9, day, hour, minute, 0, DateTimeKind.Local).ToUniversalTime();
        import.Show(new GameSync.Core.Sync.ImportReport(
        [
            new("Before Father Gascoigne", "", 3, 1_153_434, At(4, 20, 12), null),
            new("Before Vicar Amelia", "", 3, 1_160_192, At(9, 22, 40), null),
            new("Before Rom", "", 3, 1_171_456, At(12, 21, 5), null),
            new("After Rom", "", 3, 1_171_456, At(12, 21, 5), "Before Rom"),
            new("Before Orphan of Kos", "", 3, 1_189_888, At(20, 23, 18), null),
        ], ["SPRJ0005 is the live save, so it's left as it is."], 0), now);
        return import;
    }

    // Sharing (SHARE-01 to SHARE-13): the share window over this data folder's own games and versions, with a made-up
    // locked game; Import saves over a made-up zip's games. The actions only answer, so nothing is written.
    ShareActions Sharing(GameSync.Host.ImportPreview? preview = null) => new()
    {
        List = _ => Task.Run(async () =>
        {
            var list = (await GameSync.Host.Sharing.ListAsync(dataDir, CancellationToken.None)).ToList();
            list.Add(new GameSync.Host.ShareGameInfo(GameId.Parse("gta-v-enhanced"), "GTA V Enhanced",
                [new GameSync.Host.ShareVersionInfo(GameSync.Core.Model.VersionId.New(now.ToUniversalTime(), "DESKTOP"), "Current", now.ToUniversalTime().AddDays(-3), "DESKTOP", 12_582_912, false, true)],
                "Ships an anti-cheat, so its saves can't be shared."));
            return (IReadOnlyList<GameSync.Host.ShareGameInfo>)list;
        }),
        Where = () => (@"C:\Users\You\Downloads", false),
        Preview = (zip, _) => Task.FromResult(preview ?? new GameSync.Host.ImportPreview(zip, now.ToUniversalTime(), "DESKTOP", [])),
        CoverOf = id => games.FirstOrDefault(g => g.Id == id)?.CoverPath,
    };

    ShareViewModel Share(string mode, bool export = false, bool done = false)
    {
        var start = new ShareStart(mode, games.Where(g => g.Syncs).Select(g => g.Id).Take(1).ToList());
        if (export && Task.Run(() => GameSync.Host.Sharing.ListAsync(dataDir, CancellationToken.None)).GetAwaiter().GetResult().FirstOrDefault() is { } first)
        {
            start = new ShareStart(ShareStart.Pick, [first.Id], first.Id, first.Versions[^1].Id);
        }

        var share = new ShareViewModel(Sharing(), start);
        if (!done)
        {
            share.Load();
        }
        else
        {
            share.ZipPath = @"C:\Users\You\Downloads\GameSync-saves-" + now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".zip";
            share.DoneText = "2 saves of 1 game, 41.2 KB, in " + System.IO.Path.GetFileName(share.ZipPath) + ".";
            share.Stage = "done";
        }

        return share;
    }

    ImportSavesViewModel ImportSaves()
    {
        var syncing = games.FirstOrDefault(g => g.Syncs);
        var other = games.FirstOrDefault(g => !g.Syncs && !g.IsSoftware);
        var preview = new GameSync.Host.ImportPreview(@"C:\Users\You\Downloads\GameSync-saves-2026-09-27.zip", now.ToUniversalTime().AddDays(-4), "SAM-PC",
        [
            new(syncing?.Id ?? GameId.Parse("terraria"), syncing?.Id, syncing?.Title ?? "Terraria", GameSync.Host.ImportMatch.Matched, 2, 4_299_161,
                "Made on another Steam account. Some games check the account, so it may not load.", 0),
            new(other?.Id ?? GameId.Parse("hollow-knight"), other?.Id ?? GameId.Parse("hollow-knight"), other?.Title ?? "Hollow Knight", GameSync.Host.ImportMatch.NotSyncing, 1,
                1_153_434, null, 1),
            new(GameId.Parse("terraria"), null, "Terraria", GameSync.Host.ImportMatch.NotInstalled, 1, 524_288, null, 0),
            new(GameId.Parse("a-friends-game"), null, "A friend's own game", GameSync.Host.ImportMatch.Unknown, 1, 524_288,
                @"Its save folder, D:\Servers\World, isn't on this PC.", 0),
        ]);
        var import = new ImportSavesViewModel(Sharing(preview), preview.ZipPath);
        import.Load();
        return import;
    }

    Control Shell(string current, string? dialog = null) => new Shell
    {
        DataContext = new ShellViewModel(Makers(), current)
        {
            Rail = rail,
            Dialog = dialog switch
            {
                null => null,
                "add-place" => AddPlace(),
                "import-kept" => ImportKept(),
                "connect-cloud" => new ConnectCloudViewModel(setupCloud, (_, _) => Task.CompletedTask, () => { }),
                "add-game" => AddGame(setup: false, program: false),
                "add-game-program" => AddGame(setup: false, program: true),
                "setup-add-game" => AddGame(setup: true, program: false),
                "share" => Share(ShareStart.Pick),
                "share-all" => Share(ShareStart.All),
                "share-export" => Share(ShareStart.Pick, export: true),
                "share-done" => Share(ShareStart.Pick, done: true),
                "import-saves" => ImportSaves(),
                "named-save" => NamedSave(""),
                "named-save-taken" => NamedSave("taken"),
                "named-save-new" => NamedSave("new"),
                "kept-copies" => KeptCopiesDialogFor(backup: false, done: false),
                "kept-copies-backup" => KeptCopiesDialogFor(backup: true, done: false),
                "kept-copies-done" => KeptCopiesDialogFor(backup: false, done: true),
                "kept-copies-looking" => KeptCopiesDialogFor(backup: false, done: false, busy: "looking"),
                "kept-copies-keeping" => KeptCopiesDialogFor(backup: false, done: false, busy: "keeping"),
                "achievement-games" => new AchievementGamesViewModel(SettingsPage(SettingsViewModel.AchievementsId).Achievements, () => { }),
                "learn-finds" => new LearnFindsViewModel(shown?.Id ?? GameId.Parse("rounds"), shown?.Title ?? "ROUNDS", LearnMadeUp(), new Dictionary<string, string?>(), null, () => { }, now),
                _ => Properties(dialog),
            },
        },
    };
    pages["home"] = (() => Shell("home"), size.Width, size.Height);
    pages["home-second"] = (() => Shell("home-second"), size.Width, size.Height);
    pages["home-playing"] = (() => Shell("playing"), size.Width, size.Height);
    pages["home-last-month"] = (() => Shell("home-last-month"), size.Width, size.Height);
    pages["library"] = (() => Shell("library"), size.Width, size.Height);
    pages["library-full"] = (() => Shell("library"), 1280, 2400);
    pages["library-hidden"] = (() => Shell("hidden"), 1280, 800);
    pages["library-installed"] = (() => Shell("installed"), size.Width, size.Height);
    pages["saves-needs"] = (() => Shell("saves-needs"), size.Width, size.Height);
    pages["saves-by-name"] = (() => Shell("saves-by-name"), size.Width, size.Height);
    pages["saves-by-status"] = (() => Shell("saves-by-status"), size.Width, size.Height);
    pages["library-marks"] = (() => Shell("marks"), size.Width, size.Height);
    pages["library-local"] = (() => Shell("local"), size.Width, size.Height);
    pages["library-search"] = (() => Shell("searched"), size.Width, size.Height);
    pages["library-game"] = (() => Shell("game"), size.Width, size.Height);
    pages["library-game-full"] = (() => Shell("game"), size.Width, 1600);
    foreach (var page in new[] { "achievements", "achievements-game", "game-achievements", "game-achievements-locked", "game-achievements-hidden" })
    {
        pages[page] = (() => Shell(page), size.Width, size.Height);
    }

    pages["achievements-full"] = (() => Shell("achievements"), size.Width, 1500);
    pages["saves"] = (() => Shell("saves"), size.Width, size.Height);
    pages["game-saves"] = (() => Shell("game-saves"), size.Width, size.Height);
    pages["conflict"] = (() => Shell("conflict"), size.Width, size.Height);
    pages["conflict-settled"] = (() => Shell("conflict-settled"), size.Width, size.Height);
    pages["plan"] = (() => Shell("plan"), size.Width, size.Height);
    foreach (var tab in new[] { "versions", "versions-kept", "versions-empty", "saves-log", "saves-log-needs", "saves-log-search" })
    {
        pages[tab] = (() => Shell(tab), size.Width, size.Height);
    }
    foreach (var section in new[] { "general", "art", "launch", "files", "saves", "sync" })
    {
        pages["properties-" + section] = (() => Shell("game", section), size.Width, size.Height);
    }

    pages["add-place"] = (() => Shell("game-saves", "add-place"), size.Width, size.Height);
    foreach (var dialog in new[] { "named-save", "named-save-taken", "named-save-new" })
    {
        pages[dialog] = (() => Shell("game-saves", dialog), size.Width, size.Height);
    }

    pages["game-saves-kept"] = (() => Shell("game-saves-kept"), size.Width, size.Height);
    foreach (var learn in new[] { "game-saves-learn", "game-saves-learning", "game-saves-learned" })
    {
        pages[learn] = (() => Shell(learn), size.Width, size.Height);
    }

    pages["learn-finds"] = (() => Shell("game-saves-learned", "learn-finds"), size.Width, size.Height);
    pages["game-saves-busy"] = (() => Shell("game-saves-busy"), size.Width, size.Height);
    pages["game-saves-playing"] = (() => Shell("game-saves-playing"), size.Width, size.Height);
    foreach (var dialog in new[] { "kept-copies", "kept-copies-backup", "kept-copies-done", "kept-copies-looking", "kept-copies-keeping" })
    {
        pages[dialog] = (() => Shell("game-saves-kept", dialog), size.Width, size.Height);
    }

    pages["import-kept"] = (() => Shell("game-saves", "import-kept"), size.Width, size.Height);
    pages["home-nocloud"] = (() => Shell("home-nocloud"), size.Width, size.Height);
    pages["connect-cloud"] = (() => Shell("home-nocloud", "connect-cloud"), size.Width, size.Height);
    pages["add-game"] = (() => Shell("library", "add-game"), size.Width, size.Height);
    pages["add-game-program"] = (() => Shell("library", "add-game-program"), size.Width, size.Height);
    pages["setup-add-game"] = (() => Shell("setup-choose", "setup-add-game"), size.Width, size.Height);
    foreach (var dialog in new[] { "share", "share-all", "share-export", "share-done", "import-saves" })
    {
        pages[dialog] = (() => Shell("saves", dialog), size.Width, size.Height);
    }

    // Achievements by game (KAN-131), over Settings' Achievements.
    pages["achievement-games"] = (() => Shell("settings-achievements", "achievement-games"), size.Width, size.Height);
    pages["saves-selected"] = (() => Shell("saves-selected"), size.Width, size.Height);
    foreach (var section in new[] { "settings", "settings-storage", "settings-backup", "settings-backup-apply", "settings-cloud", "settings-cloud-drive", "settings-devices",
                 "settings-notifications", "settings-achievements", "settings-safety", "settings-updates", "settings-updates-latest", "settings-updates-downloading",
                 "settings-updates-refused" })
    {
        pages[section] = (() => Shell(section), size.Width, size.Height);
    }

    foreach (var step in new[] { "setup-scan", "setup", "setup-choose", "setup-cloud", "setup-cloud-folder", "setup-daily", "setup-daily-time" })
    {
        pages[step] = (() => Shell(step), size.Width, size.Height);
    }

    // The app's window as it opens, Glossy or Solid by the theme, with the art the app would use.
    var art = home.Hero?.HeroPath ?? home.Hero?.CoverPath;
    var console = new ConsoleViewModel();
    console.AddFile(
    [
        "21:40:41  GameSync started; watching 44 games.",
        "21:52:10  Hades: playing since 21:52:08.",
        "22:31:57  Hades: synced after play (3 files, 1.2 MB).",
        "22:32:02  ! Sekiro: Shadows Die Twice: changed on two PCs. A conflict waits for you.",
    ], now);
    foreach (var current in new[] { "home", "library", "game", "saves", "game-saves", "conflict", "log", "settings", "properties", "setup", "setup-choose", "plan", "versions", "saves-log", "achievements" })
    {
        glossy[current == "log" ? "glossy-console" : "glossy-" + current] = choice =>
        {
            var makers = Makers();
            makers["log"] = () => console;
            var shell = new ShellViewModel(makers, current == "properties" ? "game" : current)
            {
                Rail = rail,
                Dialog = current == "properties" ? Properties("saves") : null,
            };
            var window = new MainWindow { DataContext = shell, Width = size.Width, Height = size.Height };
            // A game's page shows its own art, as the app does.
            var shown = shell.BackdropArt ?? art;
            var glass = shown is null ? null : ThemeEngine.Glass(choice, shell.Strength);
            window.ShowSurface(glass, shown is not null && glass is not null ? Backdrop.Make(shown, glass, ThemeEngine.Build(choice)) : null);
            return window;
        };
    }

    // KAN-54: the library with the game --game names glowing in over it, for --bench (a frame of the glow each time).
    glossy["glossy-glow"] = choice =>
    {
        var window = glossy["glossy-library"](choice);
        if ((shown?.HeroPath ?? shown?.CoverPath ?? art) is { } next && ThemeEngine.Glass(choice, GlassStrength.Glass) is { } glass)
        {
            glowNext = Backdrop.Make(next, glass, ThemeEngine.Build(choice));
        }

        return window;
    };

    // KAN-124: how long switching between Dark and Light takes in Glossy for the window's own work (the theme, the glass and a
    // frame), with the glass recoloured in place, and for comparison cleared first and made again, as it was; and the picture of
    // the window a change of look starts from.
    if (wanted.Contains("look-timing") && glossy.TryGetValue("glossy-home", out var timedHome))
    {
        var dark = new ThemeChoice();
        var light = new ThemeChoice(Mode: ThemeMode.Light);
        ThemeService.Apply(Application.Current!, dark);
        var window = (MainWindow)timedHome(dark);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Dispose();
        var surfaces = new[] { dark, light }.ToDictionary(c => c.Mode, c =>
        {
            var glass = ThemeEngine.Glass(c, GlassStrength.Glass)!;
            var picture = art is null ? null : Backdrop.Make(art, glass, ThemeEngine.Build(c));
            return (Glass: glass, Picture: picture);
        });
        foreach (var clearFirst in new[] { true, false, true, false })
        {
            var times = new List<double>();
            for (var i = 0; i < 8; i++)
            {
                var choice = i % 2 == 0 ? light : dark;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                ThemeService.Apply(Application.Current!, choice);
                var themed = watch.Elapsed.TotalMilliseconds;
                if (clearFirst)
                {
                    window.ShowSurface(null, null);
                }

                window.ShowSurface(surfaces[choice.Mode].Glass, surfaces[choice.Mode].Picture);
                var glassed = watch.Elapsed.TotalMilliseconds;
                Dispatcher.UIThread.RunJobs();
                var laid = watch.Elapsed.TotalMilliseconds;
                window.CaptureRenderedFrame()?.Dispose();
                times.Add(watch.Elapsed.TotalMilliseconds);
                Console.WriteLine($"  theme {themed:F0}, glass {glassed - themed:F0}, layout {laid - glassed:F0}, frame {watch.Elapsed.TotalMilliseconds - laid:F0} ms");
            }

            Console.WriteLine($"{(clearFirst ? "cleared first (before)" : "in place (now)")}: {string.Join(", ", times.Select(t => t.ToString("F0")))} ms; median {times.Order().ElementAt(times.Count / 2):F0} ms");
        }

        var picture = System.Diagnostics.Stopwatch.StartNew();
        using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height)))
        {
            bitmap.Render(window);
        }

        Console.WriteLine($"the window's picture, as a change of look takes it: {picture.Elapsed.TotalMilliseconds:F0} ms");
        window.Close();
    }
}

var themes = new[]
{
    ("dark", new ThemeChoice()),
    ("light", new ThemeChoice(Mode: ThemeMode.Light)),
    ("sakura", new ThemeChoice("sakura")),
};

foreach (var (name, page) in pages.Where(p => wanted.Count == 0 || wanted.Contains(p.Key)))
{
    foreach (var (themeName, choice) in themes)
    {
        ThemeService.Apply(Application.Current!, choice);
        Save(new Window { Width = page.Width, Height = page.Height, Content = page.Make() }, $"{name}-{themeName}.png");
    }
}

foreach (var (name, make) in glossy.Where(p => wanted.Count == 0 || wanted.Contains(p.Key)))
{
    foreach (var (themeName, choice) in themes)
    {
        ThemeService.Apply(Application.Current!, choice);
        Save(make(choice), $"{name}-{themeName}.png");
    }
}

// KAN-76: a change of look held part way, the save manager in Dark Glossy coming through as Light from a click near its
// top left, to look at the curtain's frames.
if (wanted.Contains("look-change") && glossy.TryGetValue("glossy-saves", out var savesWindow))
{
    foreach (var t in new[] { 0.15, 0.4, 0.7 })
    {
        var dark = new ThemeChoice();
        ThemeService.Apply(Application.Current!, dark);
        var window = (MainWindow)savesWindow(dark);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var light = new ThemeChoice(Mode: ThemeMode.Light);
        window.ChangeLookFrame(() =>
        {
            ThemeService.Apply(Application.Current!, light);
            window.ShowSurface(null, null);
        }, new Point(260, 140), t);
        Save(window, $"look-change-{t * 100:0}.png");
    }
}

void Save(Window window, string fileName)
{
    var opening = System.Diagnostics.Stopwatch.StartNew();
    window.Show();
    if (bench > 0)
    {
        // PERF-03: the first frame comes before what's left for idle moments (the library's later covers), as in the app.
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
        window.CaptureRenderedFrame()?.Dispose();
        Console.WriteLine($"{fileName}: first frame in {opening.Elapsed.TotalMilliseconds:F0} ms");
    }

    Dispatcher.UIThread.RunJobs();
    if (keys.Count > 0)
    {
        Tabs(window, fileName);
        return;
    }

    if (bench > 0)
    {
        // The first frame: everything the page builds as it opens, as the app's window does.
        window.CaptureRenderedFrame()?.Dispose();
        Console.WriteLine($"{fileName}: everything made in {opening.Elapsed.TotalMilliseconds:F0} ms");
        if (revisit && window.GetVisualDescendants().OfType<GameSync.UI.Views.Shell>().FirstOrDefault()?.DataContext is ShellViewModel shell && shell.Page is { } page)
        {
            shell.NavigateCommand.Execute("home");
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Dispose();
            var again = System.Diagnostics.Stopwatch.StartNew();
            shell.Page = page;
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Dispose();
            Console.WriteLine($"{fileName}: back to it from Home in {again.Elapsed.TotalMilliseconds:F0} ms");
        }
        Bench(window, fileName);
        return;
    }

    if (hover is { } point)
    {
        // Tooltips show at once, so a hover shows what it says.
        // The tooltip of what's under the pointer shows at once, as it would after a moment.
        window.MouseMove(point);
        Dispatcher.UIThread.RunJobs();
        if ((window.GetVisualAt(point) as Visual)?.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(c => ToolTip.GetTip(c) is not null) is { } tipped)
        {
            ToolTip.SetIsOpen(tipped, true);
            Dispatcher.UIThread.RunJobs();
        }
    }

    if (click is { } at)
    {
        window.MouseMove(at);
        window.MouseDown(at, Avalonia.Input.MouseButton.Left);
        window.MouseUp(at, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    var frame = window.CaptureRenderedFrame();
    var file = Path.Combine(output, fileName);
    frame?.Save(file);
    window.Close();
    Console.WriteLine(frame is null ? $"{file}: nothing rendered" : file);
}

void Tabs(Window window, string name)
{
    window.CaptureRenderedFrame()?.Dispose();
    var first = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.DataContext is SaveRow);
    first?.Focus(Avalonia.Input.NavigationMethod.Tab);
    Dispatcher.UIThread.RunJobs();
    Console.WriteLine($"{name}: from {(first is null ? "the window" : Avalonia.Automation.AutomationProperties.GetName(first))}, {keys.Count} keys:");
    var stops = new List<(string Said, Avalonia.Media.IImage Cut)>();
    var frames = new List<IDisposable>();
    for (var i = 0; i < keys.Count; i++)
    {
        window.KeyPress(keys[i], Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        window.KeyRelease(keys[i], Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var focused = window.FocusManager?.GetFocusedElement() as Control;
        var heard = focused is null ? null : Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(focused).GetName();
        var said = focused is null ? "(nothing)" : heard is { Length: > 0 } ? heard : $"no name ({focused.GetType().Name})";
        Console.WriteLine($"  {i + 1,3}  {keys[i],-8} {said}");
        if (focused?.TranslatePoint(new Point(0, 0), window) is { } at && window.CaptureRenderedFrame() is { } frame)
        {
            // The control and 8px around it, where its ring is drawn, cut out of this frame.
            frames.Add(frame);
            var left = Math.Max(0, (int)at.X - 8);
            var top = Math.Max(0, (int)at.Y - 8);
            var width = Math.Min(frame.PixelSize.Width - left, (int)Math.Min(focused.Bounds.Width + 16, 520));
            var height = Math.Min(frame.PixelSize.Height - top, (int)Math.Min(focused.Bounds.Height + 16, 260));
            if (width > 0 && height > 0)
            {
                stops.Add(($"{i + 1}. {keys[i]}: {said}", new Avalonia.Media.Imaging.CroppedBitmap(frame, new PixelRect(left, top, width, height))));
            }
        }
    }

    window.Close();
    var sheet = new WrapPanel { Margin = new Thickness(12) };
    foreach (var (said, cut) in stops)
    {
        sheet.Children.Add(new StackPanel
        {
            Margin = new Thickness(8),
            Spacing = 4,
            Width = 300,
            Children =
            {
                new TextBlock { Text = said, FontSize = 11, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis },
                new Image { Source = cut, Stretch = Avalonia.Media.Stretch.Uniform, MaxWidth = 300, MaxHeight = 120, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left },
            },
        });
    }

    var sheetWindow = new Window
    {
        Width = 1280,
        SizeToContent = SizeToContent.Height,
        Background = Application.Current!.FindResource("bg-100") as Avalonia.Media.IBrush,
        Content = sheet,
    };
    sheetWindow.Show();
    Dispatcher.UIThread.RunJobs();
    var file = Path.Combine(output, Path.ChangeExtension(name, null) + $"-tabs.png");
    sheetWindow.CaptureRenderedFrame()?.Save(file);
    Console.WriteLine(file);
    sheetWindow.Close();
    foreach (var frame in frames)
    {
        frame.Dispose();
    }
}

void Bench(Window window, string name)
{
    var glowing = name.StartsWith("glossy-glow", StringComparison.Ordinal) && window is MainWindow && glowNext is not null;
    var scroller = glowing ? null : window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.Extent.Height > s.Viewport.Height)
        .MaxBy(s => s.Viewport.Width * s.Viewport.Height);

    window.CaptureRenderedFrame()?.Dispose();
    var times = new List<double>();
    var layouts = new List<double>();
    for (var i = 0; i < bench; i++)
    {
        if (scroller is not null)
        {
            var y = scroller.Offset.Y + 37;
            scroller.Offset = new Vector(0, y > scroller.Extent.Height - scroller.Viewport.Height ? 0 : y);
        }
        else if (glowing)
        {
            ((MainWindow)window).GlowFrame(glowNext!, (double)i / bench);
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        layouts.Add(watch.Elapsed.TotalMilliseconds);
        using var frame = window.CaptureRenderedFrame();
        times.Add(watch.Elapsed.TotalMilliseconds);
    }

    times.Sort();
    layouts.Sort();
    Console.WriteLine($"{name}: {window.Width}x{window.Height}, {(glowing ? "glowing" : scroller is null ? "nothing scrolls" : "scrolling")}: median {times[times.Count / 2]:F1} ms (layout {layouts[layouts.Count / 2]:F1}), 90% under {times[(int)(times.Count * 0.9)]:F1} ms, slowest {times[^1]:F1} ms");
    window.Close();
}

// Each picture's backdrop as the game's page shows it (Glass, Dark), before and after Backdrop.Tame, side by side and named
// with the OKLab chroma of its softened pixels (average and highest), for tuning how colourful a backdrop may stay.
void BackdropSheet(string folder)
{
    var choice = new ThemeChoice();
    ThemeService.Apply(Application.Current!, choice);
    var glass = ThemeEngine.Glass(choice, GlassStrength.Glass)!;
    var theme = ThemeEngine.Build(choice);
    var rows = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
    foreach (var file in Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase))
    {
        if (Backdrop.Pixels(file) is not { } art)
        {
            continue;
        }

        var soft = Backdrop.Soften(art.Pixels, art.Width, art.Height);
        var before = Backdrop.Chroma(soft);
        var tamed = Backdrop.Tame((double[])soft.Clone());
        var after = Backdrop.Chroma(tamed);
        var label = $"{Path.GetFileNameWithoutExtension(file)}\nbefore {before.Average:0.000} / {before.Highest:0.000}\nafter {after.Average:0.000} / {after.Highest:0.000}";
        Console.WriteLine(label.Replace('\n', ' '));
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = label, Width = 170, Foreground = Avalonia.Media.Brushes.White, FontSize = 12 });
        // Before, then the cap as set, then any others to compare (--caps knee:limit,knee:limit).
        var variants = new List<double[]> { soft, tamed };
        foreach (var cap in caps)
        {
            variants.Add(Backdrop.Tame((double[])soft.Clone(), cap.Knee, cap.Limit));
        }

        foreach (var rgb in variants)
        {
            // What shows: the chroma of the finished pixels, under the scrim.
            var baked = Backdrop.Bake(rgb, glass, Backdrop.ScrimAlphas(rgb, glass, theme));
            var shown = new double[baked.Length / 4 * 3];
            for (int i = 0, o = 0; i < baked.Length; i += 4, o += 3)
            {
                (shown[o], shown[o + 1], shown[o + 2]) = (baked[i + 2], baked[i + 1], baked[i]);
            }

            var finished = Backdrop.Chroma(shown);
            Console.WriteLine($"  shown {finished.Average:0.000} / {finished.Highest:0.000}");
            var cell = new Panel { Width = 320, Height = 200 };
            cell.Children.Add(new Avalonia.Controls.Image { Source = Backdrop.Finish(rgb, glass, theme), Width = 320, Height = 200, Stretch = Avalonia.Media.Stretch.Fill });
            cell.Children.Add(new TextBlock { Text = $"shown {finished.Average:0.000} / {finished.Highest:0.000}", Foreground = Avalonia.Media.Brushes.White, FontSize = 11, Margin = new Thickness(6) });
            row.Children.Add(cell);
        }

        rows.Children.Add(row);
    }

    var window = new Window { Width = 220 + 328 * (2 + caps.Count), Height = rows.Children.Count * 208 + 32, Background = Avalonia.Media.Brushes.Black, Content = rows };
    window.Show();
    Dispatcher.UIThread.RunJobs();
    var sheet = Path.Combine(output, "backdrops.png");
    window.CaptureRenderedFrame()?.Save(sheet);
    window.Close();
    Console.WriteLine(sheet);
}

// The Zenith medal at every size it's drawn at, earned and not, the badge alone, and the monument earned and not (design
// system version 45). Its sheen is still, as every frame here is the first.
Control ZenithSheet()
{
    static StackPanel Row(params Control[] items)
    {
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 28 };
        foreach (var item in items)
        {
            item.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            row.Children.Add(item);
        }

        return row;
    }

    static GameSync.UI.Controls.GsZenithMedal Medal(double size, bool earned = true) => new() { Width = size, Height = size, IsEarned = earned };
    return new StackPanel
    {
        Margin = new Thickness(48, 40),
        Spacing = 40,
        Children =
        {
            Row(Medal(180), Medal(104), Medal(64), Medal(56), Medal(48), Medal(40), Medal(34), Medal(30), Medal(26), Medal(22), Medal(18)),
            Row(Medal(40, earned: false), Medal(26, earned: false), new GameSync.UI.Controls.GsZenithBadge { Width = 30, Height = 30 },
                new GameSync.UI.Controls.GsZenithMonument { Width = 132, Height = 116, IsEarned = true }, new GameSync.UI.Controls.GsZenithMonument { Width = 240, Height = 200, IsEarned = true },
                new GameSync.UI.Controls.GsZenithMonument { Width = 132, Height = 116 }),
            // Home's card, earned and not yet, and a game's page (version 52: Home's Zenith is its dot), then the tier chips
            Row(new GameSync.UI.Controls.GsAchievementsOverview { IsSmall = true, Done = 1, Total = 1, Bronze = 1 },
                new GameSync.UI.Controls.GsAchievementsOverview { IsSmall = true, Done = 29, Total = 34, Gold = 4, Silver = 9, Bronze = 16 },
                new GameSync.UI.Controls.GsAchievementsOverview { Done = 34, Total = 34, Gold = 5, Silver = 11, Bronze = 18, CompletedOn = "7 Oct" }),
            Row(new GameSync.UI.Controls.GsTierChip { Tier = "gold", Count = "12" }, new GameSync.UI.Controls.GsTierChip { Tier = "silver", Count = "40" },
                new GameSync.UI.Controls.GsTierChip { Tier = "bronze", Count = "71" }, new GameSync.UI.Controls.GsTierChip { Tier = "zenith" }),
        },
    };
}

static string? TakeOption(List<string> list, string name)
{
    var at = list.IndexOf(name);
    if (at < 0 || at + 1 >= list.Count)
    {
        return null;
    }

    var value = list[at + 1];
    list.RemoveRange(at, 2);
    return value;
}
