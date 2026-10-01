using GameSync.Core.Discovery;
using GameSync.Core.Model;

namespace GameSync.Core.Tests;

/// <summary>
/// LIB-28 (KAN-65): a folder of games is read the way people keep them. A folder with no program of its own holds the
/// game in the folder inside it, or several games; tools aren't games; names come from the folder or the program, not
/// from a code; and a folder that turns out not to be a game leaves the library.
/// </summary>
public class GameFolderTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void LIB_28_a_folder_of_several_games_is_each_game_and_a_game_in_a_folder_of_its_own_is_named_by_what_is_inside()
    {
        using var world = new TestWorld();
        var games = Layout(world);

        var found = LooseScanner.Scan([games], []);

        Assert.Equal(
            [
                ("Black Ops 3", @"COD\t7_full_game"),
                ("Call of Duty Black Ops II", @"COD\Call of Duty Black Ops II"),
                ("Call of Duty Infinite Warfare", @"COD\Call.of.Duty.Infinite Warfare.RexaGames.com"),
                ("Cuphead", "Cuphead"),
                ("Dark Souls III", "Dark Souls 3"),
                ("Funkin", "FNF"),
                ("MAME", "OG"),
            ],
            found.Select(g => (g.Title, Path.GetRelativePath(games, g.InstallDir))).OrderBy(g => g.Title, StringComparer.Ordinal));
        Assert.All(found, g => Assert.Equal(StoreKind.Loose, g.Store));
    }

    [Fact]
    public void LIB_28_names_read_as_names_and_codes_and_ids_don_t()
    {
        Assert.Equal("Call of Duty Infinite Warfare", LooseScanner.Readable("Call.of.Duty.Infinite Warfare.RexaGames.com", fromFolder: true));
        Assert.Equal("Cyberpunk 2077", LooseScanner.Readable("Cyberpunk 2077 [DODI Repack]", fromFolder: true));
        Assert.Equal("MAME", LooseScanner.Readable("MAME", fromFolder: true));
        Assert.Equal("Child of Light", LooseScanner.Readable("Child of Light", fromFolder: true));
        Assert.Null(LooseScanner.Readable("t7_full_game", fromFolder: true));
        Assert.Null(LooseScanner.Readable("s1x_full_game", fromFolder: true));
        Assert.Null(LooseScanner.Readable("funkin-windows-64bit", fromFolder: true));
        Assert.Null(LooseScanner.Readable("OG", fromFolder: true));
        Assert.Null(LooseScanner.Readable("Game", fromFolder: true));

        // A program's own words: its product name or description, never an ID.
        Assert.Equal("Friday Night Funkin'", LooseScanner.Readable("Friday Night Funkin'", fromFolder: false));
        Assert.Equal("Call of Duty: Black Ops II", LooseScanner.Readable("Call of Duty(R): Black Ops II", fromFolder: false));
        Assert.Null(LooseScanner.Readable("me.funkin.fnf", fromFolder: false));
        Assert.Null(LooseScanner.Readable("Plutonium.Updater.App", fromFolder: false));
        Assert.Equal("Call of Duty: Black Ops II", Fingerprinter.Plain("Call of Duty(R): Black Ops II"));
        Assert.Equal("DARK SOULS III", Fingerprinter.Plain("DARK SOULS™ III"));
    }

    [Fact]
    public void LIB_28_a_folder_that_turns_out_not_to_be_a_game_leaves_the_library_unless_something_was_decided_about_it()
    {
        using var world = new TestWorld();
        var games = Layout(world);
        var tools = Path.Combine(games, "Game Save Managers");
        var kept = Directory.CreateDirectory(Path.Combine(games, "Old Stuff")).FullName;
        var unplugged = Path.Combine(world.Root, "Unplugged", "Some Game");
        LibraryEntry Entry(string id, string title, string dir, LibraryState state = LibraryState.Found) =>
            new() { Id = GameId.Parse(id), Title = title, Store = StoreKind.Loose, InstallDir = dir, State = state, FirstSeenUtc = Now };
        var before = new[]
        {
            // What the earlier scan made of the folders: each folder a game by its name.
            Entry("cod", "COD", Path.Combine(games, "COD")),
            Entry("fnf", "FNF", Path.Combine(games, "FNF")),
            Entry("game-save-managers", "Game Save Managers", tools),
            Entry("old-stuff", "Old Stuff", kept, LibraryState.Ignored),
            Entry("some-game", "Some Game", unplugged),
            Entry("renamed", "Renamed", Path.Combine(games, "COD")) with { TitleByHand = "My COD" },
        };

        var found = LooseScanner.Scan([games], []).Select(g => new DiscoveredGame
        {
            Installed = g, Title = g.Title, Print = Fingerprinter.Read(g.InstallDir), Proposals = [], Registry = [],
        }).ToList();
        var after = Library.Reconcile(before, found, Now.AddMinutes(1)).ToDictionary(e => e.Id.Value);

        // COD and the save managers are gone; FNF is the same entry with the game's name; the games in COD are new.
        Assert.DoesNotContain("cod", after.Keys);
        Assert.DoesNotContain("game-save-managers", after.Keys);
        Assert.Equal(("Funkin", true), (after["fnf"].Title, after["fnf"].Installed));
        Assert.Contains("call-of-duty-black-ops-ii", after.Keys);
        Assert.Contains("black-ops-3", after.Keys);

        // Something the person decided stays: an ignored folder, a name of their own. A folder that isn't here is Not installed.
        Assert.Equal(LibraryState.Ignored, after["old-stuff"].State);
        Assert.Equal(("My COD", false), (after["renamed"].DisplayTitle, after["renamed"].Installed));
        Assert.False(after["some-game"].Installed);

        // The library keeps what the scan left, and no more.
        var data = Path.Combine(world.Root, "data");
        using var library = new LibraryStore(data);
        library.SaveAll(before);
        library.ReplaceAll(after.Values.ToList());
        Assert.Equal(after.Keys.Order(StringComparer.Ordinal), library.All().Select(e => e.Id.Value));
    }

    [Fact]
    public void KAN_69_a_name_without_its_series_is_the_one_title_ending_with_it_and_numbers_match_either_way()
    {
        var list = new SaveList(
        [
            new SaveListGame { Title = "Call of Duty: Black Ops II" },
            new SaveListGame { Title = "Call of Duty: Black Ops III", SteamIds = [311210] },
            new SaveListGame { Title = "Call of Duty: Modern Warfare 2" },
            new SaveListGame { Title = "Plants vs. Zombies: Garden Warfare 2" },
            new SaveListGame { Title = "Dark Souls III" },
            new SaveListGame { Title = "Pokémon Ragnarök Edition" },
        ], "test", Now);

        Assert.Equal("Call of Duty: Black Ops III", list.ByNameEnd("Black Ops 3")?.Title);
        Assert.Equal("Dark Souls III", list.ByNameEnd("Dark Souls 3")?.Title);
        Assert.Equal("Pokémon Ragnarök Edition", list.ByNameEnd("Ragnarok Edition")?.Title);
        Assert.Null(list.ByNameEnd("Warfare 2"));
        Assert.Null(list.ByNameEnd("Ops 3"));
        Assert.Null(list.ByNameEnd("Souls"));
        Assert.Equal(["call", "of", "duty", "black", "ops", "3"], SaveList.Words("Call of Duty(R): Black Ops III"));
    }

    [Fact]
    public void KAN_69_a_GOG_install_is_known_by_its_record_and_a_Steamworks_one_by_its_app_id()
    {
        using var world = new TestWorld();
        var games = Path.Combine(world.Root, "Games");
        var guac = Path.Combine(games, "GuacameleeSTCE");
        Exe(Path.Combine(guac, "GuacSTCE.exe"), 5000);
        File.WriteAllText(Path.Combine(guac, "goggame-1207665733.info"),
            """{ "gameId": "1207665733", "name": "Guacamelee! Super Turbo Championship Edition", "playTasks": [] }""");
        var odd = Path.Combine(games, "Odd Folder Name");
        Exe(Path.Combine(odd, "bin", "game.exe"), 5000);
        File.WriteAllText(Path.Combine(odd, "bin", "steam_appid.txt"), "105600\n");

        Assert.Equal((1207665733L, "Guacamelee! Super Turbo Championship Edition"), Fingerprinter.Gog(guac));
        Assert.Equal(105600, Fingerprinter.SteamAppIdIn(odd));
        var found = LooseScanner.Scan([games], []);
        Assert.Equal("Guacamelee! Super Turbo Championship Edition", found.Single(g => g.InstallDir == guac).Title);

        var list = new SaveList(
        [
            new SaveListGame { Title = "Guacamelee! Super Turbo Championship Edition", SteamIds = [275390], GogIds = [1207665733] },
            new SaveListGame { Title = "Terraria", SteamIds = [105600] },
        ], "test", Now);
        var discoverer = new Discoverer(list, new Dictionary<string, string>());
        LooseScanner.Scan([games], []).ToList().ForEach(g =>
        {
            var match = discoverer.Match(g, Fingerprinter.Read(g.InstallDir));
            Assert.Equal(g.InstallDir == guac ? "Guacamelee! Super Turbo Championship Edition" : "Terraria", match?.Title);
        });
    }

    // The owner's E:\Games, with made-up programs: a folder of Call of Duty games with a launcher beside them, Friday
    // Night Funkin' in a folder of its own with mods beside it, MAME in OG, Dark Souls III with its program in Game and a
    // redistributable beside it, save managers, a tool on its own, and Minecraft's instances with no program.
    private static string Layout(TestWorld world)
    {
        var games = Path.Combine(world.Root, "Games");
        Exe(Path.Combine(games, "COD", "Call of Duty Black Ops II", "t6mp.exe"), 5000);
        Exe(Path.Combine(games, "COD", "Call of Duty Black Ops II", "t6sp.exe"), 4000);
        Exe(Path.Combine(games, "COD", "Call.of.Duty.Infinite Warfare.RexaGames.com", "iw7_ship.exe"), 5000);
        Exe(Path.Combine(games, "COD", "Launcher", "alterware-launcher.exe"), 3000);
        Exe(Path.Combine(games, "COD", "t7_full_game", "BlackOps3.exe"), 9000);
        Exe(Path.Combine(games, "COD", "t7_full_game", "boiii.exe"), 2000);
        Exe(Path.Combine(games, "FNF", "funkin-windows-64bit", "Funkin.exe"), 5000);
        Exe(Path.Combine(games, "FNF", "qt-rewired.zip"), 100);
        Exe(Path.Combine(games, "OG", "MAME", "mame.exe"), 9000);
        Exe(Path.Combine(games, "OG", "MAME", "chdman.exe"), 1000);
        Exe(Path.Combine(games, "Dark Souls 3", "Game", "DarkSoulsIII.exe"), 9000);
        Exe(Path.Combine(games, "Dark Souls 3", "_Redist", "QuickSFV.EXE"), 1000);
        Exe(Path.Combine(games, "Dark Souls 3", "unins000.exe"), 1000);
        Exe(Path.Combine(games, "Cuphead", "Cuphead.exe"), 5000);
        Exe(Path.Combine(games, "Game Save Managers", "ludusavi.exe"), 5000);
        Exe(Path.Combine(games, "Game Save Managers", "Playnite", "Playnite.DesktopApp.exe"), 5000);
        Exe(Path.Combine(games, "Ludusavi", "ludusavi.exe"), 5000);
        Exe(Path.Combine(games, "Minecraft", "Fabric 1.21.5", "mods", "sodium.jar"), 5000);
        return games;
    }

    private static void Exe(string path, int size)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
    }
}
