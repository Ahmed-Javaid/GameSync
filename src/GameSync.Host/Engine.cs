using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.Safety;
using GameSync.Core.Scanning;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Core.Sync;
using GameSync.Storage.Drive;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>
/// Everything that works on this PC's games, opened together: the settings, state and library, this PC's folders and
/// the checks every rule passes, the games that sync, and, once asked for, the cloud and the sync service. A command
/// opens one for its run; the agent opens one for each piece of work, so it always sees the latest games.
/// </summary>
internal sealed class Engine : IDisposable
{
    private readonly List<IDisposable> _owned = [];

    private Engine(string dataDir, AppConfig config, StateStore state, LibraryStore library, ThisPc here, List<GameDefinition> games,
        IReadOnlyDictionary<GameId, string> installDirs)
    {
        DataDir = dataDir;
        Config = config;
        State = state;
        Library = library;
        Here = here;
        Games = games;
        InstallDirs = installDirs;
        Device = state.GetOrCreateDevice(Environment.MachineName);
        _owned.Add(library);
        _owned.Add(state);
    }

    public string DataDir { get; }

    public AppConfig Config { get; }

    public StateStore State { get; }

    public LibraryStore Library { get; }

    public DeviceInfo Device { get; }

    public ThisPc Here { get; }

    /// <summary>Every game that syncs, resolved for this PC: games.json's, then the library's confirmed ones.</summary>
    public IReadOnlyList<GameDefinition> Games { get; }

    /// <summary>Where each game is installed on this PC, for &lt;installDir&gt; and for spotting its programs.</summary>
    public IReadOnlyDictionary<GameId, string> InstallDirs { get; }

    public ICloud? Cloud { get; private set; }

    public SyncService? Service { get; private set; }

    public static string DefaultDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameSync");

    /// <summary>Opens settings, state and the library; games.json problems stop it, a library game's problem only leaves that game out.</summary>
    public static Engine Open(string dataDir) => Open(dataDir, AppConfig.Load(dataDir)
        ?? throw new UsageException($"No games.json in {dataDir}. Run 'gamesync init --drive' or 'gamesync init --remote <folder>' first."));

    /// <summary>
    /// For first run, before GameSync is set up (ONB-01): the same view of this PC with no games.json yet, so it can scan
    /// and show what it found. No cloud, and nothing syncs.
    /// </summary>
    public static Engine OpenForSetup(string dataDir) => Open(dataDir, AppConfig.Load(dataDir) ?? new AppConfig { Remote = AppConfig.NoCloud });

    private static Engine Open(string dataDir, AppConfig config)
    {
        var state = new StateStore(dataDir);
        LibraryStore? library = null;
        try
        {
            var folders = Cli.FoldersForThisPc();
            var guard = SensitivePathGuard.ForThisPc(dataDir, folders.GetValueOrDefault("<steamRoot>"));
            library = new LibraryStore(dataDir);
            var entries = library.All();
            var installDirs = Cli.InstallDirs(state, config, entries);
            var resolver = new RootResolver(folders, Cli.Accounts(state), installDirs);
            var games = config.Games.Select(resolver.Resolve).ToList();
            foreach (var problem in config.Games.Concat(games).SelectMany(g => GameValidator.Problems(g, guard)).Distinct())
            {
                throw new UsageException($"games.json: {problem}");
            }

            var here = new ThisPc(dataDir, state, library, folders, resolver, guard, new WindowsRegistry());
            games.AddRange(Cli.LibraryGames(entries, config, here));
            return new Engine(dataDir, config, state, library, here, games, installDirs);
        }
        catch
        {
            library?.Dispose();
            state.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens the cloud (Google Drive needs a sign-in) and the sync service, and finishes whatever a crash cut off first
    /// (BAK-08, BAK-13), unless <paramref name="recover"/> is false, as for a quick job while a game plays.
    /// </summary>
    public async Task<(SyncService Service, IReadOnlyList<GameResult> Recovered)> OpenServiceAsync(SyncOptions options, CancellationToken ct, bool recover = true)
    {
        var amsi = new AmsiScanner();
        _owned.Insert(0, amsi);
        var drive = Config.UsesDrive ? Cli.OpenDrive(DataDir) : null;
        if (drive is not null)
        {
            _owned.Insert(0, drive);
        }

        string? Title(GameId id) => Service?.Streams.FirstOrDefault(s => s.Id == id)?.Definition.Title;
        Cloud = drive is not null ? new DriveCloud(drive, Title) : Config.HasNoCloud ? new NoCloud() : new FolderCloud(Config.Remote);
        Service = new SyncService(
            Games,
            new LocalHistory(Cli.HistoryFolder(State, DataDir)),
            Cloud,
            State,
            new SnapshotScanner(Here.Guard, State),
            amsi,
            Device,
            DataDir,
            options with { AppVersion = Cli.AppVersion, HistoryLimits = Cli.KeepLimits(State), Registry = Here.Registry });
        return (Service, recover ? await Service.RecoverAsync(ct) : []);
    }

    /// <summary>
    /// PLAY-05, PLAY-12: the games to watch for play, with their install folders: every game that syncs, and every other
    /// game installed here that isn't ignored, so Home can say what's playing and its play counts; not Steam's software
    /// (Wallpaper Engine), which can run all day. A game with no install folder here can't be spotted.
    /// </summary>
    /// <param name="isSoftware">Whether Steam lists an app ID as software, as far as it's been asked.</param>
    public IReadOnlyList<(GameId Id, string Title, string InstallDir, bool Syncs)> Watched(Func<long, bool>? isSoftware = null)
    {
        var watched = new List<(GameId Id, string Title, string InstallDir, bool Syncs)>();
        foreach (var game in Games.Where(g => InstallDirs.ContainsKey(g.Id)))
        {
            watched.Add((game.Id, game.Title, InstallDirs[game.Id], true));
        }

        foreach (var entry in Library.All().Where(e => e is { State: LibraryState.Found, MergedInto: null, Installed: true } && InstallDirs.ContainsKey(e.Id)))
        {
            var software = entry.Store == StoreKind.Steam && long.TryParse(entry.StoreId, out var app) && isSoftware?.Invoke(app) == true;
            if (!software && watched.All(w => w.Id != entry.Id))
            {
                watched.Add((entry.Id, entry.DisplayTitle, InstallDirs[entry.Id], false));
            }
        }

        return watched.Where(w => Directory.Exists(w.InstallDir)).ToList();
    }

    public void Dispose()
    {
        foreach (var owned in _owned)
        {
            owned.Dispose();
        }
    }
}
