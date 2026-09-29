// Renders pages of GameSync's UI to PNG files, in the themes asked for, with Avalonia's headless platform and Skia:
//   dotnet run --project tools/GameSync.Snapshots -- <output folder> [--data <data folder>] [--game <id>] [--size 1920x1128] [--hover x,y] [page ...]
// Pages: gallery, gallery2, gallery3, and with --data, home, home-playing (the hero game running), library, library-installed,
// library-local, library-search and library-game (a game's page, the
// one --game names or the hero game), saves and game-saves (the save manager, and that game's saves in it), conflict and
// conflict-settled (a made-up conflict of that game, waiting and settled), add-place and import-kept (those dialogs over
// its saves, made up), and
// properties-general, -art, -launch, -files, -saves and -sync (its Properties) from that data folder's games and art;
// first run over that data folder's library, as if it weren't set up (its stores' folders made up): setup-scan (the scan
// under way), setup, setup-choose, setup-cloud, setup-cloud-folder, setup-daily and setup-daily-time; home-nocloud and
// connect-cloud (Home after Skip for now, and its Connect the cloud dialog); and the
// Glossy window (LOOK-17) over the last-played game's art: glossy-home, glossy-library, glossy-game, glossy-saves,
// glossy-game-saves, glossy-conflict, glossy-properties, glossy-console, glossy-settings, glossy-setup, glossy-setup-choose.
// Themes: every page in Arcade dark, Arcade light and Sakura dark; Glossy goes Solid in light mode, as the app does.
// "icons" writes gamesync.ico (copy it to src/GameSync.Tray/Assets) and a sheet of the tray icon in every state.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using GameSync.Core.Model;
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

AppBuilder.Configure<GsApp>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

var glossy = new Dictionary<string, Func<ThemeChoice, Window>>(StringComparer.OrdinalIgnoreCase);
var pages = new Dictionary<string, (Func<Control> Make, int Width, int Height)>(StringComparer.OrdinalIgnoreCase)
{
    ["gallery"] = (() => new Gallery(), 1280, 1100),
    ["gallery2"] = (() => new Gallery2(), 1280, 1180),
    ["gallery3"] = (() => new Gallery3(), 1280, 800),
};

