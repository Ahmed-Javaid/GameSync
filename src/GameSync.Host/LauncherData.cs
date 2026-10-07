using GameSync.Core.Art;
using GameSync.Core.Discovery;
using GameSync.Windows;

namespace GameSync.Host;

/// <summary>The launcher's data from a data folder: the games, the home screen, and bringing their art down from Steam.</summary>
public static class LauncherData
{
    public static (IReadOnlyList<LauncherGame> Games, LauncherHome Home) Read(string dataDir, DateTime nowLocal)
    {
        using var engine = Engine.Open(dataDir);
        using var art = new ArtCache(dataDir);
        var cloud = engine.Config.HasNoCloud ? null : engine.Config.UsesDrive ? "your Google Drive" : "your cloud folder";
        var games = Launcher.Games(engine.Library.All(), engine.Config.Games.Select(g => (g.Id, g.Title)), engine.State,
                new SaveListStore(dataDir).Load(), SteamPlay(), art, Running(engine))
            .Select(g => g with { Cloud = cloud })
            .ToList();
        return (games, Launcher.Home(games, engine.State, nowLocal));
    }

    /// <summary>
    /// PLAY-12: the games being played now, from the sessions the agent keeps open, with when each started. Only while
    /// the agent runs: one it left open when it stopped says nothing, and it closes it when it starts again.
    /// </summary>
    private static Dictionary<GameSync.Core.Model.GameId, DateTime> Running(Engine engine)
    {
        var running = new Dictionary<GameSync.Core.Model.GameId, DateTime>();
        if (!EngineLock.AgentRunning(engine.DataDir))
        {
            return running;
        }

        foreach (var (key, value) in engine.State.GetSettings(OpenPrefix))
        {
            if (GameSync.Core.Model.GameId.TryParse(key[OpenPrefix.Length..], out var game) &&
                DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var since))
            {
                running[game] = since.ToUniversalTime();
            }
        }

