using System.Globalization;
using System.Text;
using GameSync.Core.Discovery;
using GameSync.Core.Model;
using GameSync.Core.State;
using GameSync.Core.Storage;
using GameSync.Host;
using GameSync.UI.ViewModels;
using static GameSync.Core.Tests.AchievementTests;

namespace GameSync.Core.Tests;

/// <summary>
/// Design system version 54 (the owner, 8 Oct 2026: "The achievement system needs to work even without steam. It can get
/// information about WHAT achievements are there to begin with, but after that it needs to work without it. Tracking by
/// itself or something"; then: find copies' records by itself, mark by hand "only for games that write nothing down", and
/// keep the record in the Drive folder): GameSync keeps its own record of achievements.
/// </summary>
public class KeptAchievementTests
{
    private const long Hades = 1145360;
    private static readonly GameId HadesId = GameId.Parse("hades");

    private const string List = """
        {"response":{"achievements":[
          {"internal_name":"ACH_ESCAPE","localized_name":"Is There No Escape?","localized_desc":"Clear an escape attempt","hidden":false,"player_percent_unlocked":"41"},
          {"internal_name":"ACH_HEAT","localized_name":"Hot Stuff","localized_desc":"Clear with Heat 8","hidden":false,"player_percent_unlocked":"3.2"},
          {"internal_name":"ACH_SECRET","localized_name":"The Secret","localized_desc":"Meet the one in the deep","hidden":true,"player_percent_unlocked":"12"}
        ]}}
        """;

    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Two_entries_for_one_achievement_settle_the_same_in_any_order_and_a_witness_is_never_lost()
    {
        var steamHere = new KeptUnlock(Hades, "ACH_ESCAPE", T0.AddHours(2), UnlockSeen.Steam, "d-desk", T0.AddHours(2), T0.AddHours(2));
        var recordThere = new KeptUnlock(Hades, "ach_escape", T0, UnlockSeen.Record, "d-laptop", T0.AddDays(1), T0.AddDays(1));
        var undated = new KeptUnlock(Hades, "ACH_ESCAPE", null, UnlockSeen.Steam, "d-desk", T0.AddMinutes(1), T0.AddMinutes(1));
        var marked = new KeptUnlock(Hades, "ACH_ESCAPE", T0.AddDays(3), UnlockSeen.Hand, "d-desk", T0.AddDays(3), T0.AddDays(3));
        var unmarked = marked with { Removed = true, ChangedUtc = T0.AddDays(4) };
        KeptUnlock[] all = [steamHere, recordThere, undated, marked, unmarked];

        // Every order of meeting them keeps the same one: the witness with the earliest known day, whatever its name's case.
        foreach (var order in Orders(all))
        {
            Assert.Equal(recordThere, Assert.Single(KeptAchievements.Merge(order)));
            Assert.Equal(recordThere, order.Aggregate(KeptAchievements.Better));
        }

        // A known day beats none; between witnesses with the same day, the first seen.
        Assert.Equal(steamHere, KeptAchievements.Better(undated, steamHere));
        Assert.Equal(undated, KeptAchievements.Better(undated, undated with { SeenUtc = T0.AddDays(9), ChangedUtc = T0.AddDays(9) }));

        // A mark by hand: the person's last word wins, and taking one off wins a tie; but a witness's unlock is never taken
        // off by a mark taken off.
        Assert.Equal(unmarked, KeptAchievements.Better(marked, unmarked));
        Assert.Equal(unmarked, KeptAchievements.Better(marked with { ChangedUtc = unmarked.ChangedUtc }, unmarked));
        Assert.Equal(marked with { ChangedUtc = T0.AddDays(5) }, KeptAchievements.Better(unmarked, marked with { ChangedUtc = T0.AddDays(5) }));
        Assert.Equal(steamHere, KeptAchievements.Better(unmarked, steamHere));
    }

    [Fact]
    public void A_PCs_cloud_file_reads_back_as_written_and_what_doesnt_hold_up_is_left_out()
    {
        var device = DeviceId.Parse("d-desk");
        var now = new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);
        KeptUnlock[] entries =
        [
            new(Hades, "ACH_ESCAPE", T0, UnlockSeen.Steam, "d-desk", T0, T0),
            new(Hades, "ACH_HEAT", null, UnlockSeen.Record, "d-laptop", T0.AddDays(1), T0.AddDays(1)),
            new(Hades, "ACH_SECRET", T0.AddDays(2), UnlockSeen.Hand, "d-desk", T0.AddDays(2), T0.AddDays(3), Removed: true),
        ];
        var (from, back) = KeptAchievements.Read(KeptAchievements.Write(device, entries, now), now);
        Assert.Equal("d-desk", from);
        Assert.Equal(entries.OrderBy(e => e.Id, StringComparer.Ordinal), back.OrderBy(e => e.Id, StringComparer.Ordinal));

