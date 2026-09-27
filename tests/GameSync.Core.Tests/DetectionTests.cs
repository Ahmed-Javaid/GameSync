using System.Text;
using GameSync.Core.Discovery;

namespace GameSync.Core.Tests;

/// <summary>Store detection (LIB-01 to LIB-05) and what an install folder says about its game (FIND-02, LIB-09), on fake layouts.</summary>
public class DetectionTests
{
    [Fact]
    public void LIB_01_Steam_games_come_from_every_library()
    {
        using var world = new TestWorld();
        var steam = Path.Combine(world.Root, "Steam");
        var second = Path.Combine(world.Root, "SteamLibrary");
        Write(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), $$"""
            "libraryfolders"
            {
                "0" { "path" "{{steam.Replace("\\", "\\\\")}}" "apps" { "105600" "1" } }
                "1" { "path" "{{second.Replace("\\", "\\\\")}}" }
            }
            """);
        SteamApp(steam, "105600", "Terraria", "Terraria", build: "12345");
        SteamApp(steam, "228980", "Steamworks Common Redistributables", "Steamworks Shared");
        SteamApp(second, "814380", "Sekiro™: Shadows Die Twice", "Sekiro");
        SteamApp(second, "999999", "Uninstalled Game", "Gone", createFolder: false);
        Write(Path.Combine(steam, "config", "loginusers.vdf"), """
            "users"
            {
                "76561198000000001" { "AccountName" "someone" "MostRecent" "1" }
            }
            """);

        var install = SteamReader.Read(steam)!;

