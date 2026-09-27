using System.Text;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.Safety;

namespace GameSync.Core.Tests;

/// <summary>Finding saves: the save list, engine rules and name search (FIND-01 to FIND-03, FIND-06), on a fake PC.</summary>
public class DiscoveryTests
{
    private const string Manifest = """
        Terraria:
          cloud:
            steam: true
          files:
            "<root>/userdata/<storeUserId>/105600/remote/players/*.plr":
              tags: [save]
              when:
                - store: steam
            "<winDocuments>/My Games/Terraria":
              tags: [save]
              when:
                - os: windows
            "<winDocuments>/My Games/Terraria/config.json":
              tags: [config]
          installDir:
            Terraria: {}
          steam:
            id: 105600
        "Sekiro: Shadows Die Twice":
          files:
            "<winAppData>/Sekiro/<storeUserId>/S0000.sl2":
              tags: [save]
          installDir:
            Sekiro: {}
          steam:
            id: 814380
        Online Shooter:
          files:
            "<winLocalAppData>/OnlineShooter/Settings.ini":
              tags: [config]
          installDir:
            OnlineShooter: {}
        Risk of Rain 2:
          files:
            "<root>/userdata/<storeUserId>/632360/remote/UserProfiles":
              tags: [save]
              when:
                - store: steam
          steam:
            id: 632360
        Core Keeper:
          files:
            "<home>/AppData/LocalLow/Pugstorm/Core Keeper/Steam/<storeUserId>":
              tags: [save]
              when:
                - os: windows
            "<home>/AppData/LocalLow/Pugstorm/Core Keeper/Steam/<storeUserId>/prefs.json":
              tags: [config]
              when:
                - os: windows
          installDir:
            Core Keeper: {}
        "Stick Fight: The Game":
          registry:
            "HKEY_CURRENT_USER/Software/Landfall West/Stick Fight: The Game":
              tags: [save]
            "HKEY_LOCAL_MACHINE/Software/Landfall West/Stick Fight":
              tags: [save]
          installDir:
            StickFightTheGame: {}
        """;

    [Fact]
    public void FIND_01_a_Steam_game_is_found_by_its_ID_and_every_save_list_path_that_exists_here()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        Write(pc.Folders["<documents>"], "My Games/Terraria/Worlds/home.wld", "a world");
        Write(pc.Folders["<documents>"], "My Games/Terraria/config.json", "{}");
        Write(pc.Folders["<steamRoot>"], "userdata/39734273/105600/remote/players/hero.plr", "a hero");
        var terraria = Installed(world, StoreKind.Steam, "Terraria", "Terraria", "105600");

        var found = pc.Discoverer.Discover(terraria);

