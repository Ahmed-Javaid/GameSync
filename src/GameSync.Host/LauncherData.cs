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

    private static IReadOnlyDictionary<long, SteamPlay> SteamPlay() =>
        StoreLocations.SteamRoot() is { } root ? SteamActivity.Read(root) : new Dictionary<long, SteamPlay>();
}
