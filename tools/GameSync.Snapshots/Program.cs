// Renders pages of GameSync's UI to PNG files, in the themes asked for, with Avalonia's headless platform and Skia:
//   dotnet run --project tools/GameSync.Snapshots -- <output folder> [--data <data folder>] [page ...]
// Pages: gallery, gallery2, gallery3, and with --data, home and library from that data folder's games and art.
// Themes: every page in Arcade dark, Arcade light and Sakura dark.

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
var output = Path.GetFullPath(list.Count > 0 ? list[0] : "snapshots");
var wanted = list.Skip(1).ToHashSet(StringComparer.OrdinalIgnoreCase);
Directory.CreateDirectory(output);

AppBuilder.Configure<GsApp>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

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
    pages["home"] = (() => Shell("home"), 1280, 800);
    pages["library"] = (() => Shell("library"), 1280, 800);
    pages["library-full"] = (() => Shell("library"), 1280, 2400);
    pages["library-hidden"] = (() => Shell("hidden"), 1280, 800);
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
        var window = new Window { Width = page.Width, Height = page.Height, Content = page.Make() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        var file = Path.Combine(output, $"{name}-{themeName}.png");
        frame?.Save(file);
        window.Close();
        Console.WriteLine(frame is null ? $"{file}: nothing rendered" : file);
    }
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
