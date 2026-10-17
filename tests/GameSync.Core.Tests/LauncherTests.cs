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
    public void PLAY_09_a_game_played_while_GameSync_wasnt_watching_was_last_played_when_its_saves_were_written()
    {
        // The owner's Ghost of Tsushima (8 Oct 2026): played from July to 27 Sep, before GameSync watched it, in a copy
        // Steam doesn't run, so its page said "Last played: Never".
        using var world = new TestWorld();
        using var state = new StateStore(Path.Combine(world.Root, "data"));
        DateTime At(int day, int hour) => new DateTime(2026, 9, day, hour, 0, 0, DateTimeKind.Local).ToUniversalTime();
        Proposal Place(SaveCategory category, DateTime? newest) => new(FoundBy.SaveList, "<documents>/Game", "*/**", category, 17, 5_200_000, newest);
        var saves = new[] { Place(SaveCategory.Save, At(20, 2)), Place(SaveCategory.Save, At(11, 3)), Place(SaveCategory.Config, At(20, 4)) };
        var fnfStart = Now.AddHours(-1).ToUniversalTime();
        state.AddSession(GameId.Parse("fnf"), new SessionInfo(fnfStart, fnfStart.AddMinutes(30)));
        var plays = new Dictionary<long, SteamPlay> { [105600] = new(105600, Now.AddDays(-12).ToUniversalTime(), TimeSpan.FromHours(10)) };

        var games = Launcher.Games(
        [
            Entry("ghost-of-tsushima", "Ghost of Tsushima", StoreKind.Loose, null) with { Proposals = saves },
            Entry("terraria", "Terraria", StoreKind.Steam, "105600") with { Proposals = [Place(SaveCategory.Save, At(27, 12))] },
            Entry("fnf", "FNF", StoreKind.Loose, null) with { Proposals = [Place(SaveCategory.Save, At(27, 12))] },
            Entry("server-world", "Server world", StoreKind.Loose, null) with { OwnFolder = @"D:\Server\world", Proposals = [Place(SaveCategory.Save, At(26, 12))] },
            Entry("rounds", "Rounds", StoreKind.Loose, null) with { Proposals = [Place(SaveCategory.Save, null), Place(SaveCategory.Save, new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc))] },
        ], [], state, null, plays, null);

        // Its saves' newest write, never a settings file's or a time out of reason.
        var ghost = games.Single(g => g.Title == "Ghost of Tsushima");
        Assert.Equal((At(20, 2), true), (ghost.LastPlayedUtc, ghost.LastPlayedFromSaves));
        Assert.Equal("20 Sep", Launcher.Meta(ghost, Now));
        Assert.Equal((Now.AddDays(-12).ToUniversalTime(), false), (games.Single(g => g.Title == "Terraria").LastPlayedUtc, games.Single(g => g.Title == "Terraria").LastPlayedFromSaves));
        Assert.Equal((fnfStart.AddMinutes(30), false), (games.Single(g => g.Title == "FNF").LastPlayedUtc, games.Single(g => g.Title == "FNF").LastPlayedFromSaves));
        Assert.Null(games.Single(g => g.Title == "Server world").LastPlayedUtc);
        Assert.Null(games.Single(g => g.Title == "Rounds").LastPlayedUtc);

        // Home and the library place it by that day, and its page says where the day came from and that no time was counted.
        var home = Launcher.Home(games, state, Now);
        Assert.Equal(["FNF", "Ghost of Tsushima", "Terraria"], home.JumpBackIn.Prepend(home.Hero!).Take(3).Select(g => g.Title));
        var stats = UI.ViewModels.GameViewModel.StatsOf(ghost, Now);
        Assert.Equal(["Last played: 20 Sep", "Play time: Not recorded"], stats.Take(2).Select(s => s.ToString()));
        Assert.All(stats.Take(2), s => Assert.NotNull(s.Tip));
        Assert.All(UI.ViewModels.GameViewModel.StatsOf(games.Single(g => g.Title == "FNF"), Now), s => Assert.Null(s.Tip));
        Assert.Equal(["Last played: Never", "Play time: None yet"], UI.ViewModels.GameViewModel.StatsOf(games.Single(g => g.Title == "Rounds"), Now).Take(2).Select(s => s.ToString()));
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
    public void KAN_170_a_copy_the_save_list_doesnt_know_gets_its_art_from_the_Steam_ID_its_folder_declares()
    {
        // A friend's notes, 8 Oct 2026: "Non steam launchers art, achievement etc not being captured."
        using var world = new TestWorld();
        var folder = Path.Combine(world.Root, "Games", "Some Indie");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "SomeIndie.exe"), "MZ");
        File.WriteAllText(Path.Combine(folder, "steam_appid.txt"), "999001");
        var spacewar = Path.Combine(world.Root, "Games", "Played Together");
        Directory.CreateDirectory(spacewar);
        File.WriteAllText(Path.Combine(spacewar, "steam_appid.txt"), "480");
        DiscoveredGame Found(string dir, string title) => new()
        {
            Installed = new InstalledGame { Store = StoreKind.Loose, Title = title, InstallDir = dir },
            Title = title,
            Print = Fingerprinter.Read(dir),
        };

        // The scan keeps what the folder says, but never Valve's test app, which copies say to play together.
        var library = Library.Reconcile([], [Found(folder, "Some Indie"), Found(spacewar, "Played Together")], DateTime.UtcNow);
        Assert.Equal(999001, library.Single(e => e.Title == "Some Indie").DeclaredSteamId);
        Assert.Null(library.Single(e => e.Title == "Played Together").DeclaredSteamId);

        // Its art and store page come from that ID; one that another game has as its own isn't taken, so a copy pointed at
        // the wrong game never wears that game's art.
        var saveList = new SaveList([new SaveListGame { Title = "Risk of Rain 2", SteamIds = [632360] }], "test", DateTime.UtcNow);
        using var state = new StateStore(Path.Combine(world.Root, "data"));
        var games = Launcher.Games(
        [
            .. library,
            Entry("risk-of-rain-2", "Risk of Rain 2", StoreKind.Steam, "632360"),
            Entry("not-ror2", "Not Risk of Rain 2", StoreKind.Loose, null) with { DeclaredSteamId = 632360 },
        ], [], state, saveList, new Dictionary<long, SteamPlay>(), null);
        Assert.Equal(999001, games.Single(g => g.Title == "Some Indie").SteamAppId);
        Assert.Null(games.Single(g => g.Title == "Not Risk of Rain 2").SteamAppId);
        Assert.Null(games.Single(g => g.Title == "Played Together").SteamAppId);
        Assert.Equal(632360, games.Single(g => g.Title == "Risk of Rain 2").SteamAppId);
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

    [Fact]
    public void PLAY_01_before_any_scan_home_says_there_are_no_games_instead_of_an_empty_banner()
    {
        using var world = new TestWorld();
        using var state = new StateStore(Path.Combine(world.Root, "data"));
        var games = Launcher.Games([], [], state, null, new Dictionary<long, SteamPlay>(), null);

        var home = UI.ViewModels.HomeViewModel.From(Launcher.Home(games, state, Now), games, Now);

        Assert.True(home.NoGames);
        Assert.False(home.HasHero);
        Assert.False(home.CanChoose);
    }

    [Fact]
    public void Home_fills_any_window_the_banner_takes_what_the_cards_leave()
    {
        // Jump back in: covers no wider than 200, never fewer than three, at most twelve.
        int Columns(double width, double height) => UI.Controls.ShelfPanel.Columns(new Avalonia.Size(width, height), 12, 200, 24, 3);
        Assert.Equal(3, Columns(459, double.PositiveInfinity));
        Assert.Equal(4, Columns(741, double.PositiveInfinity));
        Assert.Equal(12, Columns(2374, double.PositiveInfinity));
        Assert.Equal(6, Columns(741, 200));

        // The banner: whatever the top bar and the cards leave, at least its least, at most 3:4 of the width.
        double Banner(double width, double height) => UI.Controls.HomeLayout.Banner(width, height, top: 44, cards: 390, spacing: 16, least: 200);
        Assert.Equal(634, Banner(1808, 1100));
        Assert.Equal(514, Banner(1808, 980));
        Assert.Equal(200, Banner(912, 500));
        Assert.Equal(750, Banner(1000, 3000));
    }

    [Fact]
    public void LIB_10_a_game_its_store_syncs_shows_which_store_never_backup_only()
    {
        LauncherGame Game(StoreKind? store, GameStatus? status) => new() { Id = GameId.Parse("cyberpunk-2077"), Title = "Cyberpunk 2077", Store = store, Status = status, Syncs = true, StoreSyncs = true };

        Assert.Equal("Synced by Steam", UI.ViewModels.HomeViewModel.Tile(Game(StoreKind.Steam, GameStatus.BackupOnly), Now).StatusLabel);
        Assert.Equal("Synced by Epic", UI.ViewModels.HomeViewModel.StatusLabel(Game(StoreKind.Epic, GameStatus.BackupOnly)));
        Assert.Equal("Synced by its store", StoreNames.SyncedBy(StoreKind.Loose));
        Assert.Null(UI.ViewModels.HomeViewModel.StatusLabel(Game(StoreKind.Steam, GameStatus.Conflict)));
        Assert.Equal("Synced by its store", UI.Controls.GsStatusBadge.Describe(GameStatus.BackupOnly).Word);
    }

    [Fact]
    public void KAN_48_a_game_whose_saves_are_fine_shows_a_small_mark_with_its_words_and_the_badge_is_for_the_rest()
    {
        LauncherGame Game(StoreKind? store, GameStatus? status) => new() { Id = GameId.Parse("cyberpunk-2077"), Title = "Cyberpunk 2077", Store = store, Status = status, Syncs = status is not null, StoreSyncs = true };
        UI.ViewModels.TileItem Tile(StoreKind? store, GameStatus? status) => UI.ViewModels.HomeViewModel.Tile(Game(store, status), Now);

        Assert.Equal("Synced between your PCs", Tile(StoreKind.Steam, GameStatus.Synced).MarkLabel);
        Assert.Equal("Backed up; Steam syncs it", Tile(StoreKind.Steam, GameStatus.BackupOnly).MarkLabel);
        Assert.Equal("Backed up; its store syncs it", Tile(null, GameStatus.BackupOnly).MarkLabel);
        Assert.Null(Tile(StoreKind.Steam, GameStatus.Conflict).MarkLabel);
        Assert.Null(Tile(StoreKind.Steam, null).MarkLabel);
        Assert.Null(UI.ViewModels.HomeViewModel.Tile(Game(StoreKind.Steam, GameStatus.Synced) with { RunningSinceUtc = Now.ToUniversalTime() }, Now).MarkLabel);

        // A screen reader hears the mark's words on the list's row and the cover (A11Y-01, A11Y-03).
        Assert.Equal("Cyberpunk 2077, Backed up; Steam syncs it", Tile(StoreKind.Steam, GameStatus.BackupOnly).SpokenName);
        Assert.Equal("Cyberpunk 2077, Synced between your PCs", Tile(StoreKind.Steam, GameStatus.Synced).SpokenName);

        // KAN-115: a cloud with a check for Synced, a shield with a check for backed up; never a bare tick.
        Assert.Equal("cloudCheck", UI.Controls.GsGameTile.MarkIconOf(GameStatus.Synced));
        Assert.Equal("shield", UI.Controls.GsGameTile.MarkIconOf(GameStatus.BackupOnly));
        Assert.Equal("cloudCheck", UI.Controls.GsStatusBadge.Describe(GameStatus.Synced).Icon);
        Assert.Equal("shield", UI.Controls.GsStatusBadge.Describe(GameStatus.BackupOnly).Icon);

        var tile = new UI.Controls.GsGameTile { Title = "Cyberpunk 2077", Status = GameStatus.BackupOnly, StatusLabel = "Synced by Steam", MarkLabel = "Backed up; Steam syncs it" };
        Assert.True(tile.ShowsMark);
        Assert.False(tile.ShowsStatus);
        Assert.Equal("Cyberpunk 2077, Backed up; Steam syncs it", Avalonia.Automation.AutomationProperties.GetName(tile));
        tile.Status = GameStatus.Conflict;
        tile.StatusLabel = null;
        tile.MarkLabel = null;
        Assert.False(tile.ShowsMark);
        Assert.True(tile.ShowsStatus);
        Assert.Equal("Cyberpunk 2077, Conflict", Avalonia.Automation.AutomationProperties.GetName(tile));

        var row = new UI.Controls.GsGameRow { MarkLabel = "Synced" };
        Assert.True(row.ShowsMark);
        row.ShowsStatus = true;
        Assert.False(row.ShowsMark);
    }

    [Fact]
    public void LIB_15_search_ignores_case_accents_punctuation_and_spaces_and_takes_first_letters()
    {
        Assert.True(Launcher.Matches("Black Myth: Wukong", "black myth"));
        Assert.True(Launcher.Matches("Black Myth: Wukong", "MYTH WU"));
        Assert.True(Launcher.Matches("Ragnarök", "ragnarok"));
        Assert.True(Launcher.Matches("Slay the Spire 2", "slaythe"));
        Assert.True(Launcher.Matches("Slay the Spire 2", "sts"));
        Assert.True(Launcher.Matches("Slay the Spire 2", "sts2"));
        Assert.True(Launcher.Matches("Counter-Strike 2", "cs2"));
        Assert.True(Launcher.Matches("Counter-Strike 2", "counter strike"));
        Assert.True(Launcher.Matches("Anything", "  "));
        Assert.False(Launcher.Matches("Slay the Spire 2", "stp"));
        Assert.False(Launcher.Matches("Terraria", "tera"));
    }

    [Fact]
    public void LIB_16_the_library_sorts_by_recent_play_name_hours_or_when_it_was_found_and_the_choice_is_kept()
    {
        LauncherGame Game(string title, int daysAgo, int hours, int addedDaysAgo) => new()
        {
            Id = Library.NewId(title, []),
            Title = title,
            LastPlayedUtc = daysAgo < 0 ? null : Now.AddDays(-daysAgo).ToUniversalTime(),
            Playtime = TimeSpan.FromHours(hours),
            AddedUtc = addedDaysAgo < 0 ? null : Now.AddDays(-addedDaysAgo).ToUniversalTime(),
        };
        var games = new[]
        {
            Game("Valheim", 6, 215, 30),
            Game("apex Legends", 20, 1090, 10),
            Game("Risk of Rain 2", 3, 218, 30),
            Game("Minecraft server world", -1, 0, -1),
            Game("Balatro", -1, 20, 2),
        };

        Assert.Equal(["Risk of Rain 2", "Valheim", "apex Legends", "Balatro", "Minecraft server world"], Launcher.Sort(games, LibrarySort.RecentlyPlayed).Select(g => g.Title));
        Assert.Equal(["apex Legends", "Balatro", "Minecraft server world", "Risk of Rain 2", "Valheim"], Launcher.Sort(games, LibrarySort.Name).Select(g => g.Title));
        Assert.Equal(["apex Legends", "Risk of Rain 2", "Valheim", "Balatro", "Minecraft server world"], Launcher.Sort(games, LibrarySort.HoursPlayed).Select(g => g.Title));
        Assert.Equal(["Balatro", "apex Legends", "Risk of Rain 2", "Valheim", "Minecraft server world"], Launcher.Sort(games, LibrarySort.RecentlyAdded).Select(g => g.Title));

        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        Assert.Equal(LibrarySort.RecentlyPlayed, LauncherData.ReadSort(data));
        LauncherData.SetSort(data, LibrarySort.Name);
        Assert.Equal(LibrarySort.Name, LauncherData.ReadSort(data));
    }

    [Fact]
    public void LIB_17_a_favourite_is_kept_on_this_PC_and_the_library_knows_when_each_game_was_found()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var found = Now.AddDays(-4).ToUniversalTime();
        LauncherData.SetFavourite(data, GameId.Parse("terraria"), true);
        LauncherData.SetFavourite(data, GameId.Parse("valheim"), true);
        LauncherData.SetFavourite(data, GameId.Parse("valheim"), false);

        using var state = new StateStore(data);
        var games = Launcher.Games([Entry("terraria", "Terraria", StoreKind.Steam, "105600") with { FirstSeenUtc = found }, Entry("valheim", "Valheim", StoreKind.Steam, "892970")],
            [(GameId.Parse("minecraft-server-world"), "Minecraft server world")], state, null, new Dictionary<long, SteamPlay>(), null);

        Assert.True(games.Single(g => g.Title == "Terraria").IsFavourite);
        Assert.False(games.Single(g => g.Title == "Valheim").IsFavourite);
        Assert.Equal(found, games.Single(g => g.Title == "Terraria").AddedUtc);
        Assert.Null(games.Single(g => g.Title == "Valheim").AddedUtc);
        Assert.Null(games.Single(g => g.Title == "Minecraft server world").AddedUtc);
    }

    [Fact]
    public void KAN_66_each_day_of_Activity_says_what_was_played_and_for_how_long_and_a_click_lists_the_games()
    {
        using var world = new TestWorld();
        using var state = new StateStore(Path.Combine(world.Root, "data"));
        var sekiro = GameId.Parse("sekiro");
        var terraria = GameId.Parse("terraria");
        DateTime At(int day, int hour) => new DateTime(2026, 9, day, hour, 0, 0, DateTimeKind.Local).ToUniversalTime();
        state.AddSession(sekiro, new SessionInfo(At(27, 20), At(27, 20).AddMinutes(100)));
        state.AddSession(sekiro, new SessionInfo(At(27, 23), At(27, 23).AddMinutes(60)));
        state.AddSession(terraria, new SessionInfo(At(27, 18), At(27, 18).AddMinutes(30)));
        state.AddSession(terraria, new SessionInfo(At(3, 18), At(3, 18).AddHours(5)));
        var august = new DateTime(2026, 8, 30, 21, 0, 0, DateTimeKind.Local).ToUniversalTime();
        state.AddSession(terraria, new SessionInfo(august, august.AddMinutes(45)));
        GameId? opened = null;
        var actions = new UI.ViewModels.LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { }) { OpenGame = game => opened = game };

        var games = Launcher.Games([Entry("sekiro", "Sekiro: Shadows Die Twice", StoreKind.Loose, null), Entry("terraria", "Terraria", StoreKind.Steam, "105600")],
            [], state, null, new Dictionary<long, SteamPlay>(), null);
        var home = Launcher.Home(games, state, Now);

        // The day's games, longest first, each with its time that day; a session counts on the day it started.
        Assert.Equal(30, home.MonthPlay.Count);
        Assert.Equal([("Sekiro: Shadows Die Twice", 160), ("Terraria", 30)], home.MonthPlay[26].Select(p => (p.Title, (int)p.Played.TotalMinutes)));
        Assert.Equal([("Terraria", 300)], home.MonthPlay[2].Select(p => (p.Title, (int)p.Played.TotalMinutes)));
        Assert.Empty(home.MonthPlay[27]);
        Assert.Equal(["August", "September"], home.Months.Select(m => m.Name));

        var page = UI.ViewModels.HomeViewModel.From(home, games, Now, actions);
        Assert.Equal("Activity in September", page.ActivityTitle);
        Assert.False(page.LaterMonthCommand.CanExecute(null));
        Assert.Equal("Sunday 27 September · 3 h 10 min\nSekiro: Shadows Die Twice · 2 h 40 min\nTerraria · 30 min", page.DayTips[26]);
        Assert.Equal("Monday 28 September · nothing played", page.DayTips[27]);
        Assert.Equal("Tuesday 29 September", page.DayTips[28]);

        page.ChooseDayCommand.Execute(26);
        var day = page.ChosenDay!;
        Assert.Equal(("Sunday 27 September", "3 h 10 min played"), (day.Label, day.Total));
        Assert.Equal(["Sekiro: Shadows Die Twice, 2 h 40 min played", "Terraria, 30 min played"], day.Games.Select(g => g.Spoken));
        day.Games[1].Open.Execute(null);
        Assert.Equal(terraria, opened);

        // The month before, with the arrow: its own days, as far back as the first month with play.
        page.EarlierMonthCommand.Execute(null);
        Assert.Equal(("Activity in August", 31, 5), (page.ActivityTitle, page.Days.Count, page.StartWeekday));
        Assert.Equal("Sunday 30 August · 45 min\nTerraria · 45 min", page.DayTips[29]);
        Assert.False(page.EarlierMonthCommand.CanExecute(null));
    }

    private static LibraryEntry Entry(string id, string title, StoreKind store, string? storeId) => new()
    {
        Id = GameId.Parse(id),
        Title = title,
        Store = store,
        StoreId = storeId,
    };
}
