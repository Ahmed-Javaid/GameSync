using GameSync.Core.Art;
using GameSync.Core.Discovery;
using GameSync.Core.Games;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// A game's page about the game (LIB-19) with Steam's store basics (ART-09), its Properties (LIB-20) with its launch
/// (PLAY-11) and its files (FIND-12), the save manager where the save work moved (MGR-01, MGR-07), and Back returning
/// where the person came from (LIB-21).
/// </summary>
public class GamePropertiesTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 21, 0, 0, DateTimeKind.Local);

    [Fact]
    public void ART_09_Steams_store_page_basics_come_with_the_art_request_and_tags_come_by_name()
    {
        var request = SteamArt.AssetsRequest([632360]).ToString();
        Assert.Contains("include_basic_info", request);
        Assert.Contains("include_release", request);

        const string reply = """
            {"response":{"store_items":[{"appid":632360,"success":1,"type":0,
              "basic_info":{"short_description":"Escape a chaotic\u0007 alien   planet.","publishers":[{"name":"Gearbox Publishing"},{"name":"2K"}],"developers":[{"name":"Hopoo Games"}]},
              "tags":[{"tagid":3814,"weight":875},{"tagid":42804,"weight":859},{"tagid":19,"weight":669},{"tagid":3859,"weight":837},{"tagid":1685,"weight":746}],
              "release":{"steam_release_date":1597157640,"original_steam_release_date":1553788800}},
              {"appid":99,"success":42}]}}
            """;
        var info = Assert.Single(SteamArt.ParseInfo(reply));
        Assert.Equal("Escape a chaotic alien planet.", info.Description);
        Assert.Equal(["Hopoo Games"], info.Developers);
        Assert.Equal(["Gearbox Publishing", "2K"], info.Publishers);
        Assert.Equal(new DateTime(2020, 8, 11), info.ReleasedUtc!.Value.Date);
        // The top four tags by weight.
        Assert.Equal([3814, 42804, 3859, 1685], info.Tags);

        var names = SteamArt.ParseTags("""{"response":{"version_hash":"x","tags":[{"tagid":19,"name":"Action"},{"tagid":3814,"name":"Third-Person Shooter"}]}}""");
        Assert.Equal("Third-Person Shooter", names[3814]);
        Assert.Equal(2, names.Count);
    }

    [Fact]
    public void LIB_19_a_games_page_is_about_the_game_with_Play_the_facts_and_a_little_about_its_saves()
    {
        var game = new LauncherGame
        {
            Id = GameId.Parse("risk-of-rain-2"),
            Title = "Risk of Rain 2",
            Store = StoreKind.Steam,
            SteamAppId = 632360,
            Playtime = TimeSpan.FromHours(218),
            LastPlayedUtc = Now.AddDays(-4).ToUniversalTime(),
        };
        var page = new GameViewModel(game, HomeViewModel.Tile(game, Now), Actions());
        page.Show(new GameDetail
        {
            Id = game.Id,
            Syncs = false,
            Places = [new GamePlace("<steamRoot>/userdata", @"E:\Steam\userdata\41755\632360\remote", null, "4 files · 874.7 KB · newest 25 Sep 03:36")],
            About = new GameAbout
            {
                Description = "Escape a chaotic alien planet.",
                Developers = ["Hopoo Games"],
                Publishers = ["Gearbox Publishing", "2K"],
                ReleasedUtc = new DateTime(2020, 8, 11, 12, 0, 0, DateTimeKind.Utc),
                Genres = ["Action", "Roguelike"],
                Engine = "Unity",
                Store = StoreKind.Steam,
                StoreLink = "steam://rungameid/632360",
                StoreLaunchOptions = "-novid",
            },
            InstallDir = @"E:\Steam\steamapps\common\Risk of Rain 2",
        }, Now);

        // The play bar: Play, then Last played, Play time and Saves.
        Assert.Equal("Play", page.PrimaryLabel);
        Assert.True(page.PrimaryIsMain);
        Assert.Equal(["Last played", "Play time", "Saves"], page.Stats.Select(s => s.Label));
        Assert.Equal("218 hours", page.Stats[1].Value);
        Assert.Equal("Not syncing yet", page.Stats[2].Value);

        // About, from its Steam store page.
        Assert.Equal("From its Steam store page", page.AboutSource);
        Assert.Equal(["Developer", "Publisher", "Released", "Genres", "Engine"], page.AboutFacts.Select(f => f.Label));
        Assert.Equal("Gearbox Publishing, 2K", page.AboutFacts[1].Value);
        Assert.Equal("11 Aug 2020", page.AboutFacts[2].Value);
        Assert.Equal(["Action", "Roguelike"], page.AboutFacts[3].Tags);

        // A little about its saves: not syncing yet, so Sync these saves and Choose files; the save folder to open.
        Assert.True(page.SavesNotSyncing);
        Assert.Equal(@"E:\Steam\userdata\41755\632360\remote", page.SaveFolder);
        Assert.Equal("4 files · 874.7 KB · newest 25 Sep 03:36", page.SavesFacts.Single().Value);

        // On this PC: where, and Steam's own launch options.
        Assert.Equal(["Installed in", "Starts", "Options"], page.PcFacts.Select(f => f.Label));
        Assert.Equal(("Through Steam", "-novid"), (page.PcFacts[1].Value, page.PcFacts[2].Value));

        // A game that needs you puts its own action first, with Play beside it; one not installed offers Steam's install.
        page.Update(game with { Syncs = true, Status = GameStatus.HeldForReview }, HomeViewModel.Tile(game, Now));
        Assert.Equal(("Review", true), (page.PrimaryLabel, page.ShowsPlayBeside));
        page.Update(game with { Installed = false }, HomeViewModel.Tile(game, Now));
        Assert.Equal(("Install through Steam", false), (page.PrimaryLabel, page.PrimaryIsMain));
    }

    [Fact]
    public void LIB_21_Back_returns_to_where_the_person_came_from_and_My_games_to_the_covers()
    {
        var shown = new List<string>();
        var game = new LauncherGame { Id = GameId.Parse("terraria"), Title = "Terraria" };
        var library = new LibraryViewModel(new LauncherActions(_ => { }, () => { }, (page, _) => shown.Add(page), (_, _) => { }));
        library.Update([game], LibraryViewModel.Tiles([game], Now, actions: null));

        // Opened from Home: Back goes back Home.
        library.Open(game.Id, returnTo: "home");
        Assert.False(library.ShowsCovers);
        library.BackCommand.Execute(null);
        Assert.Equal(["home"], shown);
        Assert.True(library.ShowsCovers);

        // Opened in the library: Back shows the covers, and the breadcrumb's My games too.
        library.OpenCommand.Execute(game.Id);
        library.BackCommand.Execute(null);
        Assert.Equal(["home"], shown);
        Assert.True(library.ShowsCovers);
        library.Open(game.Id, returnTo: "home");
        library.CoversCommand.Execute(null);
        Assert.True(library.ShowsCovers);
        Assert.Equal(["home"], shown);
    }

    [Fact]
    public void MGR_07_the_save_manager_lists_every_games_saves_needs_you_first_and_opens_one()
    {
        var shown = new List<string>();
        LauncherGame Game(string id, GameStatus? status, int daysAgo) => new()
        {
            Id = GameId.Parse(id),
            Title = id,
            Status = status,
            Syncs = status is not null,
            LastPlayedUtc = Now.AddDays(-daysAgo).ToUniversalTime(),
        };
        var games = new[] { Game("terraria", GameStatus.Synced, 1), Game("cuphead", GameStatus.HeldForReview, 9), Game("core-keeper", null, 0) };
        var saves = new SaveManagerViewModel(new LauncherActions(_ => { }, () => { }, (page, _) => shown.Add(page), (_, _) => { }));
        saves.Update(games);
        saves.Show(
        [
            new GameSaveSummary(games[0].Id, @"C:\Users\You\Documents\My Games\Terraria", 52, 341_835_776, Now.AddDays(-1).ToUniversalTime(), "DESKTOP"),
            new GameSaveSummary(games[1].Id, @"C:\Users\You\AppData\LocalLow\StudioMDHR\Cuphead", 9, 48_128, Now.AddDays(-9).ToUniversalTime(), "LAPTOP"),
            new GameSaveSummary(games[2].Id, @"C:\Users\You\AppData\LocalLow\Pugstorm\Core Keeper", 0, 0, null, null),
        ], Now);

        // The one that needs you first, then the others that sync, then the ones found but not syncing yet.
        Assert.Equal(["cuphead", "terraria", "core-keeper"], saves.Rows.Select(r => r.Title));
        Assert.Equal(("9", "47 KB"), (saves.Rows[0].Versions, saves.Rows[0].Size));
        Assert.Equal("", saves.Rows[2].Versions);
        // MGR-03: the backups' real space on the drive, counted apart from the versions' own sizes.
        Assert.Equal(["Games", "Backups on this PC", "Versions", "Last backup", "Conflicts"], saves.Stats.Select(s => s.Label));
        Assert.Equal(("2", "61", "1"), (saves.Stats[0].Value, saves.Stats[2].Value, saves.Stats[4].Value));

        // A game's saves from its page: Back returns to the page; from the table, Back returns to the table.
        saves.Open(games[1].Id, returnTo: "library");
        Assert.Equal(("cuphead", "My games"), (saves.Game!.Title, saves.Game.Root));
        Assert.True(saves.Game.IsHeld);
        saves.BackCommand.Execute(null);
        Assert.Null(saves.Game);
        Assert.Equal(["library"], shown);
        saves.OpenCommand.Execute(games[0].Id);
        Assert.Equal("Save manager", saves.Game!.Root);
        saves.BackCommand.Execute(null);
        Assert.True(saves.ShowsTable);
        Assert.Equal(["library"], shown);
    }

    [Fact]
    public void FIND_12_file_choices_become_the_games_own_rules_and_nothing_is_deleted()
    {
        var game = new GameDefinition
        {
            Id = GameId.Parse("lantern-keep"),
            Title = "Lantern Keep",
            Roots = new Dictionary<string, string> { ["saves"] = "<localLow>/Lantern/Saves", ["config"] = "<localLow>/Lantern/Config" },
            Rules = [new SaveRule { Root = "saves" }, new SaveRule { Root = "config", Category = SaveCategory.Config }],
            Registry = [new RegistryRule { Key = "HKEY_CURRENT_USER/Software/Lantern" }],
        };

        // A file left out becomes an exclude of its own; a skipped log taken in gets a rule of its own.
        var changed = GameSettings.Changed(game, new GamePropertiesChange
        {
            Files = [new FileChoice("saves", "slot1.sav.bak", false), new FileChoice("saves", "logs/output.log", true)],
        });
        Assert.Equal(["slot1.sav.bak"], changed.Rules[0].Exclude);
        var own = Assert.Single(changed.Rules, r => r.Include == "logs/output.log");
        Assert.False(own.UseDefaultExcludes);
        Assert.True(GameSettings.Takes(own, "logs/output.log"));
        Assert.False(GameSettings.Takes(changed.Rules[0], "slot1.sav.bak"));
        Assert.True(GameSettings.Takes(changed.Rules[0], "slot1.sav"));

        // Taken back in: the exclude goes, and the rule of its own when it's left out again.
        var back = GameSettings.Changed(changed, new GamePropertiesChange
        {
            Files = [new FileChoice("saves", "slot1.sav.bak", true), new FileChoice("saves", "logs/output.log", false)],
        });
        Assert.DoesNotContain("slot1.sav.bak", back.Rules[0].Exclude);
        Assert.DoesNotContain(back.Rules, r => r.Include == "logs/output.log");

        // A whole place left out, and back; a registry key unticked goes from the rules.
        var off = GameSettings.Changed(game, new GamePropertiesChange { Files = [new FileChoice("config", "", false), new FileChoice("registry:HKEY_CURRENT_USER/Software/Lantern", "", false)] });
        Assert.Equal(["**"], off.Rules[1].Exclude);
        Assert.Empty(off.Registry);
        Assert.Empty(GameSettings.Changed(off, new GamePropertiesChange { Files = [new FileChoice("config", "", true)] }).Rules[1].Exclude);

        // Settings files, screenshots, the usual skips, the mode and who wins.
        var settings = GameSettings.Changed(game, new GamePropertiesChange
        {
            SettingsFiles = GameSettings.SyncBetween,
            Screenshots = true,
            SkipDefaults = false,
            Mode = GameMode.BackupOnly,
            Conflict = ConflictPolicy.AlwaysAsk,
        });
        Assert.True(settings.SyncConfig);
        Assert.True(settings.IncludeScreenshots);
        Assert.All(settings.Rules, r => Assert.False(r.UseDefaultExcludes));
        Assert.Equal((GameMode.BackupOnly, ConflictPolicy.AlwaysAsk), (settings.Mode, settings.ConflictPolicy));
        Assert.Equal(["**"], GameSettings.Changed(game, new GamePropertiesChange { SettingsFiles = GameSettings.Off }).Rules[1].Exclude);
    }

    [Fact]
    public void FIND_12_Properties_list_each_places_files_ticked_when_backed_up_with_why_others_are_out()
    {
        using var world = new TestWorld();
        var folder = Directory.CreateDirectory(Path.Combine(world.Root, "Lantern", "Saves")).FullName;
        File.WriteAllText(Path.Combine(folder, "slot1.sav"), "one");
        File.WriteAllText(Path.Combine(folder, "slot1.sav.bak"), "old");
        Directory.CreateDirectory(Path.Combine(folder, "logs"));
        File.WriteAllText(Path.Combine(folder, "logs", "output.log"), "log");
        File.WriteAllText(Path.Combine(folder, "helper.dll"), "MZ");
        var game = new GameDefinition
        {
            Id = GameId.Parse("lantern-keep"),
            Title = "Lantern Keep",
            Roots = new Dictionary<string, string> { ["saves"] = folder, ["gone"] = Path.Combine(world.Root, "Unplugged") },
            Rules = [new SaveRule { Root = "saves", Exclude = ["slot1.sav.bak"] }, new SaveRule { Root = "gone" }],
        };

        var places = GameSettings.Places(game);

        var saves = places[0];
        Assert.Equal(folder, saves.Shown);
        var files = saves.Files.ToDictionary(f => f.Path);
        Assert.True(files["slot1.sav"].Included);
        Assert.Equal((false, "You left it out"), (files["slot1.sav.bak"].Included, files["slot1.sav.bak"].Why));
        Assert.Equal((false, "Skipped: a log, dump or cache"), (files["logs/output.log"].Included, files["logs/output.log"].Why));
        Assert.True(files["helper.dll"].Locked);

        // An unplugged folder is never taken for an empty one.
        Assert.True(places[1].Missing);
        Assert.Empty(places[1].Files);
    }

    [Fact]
    public void LIB_20_the_Properties_dialog_counts_its_changes_sends_only_them_and_asks_before_dropping_them()
    {
        var saved = new List<GamePropertiesChange>();
        var closed = 0;
        var dialog = new PropertiesViewModel(GameId.Parse("lantern-keep"), "Lantern Keep", new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { })
        {
            SaveProperties = (_, change) => saved.Add(change),
            CloseDialog = () => closed++,
        }, "saves");
        dialog.Show(new GameProperties
        {
            Id = GameId.Parse("lantern-keep"),
            Title = "Lantern Keep",
            Syncs = true,
            Places =
            [
                new SavePlaceFiles("saves", @"C:\Saves", SaveCategory.Save, false, false,
                    [new SaveFileItem("slot1.sav", 3, true), new SaveFileItem("slot1.sav.bak", 3, false, "You left it out")]),
            ],
        });

        Assert.Equal("No changes", dialog.ChangesText);
        Assert.Equal("1 of 2 files · 3 B backed up", dialog.CountText);

        // Untick a file: one change, sent as that file alone.
        dialog.Rows.Single(r => r.Path == "slot1.sav").Toggle();
        Assert.Equal(1, dialog.Changes);
        Assert.Equal(false, dialog.Rows.Single(r => r.IsPlace).IsChecked);
        Assert.Equal([new FileChoice("saves", "", false)], dialog.Change().Files);

        // Tick the place back: every file in, and the one left out comes back too.
        dialog.Rows.Single(r => r.IsPlace).Toggle();
        Assert.Equal([new FileChoice("saves", "slot1.sav.bak", true)], dialog.Change().Files);

        // Closing with changes asks first; Close without saving drops them.
        dialog.CancelCommand.Execute(null);
        Assert.True(dialog.ConfirmingDiscard);
        Assert.Equal(0, closed);
        dialog.DiscardCommand.Execute(null);
        Assert.Equal(1, closed);

        // A rename is sent as the new name; nothing else is.
        dialog.Name = "Lantern Keep: Remastered";
        dialog.SaveCommand.Execute(null);
        var change = Assert.Single(saved);
        Assert.Equal("Lantern Keep: Remastered", change.Title);
        Assert.Null(change.Favourite);
        Assert.Null(change.Mode);
    }

    [Fact]
    public void PLAY_11_a_game_in_its_own_folder_starts_from_the_program_picked_with_its_options_a_store_game_through_its_store()
    {
        using var world = new TestWorld();
        using var state = new StateStore(world.Root);
        var game = GameId.Parse("bloodborne");
        var program = Path.Combine(Directory.CreateDirectory(Path.Combine(world.Root, "shadPS4")).FullName, "shadPS4.exe");
        File.WriteAllText(program, "MZ");

        GameLaunch.SetOptions(state, game, "  CUSA00207  ");
        GameLaunch.SetProgram(state, game, program);
        Assert.Equal("CUSA00207", GameLaunch.Options(state, game));
        Assert.Equal(program, GameLaunch.Program(folder: null, GameLaunch.PickedProgram(state, game)));
        Assert.Throws<UsageException>(() => GameLaunch.SetOptions(state, game, "one\ntwo"));
        Assert.Throws<UsageException>(() => GameLaunch.SetProgram(state, game, Path.Combine(world.Root, "missing.exe")));

        // Empty takes them away again.
        GameLaunch.SetOptions(state, game, "");
        Assert.Null(GameLaunch.Options(state, game));

        Assert.Equal("steam://rungameid/632360", GameLaunch.StoreLink(new LibraryEntry { Id = game, Title = "x", Store = StoreKind.Steam, StoreId = "632360" }));
        Assert.Null(GameLaunch.StoreLink(new LibraryEntry { Id = game, Title = "x", Store = StoreKind.Loose }));
    }

    [Fact]
    public void LIB_19_Open_the_save_folder_goes_as_deep_as_the_places_pattern_names()
    {
        using var world = new TestWorld();
        var userdata = Directory.CreateDirectory(Path.Combine(world.Root, "userdata")).FullName;
        Directory.CreateDirectory(Path.Combine(userdata, "11111", "730"));
        var profiles = Directory.CreateDirectory(Path.Combine(userdata, "41755", "632360", "remote", "UserProfiles")).FullName;

        Assert.Equal(profiles, GameDetails.Deepest(userdata, "*/632360/remote/UserProfiles/**"));
        Assert.Equal(userdata, GameDetails.Deepest(userdata, "**"));
        Assert.Equal(Path.Combine(userdata, "41755"), GameDetails.Deepest(userdata, "41755/prefs.json"));
    }

    private static LauncherActions Actions() => new(_ => { }, () => { }, (_, _) => { }, (_, _) => { })
    {
        OpenSaves = _ => { },
        OpenProperties = (_, _) => { },
        OpenLink = _ => { },
        OpenFolder = _ => { },
    };
}
