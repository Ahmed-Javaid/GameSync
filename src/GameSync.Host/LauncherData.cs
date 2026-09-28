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
        var games = Launcher.Games(engine.Library.All(), engine.Config.Games.Select(g => (g.Id, g.Title)), engine.State,
            new SaveListStore(dataDir).Load(), SteamPlay(), art);
        return (games, Launcher.Home(games, engine.State, nowLocal));
    }

    /// <summary>
    /// ART-01, ART-07: every game's cover, hero and logo, for games with a Steam app ID: from the Steam client's own cache
    /// on this PC first, and from Steam's store only for what that doesn't have.
    /// </summary>
    /// <param name="askAll">Ask Steam's store about every game now, not only the ones that are due.</param>
    public static async Task<(int Games, ArtRefresh Refresh)> FetchArtAsync(string dataDir, CancellationToken ct, bool askAll = false)
    {
        List<long> ids;
        using (var engine = Engine.Open(dataDir))
        {
            var saveList = new SaveListStore(dataDir).Load();
            ids = engine.Library.All().Where(e => e.State != LibraryState.Ignored && e.MergedInto is null)
                .Select(e => Launcher.SteamIdOf(e, saveList))
                .Concat(engine.Config.Games.Select(g => saveList?.ByTitle(g.Title)?.SteamIds.FirstOrDefault()))
                .OfType<long>().Where(id => id > 0).Distinct().ToList();
        }

        using var art = new ArtCache(dataDir, steamRoot: StoreLocations.SteamRoot());
        return (ids.Count, await art.RefreshAsync(ids, ct, askAll));
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

    /// <summary>
    /// For Home's top bar: this PC's name, the other PCs that sync with when each was last seen, and where the saves go
    /// ("Google Drive", or the folder standing in for it). Read from this PC's copy of the records.
    /// </summary>
    public static (string ThisPc, IReadOnlyList<(string Name, DateTime LastSeenUtc)> Others, string Cloud) Devices(string dataDir)
    {
        using var engine = Engine.Open(dataDir);
        var others = new GameSync.Core.Storage.LocalHistory(Cli.HistoryFolder(engine.State, dataDir)).LoadDevices()
            .Where(d => d.Id != engine.Device.Id)
            .OrderByDescending(d => d.LastSeenUtc)
            .Select(d => (d.Name, d.LastSeenUtc))
            .ToList();
        return (engine.Device.Name, others, engine.Config.UsesDrive ? "Google Drive" : engine.Config.Remote);
    }

    private static IReadOnlyDictionary<long, SteamPlay> SteamPlay() =>
        StoreLocations.SteamRoot() is { } root ? SteamActivity.Read(root) : new Dictionary<long, SteamPlay>();
}