        return running;
    }

    private const string OpenPrefix = "session.open.";

    /// <summary>
    /// ART-01, ART-07: every game's cover, hero and logo, for games with a Steam app ID: from the Steam client's own cache
    /// on this PC first, and from Steam's store only for what that doesn't have.
    /// </summary>
    /// <param name="askAll">Ask Steam's store about every game now, not only the ones that are due.</param>
    public static async Task<(int Games, ArtRefresh Refresh)> FetchArtAsync(string dataDir, CancellationToken ct, bool askAll = false)
    {
        var ids = SteamIds(dataDir);
        using var art = new ArtCache(dataDir, steamRoot: StoreLocations.SteamRoot());
        CopyLocalArt(dataDir, art);
        return (ids.Count, await art.RefreshAsync(ids, ct, askAll));
    }

    /// <summary>First run's covers, straight after its scan: only what the Steam client has on this PC, with no network.</summary>
    public static int CopyArtFromSteam(string dataDir)
    {
        using var art = new ArtCache(dataDir, steamRoot: StoreLocations.SteamRoot());
        return art.CopyFromSteamClient(SteamIds(dataDir)) + CopyLocalArt(dataDir, art);
    }

    /// <summary>
    /// KAN-59: the art installed games with no Steam app ID carry in their own folders (a PS4 dump's icon and background),
    /// copied into the cache; no network. A game with a Steam app ID takes Steam's. With nothing there, a game in its own
    /// folder takes its program's icon as its cover (KAN-70).
    /// </summary>
    private static int CopyLocalArt(string dataDir, ArtCache art)
    {
        using var engine = Engine.OpenForSetup(dataDir);
        var list = new SaveListStore(dataDir).Load();
        return engine.Library.All()
            .Where(e => e.MergedInto is null && e.State != LibraryState.Ignored && e.Installed && e.InstallDir is not null && Launcher.SteamIdOf(e, list) is null)
            .Sum(e => art.CopyLocal(e.Id, e.InstallDir, e.Store == StoreKind.Loose
                ? GameLaunch.Program(e.InstallDir, engine.State.GetSetting(GameLaunch.ProgramKey(e.Id)))
                : null));
    }

    /// <summary>Every game's Steam app ID; before GameSync is set up too, for first run.</summary>
    private static List<long> SteamIds(string dataDir)
    {
        using var engine = Engine.OpenForSetup(dataDir);
        var saveList = new SaveListStore(dataDir).Load();
        return engine.Library.All().Where(e => e.State != LibraryState.Ignored && e.MergedInto is null)
            .Select(e => Launcher.SteamIdOf(e, saveList))
            .Concat(engine.Config.Games.Select(g => saveList?.ByTitle(g.Title)?.SteamIds.FirstOrDefault()))
            .OfType<long>().Where(id => id > 0).Distinct().ToList();
    }

    /// <summary>Hides a game from Home and the game library on this PC, or shows it again; its saves sync as before.</summary>
    public static void SetHidden(string dataDir, GameSync.Core.Model.GameId game, bool hidden)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        state.SetSetting(Launcher.HiddenKey(game), hidden ? "1" : "");
    }

    /// <summary>LIB-17: makes a game a favourite on this PC, first in the library's list and covers, or an ordinary game again.</summary>
    public static void SetFavourite(string dataDir, GameSync.Core.Model.GameId game, bool favourite)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        state.SetSetting(Launcher.FavouriteKey(game), favourite ? "1" : "");
    }

    /// <summary>LIB-16: the library's order on this PC; Recently played until the person picks another.</summary>
    public static LibrarySort ReadSort(string dataDir)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        return Enum.TryParse<LibrarySort>(state.GetSetting(Launcher.SortKey), out var sort) && Enum.IsDefined(sort) ? sort : LibrarySort.RecentlyPlayed;
    }

    public static void SetSort(string dataDir, LibrarySort sort)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        state.SetSetting(Launcher.SortKey, sort.ToString());
    }

    /// <summary>KAN-82: how a game's named saves are listed on this PC: <c>newest</c> first (the default) or by <c>name</c>.</summary>
    public const string NamedSortKey = "named.sort";

    public static string ReadNamedSort(string dataDir)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        return state.GetSetting(NamedSortKey) == "name" ? "name" : "newest";
    }

    public static void SetNamedSort(string dataDir, string sort)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        state.SetSetting(NamedSortKey, sort == "name" ? "name" : "newest");
    }

    /// <summary>KAN-47: the library's Installed only on this PC; off until the person ticks it.</summary>
    public static bool ReadInstalledOnly(string dataDir)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        return state.GetSetting(Launcher.InstalledOnlyKey) == "true";
    }

    public static void SetInstalledOnly(string dataDir, bool installedOnly)
    {
        using var state = new GameSync.Core.State.StateStore(dataDir);
        state.SetSetting(Launcher.InstalledOnlyKey, installedOnly ? "true" : "false");
    }

    /// <summary>
    /// For Home's top bar: this PC's name, the other PCs that sync with when each was last seen, and where the saves go
    /// ("Google Drive", or the folder standing in for it; null when no cloud is connected yet). Read from this PC's copy
    /// of the records.
    /// </summary>
    public static (string ThisPc, IReadOnlyList<(string Name, DateTime LastSeenUtc)> Others, string? Cloud) Devices(string dataDir)
    {
        using var engine = Engine.Open(dataDir);
        var others = new GameSync.Core.Storage.LocalHistory(Cli.HistoryFolder(engine.State, dataDir)).LoadDevices()
            .Where(d => d.Id != engine.Device.Id)
            .OrderByDescending(d => d.LastSeenUtc)
            .Select(d => (d.Name, d.LastSeenUtc))
            .ToList();
        return (engine.Device.Name, others, engine.Config.HasNoCloud ? null : engine.Config.UsesDrive ? "Google Drive" : engine.Config.Remote);
    }

    private static IReadOnlyDictionary<long, SteamPlay> SteamPlay() =>
        StoreLocations.SteamRoot() is { } root ? SteamActivity.Read(root) : new Dictionary<long, SteamPlay>();
}
