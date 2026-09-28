// Renders pages of GameSync's UI to PNG files, in the themes asked for, with Avalonia's headless platform and Skia:
//   dotnet run --project tools/GameSync.Snapshots -- <output folder> [--data <data folder>] [--size 1920x1128] [page ...]
// Pages: gallery, gallery2, gallery3, and with --data, home and library from that data folder's games and art, and the
// Glossy window (LOOK-17) over the last-played game's art: glossy-home, glossy-library, glossy-console, glossy-settings.
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
// A window size for the app's pages instead of the design's 1280 × 800, such as a maximized window's.
var size = TakeOption(list, "--size")?.Split('x') is [var w, var h] ? (Width: int.Parse(w), Height: int.Parse(h)) : (Width: 1280, Height: 800);
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
    Control Shell(string current) => new Shell
    {
        DataContext = new ShellViewModel(new Dictionary<string, Func<object?>>
        {
            ["home"] = () => HomeViewModel.From(home, games, now),
            ["library"] = () => LibraryViewModel.From(games, now),
            ["hidden"] = () => LibraryViewModel.From(games, now, "hidden"),
        }, current) { Rail = rail },
    };
    pages["home"] = (() => Shell("home"), size.Width, size.Height);
    pages["library"] = (() => Shell("library"), size.Width, size.Height);
    pages["library-full"] = (() => Shell("library"), 1280, 2400);
    pages["library-hidden"] = (() => Shell("hidden"), 1280, 800);

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
    foreach (var current in new[] { "home", "library", "log", "settings" })
    {
        glossy[current == "log" ? "glossy-console" : "glossy-" + current] = choice =>
        {
            var shell = new ShellViewModel(new Dictionary<string, Func<object?>>
            {
                ["home"] = () => HomeViewModel.From(home, games, now),
                ["library"] = () => LibraryViewModel.From(games, now),
                ["log"] = () => console,
                ["settings"] = () => new PlaceholderViewModel("Settings", "settings", "Appearance, folders, the daily backup, the cloud, your PCs and notifications."),
            }, current) { Rail = rail };
            var window = new MainWindow { DataContext = shell, Width = size.Width, Height = size.Height };
            var glass = art is null ? null : ThemeEngine.Glass(choice, shell.Strength);
            window.ShowSurface(glass, art is not null && glass is not null ? Backdrop.Make(art, glass, ThemeEngine.Build(choice)) : null);
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
