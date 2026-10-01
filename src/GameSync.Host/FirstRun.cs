using System.Diagnostics;
using System.Globalization;
using GameSync.Core.Art;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Storage.Drive;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>How far first run's scan has got, for its progress bar (ONB-01).</summary>
/// <param name="Done">Games whose saves have been looked for.</param>
/// <param name="Total">Games found installed; 0 until the stores have been read.</param>
public sealed record ScanProgress(string Stage, int Done, int Total)
{
    /// <summary>The game folders being looked through, as the scan starts.</summary>
    public IReadOnlyList<string>? Folders { get; init; }

    /// <summary>Each store's games, once the stores have been read.</summary>
    public IReadOnlyList<StoreSummary>? Stores { get; init; }
}

/// <summary>A store's games, as first run's Scan this PC lists them: how many, and where they are.</summary>
public sealed record StoreSummary(string Name, int Games, string Where);

/// <summary>A folder GameSync looks in for games in their own folders (LIB-05), with how many it holds.</summary>
public sealed record GameFolderSummary(string Path, int Games, bool Missing);

/// <summary>What first run's scan found (ONB-01): each store's games, each game folder's, and how many games have saves.</summary>
public sealed record SetupScan(IReadOnlyList<StoreSummary> Stores, IReadOnlyList<GameFolderSummary> Folders, int Games, int WithSaves, TimeSpan Took);

/// <summary>First run's groups of games found (ONB-02), in the order Choose games shows them.</summary>
public enum SetupGroupKind
{
    /// <summary>Saves found, and no store's cloud moves them: ticked.</summary>
    Sync,

    /// <summary>Steam or Epic syncs them; GameSync keeps a backup of every version: ticked.</summary>
    StoreCloud,

    /// <summary>Only settings files found: unticked.</summary>
    OnlineOnly,

    /// <summary>Saves of a game that isn't installed: ticked, kept as history.</summary>
    NotInstalled,

    /// <summary>Installed, with no saves found yet: watched the first time it's played, nothing to tick.</summary>
    NoSaves,
}

/// <summary>A game in first run's Choose games (ONB-02, ONB-03): where its saves are and how they were found.</summary>
/// <param name="SavePath">Where its saves are, as a person reads it: "Documents\My Games\Terraria", "Registry: HKCU\Software\…".</param>
/// <param name="FoundBy">How: "save list", "engine rule", "name search".</param>
/// <param name="Bytes">What its saves come to now, for the first backup's size.</param>
/// <param name="AntiCheat">The anti-cheat it ships, when it has one (ONB-05).</param>
public sealed record SetupGame(GameId Id, string Title, string? CoverPath, string? SavePath, string? FoundBy, long Bytes, string? AntiCheat);

public sealed record SetupGroup(SetupGroupKind Kind, IReadOnlyList<SetupGame> Games);

/// <summary>What the person chose in first run.</summary>
public sealed record SetupChoice
{
    /// <summary>"drive", a folder, or "none" for Skip for now (see <see cref="AppConfig.Remote"/>).</summary>
    public required string Remote { get; init; }

    /// <summary>The games ticked in Choose games; they sync from now on.</summary>
    public IReadOnlyList<GameId> Games { get; init; } = [];

    public bool StartAtSignIn { get; init; } = true;

    /// <summary>The daily backup's time; null turns it off.</summary>
    public TimeOnly? DailyAt { get; init; } = new(20, 0);
}

/// <summary>What finishing first run did: the games that sync or are backed up, and anything to tell the person.</summary>
public sealed record SetupResult(int Syncing, int BackedUp, IReadOnlyList<string> Notes);

/// <summary>What starts GameSync at sign-in and runs the daily backup (BG-01, BG-02); tests give their own.</summary>
public interface ISetupSchedule
{
    /// <returns>What to tell the person, or null.</returns>
    string? StartAtSignIn(bool on);

    /// <returns>What to tell the person, or null.</returns>
    string? Daily(TimeOnly? at);
}

