using System.Globalization;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Host;
using GameSync.UI.ViewModels;

namespace GameSync.Core.Tests;

/// <summary>
/// KAN-123 (the owner, 4 Oct 2026: "As long as my offline games also have achievements"; design system version 48): a copy
/// Steam doesn't run that keeps its own record of what's unlocked, in a folder added in Settings, Achievements, counts like
/// a Steam game, each achievement named from Steam's list, and its unlocks pop up while it plays.
/// </summary>
public class CopyAchievementTests
{
    private const long Cuphead = 268910;
    private static readonly GameId CupheadId = GameId.Parse("cuphead");

    private const string List = """
        {"response":{"achievements":[
          {"internal_name":"ACH_DEVIL","localized_name":"Devil's Bargain","localized_desc":"Beat the Devil","hidden":false,"player_percent_unlocked":"12.4"},
          {"internal_name":"ACH_SECRET","localized_name":"Secret Ending","localized_desc":"Join the Devil","hidden":true,"player_percent_unlocked":3.1},
          {"internal_name":"ACH_ODD","localized_name":"Odd One","hidden":false,"player_percent_unlocked":"40"}
        ]}}
        """;

    [Fact]
    public void KAN_123_a_copys_own_record_is_read_in_either_shape_and_found_by_its_Steam_number()
    {
        using var world = new TestWorld();
        var records = Path.Combine(world.Root, "Records");
        var at = new DateTimeOffset(2026, 10, 3, 18, 30, 0, TimeSpan.Zero);

        // An INI record: a section per achievement, by Steam's own names, with whether and when.
        var ini = Record(records, Cuphead, Path.Combine("Stats", "Achievements.ini"), $"""
            ; written by the copy
            [ACH_DEVIL]
            achieved=true
            timestamp={at.ToUnixTimeSeconds()}

            [ACH_SECRET]
            achieved=false
            timestamp=0

            [ach_odd]
            Achieved=1
            """);
        var read = CopyAchievements.Read(ini)!;
        Assert.Equal(["ACH_DEVIL", "ach_odd"], read.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(at.UtcDateTime, read["ACH_DEVIL"]);
        Assert.Null(read["ACH_ODD"]);

        // A JSON record says the same; half written, or neither shape, it's left for the next look.
        var json = Path.Combine(world.Root, "achievements.json");
        File.WriteAllText(json, """{"ACH_DEVIL":{"earned":true,"earned_time":WHEN},"ACH_SECRET":{"earned":false},"ACH_ODD":{"earned":1}}"""
            .Replace("WHEN", at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.Equal([("ACH_DEVIL", (DateTime?)at.UtcDateTime), ("ACH_ODD", null)], CopyAchievements.Read(json)!.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => (r.Key, r.Value)));
        File.WriteAllText(json, """{"ACH_DEVIL":{"earned":tr""");
        Assert.Null(CopyAchievements.Read(json));
        Assert.Null(CopyAchievements.Read(Path.Combine(world.Root, "none.ini")));

        // Another shape of INI, with a byte-order mark, spaces and Windows' line ends, keeping thousands of seconds; and a
        // record with nothing in it yet, a game with none unlocked.
        var other = Path.Combine(world.Root, "Other");
        var kilo = Record(other, 49520, "Achievements.ini", "\uFEFF[Achievement_1]\r\nAchieved = true\r\nTimeUnlocked = 1790181\r\n\r\n[Achievement_2]\r\nAchieved = false\r\n");
        Assert.Equal([("Achievement_1", (DateTime?)DateTimeOffset.FromUnixTimeSeconds(1790181000).UtcDateTime)], CopyAchievements.Read(kilo)!.Select(r => (r.Key, r.Value)));
        Assert.Equal(new DateTime(2026, 9, 23), CopyAchievements.Read(kilo)!["achievement_1"]!.Value.Date);
        Assert.Empty(CopyAchievements.Read(Record(other, 1234, Path.Combine("Stats", "achievements.ini"), ""))!);

        // Found under the game's Steam number in any folder added, the one written last; a folder not there is passed over.
        Assert.Equal(ini, CopyAchievements.Find([Path.Combine(world.Root, "Gone"), records], Cuphead));
        var newer = Record(other, Cuphead, "achievements.json", """{"ACH_DEVIL":{"earned":true}}""");
        File.SetLastWriteTimeUtc(newer, File.GetLastWriteTimeUtc(ini).AddMinutes(1));
        Assert.Equal(newer, CopyAchievements.Find([records, other], Cuphead));
        Assert.Null(CopyAchievements.Find([records], 1234));

        // A folder counts the games with a record in it: a game's folder without one, or a folder that isn't a game's, doesn't.
        Directory.CreateDirectory(Path.Combine(records, "1234"));
        Directory.CreateDirectory(Path.Combine(records, "notes"));
        Assert.Equal(1, CopyAchievements.CountIn(records));
        Assert.Equal(0, CopyAchievements.CountIn(Path.Combine(world.Root, "Gone")));
    }

    [Fact]
    public void KAN_123_a_copy_with_its_own_record_counts_like_a_Steam_game_named_from_Steams_list()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var steam = Directory.CreateDirectory(Path.Combine(world.Root, "Steam")).FullName;
        KeepList(data);
        var records = Path.Combine(world.Root, "Records");
        var at = new DateTimeOffset(2026, 10, 3, 18, 30, 0, TimeSpan.Zero);
        Record(records, Cuphead, Path.Combine("Stats", "achievements.ini"), $"""
            [ACH_DEVIL]
            achieved=true
            timestamp={at.ToUnixTimeSeconds()}
            [ACH_ODD]
            achieved=true
            """);
        var copy = new LauncherGame { Id = CupheadId, Title = "Cuphead", Store = StoreKind.Loose, SteamAppId = Cuphead, Installed = true, LastPlayedUtc = DateTime.UtcNow };
        LauncherGame[] games = [copy];

        // No folder added: Steam's list at 0, counted nowhere.
        Assert.Equal(AchievementsFrom.ListNotSteamCopy, Achievements.For(data, copy, steam)!.From);
        Assert.Empty(Achievements.ForAll(data, games, steam));
        Assert.Empty(Achievements.SettingsRows(data, games, steam));

        // The folder added in Settings: the copy's own record, named from Steam's list, tracked like a Steam game's.
        Assert.Throws<UsageException>(() => Achievements.AddRecordFolder(data, Path.Combine(world.Root, "Gone")));
        Assert.Equal($"Added {records}: records for 1 game, whose achievements count like Steam's.", Achievements.AddRecordFolder(data, records));
        Assert.Equal($"{records} is already one of the folders.", Achievements.AddRecordFolder(data, records + Path.DirectorySeparatorChar));
        Assert.Equal([records], Achievements.RecordFolders(data));
        var view = Achievements.For(data, copy, steam)!;
        Assert.Equal((AchievementsFrom.CopyRecord, true, 2, 3), (view.From, view.IsTracked, view.Unlocked, view.Total));
        var devil = view.All.Single(a => a.Id == "ACH_DEVIL");
        Assert.Equal(("Devil's Bargain", (DateTime?)at.UtcDateTime, AchievementTier.Silver), (devil.Name, devil.KnownUnlockUtc, devil.Tier));
        var odd = view.All.Single(a => a.Id == "ACH_ODD");
        Assert.Equal((true, null), (odd.Unlocked, odd.KnownUnlockUtc));

        // A hidden one still locked stays secret; the record never makes up one Steam doesn't list.
        Assert.True(view.All.Single(a => a.Id == "ACH_SECRET").Secret);
        Assert.Equal(3, view.All.Count);

        // A Steam copy of the same game is Steam's to say: the record is for copies Steam doesn't run.
        Assert.Equal(AchievementsFrom.ListNotPlayedHere, Achievements.For(data, copy with { Store = StoreKind.Steam }, steam)!.From);

        // Counted on the Achievements page and Home, and in Achievements by game with GameSync's popup on, as no launcher shows one.
        Assert.Equal(["Cuphead"], Achievements.ForAll(data, games, steam).Select(v => v.Title));
        var row = Assert.Single(Achievements.SettingsRows(data, games, steam));
        Assert.Equal(("2 of 3", null, true), (row.Line, row.Launcher, row.Popup));
        var home = Achievements.ForHome(data, copy, games, steam);
        Assert.Equal(AchievementsFrom.CopyRecord, home.View!.From);
        Assert.Equal("Cuphead · 2 of 3 in its own record", HomeAchievements.Of(home.View, copy, home.HeroHasNone, games, DateTime.Now).Subtitle);

        // Settings' section lists the folder with how many games have records there.
        var section = new AchievementSettings(new SettingsActions());
        section.Show(new SettingsView
        {
            BackupFolder = data, ThisPc = "DESKTOP", AppVersion = "0.5.0",
            AchievementFolders = [new RecordFolderSummary(records, 1, false), new RecordFolderSummary(@"D:\Gone", 0, true)],
        });
        Assert.Equal([(records, "Records for 1 game", "trophy"), (@"D:\Gone", "Not there right now", "trophy")], section.RecordFolders.Select(f => (f.Path, f.Meta, f.Icon)));

        // Taken off again: Steam's list at 0 once more.
        Assert.Equal($"Removed {records}: its games show Steam's list at 0 again.", Achievements.RemoveRecordFolder(data, records));
        Assert.Equal(AchievementsFrom.ListNotSteamCopy, Achievements.For(data, copy, steam)!.From);
        Assert.Throws<UsageException>(() => Achievements.RemoveRecordFolder(data, records));
    }

    [Fact]
    public void KAN_123_an_unlock_in_a_running_copys_record_pops_up_with_GameSyncs_popup()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var steam = Directory.CreateDirectory(Path.Combine(world.Root, "Steam")).FullName;
        KeepList(data);
        var records = Path.Combine(world.Root, "Records");
        var longAgo = new DateTimeOffset(2025, 3, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var written = DateTime.UtcNow.AddMinutes(-10);
        var file = Record(records, Cuphead, "achievements.ini", $"[ACH_DEVIL]\nachieved=true\ntimestamp={longAgo}\n");
        void Unlock(string text)
        {
            File.WriteAllText(file, text);
            File.SetLastWriteTimeUtc(file, written = written.AddSeconds(5));
        }

        File.SetLastWriteTimeUtc(file, written);
        Achievements.AddRecordFolder(data, records);

        (long, GameId, string)? playing = null;
        using var watch = new AchievementWatch(data, _ => null, steamRoot: () => steam, running: () => null, every: TimeSpan.FromHours(1),
            runningCopy: () => playing, recordOf: app => Achievements.RecordOf(data, app));
        var popped = new List<AchievementUnlock>();
        var ended = new List<long>();
        watch.Unlocked += popped.Add;
        watch.Ended += ended.Add;
        watch.Tick();
        Assert.Null(watch.Watching);

        // The copy starts: what its record holds already is where it starts from.
        playing = (Cuphead, CupheadId, "Cuphead");
        watch.Tick();
        watch.Tick();
        Assert.Equal(Cuphead, watch.Watching);
        Assert.Empty(popped);

        // The secret ending now: one popup, GameSync's own (no launcher shows one), Gold, 2 of 3.
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Unlock($"[ACH_DEVIL]\nachieved=true\ntimestamp={longAgo}\n[ACH_SECRET]\nachieved=true\ntimestamp={now}\n");
        watch.Tick();
        var secret = Assert.Single(popped);
        Assert.Equal((CupheadId, "Cuphead", "Secret Ending", 2, 3, "gold", false), (secret.Game, secret.GameTitle, secret.Achievement.Name, secret.Done, secret.Total,
            secret.Tier, secret.BySteam));

        // Nothing new, or one the record says was unlocked long ago: never a popup.
        watch.Tick();
        Unlock($"[ACH_DEVIL]\nachieved=true\ntimestamp={longAgo}\n[ACH_SECRET]\nachieved=true\ntimestamp={now}\n[ACH_ODD]\nachieved=true\ntimestamp={longAgo}\n");
        watch.Tick();
        Assert.Single(popped);

        // The copy closes: the watch says so.
        playing = null;
        watch.Tick();
        Assert.Equal([Cuphead], ended);
        Assert.Null(watch.Watching);
    }

    [Fact]
    public void KAN_123_a_copy_that_writes_its_record_at_its_first_unlock_pops_that_one_up()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        KeepList(data);
        var records = Directory.CreateDirectory(Path.Combine(world.Root, "Records")).FullName;
        Achievements.AddRecordFolder(data, records);
        (long, GameId, string)? playing = (Cuphead, CupheadId, "Cuphead");
        using var watch = new AchievementWatch(data, _ => null, steamRoot: () => null, running: () => null, every: TimeSpan.FromHours(1),
            runningCopy: () => playing, recordOf: app => Achievements.RecordOf(data, app));
        var popped = new List<AchievementUnlock>();
        watch.Unlocked += popped.Add;
        watch.Tick();
        Assert.Equal(Cuphead, watch.Watching);

        // No record until the first unlock: one that keeps no time counts as just now.
        Record(records, Cuphead, Path.Combine("Stats", "achievements.ini"), "[ACH_ODD]\nachieved=true\n");
        watch.Tick();
        Assert.Equal(["Odd One"], popped.Select(p => p.Achievement.Name));
    }

