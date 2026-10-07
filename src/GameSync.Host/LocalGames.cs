using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.Storage;
using GameSync.Core.Sync;

namespace GameSync.Host;

/// <summary>What Scan a folder for games… found: the games in the folder, by name, and how many are new here.</summary>
public sealed record FolderScan(string Folder, IReadOnlyList<string> Games, int New)
{
    /// <summary>What the app says once the scan is done.</summary>
    public string Sentence => Games.Count switch
    {
        0 => $"No games in {Folder}: GameSync looks in it for folders with a game's program in them, such as {System.IO.Path.Combine(Folder, "Black Myth Wukong")}, and in folders of several games.",
        _ => $"Found {Count(Games.Count)} in {Folder}{(New < Games.Count ? $" ({New} new here)" : "")}: {Names()}. They're in the library's Local view; Play starts each from its folder.",
    };

    private string Names() => Games.Count <= 5 ? string.Join(", ", Games) : $"{string.Join(", ", Games.Take(4))} and {Games.Count - 4} more";

    private static string Count(int games) => games == 1 ? "1 game" : $"{games} games";
}

/// <summary>
/// Games in their own folders (LIB-22 to LIB-24): a folder of games for GameSync to look in, now and at every scan, and
/// a game found only by its saves located by its program, so Play can start it. Nothing on the PC is changed by either.
/// </summary>
public static class LocalGames
{
    /// <summary>
    /// LIB-23: Scan a folder for games…, as <c>gamesync add-folder</c> then <c>gamesync scan</c>: the folder joins the ones
    /// every scan looks in, and this PC is scanned now. Each folder in it with a game's program becomes a game in its own
    /// folder, joining the game its saves were already known by. The scan runs outside the engine lock; only folding its
    /// findings into the library waits for a sync in the background to finish. What goes wrong is thrown, for the page to say.
    /// </summary>
    public static async Task<FolderScan> ScanFolderAsync(string dataDir, string folder, IAgentOutput output, CancellationToken ct)
    {
        var full = Full(folder);
        if (!Directory.Exists(full))
        {
            throw new UsageException($"{full} isn't there on this PC.");
        }

        HashSet<GameId> before;
        using (var engine = Engine.Open(dataDir))
        {
            if (FolderRefusal(full, engine) is { } refused)
            {
                throw new UsageException(refused);
            }

            before = engine.Library.All().Where(e => e.Installed && e.MergedInto is null).Select(e => e.Id).ToHashSet();
            Cli.AddGameFolder(engine.State, full);
        }

        output.Say($"Looking for games in {full}, and scanning the rest of this PC with it...");
        var entries = await RescanAsync(dataDir, output, ct);
        var inFolder = entries.Where(e => e is { Installed: true, MergedInto: null, State: not LibraryState.Ignored, InstallDir: { } dir } && Inside(dir, full))
            .OrderBy(e => e.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new FolderScan(full, inFolder.Select(e => e.DisplayTitle).ToList(), inFolder.Count(e => !before.Contains(e.Id)));
    }

    /// <summary>
    /// FOLD-07, from Settings: a folder stops being one every scan looks in, and this PC is scanned again at once, so
    /// its games read Not installed now rather than at the next scan. Their saves and history stay, and a game located
    /// there by hand stays installed while its folder is there. Returns what the app says once it's done.
    /// </summary>
    public static async Task<string> RemoveFolderAsync(string dataDir, string folder, IAgentOutput output, CancellationToken ct)
    {
        var full = Full(folder);
        List<LibraryEntry> inFolder;
        using (var engine = Engine.Open(dataDir))
        {
            inFolder = engine.Library.All().Where(e => e is { Installed: true, MergedInto: null, InstallDir: { } dir } && Inside(dir, full)).ToList();
            if (!Cli.RemoveGameFolder(engine.State, full))
            {
                throw new UsageException($"GameSync wasn't looking in {full} for games.");
            }
        }

        output.Say($"GameSync no longer looks in {full} for games; scanning this PC again...");
        var entries = (await RescanAsync(dataDir, output, ct)).ToDictionary(e => e.Id);
        var gone = inFolder.Count(e => entries.GetValueOrDefault(e.Id) is not { Installed: true });
        return gone switch
        {
            0 => $"GameSync no longer looks in {full} for games.",
            1 => $"GameSync no longer looks in {full} for games. Its game reads Not installed now; its saves and history stay.",
            _ => $"GameSync no longer looks in {full} for games. Its {gone} games read Not installed now; their saves and history stay.",
        };
    }

    /// <summary>
    /// Scans this PC as <c>gamesync scan</c> does and folds what it found into the library: the scan runs outside the
    /// engine lock, and only folding it in waits for a sync in the background to finish. Returns the library after.
    /// </summary>
    internal static async Task<IReadOnlyList<LibraryEntry>> RescanAsync(string dataDir, IAgentOutput output, CancellationToken ct)
    {
        Cli.ScanFindings findings;
        using (var engine = Engine.Open(dataDir))
        {
            findings = await Cli.DiscoverAsync(engine.Here, refreshList: false, ct, note: n => output.Say($"! {n}"));
        }

        using var engineLock = await EngineLock.AcquireAsync(dataDir, () => output.Say("Waiting for the sync in the background to finish first."), ct);
        using var here = Engine.Open(dataDir);
        var others = await OtherGamesAsync(here, ct);
        var entries = Library.Reconcile(here.Library.All(), findings.Found, DateTime.UtcNow, others, findings.Leftovers);
        here.Library.ReplaceAll(entries);
        return entries;
    }

    /// <summary>
    /// LIB-24: Locate the game…, for a game found only by its saves (or one whose folder moved): the program picked in
    /// Windows' picker marks it installed in its game's folder on this PC, and Play starts it from that program. Rescans
    /// keep it there while the folder is on this PC. What goes wrong is thrown, for the page to say.
    /// </summary>
    /// <returns>What the app says once it's done.</returns>
    public static async Task<string> LocateAsync(string dataDir, GameId game, string program, IAgentOutput output, CancellationToken ct)
    {
        var full = System.IO.Path.GetFullPath(program);
        if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            throw new UsageException($"{full} isn't a program on this PC. Pick the game's .exe in its folder.");
        }

        var folder = LooseScanner.GameFolderOf(full);
        using var engineLock = await EngineLock.AcquireAsync(dataDir, () => output.Say("Waiting for the sync in the background to finish first."), ct);
        using var engine = Engine.Open(dataDir);
        var entry = engine.Library.All().FirstOrDefault(e => e.Id == game && e.MergedInto is null)
            ?? throw new UsageException($"GameSync doesn't know the game '{game}' on this PC.");
        if (entry.Installed && entry.Store is { } store && store != StoreKind.Loose)
        {
            throw new UsageException($"{entry.DisplayTitle} is installed through {StoreNames.Name(store)}, which starts it.");
        }

        if (ProgramRefusal(folder, engine, game) is { } refused)
        {
            throw new UsageException(refused);
        }

        var located = Library.Locate(entry, folder, Fingerprinter.Read(folder), DateTime.UtcNow);
        engine.Library.SaveAll([located]);

        // R15: a game with an anti-cheat starts only through its anti-cheat's own launcher, never the program picked.
        if (located.HasAntiCheat && !GameLaunch.IsAntiCheatLauncher(full))
        {
            GameLaunch.SetProgram(engine.State, game, null);
            return GameLaunch.AntiCheatLauncher(folder) is { } launcher
                ? $"{entry.DisplayTitle} is on this PC, in {folder}. It has an anti-cheat, so Play starts it through {System.IO.Path.GetFileName(launcher)}."
                : $"{entry.DisplayTitle} is on this PC, in {folder}. It has an anti-cheat, so start it from its launcher; GameSync backs up its saves when you've played.";
        }

        GameLaunch.SetProgram(engine.State, game, full, located.HasAntiCheat);
        return $"{entry.DisplayTitle} is on this PC, in {folder}. Play starts {System.IO.Path.GetFileName(full)} from there.";
    }