        Assert.Equal("Terraria", found.Title);
        Assert.True(found.StoreCloud);
        Assert.All(found.Proposals, p => Assert.Equal(FoundBy.SaveList, p.FoundBy));
        Assert.Contains(found.Proposals, p => (p.Root, p.Include, p.Category) == ("<documents>/My Games/Terraria", "**", SaveCategory.Save));
        Assert.Contains(found.Proposals, p => (p.Root, p.Include, p.Category) == ("<documents>/My Games/Terraria", "config.json", SaveCategory.Config));
        Assert.Contains(found.Proposals, p => (p.Root, p.Include, p.Files) == ("<steamRoot>/userdata", "*/105600/remote/players/*.plr", 1));
    }

    [Fact]
    public void FIND_01_a_loose_copy_is_matched_by_its_folder_and_skips_the_Steam_only_paths()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        Write(pc.Folders["<documents>"], "My Games/Terraria/Worlds/home.wld", "a world");
        Write(pc.Folders["<steamRoot>"], "userdata/39734273/105600/remote/players/hero.plr", "a hero");
        var terraria = Installed(world, StoreKind.Loose, "Terraria", "Terraria");

        var found = pc.Discoverer.Discover(terraria);

        Assert.Equal("Terraria", found.Listed?.Title);
        Assert.False(found.StoreCloud);
        Assert.DoesNotContain(found.Proposals, p => p.Root.StartsWith("<steamRoot>", StringComparison.Ordinal));
    }

    [Fact]
    public void FIND_01_the_account_folder_in_a_path_matches_whatever_account_this_PC_has()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        Write(pc.Folders["<roaming>"], "Sekiro/76561198000000001/S0000.sl2", "boss 1");

        var found = pc.Discoverer.Discover(Installed(world, StoreKind.Steam, "Sekiro™: Shadows Die Twice", "Sekiro", "814380"));

        var proposal = Assert.Single(found.Proposals);
        Assert.Equal(("<roaming>/Sekiro", "*/S0000.sl2"), (proposal.Root, proposal.Include));
    }

    [Fact]
    public void FIND_01_a_path_naming_a_folder_behind_an_account_folder_takes_everything_in_it()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        Write(pc.Folders["<steamRoot>"], "userdata/39734273/632360/remote/UserProfiles/profile1.xml", "a profile");
        Write(pc.Folders["<steamRoot>"], "userdata/39734273/632360/remote/UserProfiles/profile2.xml", "another");

        var found = pc.Discoverer.Discover(Installed(world, StoreKind.Steam, "Risk of Rain 2", "Risk of Rain 2", "632360"));

        var proposal = Assert.Single(found.Proposals);
        Assert.Equal(("<steamRoot>/userdata", "*/632360/remote/UserProfiles/**", 2), (proposal.Root, proposal.Include, proposal.Files));
    }

    [Fact]
    public void FIND_01_an_account_folder_is_a_save_and_a_settings_file_inside_it_stays_settings()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        Write(pc.Folders["<localLow>"], "Pugstorm/Core Keeper/Steam/76561198000000001/worlds/0.world.gzip", "a world");
        Write(pc.Folders["<localLow>"], "Pugstorm/Core Keeper/Steam/76561198000000001/prefs.json", "{}");

        var found = pc.Discoverer.Discover(Installed(world, StoreKind.Steam, "Core Keeper", "Core Keeper", "1621690"));
        var definition = Discoverer.ToDefinition(GameId.Parse("core-keeper"), "Core Keeper", found.Proposals);

        Assert.False(found.ProbablyOnlineOnly);
        Assert.Equal("<localLow>/Pugstorm/Core Keeper/Steam", Assert.Single(definition.Roots).Value);
        Assert.Equal([("*/prefs.json", SaveCategory.Config), ("*/**", SaveCategory.Save)], definition.Rules.Select(r => (r.Include, r.Category)));
    }

    [Fact]
    public void Saves_of_games_that_arent_installed_are_found_from_the_save_list()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        Write(pc.Folders["<documents>"], "My Games/Terraria/Worlds/home.wld", "a world");
        Write(pc.Folders["<roaming>"], "Sekiro/76561198000000001/S0000.sl2", "boss 1");
        Write(pc.Folders["<localAppData>"], "OnlineShooter/Settings.ini", "fov=100");
        var sekiro = pc.Discoverer.Discover(Installed(world, StoreKind.Steam, "Sekiro™: Shadows Die Twice", "Sekiro", "814380"));

        var leftovers = pc.Discoverer.FindUninstalled([sekiro]);

        Assert.Equal(["Online Shooter", "Terraria"], leftovers.Select(l => l.Listed.Title));
        var terraria = leftovers.Single(l => l.Listed.Title == "Terraria");
        Assert.Equal(("<documents>/My Games/Terraria", "**"), (terraria.Proposals[0].Root, terraria.Proposals[0].Include));
        Assert.True(terraria.StoreCloud);
        Assert.True(leftovers.Single(l => l.Listed.Title == "Online Shooter").ProbablyOnlineOnly);
    }

    [Fact]
    public void Saves_an_installed_game_already_takes_arent_found_again_under_the_save_lists_name()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        Write(pc.Folders["<documents>"], "My Games/Terraria/Worlds/home.wld", "a world");

        // A loose copy the save list didn't match, whose name search found the same folder.
        var copy = pc.Discoverer.Discover(Installed(world, StoreKind.Loose, "My Games", "TerrariaCopy")) with
        {
            Proposals = [new Proposal(FoundBy.NameSearch, "<documents>/My Games/Terraria", "**", SaveCategory.Save, 1, 7, null)],
        };

        Assert.Empty(pc.Discoverer.FindUninstalled([copy]));
    }

    [Fact]
    public void FIND_03_the_name_search_looks_inside_the_install_folder_for_save_folders()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        var bloodborne = Installed(world, StoreKind.Loose, "Bloodborne GOTY", "Bloodborne GOTY");
        Write(bloodborne.InstallDir, "Emulator/user/savedata/1/CUSA00207/SPRJ0005/userdata0000", "a hunter");
        Write(bloodborne.InstallDir, "Emulator/user/logs/log.txt", "noise");

        var found = pc.Discoverer.Discover(bloodborne);

        var proposal = Assert.Single(found.Proposals);
        Assert.Equal((FoundBy.NameSearch, "<installDir>/Emulator/user/savedata"), (proposal.FoundBy, proposal.Root));
    }

    [Fact]
    public void FIND_02_an_engine_rule_finds_saves_the_list_doesnt_know()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        var wukong = Installed(world, StoreKind.Loose, "Black Myth Wukong", "Black Myth Wukong");
        Write(wukong.InstallDir, "b1/Binaries/Win64/b1-Win64-Shipping.exe", new string('x', 9000));
        Write(wukong.InstallDir, "b1/Saved/SaveGames/ArchiveSaveFile.1.sav", "chapter 3");

        var found = pc.Discoverer.Discover(wukong);

        var proposal = Assert.Single(found.Proposals);
        Assert.Equal((FoundBy.EngineRule, "<installDir>/b1/Saved/SaveGames"), (proposal.FoundBy, proposal.Root));
    }

    [Fact]
    public void FIND_03_a_name_search_finds_saves_named_like_the_game()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        var spire = Installed(world, StoreKind.Loose, "Slay the Spire 2", "Slay the Spire 2");
        Write(pc.Folders["<roaming>"], "SlayTheSpire2/steam/profile1/progress.save", "act 2");
        Write(pc.Folders["<localAppData>"], "Temp/SlayTheSpire2/crash.txt", "noise");

        var found = pc.Discoverer.Discover(spire);

        var proposal = Assert.Single(found.Proposals);
        Assert.Equal((FoundBy.NameSearch, "<roaming>/SlayTheSpire2"), (proposal.FoundBy, proposal.Root));
    }

    [Fact]
    public void FOLD_08_a_folder_named_by_ID_finds_a_game_by_its_Steam_app_ID()
    {
        using var world = new TestWorld();
        var emulator = Path.Combine(world.Root, "Emulator Saves");
        Write(emulator, "105600/remote/hero.plr", "a hero");
        Write(emulator, "814380/S0000.sl2", "another game's");
        var pc = FakePc(world, extra: [new ExtraSaveFolder(emulator, ById: true)]);

        // A loose copy: its Steam app ID comes from the save list.
        var found = pc.Discoverer.Discover(Installed(world, StoreKind.Loose, "Terraria", "Terraria"));

        var proposal = Assert.Single(found.Proposals);
        Assert.Equal((FoundBy.IdFolder, Path.Combine(emulator, "105600")), (proposal.FoundBy, proposal.Root));
    }

    [Fact]
    public void FOLD_08_an_extra_save_folder_joins_the_name_search()
    {
        using var world = new TestWorld();
        var saves = Path.Combine(world.Root, "My Saves");
        Write(saves, "SlayTheSpire2/progress.save", "act 3");
        var pc = FakePc(world, extra: [new ExtraSaveFolder(saves, ById: false)]);

        var found = pc.Discoverer.Discover(Installed(world, StoreKind.Loose, "Slay the Spire 2", "Slay the Spire 2"));

        var proposal = Assert.Single(found.Proposals);
        Assert.Equal((FoundBy.NameSearch, Path.Combine(saves, "SlayTheSpire2")), (proposal.FoundBy, proposal.Root));
    }

    [Fact]
    public void R5_a_folder_no_rule_may_reach_is_never_proposed_even_when_its_name_matches()
    {
        using var world = new TestWorld();
        var roaming = Path.Combine(world.Root, "PC", "home", "AppData", "Roaming");
        var pc = FakePc(world, guard: new SensitivePathGuard([(Path.Combine(roaming, "rclone"), "it holds passwords or keys")]));
        Write(pc.Folders["<roaming>"], "rclone/rclone.conf", "token = secret");
        var tools = Installed(world, StoreKind.Loose, "Save Managers", "Save Managers");
        Write(tools.InstallDir, "rclone/rclone.exe", new string('x', 90000));

        Assert.Empty(pc.Discoverer.Discover(tools).Proposals);
    }

    [Fact]
    public void FIND_10_the_save_lists_registry_keys_that_exist_are_proposed_and_confirmed_as_registry_rules()
    {
        using var world = new TestWorld();
        var registry = new FakeRegistry();
        var node = new Scanning.RegistryNode();
        node.Values["unlocks"] = new Scanning.RegistryValue("DWord", "12");
        registry.Set("HKEY_CURRENT_USER/Software/Landfall West/Stick Fight: The Game", node, new DateTime(2026, 5, 18, 0, 0, 0, DateTimeKind.Utc));
        registry.Set("HKEY_LOCAL_MACHINE/Software/Landfall West/Stick Fight", node, DateTime.UtcNow);
        var pc = FakePc(world, registry: registry);

        var found = pc.Discoverer.Discover(Installed(world, StoreKind.Loose, "StickFightTheGame", "StickFightTheGame"));
        var confirmed = Library.Confirm(Assert.Single(Library.Reconcile([], [found], DateTime.UtcNow))).Confirmed!;

        var key = Assert.Single(found.Registry);
        Assert.Equal((FoundBy.SaveList, SaveCategory.Save, 1), (key.FoundBy, key.Category, key.Values));
        Assert.False(found.ProbablyOnlineOnly);
        Assert.Equal("HKEY_CURRENT_USER/Software/Landfall West/Stick Fight: The Game", Assert.Single(confirmed.Registry).Key);
        Assert.Empty(confirmed.Rules);
    }

    [Fact]
    public void FIND_10_Unity_PlayerPrefs_are_proposed_as_settings_when_the_list_has_no_key()
    {
        using var world = new TestWorld();
        var registry = new FakeRegistry();
        var node = new Scanning.RegistryNode();
        node.Values["Screenmanager Fullscreen mode_h3630240806"] = new Scanning.RegistryValue("DWord", "1");
        registry.Set("HKEY_CURRENT_USER/Software/Tiny Studio/Tiny Game", node, DateTime.UtcNow);
        var pc = FakePc(world, registry: registry);
        var game = Installed(world, StoreKind.Loose, "Tiny Game", "Tiny Game");
        Write(game.InstallDir, "Tiny Game_Data/app.info", "Tiny Studio\nTiny Game");
        var absent = Installed(world, StoreKind.Loose, "Other Game", "Other Game");
        Write(absent.InstallDir, "Other Game_Data/app.info", "Other Studio\nOther Game");

        var key = Assert.Single(pc.Discoverer.Discover(game).Registry);

        Assert.Equal((FoundBy.EngineRule, SaveCategory.Config), (key.FoundBy, key.Category));
        Assert.Empty(pc.Discoverer.Discover(absent).Registry);
    }

    [Fact]
    public void LIB_10_a_game_with_only_settings_found_is_probably_online_only()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);
        Write(pc.Folders["<localAppData>"], "OnlineShooter/Settings.ini", "fov=100");

        var found = pc.Discoverer.Discover(Installed(world, StoreKind.Loose, "OnlineShooter", "OnlineShooter"));

        Assert.True(found.ProbablyOnlineOnly);
    }

    [Fact]
    public void Nothing_found_leaves_no_proposals()
    {
        using var world = new TestWorld();
        var pc = FakePc(world);

        Assert.Empty(pc.Discoverer.Discover(Installed(world, StoreKind.Loose, "ROUNDS", "ROUNDS")).Proposals);
    }

    [Theory]
    [InlineData("<winDocuments>/My Games/Terraria", "<documents>/My Games/Terraria", "")]
    [InlineData("<winAppData>/Sekiro/<storeUserId>/S0000.sl2", "<roaming>/Sekiro", "*/S0000.sl2")]
    [InlineData("<base>/b1/Saved/SaveGames/<storeUserId>/*.sav", "<installDir>/b1/Saved/SaveGames", "*/*.sav")]
    [InlineData("<root>/userdata/<storeUserId>/105600/remote", "<steamRoot>/userdata", "*/105600/remote")]
    [InlineData("<winLocalAppDataLow>/Studio MDHR/Cuphead", "<localLow>/Studio MDHR/Cuphead", "")]
    [InlineData("<winPublic>/Documents/Some Studio/Game", "<publicDocuments>/Some Studio/Game", "")]
    [InlineData("<winPublic>/Game", "<public>/Game", "")]
    [InlineData("<home>/AppData/LocalLow/IronGate/Valheim", "<localLow>/IronGate/Valheim", "")]
    [InlineData("<home>/AppData/Local/Game/<storeUserId>", "<localAppData>/Game", "*")]
    [InlineData("<home>/Saved Games/Respawn/Apex/local/settings.cfg", "<savedGames>/Respawn/Apex/local/settings.cfg", "")]
    [InlineData("<home>/Documents/Game", "<documents>/Game", "")]
    [InlineData("<home>/.config/Game", "<home>/.config/Game", "")]
    [InlineData("<winLocalAppData>Low/Kwalee/ShiftAtMidnight", "<localLow>/Kwalee/ShiftAtMidnight", "")]
    [InlineData("C:/XboxGames/GameSave/pgs/u_<storeUserId>_16D460", @"C:\XboxGames\GameSave\pgs", "u_*_16D460")]
    public void Save_list_paths_become_portable_roots_and_patterns(string path, string root, string pattern) =>
        Assert.Equal((root, pattern), Discoverer.ToPortable(path, StoreKind.Steam, "105600"));

    [Theory]
    [InlineData("<xdgData>/Game")]
    [InlineData("<winDir>/System32/game.ini")]
    [InlineData("<winLocalAppData>Packages/x")]
    [InlineData("C:/*/saves")]
    [InlineData("/home/user/.local/share/Game")]
    public void Paths_that_dont_apply_on_this_PC_are_left_out(string path) =>
        Assert.Null(Discoverer.ToPortable(path, StoreKind.Steam, "1"));

    [Fact]
    public void Root_keys_come_from_the_folder_so_every_PC_agrees()
    {
        Assert.Equal("documents-my-games-terraria", Discoverer.RootKey("<documents>/My Games/Terraria"));
        Assert.Equal("installdir-b1-saved-savegames", Discoverer.RootKey("<installDir>/b1/Saved/SaveGames"));
        var longKey = Discoverer.RootKey("<documents>/A Very Long Publisher Name/An Even Longer Game Title/Saved Games");
        Assert.True(longKey.Length <= 40);
        Assert.Equal(longKey, Discoverer.RootKey("<documents>/A Very Long Publisher Name/An Even Longer Game Title/Saved Games"));
        Assert.NotEqual(longKey, Discoverer.RootKey("<documents>/A Very Long Publisher Name/An Even Longer Game Title/Saved Data"));
    }

    [Fact]
    public void A_confirmed_definition_puts_single_file_rules_first_so_their_category_wins()
    {
        var definition = Discoverer.ToDefinition(GameId.Parse("terraria"), "Terraria",
        [
            new Proposal(FoundBy.SaveList, "<documents>/My Games/Terraria", "**", SaveCategory.Save, 3, 100, null),
            new Proposal(FoundBy.SaveList, "<documents>/My Games/Terraria", "config.json", SaveCategory.Config, 1, 2, null),
        ]);

        Assert.Equal("<documents>/My Games/Terraria", Assert.Single(definition.Roots).Value);
        Assert.Equal(["config.json", "**"], definition.Rules.Select(r => r.Include));
    }

    internal sealed record Pc(Dictionary<string, string> Folders, Discoverer Discoverer);

    internal static Pc FakePc(TestWorld world, string name = "PC", IReadOnlyList<ExtraSaveFolder>? extra = null, SensitivePathGuard? guard = null,
        FakeRegistry? registry = null)
    {
        var home = Path.Combine(world.Root, name, "home");
        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["<home>"] = home,
            ["<documents>"] = Path.Combine(home, "Documents"),
            ["<roaming>"] = Path.Combine(home, "AppData", "Roaming"),
            ["<localAppData>"] = Path.Combine(home, "AppData", "Local"),
            ["<localLow>"] = Path.Combine(home, "AppData", "LocalLow"),
            ["<savedGames>"] = Path.Combine(home, "Saved Games"),
            ["<public>"] = Path.Combine(world.Root, name, "Public"),
            ["<publicDocuments>"] = Path.Combine(world.Root, name, "Public", "Documents"),
            ["<programData>"] = Path.Combine(world.Root, name, "ProgramData"),
            ["<steamRoot>"] = Path.Combine(world.Root, name, "Steam"),
        };
        foreach (var folder in folders.Values)
        {
            Directory.CreateDirectory(folder);
        }

        var list = SaveListParser.Parse(new StringReader(Manifest), "test", DateTime.UtcNow);
        return new Pc(folders, new Discoverer(list, folders, extra, guard, registry));
    }

    internal static InstalledGame Installed(TestWorld world, StoreKind store, string title, string folder, string? storeId = null, string pc = "PC")
    {
        var dir = Path.Combine(world.Root, pc, "installs", folder);
        Write(dir, $"{folder}.exe", new string('x', 5000));
        return new InstalledGame { Store = store, Title = title, InstallDir = dir, StoreId = storeId };
    }

    internal static void Write(string folder, string relative, string content)
    {
        var path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
    }
}