        Assert.Equal([steam, second], install.Libraries);
        Assert.Equal(["Sekiro™: Shadows Die Twice", "Terraria"], install.Games.Select(g => g.Title).Order());
        var terraria = install.Games.Single(g => g.StoreId == "105600");
        Assert.Equal(Path.Combine(steam, "steamapps", "common", "Terraria"), terraria.InstallDir);
        Assert.Equal("12345", terraria.Build);
        Assert.Equal(("39734273", "76561198000000001", true), (install.Accounts[0].AccountId, install.Accounts[0].SteamId64, install.Accounts[0].MostRecent));
    }

    [Fact]
    public void LIB_02_Epic_games_come_from_the_launchers_manifests()
    {
        using var world = new TestWorld();
        var manifests = Directory.CreateDirectory(Path.Combine(world.Root, "Manifests")).FullName;
        var gta = Directory.CreateDirectory(Path.Combine(world.Root, "Epic Games", "GTAVEnhanced")).FullName;
        Epic(manifests, "gta", """{"DisplayName": "Grand Theft Auto V Enhanced", "AppName": "8769e", "InstallLocation": "{{gta}}", "AppCategories": ["public", "games"], "AppVersionString": "1.0.1013", "bIsIncompleteInstall": false}""", gta);
        Epic(manifests, "half", """{"DisplayName": "Half Installed", "AppName": "half", "InstallLocation": "{{gta}}", "bIsIncompleteInstall": true}""", gta);
        Epic(manifests, "dlc", """{"DisplayName": "Some DLC", "AppName": "dlc1", "MainGameAppName": "8769e", "InstallLocation": "{{gta}}"}""", gta);
        Epic(manifests, "engine", """{"DisplayName": "Unreal Engine", "AppName": "UE_5", "AppCategories": ["engines"], "InstallLocation": "{{gta}}"}""", gta);

        var game = Assert.Single(EpicReader.Read(manifests));

        Assert.Equal(("Grand Theft Auto V Enhanced", "8769e", "1.0.1013", gta), (game.Title, game.StoreId, game.Build, game.InstallDir));
    }

    [Fact]
    public void LIB_03_EA_games_come_from_their_installer_data()
    {
        using var world = new TestWorld();
        var ea = Path.Combine(world.Root, "EA Games");
        Write(Path.Combine(ea, "Battlefield 6", "__Installer", "installerdata.xml"), """
            <DiPManifest version="4.0">
              <gameTitles><gameTitle locale="de_DE">Battlefield 6 (DE)</gameTitle><gameTitle locale="en_US">Battlefield 6</gameTitle></gameTitles>
              <contentIDs><contentID>198235</contentID></contentIDs>
            </DiPManifest>
            """);

        var game = Assert.Single(EaReader.Read([ea]));

        Assert.Equal(("Battlefield 6", "198235"), (game.Title, game.StoreId));
    }

    [Fact]
    public void LIB_05_loose_folders_with_a_game_in_them_are_games_and_the_rest_are_left_alone()
    {
        using var world = new TestWorld();
        var games = Path.Combine(world.Root, "Games");
        Exe(Path.Combine(games, "Cuphead", "Cuphead.exe"), 5000);
        Exe(Path.Combine(games, "Tools", "notes.txt"), 10);
        Exe(Path.Combine(games, "Installers", "setup.exe"), 5000);
        Exe(Path.Combine(games, "Downloads", "SomeTool.exe"), 5000);
        Exe(Path.Combine(games, "Rockstar Launcher", "Launcher.exe"), 5000);
        Exe(Path.Combine(games, "SteamLibrary", "steamapps", "common", "X", "x.exe"), 5000);
        var hidden = Directory.CreateDirectory(Path.Combine(games, "$RECYCLE.BIN")).FullName;
        Exe(Path.Combine(hidden, "old.exe"), 5000);
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Directory);

        var found = LooseScanner.Scan([games], [Path.Combine(games, "SteamLibrary")]);

        Assert.Equal(["Cuphead"], found.Select(g => g.Title));
        Assert.Equal(StoreKind.Loose, found[0].Store);
    }

    [Fact]
    public void Each_install_folder_is_one_game_and_a_stores_record_beats_a_loose_folder()
    {
        using var world = new TestWorld();
        var steam = Path.Combine(world.Root, "Steam");
        SteamApp(steam, "1262540", "Need for Speed™", "Need for Speed");
        var nfs = Path.Combine(steam, "steamapps", "common", "Need for Speed");
        Write(Path.Combine(nfs, "__Installer", "installerdata.xml"), "<DiPManifest><gameTitles><gameTitle locale=\"en_US\">Need for Speed</gameTitle></gameTitles></DiPManifest>");
        var games = Path.Combine(world.Root, "Games");
        var battlefield = Path.Combine(games, "Battlefield 1");
        Exe(Path.Combine(battlefield, "bf1.exe"), 5000);
        Write(Path.Combine(battlefield, "__Installer", "installerdata.xml"), "<DiPManifest><gameTitles><gameTitle locale=\"en_US\">Battlefield 1</gameTitle></gameTitles></DiPManifest>");
        Exe(Path.Combine(games, "Cuphead", "Cuphead.exe"), 5000);

        var detection = Detector.Detect(new StoreSources { SteamRoot = steam, EaFolders = [nfs, battlefield], GameFolders = [games] });

        Assert.Equal(["Battlefield 1 (Ea)", "Cuphead (Loose)", "Need for Speed™ (Steam)"], detection.Games.Select(g => $"{g.Title} ({g.Store})").Order());
    }

    [Fact]
    public void FIND_02_a_Unity_game_names_its_company_and_product_and_where_it_saves()
    {
        using var world = new TestWorld();
        var cuphead = Path.Combine(world.Root, "Cuphead");
        Exe(Path.Combine(cuphead, "Cuphead.exe"), 5000);
        Exe(Path.Combine(cuphead, "UnityCrashHandler64.exe"), 9000);
        Write(Path.Combine(cuphead, "Cuphead_Data", "app.info"), "Studio MDHR\nCuphead");

        var print = Fingerprinter.Read(cuphead);

        Assert.Equal("Unity", print.Engine);
        Assert.Equal(Path.Combine(cuphead, "Cuphead.exe"), print.MainExe);
        Assert.Contains("Studio MDHR", print.Names);
        Assert.Equal(["<localLow>/Studio MDHR/Cuphead"], print.PredictedFolders);
        Assert.Equal(["HKEY_CURRENT_USER/Software/Studio MDHR/Cuphead"], print.PredictedRegistry);
        Assert.Null(print.AntiCheat);
    }

    [Fact]
    public void FIND_02_Unity_swaps_characters_Windows_forbids_in_folder_names()
    {
        using var world = new TestWorld();
        var stick = Path.Combine(world.Root, "StickFightTheGame");
        Exe(Path.Combine(stick, "StickFight.exe"), 5000);
        Write(Path.Combine(stick, "StickFight_Data", "app.info"), "Landfall\nStick Fight: The Game");

        Assert.Equal(["<localLow>/Landfall/Stick Fight_ The Game"], Fingerprinter.Read(stick).PredictedFolders);
    }

    [Fact]
    public void FIND_02_an_Unreal_game_saves_under_its_project_including_inside_its_install_folder()
    {
        using var world = new TestWorld();
        var wukong = Path.Combine(world.Root, "Black Myth Wukong");
        Exe(Path.Combine(wukong, "b1.exe"), 1000);
        Exe(Path.Combine(wukong, "b1", "Binaries", "Win64", "b1-Win64-Shipping.exe"), 90000);

        var print = Fingerprinter.Read(wukong);

        Assert.Equal("Unreal", print.Engine);
        Assert.Equal(["<localAppData>/b1/Saved/SaveGames", "<installDir>/b1/Saved/SaveGames"], print.PredictedFolders);
    }

    [Theory]
    [InlineData("EasyAntiCheat/EasyAntiCheat_EOS_Setup.exe", "EasyAntiCheat")]
    [InlineData("BattlEye/BEService_x64.exe", "BattlEye")]
    [InlineData("GameGuard/GameMon.des", "GameGuard")]
    [InlineData("start_protected_game.exe", "start_protected_game.exe")]
    public void LIB_09_a_game_that_ships_an_anti_cheat_is_flagged(string file, string expected)
    {
        using var world = new TestWorld();
        var game = Path.Combine(world.Root, "Game");
        Exe(Path.Combine(game, "Game.exe"), 5000);
        Exe(Path.Combine(game, file.Replace('/', Path.DirectorySeparatorChar)), 100);

        Assert.Equal(expected, Fingerprinter.Read(game).AntiCheat);
    }

    private static void SteamApp(string library, string id, string name, string folder, string build = "1", bool createFolder = true)
    {
        Write(Path.Combine(library, "steamapps", $"appmanifest_{id}.acf"), $$"""
            "AppState"
            {
                "appid"     "{{id}}"
                "name"      "{{name}}"
                "installdir" "{{folder}}"
                "buildid"   "{{build}}"
                "StateFlags" "4"
            }
            """);
        if (createFolder)
        {
            Directory.CreateDirectory(Path.Combine(library, "steamapps", "common", folder));
        }
    }

    private static void Epic(string manifests, string name, string json, string location) =>
        Write(Path.Combine(manifests, $"{name}.item"), json.Replace("{{gta}}", location.Replace("\\", "\\\\"), StringComparison.Ordinal));

    private static void Exe(string path, int size)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}