    /// <summary>
    /// Why a folder can't be one GameSync looks in for games: Windows, the whole system drive, the user folder, Program
    /// Files or AppData as a whole, which hold far more programs than games, and GameSync's own folders.
    /// </summary>
    internal static string? FolderRefusal(string folder, Engine engine)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length > 0 && (Same(folder, windows) || Inside(folder, windows)))
        {
            return "That's Windows' own folder, not a folder of games. Pick the folder your games are in, such as E:\\Games.";
        }

        if (System.IO.Path.GetPathRoot(windows) is { Length: > 0 } system && Same(folder, system))
        {
            return $"{system} holds Windows and your programs as well as games, so GameSync won't take every folder on it for a game. Pick the folder your games are in, such as {System.IO.Path.Combine(system, "Games")}.";
        }

        if (WholeFolder(folder) is { } whole)
        {
            return $"That's your whole {whole} folder, which holds programs that aren't games. Pick the folder your games are in, such as E:\\Games.";
        }

        return Ours(folder, engine);
    }

    /// <summary>
    /// Why a program's folder can't be a game's own: a drive's top, a folder the safety guard refuses (R5), a whole
    /// Windows folder, GameSync's own folders, or another game's folder.
    /// </summary>
    internal static string? ProgramRefusal(string folder, Engine engine, GameId game)
    {
        if (System.IO.Path.GetPathRoot(folder) is { } root && Same(folder, root))
        {
            return $"That program sits at the top of {root}, so GameSync can't tell which folder is the game's. Pick a program inside the game's own folder.";
        }

        if (engine.Here.Guard.CheckRoot(folder) is { } refusal && !refusal.StartsWith("a whole drive", StringComparison.Ordinal))
        {
            var reason = refusal.Contains(": ", StringComparison.Ordinal) ? refusal[(refusal.IndexOf(": ", StringComparison.Ordinal) + 2)..] : refusal;
            return $"{folder} can't be a game's folder: {reason.TrimEnd('.')}.";
        }

        if (WholeFolder(folder) is { } whole)
        {
            return $"That's your whole {whole} folder, not a game's own. Pick the program inside the game's own folder.";
        }

        if (engine.InstallDirs.FirstOrDefault(d => d.Key != game && Same(d.Value, folder)) is { Key.Value: not null } other)
        {
            var title = engine.Library.All().FirstOrDefault(e => e.Id == other.Key)?.DisplayTitle ?? other.Key.Value;
            return $"That's {title}'s folder. Pick the program inside this game's own folder.";
        }

        return Ours(folder, engine);
    }

    /// <summary>GameSync's own data and backup folders, which never hold a game.</summary>
    private static string? Ours(string folder, Engine engine)
    {
        var history = Cli.HistoryFolder(engine.State, engine.DataDir);
        return Same(folder, engine.DataDir) || Inside(folder, engine.DataDir) || Same(folder, history) || Inside(folder, history)
            ? "That's one of GameSync's own folders, not a folder of games."
            : null;
    }

    /// <summary>A Windows folder that holds far more than games, taken as a whole: its name, or null.</summary>
    private static string? WholeFolder(string folder)
    {
        (Environment.SpecialFolder Folder, string Name)[] wholes =
        [
            (Environment.SpecialFolder.UserProfile, "user"),
            (Environment.SpecialFolder.ProgramFiles, "Program Files"),
            (Environment.SpecialFolder.ProgramFilesX86, "Program Files (x86)"),
            (Environment.SpecialFolder.ApplicationData, @"AppData\Roaming"),
            (Environment.SpecialFolder.LocalApplicationData, @"AppData\Local"),
            (Environment.SpecialFolder.CommonApplicationData, "ProgramData"),
            (Environment.SpecialFolder.Desktop, "Desktop"),
            (Environment.SpecialFolder.MyDocuments, "Documents"),
        ];
        foreach (var (special, name) in wholes)
        {
            var path = Environment.GetFolderPath(special);
            if (path.Length > 0 && Same(folder, path))
            {
                return name;
            }
        }

        var downloads = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Same(folder, downloads) ? "Downloads" : null;
    }

    /// <summary>The other PCs' games, so a game new here takes the ID they gave it; offline or signed out, none.</summary>
    private static async Task<IReadOnlyDictionary<GameId, string>> OtherGamesAsync(Engine engine, CancellationToken ct)
    {
        try
        {
            var (service, _) = await engine.OpenServiceAsync(new SyncOptions { IsRunning = new RunningGames(engine).IsRunning }, ct, recover: false);
            return (await service.OtherGamesAsync(ct)).ToDictionary(o => o.Id, o => o.Title);
        }
        catch (Exception e) when (e is UsageException or CloudException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new Dictionary<GameId, string>();
        }
    }

    private static string Full(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        return System.IO.Path.GetPathRoot(full) is { } root && Same(full, root) ? root : System.IO.Path.TrimEndingDirectorySeparator(full);
    }

    private static string Norm(string path) => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));

    private static bool Same(string a, string b) => string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    /// <summary><paramref name="path"/> is inside <paramref name="folder"/>.</summary>
    private static bool Inside(string path, string folder)
    {
        var parent = Norm(folder);
        return Norm(path).StartsWith(parent.EndsWith(System.IO.Path.DirectorySeparatorChar) ? parent : parent + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
