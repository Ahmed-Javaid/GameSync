using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Host;

namespace GameSync.Core.Tests;

/// <summary>
/// ACH-18 (the owner, 8 Oct 2026: "Make sure that games have achievements to the best of our ability"):
/// every shape of record copies Steam doesn't run keep is read, a list of a game's achievements that says nothing of what's
/// unlocked is never taken for one, a record kept anywhere in the game's own folder is found, and a copy's record kept
/// under the Steam app ID the copy says it is counts for the game.
/// </summary>
public class RecordFormatTests
{
    private static readonly string[] Ours = ["ACH_ESCAPE", "ACH_HEAT", "ACH_SECRET", "ACH_BOSS"];

    private static readonly DateTime When = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    private static long Unix => new DateTimeOffset(When).ToUnixTimeSeconds();

    /// <summary>Seconds as a little-endian number in hexadecimal, as some records keep times.</summary>
    private static string HexLe(long n) => Convert.ToHexString(BitConverter.GetBytes((uint)n));

    [Fact]
    public void Each_shape_of_INI_record_says_what_is_unlocked_and_when()
    {
        using var world = new TestWorld();

        // A section per achievement, with state and time, and progress at its maximum counting as unlocked.
        var sections = Write(world, "sections.ini", $"""
            [SteamAchievements]
            Count=3
            00000=ACH_ESCAPE
            [ACH_ESCAPE]
            Achieved=1
            CurProgress=0
            MaxProgress=0
            UnlockTime={Unix}
            [ACH_HEAT]
            Achieved=0
            CurProgress=10
            MaxProgress=10
            [ACH_SECRET]
            HaveAchieved=0
            """);
        Assert.Equal([("ACH_ESCAPE", (DateTime?)When), ("ACH_HEAT", null)], Sorted(CopyAchievements.Read(sections)!));
        Assert.Equal(3, CopyAchievements.Matches(sections, Ours));

        // A list of names with 1 or 0, their times in another list.
        var listed = Write(world, "listed.ini", $"""
            [Achievements]
            Count=2
            ACH_ESCAPE=1
            ACH_HEAT=0
            ACH_BOSS=1
            [AchievementsUnlockTimes]
            ACH_ESCAPE={Unix}
            """);
        Assert.Equal([("ACH_BOSS", (DateTime?)null), ("ACH_ESCAPE", When)], Sorted(CopyAchievements.Read(listed)!));
        Assert.Equal(3, CopyAchievements.Matches(listed, Ours));

        // State as bytes (0101 unlocked) and times as little-endian numbers in hexadecimal.
        var bytes = Write(world, "state.ini", $"""
            [State]
            ACH_ESCAPE=0101
            ACH_HEAT=0000
            [Time]
            ACH_ESCAPE={HexLe(Unix)}
            """);
        Assert.Equal([("ACH_ESCAPE", (DateTime?)When)], Sorted(CopyAchievements.Read(bytes)!));

        // A section per achievement whose numbers are all hexadecimal, little-endian.
        var hex = Write(world, "hex.ini", $"""
            [ACH_ESCAPE]
            State=01000000
            CurProgress=00000000
            MaxProgress=00000000
            Time={HexLe(Unix)}
            [ACH_BOSS]
            State=00000000
            CurProgress=05000000
            MaxProgress=0a000000
            """);
        Assert.Equal([("ACH_ESCAPE", (DateTime?)When)], Sorted(CopyAchievements.Read(hex)!));

        // A configuration file's own way of writing: sections named ACHIEVEMENTS.<name>, quotes and comments; and a time
        // in thousands of seconds, as some keep it.
        var prefixed = Write(world, "prefixed.cfg", $"""
            [ACHIEVEMENTS.ACH_ESCAPE]
            achieved = true  # unlocked in the first hour
            unlocktime = "{Unix / 1000}"
            [ACHIEVEMENTS."ACH_HEAT"]
            achieved = false
            """);
        Assert.Equal([("ACH_ESCAPE", (DateTime?)When.AddSeconds(-(Unix % 1000)))], Sorted(CopyAchievements.Read(prefixed)!));
        Assert.Equal(2, CopyAchievements.Matches(prefixed, Ours));

        // Saved as UTF-16, with or without its mark.
        var wide = Path.Combine(world.Root, "wide.ini");
        File.WriteAllBytes(wide, [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes($"[ACH_HEAT]\r\nAchieved=1\r\nUnlockTime={Unix}\r\n")]);
        Assert.Equal([("ACH_HEAT", (DateTime?)When)], Sorted(CopyAchievements.Read(wide)!));
        File.WriteAllBytes(wide, Encoding.Unicode.GetBytes("[ACH_HEAT]\r\nAchieved=1\r\n"));
        Assert.Equal([("ACH_HEAT", (DateTime?)null)], Sorted(CopyAchievements.Read(wide)!));
    }

