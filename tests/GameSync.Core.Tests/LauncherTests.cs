using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;

namespace GameSync.Core.Tests;

/// <summary>PLAY-01, PLAY-09, LIB-11: what the launcher shows, from the library, this PC's state and Steam's own play record.</summary>
public class LauncherTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 21, 30, 0, DateTimeKind.Local);

    [Fact]
    public void PLAY_01_home_shows_games_the_person_hasnt_hidden_newest_played_first()
    {
        using var world = new TestWorld();
        using var state = new StateStore(Path.Combine(world.Root, "data"));
        var library = new[]
        {
            Entry("risk-of-rain-2", "Risk of Rain 2", StoreKind.Steam, "632360"),
            Entry("valheim", "Valheim", StoreKind.Steam, "892970"),
            Entry("counter-strike-2", "Counter-Strike 2", StoreKind.Steam, "730"),
            Entry("finished-game", "A finished game", StoreKind.Steam, "105600"),
            Entry("tools", "Game Save Managers", StoreKind.Loose, null),
        };
        var plays = new Dictionary<long, SteamPlay>
        {
            [632360] = new(632360, Now.AddDays(-3).ToUniversalTime(), TimeSpan.FromHours(218)),
            [892970] = new(892970, Now.AddDays(-6).ToUniversalTime(), TimeSpan.FromHours(215)),
            [730] = new(730, Now.AddDays(-7).ToUniversalTime(), TimeSpan.FromHours(2067)),
            [105600] = new(105600, Now.AddHours(-2).ToUniversalTime(), TimeSpan.FromHours(155)),
        };
        state.SetSetting(Launcher.HiddenKey(GameId.Parse("finished-game")), "1");
        state.SetSetting(Launcher.HiddenKey(GameId.Parse("tools")), "1");

        var games = Launcher.Games(library, [], state, saveList: null, plays, art: null);
        var home = Launcher.Home(games, state, Now);

        Assert.Equal("Risk of Rain 2", home.Hero!.Title);
        Assert.Equal(["Valheim", "Counter-Strike 2"], home.JumpBackIn.Select(g => g.Title));
        Assert.True(games.Single(g => g.Title == "A finished game").IsHidden);
        Assert.Equal(TimeSpan.FromHours(2067), games.Single(g => g.Title == "Counter-Strike 2").Playtime);
    }

    [Fact]
    public void PLAY_09_play_is_Steams_total_or_GameSyncs_sessions_whichever_is_more_and_the_latest_counts()
    {
        using var world = new TestWorld();
        using var state = new StateStore(Path.Combine(world.Root, "data"));
        var terraria = GameId.Parse("terraria");
        var start = Now.AddHours(-1).ToUniversalTime();
        state.AddSession(terraria, new SessionInfo(start, start.AddMinutes(50)));
        state.AddSession(GameId.Parse("fnf"), new SessionInfo(start, start.AddMinutes(30)));
        var plays = new Dictionary<long, SteamPlay> { [105600] = new(105600, Now.AddDays(-2).ToUniversalTime(), TimeSpan.FromHours(10)) };

        var games = Launcher.Games([Entry("terraria", "Terraria", StoreKind.Steam, "105600"), Entry("fnf", "FNF", StoreKind.Loose, null)], [],
            state, null, plays, null);

        var steamGame = games.Single(g => g.Title == "Terraria");
        Assert.Equal(TimeSpan.FromHours(10), steamGame.Playtime);
        Assert.Equal(start.AddMinutes(50), steamGame.LastPlayedUtc);
        Assert.Equal(TimeSpan.FromMinutes(30), games.Single(g => g.Title == "FNF").Playtime);
        Assert.Equal("10 h · Today 21:20", Launcher.Meta(steamGame, Now));
    }

    [Fact]
    public void ART_01_an_Epic_or_loose_copy_of_a_Steam_game_gets_its_Steam_ID_from_the_save_list()
    {
        var saveList = new SaveList([new SaveListGame { Title = "Grand Theft Auto V Enhanced", SteamIds = [3240220] }], "test", DateTime.UtcNow);

        Assert.Equal(3240220, Launcher.SteamIdOf(Entry("gta", "GTA V Enhanced", StoreKind.Epic, "9d2d0eb64d5c44529cece33fe2a46482") with
        {
            SaveListTitle = "Grand Theft Auto V Enhanced",
        }, saveList));
        Assert.Equal(730, Launcher.SteamIdOf(Entry("cs2", "Counter-Strike 2", StoreKind.Steam, "730"), saveList));
        Assert.Null(Launcher.SteamIdOf(Entry("og", "OG", StoreKind.Loose, null), saveList));
    }

    [Fact]
    public void PLAY_01_with_nothing_syncing_yet_home_still_has_a_hero_and_counts_nothing_as_synced()
    {
        using var world = new TestWorld();
        using var state = new StateStore(Path.Combine(world.Root, "data"));

        var games = Launcher.Games([Entry("og", "OG", StoreKind.Loose, null)], [], state, null, new Dictionary<long, SteamPlay>(), null);
        var home = Launcher.Home(games, state, Now);

        Assert.Equal("OG", home.Hero!.Title);
        Assert.Equal((0, 0), (home.Synced, home.Syncing));
        Assert.All(home.MonthDays, d => Assert.Equal(0, d));
        Assert.Equal(1, home.MonthStartWeekday);
    }

    private static LibraryEntry Entry(string id, string title, StoreKind store, string? storeId) => new()
    {
        Id = GameId.Parse(id),
        Title = title,
        Store = store,
        StoreId = storeId,
    };
}