    [Fact]
    public void KAN_123_a_record_there_before_its_folder_is_added_mid_game_is_where_it_starts_from()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        KeepList(data);
        var records = Path.Combine(world.Root, "Records");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var file = Record(records, Cuphead, "achievements.ini", $"[ACH_DEVIL]\nachieved=true\ntimestamp={now - 60}\n");
        File.SetCreationTimeUtc(file, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-1));
        (long, GameId, string)? playing = (Cuphead, CupheadId, "Cuphead");
        using var watch = new AchievementWatch(data, _ => null, steamRoot: () => null, running: () => null, every: TimeSpan.FromHours(1),
            runningCopy: () => playing, recordOf: app => Achievements.RecordOf(data, app));
        var popped = new List<AchievementUnlock>();
        watch.Unlocked += popped.Add;
        watch.Tick();

        // The folder added while the game runs: what its record held already never pops up, though it's recent.
        Achievements.AddRecordFolder(data, records);
        watch.Tick();
        Assert.Empty(popped);

        // What comes after does.
        File.WriteAllText(file, $"[ACH_DEVIL]\nachieved=true\ntimestamp={now - 60}\n[ACH_ODD]\nachieved=true\ntimestamp={now}\n");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
        watch.Tick();
        Assert.Equal(["Odd One"], popped.Select(p => p.Achievement.Name));
    }

    /// <summary>Steam's public list of Cuphead's achievements, kept as GameSync keeps it once asked.</summary>
    private static void KeepList(string data)
    {
        var kept = Directory.CreateDirectory(Path.Combine(data, "art", "achievements", Cuphead.ToString(CultureInfo.InvariantCulture))).FullName;
        File.WriteAllText(Path.Combine(kept, "list.json"), List);
    }

    /// <summary>A copy's record in a folder of them, under the game's Steam number.</summary>
    private static string Record(string folder, long appId, string relative, string text)
    {
        var file = Path.Combine(folder, appId.ToString(CultureInfo.InvariantCulture), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }
}