    [Fact]
    public void Each_shape_of_JSON_record_says_what_is_unlocked_and_a_list_of_definitions_is_no_record()
    {
        using var world = new TestWorld();

        // Keyed by name, maybe under an achievements key; an entry may be just its state.
        var keyed = Write(world, "keyed.json", $$$"""{"achievements":{"ACH_ESCAPE":{"earned":true,"earned_time":{{{Unix}}}},"ACH_HEAT":{"earned":false},"ACH_BOSS":1}}""");
        Assert.Equal([("ACH_BOSS", (DateTime?)null), ("ACH_ESCAPE", When)], Sorted(CopyAchievements.Read(keyed)!));
        Assert.Equal(3, CopyAchievements.Matches(keyed, Ours));

        // An array of entries naming themselves, with milliseconds for a time.
        var array = Write(world, "array.json", $$"""[{"apiname":"ACH_HEAT","achieved":1,"unlocktime":{{Unix * 1000}}},{"name":"ACH_SECRET","achieved":0}]""");
        Assert.Equal([("ACH_HEAT", (DateTime?)When)], Sorted(CopyAchievements.Read(array)!));

        // The lists a copy keeps to know a game's achievements, with their names, icons and words but no state: never a
        // record, so never taken for one over the real record however many they name.
        var definitions = Write(world, Path.Combine("steam_settings", "achievements.json"),
            """[{"name":"ACH_ESCAPE","displayName":"Is There No Escape?","hidden":"0","icon":"a.jpg"},{"name":"ACH_HEAT","displayName":"Hot Stuff","hidden":"0"}]""");
        var config = Write(world, "copy.ini", "[SETTINGS]\nid = 1145360 # Hades\n[ACHIEVEMENTS.ACH_ESCAPE]\nenglish = \"Is There No Escape?\"\nhidden = 0\n[ACHIEVEMENTS.ACH_HEAT]\nenglish = \"Hot Stuff\"\n");
        Assert.Empty(CopyAchievements.Read(definitions)!);
        Assert.Equal((0, 0), (CopyAchievements.Matches(definitions, Ours), CopyAchievements.Matches(config, Ours)));
        Assert.Equal(array, CopyAchievements.Best([definitions, config, array], Ours));
    }

    [Fact]
    public void A_binary_table_keyed_by_checksums_of_the_names_is_named_from_Steams_list()
    {
        using var world = new TestWorld();

        // The standard CRC-32 check, then a table of three: one unlocked, a stat (its value runs higher), one locked.
        Assert.Equal(0xCBF43926u, CopyAchievements.Crc32("123456789"));
        var table = new byte[4 + 3 * 24];
        BinaryPrimitives.WriteInt32LittleEndian(table, 3);
        void Row(int i, string name, int time, int value)
        {
            var row = table.AsSpan(4 + i * 24, 24);
            BinaryPrimitives.WriteUInt32LittleEndian(row, CopyAchievements.Crc32(name));
            BinaryPrimitives.WriteInt32LittleEndian(row[8..], time);
            BinaryPrimitives.WriteInt32LittleEndian(row[20..], value);
        }

        Row(0, "ACH_ESCAPE", (int)Unix, 1);
        Row(1, "STAT_KILLS", 0, 57);
        Row(2, "ACH_HEAT", 0, 0);
        var stats = Path.Combine(world.Root, "stats.bin");
        File.WriteAllBytes(stats, table);

        Assert.Equal([("ACH_ESCAPE", (DateTime?)When)], Sorted(CopyAchievements.Read(stats, Ours)!));
        Assert.Empty(CopyAchievements.Read(stats)!);
        Assert.Equal(2, CopyAchievements.Matches(stats, Ours));
        Assert.Equal(0, CopyAchievements.Matches(stats, ["SOMETHING_ELSE"]));

        // A table whose count doesn't match its size isn't one.
        File.WriteAllBytes(stats, table[..^1]);
        Assert.Null(CopyAchievements.Read(stats, Ours));
    }

    [Fact]
    public void A_copys_own_folder_says_its_Steam_app_IDs_and_holds_records_anywhere_in_it()
    {
        using var world = new TestWorld();
        var game = Directory.CreateDirectory(Path.Combine(world.Root, "Games", "Dark Souls II")).FullName;
        Write(game, Path.Combine("Game", "steam_appid.txt"), "335300\n");
        Write(game, Path.Combine("Game", "settings.ini"), "[Settings]\r\nAppId=335300\r\nUserName=Player\r\n");
        Write(game, Path.Combine("Game", "other.ini"), "[Other]\napp_id: \"236430\" ; the first edition\n");
        Write(game, "readme.txt", "AppId=999999 isn't read from a text file");
        Assert.Equal([236430L, 335300L], CopyAchievements.DeclaredAppIds(game).Order());

        // Anywhere in the game's own folder, a file named for achievements or stats, however deep; others aren't looked at.
        var deep = Write(game, Path.Combine("Engine", "Binaries", "ThirdParty", "Steamworks", "Steamv151", "Win64", "saves", "achievements.json"), "{}");
        Write(game, Path.Combine("Engine", "Config", "Engine.ini"), "[Core]\n");
        Assert.Equal([deep], CopyAchievements.GameFolderCandidates(game));
        Assert.Empty(CopyAchievements.GameFolderCandidates(Path.Combine(world.Root, "Gone")));
    }

