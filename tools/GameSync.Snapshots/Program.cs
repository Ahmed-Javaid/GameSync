// Renders pages of GameSync's UI to PNG files, in the themes asked for, with Avalonia's headless platform and Skia:
//   dotnet run --project tools/GameSync.Snapshots -- <output folder> [--data <data folder>] [--game <id>] [--size 1920x1128] [--hover x,y] [page ...]
// Pages: gallery, gallery2, gallery3, and with --data, home, library, library-search and library-game (a game's page, the
// one --game names or the hero game), saves and game-saves (the save manager, and that game's saves in it), and
// properties-general, -launch, -files, -saves and -sync (its Properties) from that data folder's games and art; and the
// Glossy window (LOOK-17) over the last-played game's art: glossy-home, glossy-library, glossy-game, glossy-saves,
// glossy-game-saves, glossy-properties, glossy-console, glossy-settings.
// Themes: every page in Arcade dark, Arcade light and Sakura dark; Glossy goes Solid in light mode, as the app does.
// "icons" writes gamesync.ico (copy it to src/GameSync.Tray/Assets) and a sheet of the tray icon in every state.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
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
// A window size for the app's pages instead of the design's 1280 × 800, such as a maximized window's.
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
    var rail = ShellViewModel.DefaultRail(games.FirstOrDefault(g => g.Status == GameSync.Core.State.GameStatus.Playing)?.Title, games.Count(g => g.NeedsYou));
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
        SaveProperties = (_, _) => { },
        CloseDialog = () => { },
    };

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
        ["library"] = () => Library(),
        ["hidden"] = () => Library("hidden"),
        ["searched"] = () => Library(search: "re"),
        ["game"] = () => Library(open: true),
        ["saves"] = () => Saves(),
        ["game-saves"] = () => Saves(open: true),
    };

    Control Shell(string current, string? dialog = null) => new Shell
    {
        DataContext = new ShellViewModel(Makers(), current) { Rail = rail, Dialog = dialog is null ? null : Properties(dialog) },
    };
    pages["home"] = (() => Shell("home"), size.Width, size.Height);
    pages["library"] = (() => Shell("library"), size.Width, size.Height);
    pages["library-full"] = (() => Shell("library"), 1280, 2400);
    pages["library-hidden"] = (() => Shell("hidden"), 1280, 800);
    pages["library-search"] = (() => Shell("searched"), size.Width, size.Height);
    pages["library-game"] = (() => Shell("game"), size.Width, size.Height);
    pages["saves"] = (() => Shell("saves"), size.Width, size.Height);
    pages["game-saves"] = (() => Shell("game-saves"), size.Width, size.Height);
    foreach (var section in new[] { "general", "launch", "files", "saves", "sync" })
    {
        pages["properties-" + section] = (() => Shell("game", section), size.Width, size.Height);
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
    foreach (var current in new[] { "home", "library", "game", "saves", "game-saves", "log", "settings", "properties" })
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