/// <summary>
/// First run (ONB-01 to ONB-05; design system → OnboardingScreen): scan this PC, choose games, connect the cloud, then
/// backups and startup. Nothing syncs until it's finished: until then there's no games.json, the scan only reads, and
/// the library it fills is this PC's own.
/// </summary>
public static class FirstRun
{
    /// <summary>
    /// Scan this PC: what Steam, Epic and EA hold, the game folders the person added, and each game's saves, folded into
    /// this PC's library as the command line's scan does. It only reads.
    /// </summary>
    /// <param name="sources">Where the stores keep their records; Windows' own by default (tests give their own).</param>
    public static async Task<SetupScan> ScanAsync(string dataDir, IProgress<ScanProgress>? progress, CancellationToken ct,
        Func<IReadOnlyList<string>, StoreSources>? sources = null)
    {
        var clock = Stopwatch.StartNew();
        using var engine = Engine.OpenForSetup(dataDir);
        Detection? detection = null;
        progress?.Report(new ScanProgress("Getting the list of where games keep their saves", 0, 0) { Folders = Cli.GameFoldersOf(engine.State) });
        var findings = await Cli.DiscoverAsync(engine.Here, refreshList: false, ct,
            detected: d =>
            {
                detection = d;
                progress?.Report(new ScanProgress("Looking for each game's saves", 0, d.Games.Count) { Stores = Stores(d) });
            },
            note: _ => { },
            looked: (done, total) => progress?.Report(new ScanProgress("Looking for each game's saves", done, total)),
            sources: sources);
        var entries = Library.Reconcile(engine.Library.All(), findings.Found, DateTime.UtcNow, null, findings.Leftovers);
        engine.Library.ReplaceAll(entries);

        var games = detection?.Games ?? [];
        var live = entries.Where(e => e.MergedInto is null && e.State != LibraryState.Ignored).ToList();
        var folders = Cli.GameFoldersOf(engine.State)
            .Select(folder => new GameFolderSummary(folder, games.Count(g => g.Store == StoreKind.Loose && Inside(g.InstallDir, folder)), !Directory.Exists(folder)))
            .ToList();
        return new SetupScan(Stores(detection), folders, live.Count, live.Count(e => e.Proposals.Count + e.RegistryProposals.Count > 0), clock.Elapsed);
    }

    /// <summary>Steam, Epic Games and the EA app, each with its games and where they are.</summary>
    private static IReadOnlyList<StoreSummary> Stores(Detection? detection)
    {
        var games = detection?.Games ?? [];
        StoreSummary Summary(string name, StoreKind store, IEnumerable<string>? libraries = null)
        {
            var mine = games.Where(g => g.Store == store).ToList();
            var where = (libraries ?? mine.Select(g => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(g.InstallDir)) ?? g.InstallDir))
                .Select(p => Capital(Path.TrimEndingDirectorySeparator(p))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new StoreSummary(name, mine.Count, mine.Count == 0 || where.Count == 0 ? "Nothing installed right now" : Join(where));
        }

        return
        [
            Summary("Steam", StoreKind.Steam, detection?.Steam?.Libraries),
            Summary("Epic Games", StoreKind.Epic),
            Summary("EA app", StoreKind.Ea),
        ];
    }

    /// <summary>A drive's letter as Windows shows it: Steam may record its library as "e:\steam".</summary>
    private static string Capital(string path) =>
        path.Length >= 2 && path[1] == ':' && char.IsLower(path[0]) ? char.ToUpperInvariant(path[0]) + path[1..] : path;

    /// <summary>"E:\Steam and G:\SteamLibrary", "A, B and 2 more".</summary>
    private static string Join(IReadOnlyList<string> items) => items.Count switch
    {
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        3 => $"{items[0]}, {items[1]} and {items[2]}",
        _ => $"{items[0]}, {items[1]} and {items.Count - 2} more",
    };