if (dataDir is not null)
{
    var now = DateTime.Now;
    var (games, home) = LauncherData.Read(dataDir, now);
    var rail = ShellViewModel.DefaultRail(games.FirstOrDefault(g => g.IsRunning)?.Title, games.Count(g => g.NeedsYou));
    // The library as it opens, on its Hidden view, with a search typed, and with a game's page open beside the list: the
    // one --game names, or the hero game, its places, saves and history read from the data folder as the app reads them.
    var shown = gameId is not null ? games.FirstOrDefault(g => g.Id.Value == gameId) : home.Hero ?? games.FirstOrDefault();

    // Actions that do nothing, so buttons look as the app's do; pages read their content here, not through them.
    var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { })
    {
        SetFavourite = (_, _) => { },
        OpenGame = _ => { },
        OpenFolder = _ => { },
        SyncGame = _ => { },
        BackUpNow = _ => { },
        SaveAs = (_, _) => { },
        Restore = (_, _, _) => { },
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
        SetView = _ => { },
        ScanFolder = (folder, _) => Task.FromResult(new FolderScan(folder, [], 0)),
        Locate = (_, _) => { },
    };

    // First run's and Connect the cloud's two ways, answering without signing in or writing: a folder picked becomes
    // GameSync's folder in it.
    var setupCloud = new CloudActions(true, _ => Task.FromResult<string?>(null), picked => Path.Combine(picked, "GameSync"));

    // Read off the UI thread, as the app does: waiting on it here would hold the thread its awaits come back to.
    T Off<T>(Func<Task<T>> read) => Task.Run(read).GetAwaiter().GetResult();

    LibraryViewModel Library(string tab = "all", string search = "", bool open = false)
    {
        var library = LibraryViewModel.From(games, now, tab, actions);
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
        saves.Show(Off(() => SaveOverview.ReadAsync(dataDir, CancellationToken.None)), now);
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

    Dictionary<string, Func<object?>> Makers() => new()
    {
        ["home"] = () => HomeViewModel.From(home, games, now),
        ["playing"] = Playing,
        ["library"] = () => Library(),
        ["installed"] = () => Library("installed"),
        ["local"] = () => Library("local"),
        ["hidden"] = () => Library("hidden"),
        ["searched"] = () => Library(search: "re"),
        ["game"] = () => Library(open: true),
        ["saves"] = () => Saves(),
        ["game-saves"] = () => Saves(open: true),
        ["conflict"] = () => Conflict(settled: false),
        ["conflict-settled"] = () => Conflict(settled: true),
        ["home-nocloud"] = () => HomeViewModel.From(home, games, now, actions,
            new HomeStatus("No cloud yet", GameSync.Core.Storage.NoCloud.Message, Environment.MachineName, []) { NoCloud = true }),
        ["setup-scan"] = () => Setup("scanning"),
        ["setup"] = () => Setup("scan"),
        ["setup-choose"] = () => Setup("choose"),
        ["setup-cloud"] = () => Setup("cloud"),
        ["setup-cloud-folder"] = () => Setup("cloud-folder"),
        ["setup-daily"] = () => Setup("daily"),
        ["setup-daily-time"] = () => Setup("daily-time"),
    };

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
            (_, _) => Task.FromResult(new SetupResult(0, 0, []))));
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

    // Home with a game running (PLAY-12): the game --game names, or the hero game, playing for the last 35 minutes.
    HomeViewModel Playing()
    {
        var running = games.Select(g => g.Id == shown?.Id ? g with { RunningSinceUtc = now.AddMinutes(-35).ToUniversalTime() } : g).ToList();
        using var state = new GameSync.Core.State.StateStore(dataDir);
        return HomeViewModel.From(Launcher.Home(running, state, now), running, now);
    }

    // Add a place over a game's saves, with a folder picked (made up, in the game --game names): as the design draws it.
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
                _ => Properties(dialog),
            },
        },
    };
    pages["home"] = (() => Shell("home"), size.Width, size.Height);
    pages["home-playing"] = (() => Shell("playing"), size.Width, size.Height);
    pages["library"] = (() => Shell("library"), size.Width, size.Height);
    pages["library-full"] = (() => Shell("library"), 1280, 2400);
    pages["library-hidden"] = (() => Shell("hidden"), 1280, 800);
    pages["library-installed"] = (() => Shell("installed"), size.Width, size.Height);
    pages["library-local"] = (() => Shell("local"), size.Width, size.Height);
    pages["library-search"] = (() => Shell("searched"), size.Width, size.Height);
    pages["library-game"] = (() => Shell("game"), size.Width, size.Height);
    pages["saves"] = (() => Shell("saves"), size.Width, size.Height);
    pages["game-saves"] = (() => Shell("game-saves"), size.Width, size.Height);
    pages["conflict"] = (() => Shell("conflict"), size.Width, size.Height);
    pages["conflict-settled"] = (() => Shell("conflict-settled"), size.Width, size.Height);
    foreach (var section in new[] { "general", "art", "launch", "files", "saves", "sync" })
    {
        pages["properties-" + section] = (() => Shell("game", section), size.Width, size.Height);
    }

    pages["add-place"] = (() => Shell("game-saves", "add-place"), size.Width, size.Height);
    pages["import-kept"] = (() => Shell("game-saves", "import-kept"), size.Width, size.Height);
    pages["home-nocloud"] = (() => Shell("home-nocloud"), size.Width, size.Height);
    pages["connect-cloud"] = (() => Shell("home-nocloud", "connect-cloud"), size.Width, size.Height);
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
    foreach (var current in new[] { "home", "library", "game", "saves", "game-saves", "conflict", "log", "settings", "properties", "setup", "setup-choose" })
    {
        glossy[current == "log" ? "glossy-console" : "glossy-" + current] = choice =>
        {
            var makers = Makers();
            makers["log"] = () => console;
            makers["settings"] = () => new PlaceholderViewModel("Settings", "settings", "Appearance, folders, the daily backup, the cloud, your PCs and notifications.");
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

void Save(Window window, string fileName)
{
    window.Show();
    Dispatcher.UIThread.RunJobs();
    if (hover is { } point)
    {
        window.MouseMove(point);
        Dispatcher.UIThread.RunJobs();
    }

    var frame = window.CaptureRenderedFrame();
    var file = Path.Combine(output, fileName);
    frame?.Save(file);
    window.Close();
    Console.WriteLine(frame is null ? $"{file}: nothing rendered" : file);
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