    [Fact]
    public async Task A_copys_record_under_the_ID_it_says_it_is_counts_for_the_game_named_from_that_IDs_list()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var steam = Directory.CreateDirectory(Path.Combine(world.Root, "Steam")).FullName;

        // GameSync took Dark Souls II for the first edition (236430); the copy is Scholar of the First Sin (335300), says so
        // in its folder, and keeps its record under that ID.
        KeepList(data, 236430, "ACH_ORIGINAL");
        KeepList(data, 335300, "ACH_SCHOLAR_A", "ACH_SCHOLAR_B");
        var game = Directory.CreateDirectory(Path.Combine(world.Root, "Games", "DS2")).FullName;
        Write(game, "steam_emu.ini", "[Settings]\nAppId=335300\nAccountId=0\n");
        var publicDocs = Path.Combine(world.Root, "Public");
        Write(publicDocs, Path.Combine("Saves", "335300", "achievements.ini"), $"[ACH_SCHOLAR_A]\nAchieved=1\nUnlockTime={Unix}\n");
        var id = GameId.Parse("dark-souls-ii");
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([new LibraryEntry { Id = id, Title = "DARK SOULS II", Store = StoreKind.Loose, InstallDir = game }]);
        }

        var copy = new LauncherGame { Id = id, Title = "DARK SOULS II", Store = StoreKind.Loose, SteamAppId = 236430, Installed = true };
        Assert.Equal(1, await Achievements.FindRecordsAsync(data, [copy], CancellationToken.None, roots: [publicDocs]));
        Assert.Equal(335300, Achievements.AppIdOf(data, copy));
        var view = Achievements.For(data, copy, steam)!;
        Assert.Equal((335300L, AchievementsFrom.CopyRecord, 1, 2), (view.AppId, view.From, view.Unlocked, view.Total));
        Assert.Equal("ACH_SCHOLAR_A", view.All.Single(a => a.Unlocked).Id);

        // What's seen is kept under that ID, and the game counts on the Achievements page.
        using (var state = new StateStore(data))
        {
            Assert.Equal([335300L], state.GetUnlocks().Select(e => e.AppId));
        }

        Assert.Equal([335300L], Achievements.ForAll(data, [copy], steam).Select(v => v.AppId));
    }

    [Fact]
    public async Task Valves_test_app_a_copy_may_say_it_is_is_never_taken_for_the_game()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        KeepList(data, 480, "ACH_SPACEWAR");
        var game = Directory.CreateDirectory(Path.Combine(world.Root, "Games", "Some Game")).FullName;
        Write(game, "steam_appid.txt", "480");
        var roaming = Path.Combine(world.Root, "Roaming");
        Write(roaming, Path.Combine("Saves", "480", "achievements.json"), """{"ACH_SPACEWAR":{"earned":true}}""");
        var id = GameId.Parse("some-game");
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([new LibraryEntry { Id = id, Title = "Some Game", Store = StoreKind.Loose, InstallDir = game }]);
        }

        var copy = new LauncherGame { Id = id, Title = "Some Game", Store = StoreKind.Loose, Installed = true };
        Assert.Equal(0, await Achievements.FindRecordsAsync(data, [copy], CancellationToken.None, roots: [roaming]));
        Assert.Null(Achievements.AppIdOf(data, copy));
    }

    private static List<(string, DateTime?)> Sorted(IReadOnlyDictionary<string, DateTime?> read) =>
        read.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => (r.Key, r.Value)).ToList();

    private static string Write(TestWorld world, string relative, string text) => Write(world.Root, relative, text);

    private static string Write(string folder, string relative, string text)
    {
        var file = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }

    /// <summary>Steam's public list of a game's achievements, kept as GameSync keeps it once asked.</summary>
    private static void KeepList(string data, long app, params string[] names)
    {
        var kept = Directory.CreateDirectory(Path.Combine(data, "art", "achievements", app.ToString(CultureInfo.InvariantCulture))).FullName;
        var entries = string.Join(',', names.Select(n => $$"""{"internal_name":"{{n}}","localized_name":"{{n}} name","hidden":false,"player_percent_unlocked":"10"}"""));
        File.WriteAllText(Path.Combine(kept, "list.json"), """{"response":{"achievements":[""" + entries + "]}}");
    }
}