    /// <summary>
    /// Choose games (ONB-02, ONB-03): the library's games in first run's groups, each with where its saves are and how
    /// they were found, by name within a group.
    /// </summary>
    public static IReadOnlyList<SetupGroup> Groups(string dataDir)
    {
        using var engine = Engine.OpenForSetup(dataDir);
        using var art = new ArtCache(dataDir);
        var saveList = new SaveListStore(dataDir).Load();
        var groups = new Dictionary<SetupGroupKind, List<SetupGame>>();
        foreach (var entry in engine.Library.All().Where(e => e.MergedInto is null && e.State != LibraryState.Ignored))
        {
            var found = entry.Proposals.Count + entry.RegistryProposals.Count > 0;
            var kind = !found ? (entry.Installed ? SetupGroupKind.NoSaves : (SetupGroupKind?)null)
                : entry.ProbablyOnlineOnly ? SetupGroupKind.OnlineOnly
                : !entry.Installed ? SetupGroupKind.NotInstalled
                : entry.StoreCloud ? SetupGroupKind.StoreCloud
                : SetupGroupKind.Sync;
            var steamId = Launcher.SteamIdOf(entry, saveList);
            if (kind is not { } group || (steamId is { } id && art.IsSoftware(id)))
            {
                // Steam's software (Wallpaper Engine, 3DMark) stays in the library's Software tab, where its page can
                // sync it; first run is about games. Known once Steam's store has been asked about it.
                continue;
            }

            var best = entry.Proposals.OrderByDescending(p => p.Category == SaveCategory.Save).ThenByDescending(p => p.Files).FirstOrDefault();
            var registry = entry.RegistryProposals.FirstOrDefault();
            var game = new SetupGame(
                entry.Id,
                entry.DisplayTitle,
                art.FindOwn(entry.Id, ArtKind.Cover) ?? (steamId is { } app ? art.Find(app, ArtKind.Cover) : null),
                best is not null ? Friendly(best.Root) : registry is not null ? $"Registry: {registry.Key.Replace('/', '\\')}" : null,
                best is not null ? Layer(best.FoundBy) : registry is not null ? Layer(registry.FoundBy) : null,
                entry.Proposals.Sum(p => p.Bytes),
                entry.HasAntiCheat ? entry.AntiCheat ?? "an anti-cheat" : null);
            if (!groups.TryGetValue(group, out var list))
            {
                groups[group] = list = [];
            }

            list.Add(game);
        }

        return Enum.GetValues<SetupGroupKind>()
            .Where(groups.ContainsKey)
            .Select(kind => new SetupGroup(kind, groups[kind].OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase).ToList()))
            .ToList();
    }

    /// <summary>Adds a folder of games to every scan (LIB-05), as Scan this PC's Add a game folder does; returns why not, or null.</summary>
    public static string? AddGameFolder(string dataDir, string folder)
    {
        var full = Path.GetFullPath(folder);
        full = Path.GetPathRoot(full) is { } root && string.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(full), StringComparison.OrdinalIgnoreCase)
            ? root
            : Path.TrimEndingDirectorySeparator(full);
        if (!Directory.Exists(full))
        {
            return $"{full} isn't there on this PC.";
        }

        using var engine = Engine.OpenForSetup(dataDir);
        if (LocalGames.FolderRefusal(full, engine) is { } refused)
        {
            return refused;
        }

        Cli.AddGameFolder(engine.State, full);
        return null;
    }

    /// <summary>
    /// GameSync's Google client (CLOUD-12): the one in the data folder (a developer's, from <c>gamesync signin --client</c>),
    /// or the one a release build carries beside the app; null when there's neither, and signing in to Drive can't work.
    /// </summary>
    public static string? GoogleClientFile(string dataDir) =>
        new[] { Path.Combine(dataDir, "google-client.json"), Path.Combine(AppContext.BaseDirectory, "google-client.json") }.FirstOrDefault(File.Exists);

    /// <summary>
    /// Sign in with Google (CLOUD-02): the browser opens on Google's page, and GameSync waits for its answer on
    /// 127.0.0.1 for up to 5 minutes. The sign-in is kept, encrypted for this Windows user, in the data folder. Returns
    /// the account's address when Google gives it.
    /// </summary>
    public static async Task<string?> SignInAsync(string dataDir, Action<Uri> openBrowser, CancellationToken ct)
    {
        var client = GoogleClientFile(dataDir) ?? throw new UsageException("This copy of GameSync has no Google client, so it can't sign in to Google Drive. Use a folder instead.");
        var bytes = await File.ReadAllBytesAsync(client, ct);
        var google = GoogleClient.Parse(bytes);
        Directory.CreateDirectory(dataDir);
        var mine = Path.Combine(dataDir, "google-client.json");
        if (!string.Equals(Path.GetFullPath(client), Path.GetFullPath(mine), StringComparison.OrdinalIgnoreCase))
        {
            await File.WriteAllBytesAsync(mine, bytes, ct);
        }

        var auth = new GoogleAuth(google, Path.Combine(dataDir, "google-token.bin"), new DpapiProtector());
        await auth.SignInAsync(openBrowser, TimeSpan.FromMinutes(5), ct);
        using var drive = new GoogleDriveClient(auth);
        return (await drive.AboutAsync(ct)).Email;
    }

    /// <summary>Whether this data folder is signed in to Google Drive already.</summary>
    public static bool SignedIn(string dataDir) =>
        GoogleClientFile(dataDir) is { } client &&
        new GoogleAuth(GoogleClient.Parse(File.ReadAllBytes(client)), Path.Combine(dataDir, "google-token.bin"), new DpapiProtector()).IsSignedIn;

    /// <summary>
    /// The cloud in a folder the person picked (a NAS, a USB drive, another disk): GameSync keeps its files in a
    /// GameSync folder inside it, unless the one picked is called that already. Refuses what can't be the cloud.
    /// </summary>
    public static string CloudFolder(string dataDir, string picked)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(picked));
        if (!Directory.Exists(full) && !Directory.Exists(Path.GetPathRoot(full)))
        {
            throw new UsageException($"{full} isn't there on this PC.");
        }

        var cloud = string.Equals(Path.GetFileName(full), "GameSync", StringComparison.OrdinalIgnoreCase) ? full : Path.Combine(full, "GameSync");
        var data = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDir));
        if (Inside(cloud, data) || Inside(data, cloud) || string.Equals(cloud, data, StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException("That's GameSync's own data folder on this PC; the cloud has to be somewhere else, such as another disk or a NAS.");
        }

        return cloud;
    }

    /// <summary>
    /// Start using GameSync: the cloud goes into games.json, the games ticked sync from now on (each checked as the
    /// command line checks them, FIND-06, R8), and GameSync starts at sign-in and backs up daily as chosen. The agent
    /// takes it from there, and the first backup runs in the background.
    /// </summary>
    public static async Task<SetupResult> FinishAsync(string dataDir, SetupChoice choice, ISetupSchedule schedule, CancellationToken ct)
    {
        var notes = new List<string>();
        if (!choice.Remote.Equals("drive", StringComparison.OrdinalIgnoreCase) && !choice.Remote.Equals(AppConfig.NoCloud, StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(choice.Remote);
        }

        var existing = AppConfig.Load(dataDir);
        (existing is null ? new AppConfig { Remote = choice.Remote } : existing with { Remote = choice.Remote }).Save(dataDir);

        int syncing = 0, backedUp = 0;
        using (var engineLock = await EngineLock.AcquireAsync(dataDir, null, ct))
        using (var engine = Engine.Open(dataDir))
        {
            var entries = engine.Library.All().Where(e => e.MergedInto is null).ToDictionary(e => e.Id);
            var defaults = Core.Games.GameDefaults.Load(engine.State);
            var confirmed = new List<LibraryEntry>();
            foreach (var id in choice.Games.Distinct())
            {
                if (!entries.TryGetValue(id, out var entry) || entry.Proposals.Count + entry.RegistryProposals.Count == 0)
                {
                    continue;
                }

                var next = Library.Confirm(entry, defaults: defaults);
                var portable = next.Confirmed!;
                if (Cli.Problems(portable, engine.Here.Resolver.Resolve(portable), engine.Here) is [var problem, ..])
                {
                    notes.Add($"{entry.DisplayTitle} doesn't sync yet: {problem}");
                    continue;
                }

                confirmed.Add(next);
                if (portable.Mode == Core.Games.GameMode.BackupOnly)
                {
                    backedUp++;
                }
                else
                {
                    syncing++;
                }
            }

            engine.Library.SaveAll(confirmed);
        }

        notes.Add(schedule.StartAtSignIn(choice.StartAtSignIn) ?? "");
        notes.Add(schedule.Daily(choice.DailyAt) ?? "");
        return new SetupResult(syncing, backedUp, notes.Where(n => n.Length > 0).ToList());
    }

    /// <summary>
    /// Connects the cloud after first run skipped it (design system → ConnectCloudDialog): games.json takes it, under the
    /// engine lock so no sync is halfway through, and what waited on this PC goes up at the next sync.
    /// </summary>
    public static async Task ConnectAsync(string dataDir, string remote, CancellationToken ct)
    {
        using var engineLock = await EngineLock.AcquireAsync(dataDir, null, ct);
        var config = AppConfig.Load(dataDir) ?? throw new UsageException("GameSync isn't set up on this PC yet.");
        if (!remote.Equals("drive", StringComparison.OrdinalIgnoreCase) && !remote.Equals(AppConfig.NoCloud, StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(remote);
        }

        (config with { Remote = remote }).Save(dataDir);
    }

    /// <summary>
    /// The person's own Run key and Task Scheduler tasks (BG-01, BG-02), as <c>gamesync schedule</c> sets them. Only for
    /// GameSync's usual data folder: a copy running on another one (a test, a second setup) never changes what starts at
    /// sign-in, which would take the place of the real one.
    /// </summary>
    public sealed class WindowsSchedule(string dataDir) : ISetupSchedule
    {
        private bool Usual => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDir)), Path.TrimEndingDirectorySeparator(Engine.DefaultDataDir),
            StringComparison.OrdinalIgnoreCase);

        public string? StartAtSignIn(bool on) => !Usual
            ? on ? "GameSync didn't change what starts at sign-in: this isn't its usual data folder." : null
            : Cli.SetBackground(dataDir, on, Program);

        public string? Daily(TimeOnly? at)
        {
            if (!Usual)
            {
                return at is null ? null : "GameSync didn't set the daily backup: this isn't its usual data folder.";
            }

            using var state = new StateStore(dataDir);
            return Cli.SetDaily(dataDir, state, at, Program);
        }

        private static string Program() =>
            Environment.ProcessPath is { } path && Path.GetFileName(path).Equals("GameSync.Tray.exe", StringComparison.OrdinalIgnoreCase) ? path : Schedule.BackgroundProgram;
    }

    /// <summary>A save place as a person reads it: <c>&lt;documents&gt;/My Games/Terraria</c> is "Documents\My Games\Terraria".</summary>
    internal static string Friendly(string portable)
    {
        var text = portable.Replace('/', '\\');
        foreach (var (placeholder, name) in Places)
        {
            text = text.Replace(placeholder, name, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    private static readonly (string Placeholder, string Name)[] Places =
    [
        ("<documents>", "Documents"),
        ("<savedGames>", "Saved Games"),
        ("<roaming>", @"AppData\Roaming"),
        ("<localAppData>", @"AppData\Local"),
        ("<localLow>", @"AppData\LocalLow"),
        ("<programData>", "ProgramData"),
        ("<publicDocuments>", "Public Documents"),
        ("<public>", "Public"),
        ("<home>", "Your user folder"),
        ("<installDir>", "Game folder"),
        ("<steamRoot>", "Steam"),
        ("<steamUser>", "your Steam ID"),
        ("<epicUser>", "your Epic ID"),
    ];

    private static string Layer(FoundBy by) => by switch
    {
        FoundBy.SaveList => "save list",
        FoundBy.EngineRule => "engine rule",
        FoundBy.NameSearch => "name search",
        FoundBy.IdFolder => "ID folder",
        FoundBy.Ludusavi => "Ludusavi",
        _ => "added by you",
    };

    private static bool Inside(string path, string folder)
    {
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var child = Path.GetFullPath(path);
        return child.StartsWith(parent.EndsWith(Path.DirectorySeparatorChar) ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"1.1 GB" for the first backup, or "a few KB".</summary>
    public static string Size(long bytes) => bytes <= 0 ? "a few KB" : Cli.FormatSize(bytes);

    /// <summary>The daily time as 24-hour text: "20:00".</summary>
    public static string Time(TimeOnly at) => at.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A daily time as the person typed it: "20:00" or "8:30", 24-hour.</summary>
    public static bool TryParseTime(string text, out TimeOnly at) => Daily.TryParseTime(text.Trim(), out at);
}