        // Not GameSync's file, half written, or another shape: nothing.
        Assert.Empty(KeptAchievements.Read("not json"u8, now).Entries);
        Assert.Empty(KeptAchievements.Read("""{"format":2,"unlocks":[]}"""u8, now).Entries);
        Assert.Empty(KeptAchievements.Read("""{"format":1,"unlocks":{}}"""u8, now).Entries);

        // Each entry is checked: a name with a line break, a time in the future, a witness's unlock taken off, a device that
        // isn't one, a missing field or a time that isn't one are left out; the rest are kept.
        string Entry(string fields) => "{" + fields + "}";
        const string ok = "\"app\":1145360,\"id\":\"ACH_OK\",\"by\":\"steam\",\"device\":\"d-desk\",\"seen\":\"2026-10-01T12:00:00Z\",\"changed\":\"2026-10-01T12:00:00Z\"";
        var file = "{\"format\":1,\"device\":\"d-laptop\",\"unlocks\":[" + string.Join(',',
            Entry(ok),
            Entry(ok.Replace("ACH_OK", "ACH\\nBAD", StringComparison.Ordinal)),
            Entry(ok + ",\"at\":\"2099-01-01T00:00:00Z\""),
            Entry(ok + ",\"removed\":true"),
            Entry(ok.Replace("d-desk", "../evil", StringComparison.Ordinal)),
            Entry(ok.Replace("\"by\":\"steam\",", "", StringComparison.Ordinal)),
            Entry(ok + ",\"at\":\"yesterday\""),
            Entry(ok.Replace("1145360", "-4", StringComparison.Ordinal)),
            "\"just text\"") + "]}";
        var read = KeptAchievements.Read(Encoding.UTF8.GetBytes(file), now);
        Assert.Equal("d-laptop", read.Device);
        Assert.Equal(["ACH_OK"], read.Entries.Select(e => e.Id));
    }

    [Fact]
    public void What_Steam_unlocked_stays_once_Steam_is_gone_named_and_dated()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var steam = Path.Combine(world.Root, "Steam");
        var stats = Directory.CreateDirectory(Path.Combine(steam, "appcache", "stats")).FullName;
        var unlocked = new DateTimeOffset(2026, 9, 22, 15, 54, 0, TimeSpan.Zero);
        AchievementTests.Write(Path.Combine(stats, $"UserGameStatsSchema_{Valheim}.bin"), Kv.Block("892970", Kv.Block("stats", Kv.Block("1", Kv.Text("type", "ACHIEVEMENTS"),
            Kv.Block("bits", Achievement(0, "ArrivedHere", Kv.Text("name", "I Have Arrived!"), "Arrive", hidden: false, "arrived.jpg"),
                Achievement(1, "Eikthyr", Kv.Text("name", "Hunter"), "Defeat Eikthyr", hidden: false, "hunter.jpg"))))));
        AchievementTests.Write(Path.Combine(stats, $"UserGameStats_281062582_{Valheim}.bin"), Kv.Block("cache", Kv.Block("1", Kv.Int("data", 1),
            Kv.Block("AchievementTimes", Kv.Int("0", (int)unlocked.ToUnixTimeSeconds())))));
        var valheim = new LauncherGame { Id = GameId.Parse("valheim"), Title = "Valheim", Store = StoreKind.Steam, SteamAppId = Valheim, Installed = true };

        // Read from Steam: kept in GameSync's record as it's read.
        var seen = Achievements.For(data, valheim, steam)!;
        Assert.Equal((AchievementsFrom.Steam, 1), (seen.From, seen.Unlocked));
        using (var state = new StateStore(data))
        {
            var kept = Assert.Single(state.GetUnlocks(Valheim));
            Assert.Equal(("ArrivedHere", (DateTime?)unlocked.UtcDateTime, UnlockSeen.Steam), (kept.Id, kept.UnlockedUtc, kept.Seen));
        }

        // Steam uninstalled, or another account signed in with nothing unlocked: what GameSync saw stays, with its name and day.
        Directory.Delete(steam, recursive: true);
        var after = Achievements.For(data, valheim, steam)!;
        Assert.Equal((AchievementsFrom.Kept, 1, 2, false), (after.From, after.Unlocked, after.Total, after.CanMark));
        var arrived = after.All.Single(a => a.Unlocked);
        Assert.Equal(("I Have Arrived!", (DateTime?)unlocked.UtcDateTime, false, null), (arrived.Name, arrived.KnownUnlockUtc, arrived.Marked, arrived.OnPc));
        Assert.Equal(["Valheim"], Achievements.ForAll(data, [valheim], steam).Select(v => v.Title));
    }

    [Fact]
    public void Achievements_are_marked_by_hand_only_for_a_copy_that_writes_nothing_down()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var steam = Directory.CreateDirectory(Path.Combine(world.Root, "Steam")).FullName;
        KeepList(data);
        var copy = new LauncherGame { Id = HadesId, Title = "Hades", Store = StoreKind.Loose, SteamAppId = Hades, Installed = true };

        // Nothing writes its unlocks down: Steam's list at 0, and it can be marked.
        var before = Achievements.For(data, copy, steam)!;
        Assert.Equal((AchievementsFrom.ListNotSteamCopy, true, false), (before.From, before.CanMark, before.IsTracked));

        // Marked: unlocked today, by the person, by Steam's name or the name shown; counted like any other.
        Assert.Equal("Marked Hot Stuff unlocked in Hades.", Achievements.Mark(data, copy, "Hot Stuff", unlocked: true, steam));
        Assert.Equal("Marked Is There No Escape? unlocked in Hades.", Achievements.Mark(data, copy, "ach_escape", unlocked: true, steam));
        Assert.Equal("Hot Stuff is already unlocked.", Achievements.Mark(data, copy, "ACH_HEAT", unlocked: true, steam));
        var marked = Achievements.For(data, copy, steam)!;
        Assert.Equal((AchievementsFrom.Kept, 2, 2, true, true), (marked.From, marked.Unlocked, marked.Marked, marked.CanMark, marked.IsTracked));
        var heat = marked.All.Single(a => a.Id == "ACH_HEAT");
        Assert.True(heat.Marked);
        Assert.Equal(DateTime.UtcNow.Date, heat.KnownUnlockUtc!.Value.Date);
        Assert.Throws<UsageException>(() => Achievements.Mark(data, copy, "No Such One", unlocked: true, steam));

        // Taken off: locked again; one never marked says so.
        Assert.Equal("Took the mark off Hot Stuff in Hades.", Achievements.Mark(data, copy, "ACH_HEAT", unlocked: false, steam));
        Assert.Equal("The Secret isn't unlocked.", Achievements.Mark(data, copy, "ACH_SECRET", unlocked: false, steam));
        Assert.Equal(1, Achievements.For(data, copy, steam)!.Unlocked);

        // A copy whose own record GameSync reads, or a Steam copy, writes its unlocks down: no marking there.
        var records = Path.Combine(world.Root, "Records");
        Record(records, Hades, "achievements.json", """{"ACH_SECRET":{"earned":true,"earned_time":1790000000}}""");
        Achievements.AddRecordFolder(data, records);
        var withRecord = Achievements.For(data, copy, steam)!;
        Assert.Equal((AchievementsFrom.CopyRecord, false, 2), (withRecord.From, withRecord.CanMark, withRecord.Unlocked));
        Assert.Throws<UsageException>(() => Achievements.Mark(data, copy, "ACH_HEAT", unlocked: true, steam));
        Assert.Throws<UsageException>(() => Achievements.Mark(data, copy with { Store = StoreKind.Steam }, "ACH_HEAT", unlocked: true, steam));

        // What a witness saw is never taken off by hand; the mark made before the record turned up stays, as the person's.
        Assert.Throws<UsageException>(() => Achievements.Mark(data, copy, "ACH_SECRET", unlocked: false, steam));
        Assert.True(withRecord.All.Single(a => a.Id == "ACH_ESCAPE").Marked);
        Assert.Equal("Took the mark off Is There No Escape? in Hades.", Achievements.Mark(data, copy, "ACH_ESCAPE", unlocked: false, steam));
    }

    [Fact]
    public async Task GameSync_finds_a_copys_own_record_by_the_achievements_named_inside_it()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var steam = Directory.CreateDirectory(Path.Combine(world.Root, "Steam")).FullName;
        KeepList(data);
        var roaming = Path.Combine(world.Root, "Roaming");
        var copy = new LauncherGame { Id = HadesId, Title = "Hades", Store = StoreKind.Loose, SteamAppId = Hades, Installed = true };

        // Under the game's Steam number somewhere a few folders down, as such copies keep them: the one naming its
        // achievements is the record. A list of the achievements in another shape, a file that isn't about them, a game's
        // folder too deep, and GameSync's own folders are never taken for it.
        var record = Record(Path.Combine(roaming, "SomeTool", "Saves"), Hades, Path.Combine("stats", "achievements.json"),
            """{"ACH_ESCAPE":{"earned":true,"earned_time":1790000000},"ACH_HEAT":{"earned":false}}""");
        Record(Path.Combine(roaming, "Other"), Hades, "achievements.json", """[{"name":"ACH_ESCAPE","displayName":"Is There No Escape?"}]""");
        Record(Path.Combine(roaming, "Other"), Hades, "settings.ini", "[Video]\nwidth=1920\n[ACH_ESCAPE]\nenglish=Is There No Escape?\n[Audio]\n[Input]\n");
        Record(Path.Combine(roaming, "a", "b", "c", "d"), Hades, "achievements.json", """{"ACH_HEAT":{"earned":true}}""");
        Record(Path.Combine(data, "art"), Hades, "achievements.json", """{"ACH_HEAT":{"earned":true}}""");
        var candidates = CopyAchievements.Candidates([roaming], new HashSet<long> { Hades }, leftOut: [data]);
        Assert.Equal(3, candidates[Hades].Count);
        Assert.Equal(record, CopyAchievements.Best(candidates[Hades], ["ACH_ESCAPE", "ACH_HEAT", "ACH_SECRET"]));

        // A record with nothing in it yet (the owner's Sons of the Forest, 8 Oct 2026: an empty Achievements.ini) is the
        // record when nothing names the game's achievements; any other empty file isn't.
        var empty = Record(Path.Combine(world.Root, "Public"), 1326470, Path.Combine("Stats", "Achievements.ini"), "");
        Record(Path.Combine(world.Root, "Public"), 1326470, Path.Combine("Stats", "Stats.ini"), "");
        var sons = CopyAchievements.Candidates([Path.Combine(world.Root, "Public")], new HashSet<long> { 1326470 })[1326470];
        Assert.Equal(empty, CopyAchievements.Best(sons, ["ACH_THIS_CANT_BE_HEALTHY"]));
        Assert.Null(CopyAchievements.Best(sons.Where(f => f != empty), ["ACH_THIS_CANT_BE_HEALTHY"]));

        // Look again: kept for the game, and its unlocks count like a Steam game's.
        Assert.Equal(1, await Achievements.FindRecordsAsync(data, [copy], CancellationToken.None, roots: [roaming, data]));
        Assert.Equal(record, Achievements.RecordOf(data, Hades));
        var view = Achievements.For(data, copy, steam)!;
        Assert.Equal((AchievementsFrom.CopyRecord, 1, false), (view.From, view.Unlocked, view.CanMark));
        using (var state = new StateStore(data))
        {
            Assert.Equal([(Hades, record)], Achievements.FoundRecords(state).Select(f => (f.Key, f.Value)));
            Assert.Equal("Hades", state.GetSetting(Achievements.FoundGameKey(Hades)));
        }

        // Gone later: forgotten at the next look, and what GameSync saw in it stays.
        File.Delete(record);
        Assert.Equal(0, await Achievements.FindRecordsAsync(data, [copy], CancellationToken.None, roots: [roaming]));
        Assert.Null(Achievements.RecordOf(data, Hades));
        Assert.Equal((AchievementsFrom.Kept, 1), (Achievements.For(data, copy, steam)!.From, Achievements.For(data, copy, steam)!.Unlocked));
    }

    [Fact]
    public async Task A_record_a_copy_writes_while_it_plays_is_found_and_its_first_unlock_pops_up()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        KeepList(data);
        var roaming = Directory.CreateDirectory(Path.Combine(world.Root, "Roaming")).FullName;
        var copy = new LauncherGame { Id = HadesId, Title = "Hades", Store = StoreKind.Loose, SteamAppId = Hades, Installed = true };
        var found = new List<string>();
        using var record = (await Achievements.WatchForRecordAsync(data, copy, found.Add, roots: [roaming], every: TimeSpan.FromHours(1)))!;
        Assert.True(record.Watching);

        (long, GameId, string)? playing = (Hades, HadesId, "Hades");
        using var watch = new AchievementWatch(data, _ => null, steamRoot: () => null, running: () => null, every: TimeSpan.FromHours(1),
            runningCopy: () => playing, recordOf: app => Achievements.RecordOf(data, app));
        var popped = new List<AchievementUnlock>();
        watch.Unlocked += popped.Add;
        watch.Tick();

        // Other files written meanwhile aren't it; the copy's record, written at its first unlock, is.
        var other = Path.Combine(roaming, "Browser", "prefs.json");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllText(other, """{"theme":{"dark":true}}""");
        record.Heard(other);
        record.Look();
        Assert.Empty(found);
        var written = Record(Path.Combine(roaming, "Tool"), Hades, "achievements.ini", "[ACH_HEAT]\nachieved=1\n");
        record.Heard(written);
        record.Look();
        Assert.Equal([written], found);
        Assert.False(record.Watching);
        Assert.Equal(written, Achievements.RecordOf(data, Hades));

        // The watch over the game reads it at its next look, and the unlock it was written for pops up, GameSync's own.
        watch.Tick();
        var hot = Assert.Single(popped);
        Assert.Equal(("Hot Stuff", false), (hot.Achievement.Name, hot.BySteam));

        // Kept in GameSync's record as it's seen, unlocked now, as the record keeps no time.
        using var state = new StateStore(data);
        var kept = Assert.Single(state.GetUnlocks(Hades));
        Assert.Equal(("ACH_HEAT", UnlockSeen.Record), (kept.Id, kept.Seen));
        Assert.True((DateTime.UtcNow - kept.UnlockedUtc!.Value).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Two_PCs_keep_one_record_through_the_cloud()
    {
        using var world = new TestWorld();
        var cloud = new FolderCloud(Directory.CreateDirectory(Path.Combine(world.Root, "cloud")).FullName);
        var desk = DeviceId.Parse("d-desk");
        var laptop = DeviceId.Parse("d-laptop");
        using var deskState = new StateStore(Path.Combine(world.Root, "desk"));
        using var laptopState = new StateStore(Path.Combine(world.Root, "laptop"));

        // Nothing kept yet: nothing written.
        Assert.Equal(new AchievementExchange(0, false), await AchievementShare.ExchangeAsync(cloud, deskState, desk, CancellationToken.None));

        // Unlocked in Steam on the desktop: it goes up, and the laptop takes it in.
        deskState.KeepUnlocks([new KeptUnlock(Hades, "ACH_ESCAPE", T0, UnlockSeen.Steam, desk.Value, T0, T0)]);
        Assert.Equal(new AchievementExchange(0, true), await AchievementShare.ExchangeAsync(cloud, deskState, desk, CancellationToken.None));
        Assert.Equal(new AchievementExchange(1, true), await AchievementShare.ExchangeAsync(cloud, laptopState, laptop, CancellationToken.None));
        Assert.Equal("d-desk", Assert.Single(laptopState.GetUnlocks(Hades)).Device);

        // Unchanged since: nothing read or written again.
        Assert.Equal(new AchievementExchange(0, false), await AchievementShare.ExchangeAsync(cloud, deskState, desk, CancellationToken.None));
        Assert.Equal(new AchievementExchange(0, false), await AchievementShare.ExchangeAsync(cloud, laptopState, laptop, CancellationToken.None));

        // Marked on the laptop, then taken off there: the desktop ends the same, with the escape still unlocked.
        laptopState.KeepUnlocks([new KeptUnlock(Hades, "ACH_HEAT", T0.AddDays(1), UnlockSeen.Hand, laptop.Value, T0.AddDays(1), T0.AddDays(1))]);
        await AchievementShare.ExchangeAsync(cloud, laptopState, laptop, CancellationToken.None);
        Assert.Equal(1, (await AchievementShare.ExchangeAsync(cloud, deskState, desk, CancellationToken.None)).Pulled);
        Assert.Equal(["ACH_ESCAPE", "ACH_HEAT"], deskState.GetUnlocks(Hades).Where(e => !e.Removed).Select(e => e.Id).Order(StringComparer.Ordinal));
        laptopState.KeepUnlocks([new KeptUnlock(Hades, "ACH_HEAT", T0.AddDays(1), UnlockSeen.Hand, laptop.Value, T0.AddDays(1), T0.AddDays(2), Removed: true)]);
        await AchievementShare.ExchangeAsync(cloud, laptopState, laptop, CancellationToken.None);
        await AchievementShare.ExchangeAsync(cloud, deskState, desk, CancellationToken.None);
        Assert.Equal(["ACH_ESCAPE"], deskState.GetUnlocks(Hades).Where(e => !e.Removed).Select(e => e.Id));

        // A reinstall with an empty record reads its own file back too.
        using var fresh = new StateStore(Path.Combine(world.Root, "desk-again"));
        Assert.Equal(2, (await AchievementShare.ExchangeAsync(cloud, fresh, desk, CancellationToken.None)).Pulled);

        // Each PC writes only its own file in the cloud's achievements folder.
        Assert.Equal(["d-desk.json", "d-laptop.json"], Directory.EnumerateFiles(Path.Combine(world.Root, "cloud", "achievements")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Keeping_the_record_in_the_cloud_is_on_unless_turned_off_and_Settings_says_what_was_found()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        Assert.True(AchievementShare.IsOn(data));
        AchievementShare.SetOn(data, false);
        Assert.False(AchievementShare.IsOn(data));
        AchievementShare.SetOn(data, true);
        Assert.True(AchievementShare.IsOn(data));

        var asked = new List<bool>();
        var section = new AchievementSettings(new SettingsActions { SetAchievementsInCloud = on => { asked.Add(on); return Task.FromResult(new Outcome("ok")); } });
        section.Show(new SettingsView
        {
            BackupFolder = data, ThisPc = "DESKTOP", AppVersion = "0.5.0", Cloud = SettingsData.Drive,
            AchievementRecordsFound = [new FoundRecordSummary(@"C:\Users\Public\Documents\Records\1145360\achievements.ini", "Hades")],
            AchievementsLookedUtc = DateTime.UtcNow.AddMinutes(-2.5), AchievementsInCloud = true,
        });
        Assert.Equal(("Found 1 record", true, true), (section.FoundTitle, section.HasFound, section.KeepInCloud));
        Assert.EndsWith("Looked 2 minutes ago.", section.FoundLine, StringComparison.Ordinal);
        Assert.Equal([("Hades", "trophy")], section.RecordsFound.Select(f => (f.Meta!, f.Icon)));
        Assert.StartsWith("Every PC shows the same achievements", section.CloudLine, StringComparison.Ordinal);
        Assert.Empty(asked);

        // The switch turned off asks for it; with no cloud connected the row says to connect one.
        section.KeepInCloud = false;
        Assert.Equal([false], asked);
        section.Show(new SettingsView { BackupFolder = data, ThisPc = "DESKTOP", AppVersion = "0.5.0", Cloud = SettingsData.NoCloud });
        Assert.Equal(("None found yet", false), (section.FoundTitle, section.HasCloud));
        Assert.StartsWith("Connect the cloud first", section.CloudLine, StringComparison.Ordinal);
        Assert.Equal([false], asked);
    }

    [Fact]
    public async Task Every_achievement_of_a_copy_that_writes_nothing_down_is_marked_and_unmarked_there()
    {
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var steam = Directory.CreateDirectory(Path.Combine(world.Root, "Steam")).FullName;
        KeepList(data);
        var copy = new LauncherGame { Id = HadesId, Title = "Hades", Store = StoreKind.Loose, SteamAppId = Hades, Installed = true };
        var actions = new LauncherActions(_ => { }, () => { }, (_, _) => { }, (_, _) => { })
        {
            MarkAchievement = (game, achievement, unlocked, _) =>
            {
                Achievements.Mark(data, copy, achievement, unlocked, steam);
                return Task.FromResult(Achievements.For(data, copy, steam));
            },
        };
        var page = new AchievementsViewModel(HadesId, "Hades", actions, () => { }, []);
        page.Show(Achievements.For(data, copy, steam)!, DateTime.Now);

        // None marked yet: it opens on Locked, says why, and the one picked can be marked.
        Assert.Equal((true, AchievementsViewModel.LockedTab), (page.CanMark, page.Tab));
        Assert.Equal("Kept by GameSync · how rare each is, from Steam's players this week · nothing written down yet", page.Subtitle);
        page.Picked = page.Shown.Single(a => a.Id == "ACH_HEAT");
        Assert.Equal((true, false, false), (page.CanMarkPicked, page.CanUnmarkPicked, page.CanRevealPicked));
        await page.MarkCommand.ExecuteAsync(null);
        var heat = page.All.Single(a => a.Id == "ACH_HEAT");
        Assert.Equal((true, true, "Marked by you", " · marked by you"), (heat.IsUnlocked, heat.Marked, heat.Seen, heat.WhenBy));
        Assert.Equal("Kept by GameSync · how rare each is, from Steam's players this week · marked by you", page.Subtitle);
        Assert.Equal("ACH_HEAT", page.Picked?.Id);
        Assert.Equal((false, true), (page.CanMarkPicked, page.CanUnmarkPicked));

        // A hidden one stays secret until shown on purpose, then can be marked.
        page.Tab = AchievementsViewModel.HiddenTab;
        page.Picked = page.Shown.Single();
        Assert.Equal((AchievementItem.HiddenName, false, true), (page.PickedName, page.CanMarkPicked, page.CanRevealPicked));
        page.RevealCommand.Execute(null);
        Assert.Equal(("The Secret", "Meet the one in the deep", "locked", true), (page.PickedName, page.PickedText, page.PickedState, page.CanMarkPicked));
        await page.MarkCommand.ExecuteAsync(null);
        Assert.Equal(2, page.Progress.Done);

        // Unmarked: locked again.
        page.Pick("ACH_HEAT");
        await page.UnmarkCommand.ExecuteAsync(null);
        Assert.False(page.All.Single(a => a.Id == "ACH_HEAT").IsUnlocked);
    }

    [Fact]
    public void A_record_that_has_never_held_an_unlock_is_marked_by_hand_until_it_does()
    {
        // The owner's Sons of the Forest, 8 Oct 2026: its copy keeps its stats (17 days survived) while its file of
        // achievements stays empty, as on Steam it's Steam that unlocks what stats earn. "If the marking exists we should be
        // able to do it."
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var steam = Directory.CreateDirectory(Path.Combine(world.Root, "Steam")).FullName;
        KeepList(data);
        var records = Path.Combine(world.Root, "Records");
        var achievements = Record(records, Hades, Path.Combine("Stats", "Achievements.ini"), "");
        Record(records, Hades, Path.Combine("Stats", "Stats.ini"), "[Stats]\nSTAT_ESCAPES=17\n");
        Achievements.AddRecordFolder(data, records);
        var copy = new LauncherGame { Id = HadesId, Title = "Hades", Store = StoreKind.Loose, SteamAppId = Hades, Installed = true };

        // Its record is read, but says nothing: like a copy that writes nothing down, it's marked by hand.
        Assert.Equal(achievements, Achievements.RecordOf(data, Hades));
        var empty = Achievements.For(data, copy, steam)!;
        Assert.Equal((AchievementsFrom.ListNotSteamCopy, true, false), (empty.From, empty.CanMark, empty.IsTracked));
        Assert.Equal("Marked Hot Stuff unlocked in Hades.", Achievements.Mark(data, copy, "ACH_HEAT", unlocked: true, steam));

        // Once it writes an unlock down, its record says, and marking stops; the mark made before stays the person's.
        File.WriteAllText(achievements, "[ACH_ESCAPE]\nAchieved=1\nUnlockTime=1790000000\n");
        var written = Achievements.For(data, copy, steam)!;
        Assert.Equal((AchievementsFrom.CopyRecord, false, 2, 1), (written.From, written.CanMark, written.Unlocked, written.Marked));
        Assert.Throws<UsageException>(() => Achievements.Mark(data, copy, "ACH_SECRET", unlocked: true, steam));

        // Emptied again later (a reinstall), it still wrote one down once: GameSync's record keeps saying so.
        File.WriteAllText(achievements, "");
        var emptied = Achievements.For(data, copy, steam)!;
        Assert.Equal((AchievementsFrom.CopyRecord, false, 2), (emptied.From, emptied.CanMark, emptied.Unlocked));
    }

    [Fact]
    public async Task The_question_mark_says_why_GameSync_cant_see_a_copys_achievements()
    {
        // Design system version 55 (the owner, 8 Oct 2026: "have a little question mark on the achivement page for the game
        // that when hovered it explains why the game is prolly not being picked up"), from what GameSync found when it looked.
        using var world = new TestWorld();
        var data = Path.Combine(world.Root, "data");
        var steam = Directory.CreateDirectory(Path.Combine(world.Root, "Steam")).FullName;
        KeepList(data);
        var publicDocs = Path.Combine(world.Root, "Public");
        var folder = Directory.CreateDirectory(Path.Combine(world.Root, "Games", "Hades")).FullName;
        using (var library = new LibraryStore(data))
        {
            library.SaveAll([new LibraryEntry { Id = HadesId, Title = "Hades", Store = StoreKind.Loose, InstallDir = folder }]);
        }

        var copy = new LauncherGame { Id = HadesId, Title = "Hades", Store = StoreKind.Loose, SteamAppId = Hades, Installed = true };
        async Task<AchievementsWhy?> Why(LauncherGame game)
        {
            await Achievements.FindRecordsAsync(data, [game], CancellationToken.None, roots: [publicDocs]);
            return Achievements.For(data, game, steam)!.Why;
        }

        // Before GameSync has looked.
        Assert.Contains("hasn't looked", Achievements.For(data, copy, steam)!.Why!.Text, StringComparison.Ordinal);

        // Nothing anywhere: where it looked, and what the person can do.
        var nothing = await Why(copy);
        Assert.Contains("GameSync found nothing this copy writes about achievements", nothing!.Text, StringComparison.Ordinal);
        Assert.Contains("(1145360)", nothing.Text, StringComparison.Ordinal);
        Assert.Null(nothing.Path);

        // Its Steam files' settings folder has no list of its achievements, so it can't record any (the owner's Wukong).
        var settings = Directory.CreateDirectory(Path.Combine(folder, "Engine", "Binaries", "Win64", "steam_settings")).FullName;
        File.WriteAllText(Path.Combine(settings, "steam_appid.txt"), "1145360");
        var noList = await Why(copy);
        Assert.Contains("settings folder with no list of Hades's achievements", noList!.Text, StringComparison.Ordinal);
        Assert.Equal(settings, noList.Path);

        // A file made at the first start and never written to since, though played after (the owner's Ghost of Tsushima).
        var made = Record(publicDocs, Hades, "achievements.ini", "[SteamAchievements]\r\nCount=0\r\n\r\n");
        File.SetLastWriteTimeUtc(made, DateTime.UtcNow.AddDays(-90));
        var played = await Why(copy with { LastPlayedUtc = DateTime.UtcNow.AddDays(-12), LastPlayedFromSaves = true });
        Assert.Contains("though you've played since", played!.Text, StringComparison.Ordinal);
        Assert.Contains("it isn't recording them", played.Text, StringComparison.Ordinal);
        Assert.Equal(made, played.Path);
        Assert.Contains("holds none yet", (await Why(copy))!.Text, StringComparison.Ordinal);

        // Its stats kept beside an empty file of achievements (the owner's Sons of the Forest).
        File.Delete(made);
        var empty = Record(publicDocs, Hades, Path.Combine("Stats", "Achievements.ini"), "");
        Record(publicDocs, Hades, Path.Combine("Stats", "Stats.ini"), "[Stats]\nSTAT_ESCAPES=17\n");
        var stats = await Why(copy);
        Assert.Contains("keeps Hades's stats but never its achievements", stats!.Text, StringComparison.Ordinal);
        Assert.Equal(Path.GetDirectoryName(empty), stats.Path);

        // A record that says something, and a Steam copy: GameSync sees them, so there's nothing to explain.
        File.WriteAllText(empty, "[ACH_ESCAPE]\nAchieved=1\nUnlockTime=1790000000\n");
        await Achievements.FindRecordsAsync(data, [copy], CancellationToken.None, roots: [publicDocs]);
        Assert.Null(Achievements.For(data, copy, steam)!.Why);
        Assert.Null(Achievements.For(data, copy with { Store = StoreKind.Steam }, steam)?.Why);

        // The page shows it after the header's words, with the file it's about able to wrap.
        var page = new AchievementsViewModel(HadesId, "Hades", null, () => { }, []);
        page.Show(Achievements.For(data, copy, steam)! with { CanMark = true, Why = noList }, DateTime.Now);
        Assert.Equal((true, noList.Text, true), (page.HasWhy, page.WhyText, page.HasWhyPath));
        Assert.Equal(settings.Replace(@"\", "\\​", StringComparison.Ordinal), page.WhyPath);
    }

    /// <summary>Every order of a few entries, to show they settle the same whichever PC meets which first.</summary>
    private static IEnumerable<KeptUnlock[]> Orders(KeptUnlock[] items) => items.Length <= 1 ? [items]
        : items.SelectMany((item, i) => Orders([.. items.Take(i), .. items.Skip(i + 1)]).Select(rest => (KeptUnlock[])[item, .. rest]));

    /// <summary>Steam's public list of Hades' achievements, kept as GameSync keeps it once asked.</summary>
    private static void KeepList(string data)
    {
        var kept = Directory.CreateDirectory(Path.Combine(data, "art", "achievements", Hades.ToString(CultureInfo.InvariantCulture))).FullName;
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
